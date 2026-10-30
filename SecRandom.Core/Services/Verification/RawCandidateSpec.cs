using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SecRandom.Core.Services.Draw;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     The resolved candidate space of a raw-number draw: a sorted, merged, non-overlapping set of closed intervals.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why intervals and not a list.</b> "1-1000000 excluding 7" is a perfectly reasonable classroom request and
///         would be a million elements as an array. Intervals keep it at two numbers, and they make the two questions
///         that matter exact without sampling: the cardinality (so the draw is uniform) and the value at a given index.
///     </para>
///     <para>
///         <b>The result set is what gets recorded, not the user's text.</b> The proof stores these intervals, so a replay
///         re-samples the same space without re-parsing an expression or re-evaluating a predicate — an expression that
///         parses differently in a later build therefore cannot silently change what an old record replays to.
///     </para>
///     <para>
///         Sample order: index → walk the intervals by their lengths. Uniform over the whole space in one step, with no
///         rejection loop outside the single index draw and no bias from an excluded middle.
///     </para>
/// </remarks>
public sealed class RawCandidateSpec
{
    /// <summary>Refuse spaces larger than this: a teacher cannot reason about, or verify, a bigger pool than this anyway.</summary>
    public const long MaximumCardinality = 10_000_000_000L;

    /// <summary>Cap on the enumeration generator's own loop, which is the only thing here that walks every value.</summary>
    public const int MaximumEnumerationSteps = 200_000;

    private readonly IReadOnlyList<(long Start, long End)> _intervals;

    private RawCandidateSpec(IReadOnlyList<(long Start, long End)> intervals)
    {
        _intervals = intervals;
        Cardinality = intervals.Sum(interval => interval.End - interval.Start + 1);
    }

    public IReadOnlyList<(long Start, long End)> Intervals => _intervals;

    public long Cardinality { get; }

    /// <summary>The canonical text of the resolved space, e.g. <c>5-6,9-10</c>. Stable, and what the proof records.</summary>
    public string CanonicalText => string.Join(',', _intervals.Select(FormatInterval));

    /// <summary>
    ///     Builds the space from three inputs that compose in the obvious way:
    ///     candidates (lists and ranges) ∪ enumeration generator, minus exclusions.
    /// </summary>
    /// <param name="candidates">Comma-separated items, each a number or <c>X-Y</c>.</param>
    /// <param name="exclusions">Same syntax; subtracted from the candidates. May be empty.</param>
    /// <param name="generatorFrom">Enumeration generator lower bound; null disables the generator.</param>
    /// <param name="generatorTo">Enumeration generator upper bound.</param>
    /// <param name="predicate">Predicate over <c>X</c> that a value must satisfy to be kept. Empty keeps everything.</param>
    public static bool TryCreate(
        string? candidates,
        string? exclusions,
        long? generatorFrom,
        long? generatorTo,
        string? predicate,
        out RawCandidateSpec? spec,
        out string? error)
    {
        spec = null;
        error = null;

        if (!TryParseIntervals(candidates, "candidates", out var included, out error))
            return false;

        if (!TryParseIntervals(exclusions, "exclusions", out var excluded, out error))
            return false;

        if (generatorFrom is { } from || generatorTo is { } to)
        {
            if (generatorFrom is not { } lower || generatorTo is not { } upper)
            {
                error = "The enumeration generator needs both bounds.";
                return false;
            }

            if (upper < lower)
            {
                error = "The enumeration generator's upper bound is below its lower bound.";
                return false;
            }

            var steps = upper - lower + 1;
            if (steps <= 0 || steps > MaximumEnumerationSteps)
            {
                error = $"The enumeration generator may walk at most {MaximumEnumerationSteps} values.";
                return false;
            }

            RawPredicate? compiled = null;
            if (!string.IsNullOrWhiteSpace(predicate))
            {
                if (!RawPredicate.TryCompile(predicate, out compiled, out error))
                    return false;
            }

            try
            {
                for (var value = lower; value <= upper; value++)
                    if (compiled is null || compiled.Test(value))
                        included.Add((value, value));
            }
            catch (DivideByZeroException)
            {
                error = "The predicate divides by zero for some value of X.";
                return false;
            }
        }

        var merged = Merge(included);
        var reduced = Subtract(merged, Merge(excluded));

        if (reduced.Count == 0)
        {
            error = "The exclusions remove every candidate.";
            return false;
        }

        var total = 0L;
        foreach (var (start, end) in reduced)
        {
            var length = end - start + 1;
            if (length <= 0 || length > MaximumCardinality || total > MaximumCardinality - length)
            {
                error = $"The candidate space is larger than {MaximumCardinality} values.";
                return false;
            }

            total += length;
        }

        spec = new RawCandidateSpec(reduced);
        return true;
    }

