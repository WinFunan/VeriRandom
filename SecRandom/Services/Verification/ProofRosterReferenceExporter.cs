using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Verification;

/// <summary>
///     Writes the "proof reference roster" JSON that the replay window reads as its reference list: the same records,
///     with the same hidden <c>RecordId</c> values, that a draw was taken from — and nothing else.
/// </summary>
/// <remarks>
///     No human-readable export can stand in for this file. The history export deliberately never writes
///     <c>RecordId</c>, and both things the replay window needs are keyed on it: winners are recorded as record ids,
///     and the roster digest is computed over the RecordId-to-display mapping. A CSV/XLSX therefore cannot be matched
///     against a proof at all, and re-importing one assigns brand-new record ids, so even "the same names" would
///     produce a different digest.
///     <para>
///         The copied fields must stay exactly the ones <see cref="RosterDigest" /> hashes — a student's
///         <c>Id</c>/<c>Name</c>/<c>Group</c>/<c>Gender</c>/<c>Exists</c>, a prize's <c>Id</c>/<c>Name</c>/
///         <c>Count</c>/<c>Weight</c>/<c>Exists</c>, plus the record id itself. Dropping one silently changes the
///         digest, and every later check would report a mismatch that nothing on the screen explains.
///     </para>
///     <para>
///         The output is the same JSON shape as a stored list file, so <c>ProofReplayFileService.LoadRoster</c> reads
///         it unchanged (students and prizes are told apart by their root property).
///     </para>
/// </remarks>
public static class ProofRosterReferenceExporter
{
    public static byte[] CreateStudentReference(StudentList list) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new StudentList { Students = new ObservableCollection<Student>(list.Students.Select(Copy)) },
            ConfigServiceBase.JsonOptions);

    public static byte[] CreatePrizeReference(PrizeList list) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new PrizeList { Prizes = new ObservableCollection<Prize>(list.Prizes.Select(Copy)) },
            ConfigServiceBase.JsonOptions);

    private static Student Copy(Student student) => new()
    {
        RecordId = student.RecordId,
        Id = student.Id,
        Name = student.Name,
        Gender = student.Gender,
        Group = student.Group,
        Tags = student.Tags,
        Exists = student.Exists
    };

    private static Prize Copy(Prize prize) => new()
    {
        RecordId = prize.RecordId,
        Id = prize.Id,
        Name = prize.Name,
        Count = prize.Count,
        Weight = prize.Weight,
        Tags = prize.Tags,
        Exists = prize.Exists
    };
}
