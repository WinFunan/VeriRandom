using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Security;

/// <summary>
///     Digests the part of a roster that a signed configuration policy covers: the RecordId-to-display
///     mapping, and deliberately not the parts an operator legitimately adjusts while the machine is in use.
///     <para>
///         This is <em>not</em> the proof's <c>RosterDigest</c>, and it must not be merged with it. The proof
///         digest binds everything that can change a result — including a prize's weight — because it answers
///         "is this the same draw afterwards". A configuration policy answers a different question: "is this
///         still the roster the policy was written for", so covering a weight, a prize inventory count, or any
///         other draw input would invalidate the signature on the first ordinary adjustment and make the
///         feature unusable rather than protective.
///     </para>
///     <para>
///         What is covered is the identity mapping: renaming, renumbering, regrouping, or disabling a record
///         is a change, while reordering the list, editing a weight, or using up prize inventory is not.
///     </para>
/// </summary>
public static class RosterPolicyDigest
{
    /// <summary>
    ///     Domain separator. Deliberately different from the proof's <c>SecRandomProof/v3/roster</c> so the two
    ///     digests can never be confused with one another.
    /// </summary>
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("VeriRandomConfigPolicy/v2/roster");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Digest of one roll-call list's identity mapping.</summary>
    public static string ComputeList(StudentList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        var entries = list.Students
            .OrderBy(student => student.RecordId.ToString("N"), StringComparer.Ordinal)
            .Select(student => new
            {
                recordId = student.RecordId.ToString("N"),
                id = student.Id,
                name = student.Name,
                group = student.Group,
                gender = student.Gender,
                exists = student.Exists
            })
            .ToArray();

        return Compute("student", list.Name, entries);
    }

    /// <summary>
    ///     Digest of one lottery pool's identity mapping. A prize's <c>Weight</c> and inventory <c>Count</c> are
    ///     excluded on purpose: both are draw inputs the operator adjusts, and covering them would break the
    ///     signature during normal use.
    /// </summary>
    public static string ComputeList(PrizeList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        var entries = list.Prizes
            .OrderBy(prize => prize.RecordId.ToString("N"), StringComparer.Ordinal)
            .Select(prize => new
            {
                recordId = prize.RecordId.ToString("N"),
                id = prize.Id,
                name = prize.Name,
                exists = prize.Exists
            })
            .ToArray();

        return Compute("prize", list.Name, entries);
    }

    /// <summary>
    ///     Folds the per-list digests into the single value the configuration policy covers. Sorted ordinally so
    ///     the order the lists happen to be enumerated in is not a change.
    /// </summary>
    public static string Combine(IEnumerable<string> listDigests)
    {
        ArgumentNullException.ThrowIfNull(listDigests);

        var ordered = listDigests
            .Where(digest => !string.IsNullOrWhiteSpace(digest))
            .OrderBy(digest => digest, StringComparer.Ordinal)
            .ToArray();

        return ToHex(Hash(JsonSerializer.SerializeToUtf8Bytes(ordered, JsonOptions)));
    }

    // Entries are sorted by record id before hashing, so reordering a list is not a change while renaming,
    // renumbering, regrouping, or disabling a record is.
    private static string Compute<T>(string kind, string listName, T[] entries)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { kind, listName, entries }, JsonOptions);
        return ToHex(Hash(payload));
    }

    private static byte[] Hash(byte[] payload)
    {
        var material = new byte[DomainSeparator.Length + payload.Length];
        DomainSeparator.CopyTo(material, 0);
        payload.CopyTo(material, DomainSeparator.Length);
        return SHA256.HashData(material);
    }

    private static string ToHex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
}
