using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;

namespace SecRandom.Services.Security.Fido2;

/// <summary>
///     One enrolled hardware security key / platform authenticator.
/// </summary>
/// <remarks>
///     Everything here is public material: the credential id, the COSE public key, the signature counter and a
///     user-chosen label. The file is still kept owner-only, because a writable copy would let someone swap the
///     public key (every assertion then fails: denial of service, not a bypass) or roll the counter back (which
///     hides clone detection). That is the same boundary the credential lockout state already accepts, and it is
///     why this list is not part of the AES-GCM credential envelope: the envelope can only be opened with the
///     password, while a security-key-only deployment has no password to open it with.
/// </remarks>
public sealed class Fido2CredentialRecord
{
    /// <summary>Internal, stable identity used by the app; never sent to the authenticator.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>base64url raw credential id emitted by the authenticator.</summary>
    public string CredentialId { get; set; } = string.Empty;

    /// <summary>base64url of the raw COSE_Key CBOR item.</summary>
    public string PublicKeyCose { get; set; } = string.Empty;

    /// <summary>COSE algorithm the key registered with (-7 ES256, -257 RS256, ...).</summary>
    public int Algorithm { get; set; }

    /// <summary>Highest signature counter observed; 0 means the authenticator keeps no counter.</summary>
    public uint SignCount { get; set; }

    /// <summary>Relying-party id the credential was created for (always <c>localhost</c> today).</summary>
    public string RpId { get; set; } = string.Empty;

    /// <summary>base64url of the WebAuthn user handle the credential belongs to.</summary>
    public string UserHandle { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public List<string> Transports { get; set; } = [];

    public bool UserVerified { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset LastUsedAtUtc { get; set; }
}

/// <summary>
///     Owns <c>data/config/security/fido2-keys.json</c>. Saves are atomic replacements and the credential
///     directory keeps its owner-only hardening, matching the rest of the security store.
/// </summary>
public sealed class Fido2CredentialStore(ILogger<Fido2CredentialStore> logger)
{
    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private List<Fido2CredentialRecord>? _cache;
    private bool _directoryHardened;

    /// <summary>
    ///     Test seam: an explicit file path instead of the package data root. The Host resolves the single
    ///     constructor above, so production code never chooses a path here.
    /// </summary>
    public string? StorePathOverride { get; init; }

    private string StorePath => string.IsNullOrWhiteSpace(StorePathOverride)
        ? Utils.GetFilePath("config", "security", "fido2-keys.json")
        : StorePathOverride;

    /// <summary>Raised after a credential is added, removed or updated, so an open surface can refresh.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<Fido2CredentialRecord> List()
    {
        lock (_gate)
            return LoadLocked().ToList();
    }

    public Fido2CredentialRecord? Find(string idOrCredentialId)
    {
        if (string.IsNullOrWhiteSpace(idOrCredentialId))
            return null;

        lock (_gate)
        {
            return LoadLocked().FirstOrDefault(record =>
                string.Equals(record.Id, idOrCredentialId, StringComparison.Ordinal) ||
                string.Equals(record.CredentialId, idOrCredentialId, StringComparison.Ordinal));
        }
    }

    /// <summary>
    ///     Replaces an existing record with the same credential id or appends the new one. A re-registration of
    ///     the same authenticator must not leave two rows pointing at the same credential id.
    /// </summary>
    public void AddOrReplace(Fido2CredentialRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.CredentialId))
            throw new ArgumentException("A FIDO2 credential needs both an internal id and a credential id.", nameof(record));

        lock (_gate)
        {
            var records = LoadLocked();
            var index = records.FindIndex(existing =>
                string.Equals(existing.CredentialId, record.CredentialId, StringComparison.Ordinal));
            if (index >= 0)
                records[index] = record;
            else
                records.Add(record);

            SaveLocked(records);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string idOrCredentialId)
    {
        bool removed;
        lock (_gate)
        {
            var records = LoadLocked();
            removed = records.RemoveAll(record =>
                string.Equals(record.Id, idOrCredentialId, StringComparison.Ordinal) ||
                string.Equals(record.CredentialId, idOrCredentialId, StringComparison.Ordinal)) > 0;
            if (removed)
                SaveLocked(records);
        }

        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>
    ///     Records a successful assertion. The counter only ever moves forward; a zero from an authenticator that
    ///     previously reported a counter keeps the stored value, so a key without a counter cannot erase one.
    /// </summary>
    public void RecordAssertion(string idOrCredentialId, uint signCount, bool userVerified)
    {
        lock (_gate)
        {
            var records = LoadLocked();
            var record = records.FirstOrDefault(existing =>
                string.Equals(existing.Id, idOrCredentialId, StringComparison.Ordinal) ||
                string.Equals(existing.CredentialId, idOrCredentialId, StringComparison.Ordinal));
            if (record is null)
                return;

            if (signCount > record.SignCount)
                record.SignCount = signCount;
            record.UserVerified = userVerified;
            record.LastUsedAtUtc = DateTimeOffset.UtcNow;
            SaveLocked(records);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<Fido2CredentialRecord> LoadLocked()
    {
        if (_cache is not null)
            return _cache;

        var path = StorePath;
        if (!File.Exists(path))
            return _cache = [];

        try
        {
            var state = JsonSerializer.Deserialize<StorageState>(File.ReadAllText(path), JsonOptions);
            _cache = (state?.Credentials ?? []).Where(IsUsable).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file as evidence instead of silently overwriting it on the next save.
            logger.LogWarning(ex, "FIDO2 凭据文件无法读取，已改名保留，本次按未登记处理");
            TryPreserveCorruptFile(path);
            _cache = [];
        }

        return _cache;
    }

    private void SaveLocked(List<Fido2CredentialRecord> records)
    {
        var path = StorePath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        HardenDirectoryOnce(directory);

        var temporary = path + ".tmp";
        var payload = JsonSerializer.Serialize(new StorageState
        {
            FormatVersion = FormatVersion,
            Credentials = records.Where(IsUsable).ToList()
        }, JsonOptions);
        File.WriteAllText(temporary, payload);
        SecurityPathProtection.RestrictFileToOwner(temporary);
        File.Move(temporary, path, true);

        _cache = records;
    }

    private void HardenDirectoryOnce(string directory)
    {
        if (_directoryHardened)
            return;
        _directoryHardened = true;
        SecurityPathProtection.RestrictDirectoryToOwner(directory);
    }

    private void TryPreserveCorruptFile(string path)
    {
        try
        {
            var preserved = path + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.Move(path, preserved, true);
            SecurityPathProtection.RestrictFileToOwner(preserved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "FIDO2 凭据文件保留副本失败");
        }
    }

    private static bool IsUsable(Fido2CredentialRecord? record) =>
        record is not null &&
        !string.IsNullOrWhiteSpace(record.Id) &&
        !string.IsNullOrWhiteSpace(record.CredentialId) &&
        !string.IsNullOrWhiteSpace(record.PublicKeyCose);

    

    private sealed class StorageState
    {
        public int FormatVersion { get; init; }
        public List<Fido2CredentialRecord> Credentials { get; init; } = [];
    }
}
