#if NEWALGORITHM
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.Draw;
using SecRandom.NewAlgorithm;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

/// <summary>
///     The vendored VeriRandom.NewAlgorithm share-debt tier, selectable beside the built-in fair and random tiers.
/// </summary>
/// <remarks>
///     <para>
///         It is a pure weight source: it never selects a winner and never touches history. Its output enters the same
///         frozen-candidate pipeline as <see cref="FairRollCallAlgorithm" />, so the proof records the exact weights the
///         sampler used — that is what keeps replay exact even though this tier computes those weights with a different
///         formula from <see cref="DrawEngine.CalculateStudentWeight" />.
///     </para>
///     <para>
///         The tier deliberately stays inside the existing <c>StudentFair*</c> verification profile family. The wire
///         request carries per-candidate <c>WeightMicros</c>, so the verifier samples the recorded weights instead of
///         recomputing them, and the profile only names the policy family both tiers belong to. A dedicated profile
///         byte cannot be introduced before the service-side replay kernel understands it, and a tier that always fails
///         attestation would be worse than one that describes itself honestly.
///     </para>
/// </remarks>
public sealed class NewAlgorithmRollCallAlgorithm : IRollCallAlgorithm
{
    public IReadOnlyList<WeightedCandidate<Student>> BuildCandidates(
        DrawEngine engine,
        IReadOnlyList<Student> eligibleCandidates,
        IReadOnlyDictionary<Student, History> history,
        FairDrawPolicySnapshot fairSettings,
        string courseName)
    {
        ArgumentNullException.ThrowIfNull(eligibleCandidates);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(fairSettings);

        if (eligibleCandidates.Count == 0)
            return [];

        // Group/gender become balance dimensions only when the draw settings ask for them; an empty set keeps the tier
        // at the vendor model's own default, personal share-debt balancing only.
        var labelSources = new List<(BalanceDimension Dimension, Func<Student, string> Selector)>(2);
        if (fairSettings.FairDrawGroup)
            labelSources.Add((new BalanceDimension(labelSources.Count, DrawEngine.ShareDebtDimensionHorizonPerPick),
                student => student.Group));
        if (fairSettings.FairDrawGender)
            labelSources.Add((new BalanceDimension(labelSources.Count, DrawEngine.ShareDebtDimensionHorizonPerPick),
                student => student.Gender));

        var labelMaps = new Dictionary<string, int>[labelSources.Count];
        for (var slot = 0; slot < labelMaps.Length; slot++)
            labelMaps[slot] = new Dictionary<string, int>(StringComparer.Ordinal);

        var pool = new StudentMetaData[eligibleCandidates.Count];
        var histories = new DrawHistory[eligibleCandidates.Count];
        for (var index = 0; index < eligibleCandidates.Count; index++)
        {
            var student = eligibleCandidates[index];
            var labels = new int[labelSources.Count];
            for (var slot = 0; slot < labelSources.Count; slot++)
            {
                var key = labelSources[slot].Selector(student) ?? string.Empty;
                var map = labelMaps[slot];
                if (!map.TryGetValue(key, out var label))
                {
                    label = map.Count;
                    map[key] = label;
                }

                labels[slot] = label;
            }

            // Cap stays 0 on purpose: it is a caller-side pool filter in the vendor model and never enters the weight
            // math, while the repeat and average-gap gates have already filtered this pool.
            pool[index] = new StudentMetaData(index, 0, 1.0, labels);
            histories[index] = new DrawHistory(index,
                history.TryGetValue(student, out var record) ? record.TotalCount : 0);
        }

        var settings = new WeightSettings
        {
            PersonalHorizonRounds = DrawEngine.ShareDebtPersonalHorizonRounds,
            RandomFloor = DrawEngine.ShareDebtRandomFloor,
            Dimensions = labelSources.Select(source => source.Dimension).ToArray()
        };

        var batchSize = Math.Clamp(fairSettings.BatchSize, 1, eligibleCandidates.Count);
        var result = FairDrawWeights.Compute(pool, histories, settings, batchSize);

        // ToProbabilities applies RandomFloor exactly once. Returning the raw weights would silently drop the floor,
        // and multiplying the probabilities again before the sampler would apply it twice.
        var probabilities = FairDrawWeights.ToProbabilities(result, settings);
        return eligibleCandidates
            .Select((student, index) => new WeightedCandidate<Student>
            {
                Candidate = student,
                Weight = probabilities[index]
            })
            .ToArray();
    }
}
#endif
