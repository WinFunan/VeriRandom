using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Services.Security.Fido2;

namespace SecRandom.Core.Tests;

/// <summary>
///     The credential list is the only thing a future security-key factor trusts between ceremonies, so its
///     persistence rules are pinned here: a reload sees what was saved, a re-registration does not duplicate a
///     credential id, and the signature counter can never move backwards.
/// </summary>
public class Fido2CredentialStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "verirandom-fido2-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "fido2-keys.json");

    private Fido2CredentialStore CreateStore() =>
        new(NullLogger<Fido2CredentialStore>.Instance) { StorePathOverride = StorePath };

    [Fact]
    public void AddOrReplace_PersistsACredentialForTheNextProcess()
    {
        var store = CreateStore();
        store.AddOrReplace(NewRecord("credential-a", signCount: 3));

        var reloaded = CreateStore();

        var record = Assert.Single(reloaded.List());
        Assert.Equal("credential-a", record.CredentialId);
        Assert.Equal(3u, record.SignCount);
        Assert.Equal(-7, record.Algorithm);
    }

    [Fact]
    public void Find_ResolvesByEitherInternalIdOrCredentialId()
    {
        var store = CreateStore();
        var record = NewRecord("credential-a", signCount: 0);
        store.AddOrReplace(record);

        Assert.NotNull(store.Find(record.Id));
        Assert.NotNull(store.Find(record.CredentialId));
        Assert.Null(store.Find("credential-b"));
    }

    [Fact]
    public void AddOrReplace_DoesNotDuplicateAReRegisteredCredential()
    {
        var store = CreateStore();
        store.AddOrReplace(NewRecord("credential-a", signCount: 1));
        store.AddOrReplace(NewRecord("credential-a", signCount: 9));

        var record = Assert.Single(store.List());
        Assert.Equal(9u, record.SignCount);
    }

    [Fact]
    public void RecordAssertion_KeepsTheHighestCounterEvenWhenTheKeyReportsZero()
    {
        var store = CreateStore();
        var record = NewRecord("credential-a", signCount: 0);
        store.AddOrReplace(record);

        store.RecordAssertion(record.Id, 5, userVerified: true);
        Assert.Equal(5u, store.Find(record.Id)!.SignCount);

        store.RecordAssertion(record.Id, 0, userVerified: false);
        Assert.Equal(5u, store.Find(record.Id)!.SignCount);
        Assert.False(store.Find(record.Id)!.UserVerified);
    }

    [Fact]
    public void Remove_DeletesTheCredentialFromThePersistedFile()
    {
        var store = CreateStore();
        var record = NewRecord("credential-a", signCount: 1);
        store.AddOrReplace(record);

        Assert.True(store.Remove(record.CredentialId));
        Assert.Empty(store.List());
        Assert.Empty(CreateStore().List());
    }

    [Fact]
    public void Load_PreservesAnUnreadableFileInsteadOfOverwritingIt()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "{ this is not json");

        var store = CreateStore();
        Assert.Empty(store.List());

        var preserved = Directory.GetFiles(_directory, "fido2-keys.json.corrupt-*");
        Assert.Single(preserved);
    }

    private static Fido2CredentialRecord NewRecord(string credentialId, uint signCount) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        CredentialId = credentialId,
        PublicKeyCose = "c29tZS1rZXk",
        Algorithm = -7,
        SignCount = signCount,
        RpId = "localhost",
        UserHandle = "dXNlci1oYW5kbGU",
        Label = "Test key",
        Transports = ["usb"],
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        LastUsedAtUtc = DateTimeOffset.UnixEpoch
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail the suite.
        }
    }
}
