using System;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     Everything the details dialog shows about one proof, already resolved to raw values.
/// </summary>
/// <remarks>
///     Raw values, not formatted text: the view owns wording and localization, this only reports what the file says.
///     Every field except <see cref="SeedBase64Url" /> is read straight out of the proof; the seed is decoded from the
///     recorded request frame, which is also what a replay uses.
/// </remarks>
public sealed record ProofDetails(
    Guid ProofId,
    VerificationProofMode Mode,
    string AlgorithmId,
    string EngineVersion,
    int RequestFrameVersion,
    string InputHashBase64Url,
    string AuditHashBase64Url,
    string SeedBase64Url,
    bool HasServerReceipt,
    int RequestedCount,
    int CandidateCount,
    AnonymizedAuditPayload? Audit,
    ProofConsistencyReport Consistency,
    ProofGateEvidenceInfo? GateEvidence);

/// <summary>
///     What the fork-local gate-evidence file beside a proof says, and whether it still belongs to that proof.
/// </summary>
/// <param name="BoundToProof">
///     False when the Up chain hash, chain index or proof id the evidence names no longer match the proof beside it —
///     the shape a copied-onto-another-draw exclusion would take.
/// </param>
public sealed record ProofGateEvidenceInfo(
    int RemovedCount,
    int CandidateCountBefore,
    int CandidateCountAfter,
    int Threshold,
    bool SelfHashValid,
    bool BoundToProof);