    /// <summary>Rebuilds a spec from recorded intervals. Used by replay, which must not re-parse anything.</summary>
    public static RawCandidateSpec FromIntervals(IEnumerable<(long Start, long End)> intervals) =>
        new(Merge(intervals.ToList()));

    /// <summary>The single value this seed lands on, uniformly over the whole space.</summary>
    public long Sample(ReadOnlySpan<byte> seed) =>
        ValueAt(RejectionSampler.Sample(0, Cardinality - 1, seed));

    /// <summary>Batch form: the same per-index seed derivation as a plain range, mapped through the intervals.</summary>
    public long[] SampleMany(ReadOnlySpan<byte> seed, int count) =>
        RejectionSampler.SampleMany(0, Cardinality - 1, seed, count)
            .Select(ValueAt)
            .ToArray();

    /// <summary>Index inside the space to its value, walking the intervals by length.</summary>
    private long ValueAt(long index)
    {
        foreach (var (start, end) in _intervals)
        {
            var length = end - start + 1;
            if (index < length)
                return start + index;

            index -= length;
        }

        throw new InvalidOperationException("The sampled index fell outside the recorded candidate space.");
    }

    private static string FormatInterval((long Start, long End) interval) =>
        interval.Start == interval.End
            ? interval.Start.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{interval.Start}-{interval.End}");

    private static bool TryParseIntervals(
        string? text,
        string field,
        out List<(long Start, long End)> intervals,
        out string? error)
    {
        intervals = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        foreach (var raw in text.Split(
                     [',', '，', ';', '；', ' ', '\t', '\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var item = raw.Trim();
            var separator = item.IndexOf('-', 1);

            if (separator < 0)
            {
                if (!TryParseNumber(item, out var single))
                {
                    error = $"{field}: '{item}' is not a number.";
                    return false;
                }

                intervals.Add((single, single));
                continue;
            }

            var left = item[..separator];
            var right = item[(separator + 1)..];

            if (!TryParseNumber(left, out var start) || !TryParseNumber(right, out var end))
            {
                error = $"{field}: '{item}' is not a range.";
                return false;
            }

            if (end < start)
            {
                error = $"{field}: '{item}' ends before it starts.";
                return false;
            }

            intervals.Add((start, end));
        }

        return true;
    }

    private static bool TryParseNumber(string text, out long value) =>
        long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static List<(long Start, long End)> Merge(List<(long Start, long End)> intervals)
    {
        if (intervals.Count == 0)
            return [];

        var ordered = intervals.OrderBy(interval => interval.Start).ThenBy(interval => interval.End).ToList();
        var merged = new List<(long Start, long End)>(ordered.Count);
        var (start, end) = ordered[0];

        foreach (var (nextStart, nextEnd) in ordered.Skip(1))
        {
            // +1 so that 5-6 and 7-8 merge into 5-8: adjacent integers are one interval, not two.
            if (nextStart <= end + 1)
            {
                end = Math.Max(end, nextEnd);
                continue;
            }

            merged.Add((start, end));
            (start, end) = (nextStart, nextEnd);
        }

        merged.Add((start, end));
        return merged;
    }

    private static List<(long Start, long End)> Subtract(
        List<(long Start, long End)> included,
        List<(long Start, long End)> excluded)
    {
        if (excluded.Count == 0)
            return included;

        var result = new List<(long Start, long End)>(included.Count);

        foreach (var (start, end) in included)
        {
            var cursor = start;
            var emitted = false;

            foreach (var (cutStart, cutEnd) in excluded)
            {
                if (cutEnd < cursor)
                    continue;
                if (cutStart > end)
                    break;

                if (cutStart > cursor)
                    result.Add((cursor, Math.Min(end, cutStart - 1)));

                cursor = Math.Max(cursor, cutEnd + 1);
                if (cursor > end)
                {
                    emitted = true;
                    break;
                }
            }

            if (!emitted && cursor <= end)
                result.Add((cursor, end));
        }

        return result;
    }
}
