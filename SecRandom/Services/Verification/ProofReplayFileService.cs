using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Profile;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

/// <summary>One proof as the replay window needs it: where it sits in the chain, when it was drawn, and the bytes
/// needed to replay it. The payload bytes are kept in memory because the window replays on user command. </summary>
public sealed record ProofReplayNode(
    string Path,
    string FileName,
    long ChainIndex,
    DateTimeOffset CreatedAtUtc,
    VerificationDrawKind Kind,
    VerificationProofMode Mode,
    int CandidateCount,
    int WinnerCount,
    bool HasRosterDigest,
    string EngineVersion,
    IReadOnlyList<Guid> WinnerRecordIds,
    byte[] Payload,
    string AuditPayloadBase64,
    string? DecodeError,
    ProofConsistencyReport Consistency)
{
    /// <summary>Formal notarization stores a zero seed, so only an ordinary proof can be replayed from its file.</summary>
    public bool CanReplay => DecodeError is null && Mode == VerificationProofMode.OfflineReproducible;
}

/// <summary>A roster the user picked, ready to resolve winner record ids back to names.</summary>
public sealed record ProofRosterContext(
    string Kind,
    IReadOnlyDictionary<Guid, string> Names,
    string Digest);

/// <summary>
///     One member as the preview shows it. <see cref="Trailing" /> carries the values the digest also commits but
///     which are not the member's identity (a student's group and gender, a prize's count), so the preview shows what
///     the user is actually about to vouch for instead of just a name.
/// </summary>
public sealed record ProofRosterMember(
    string Number,
    string Name,
    string Trailing,
    bool Enabled);

/// <summary>A roster plus its members: the context used for replay/assessment, and the rows previewed first.</summary>
public sealed record ProofRosterSnapshot(
    ProofRosterContext Context,
    string Name,
    IReadOnlyList<ProofRosterMember> Members);

/// <summary>What the window can honestly say about a proof's entropy signature.</summary>
public enum ProofSignatureNote
{
    /// <summary>No signed entropy source was recorded (NIST, drand, or no beacon at all).</summary>
    NoSignedSource,

    /// <summary>The source publishes no signature (the unsigned random.org endpoint).</summary>
    UnsignedSource,

    /// <summary>A signature was recorded, but verifying it again needs material the proof does not hold.</summary>
    SignatureRecorded
}

/// <summary>Result of replaying one node, including the roster check when a roster was supplied.</summary>
public sealed record ProofReplayOutcome(
    ProofReplayReport Report,
    bool? RosterMatches,
    IReadOnlyList<string> RecordedWinnerNames,
    IReadOnlyList<string> ReplayedWinnerNames);

/// <summary>
///     App-layer shell around <see cref="ProofReplayService" />: it owns the file access that Core is deliberately
///     kept out of (walking <c>data/proofs</c>, reading proofs and rosters, decoding base64url) and leaves the
///     actual replay to the pure Core service.
/// </summary>
public sealed class ProofReplayFileService(
    DrawProofExportService proofExporter,
    OwnProofExportService ownProofExporter,
    GateEvidenceExportService gateEvidenceExporter,
    ProofIntegrityVerifier integrityVerifier,
    IProfileService profileService,
    ILogger<ProofReplayFileService> logger)
{
    /// <summary>
    ///     Every retained proof in hash-chain order. A proof without a chain block cannot be placed in the chain and
    ///     is skipped with a log line rather than dropped silently.
    /// </summary>
    /// <param name="rootDirectory">
    ///     A folder to read instead of this installation's <c>data/proofs</c> — the cross-device case, where the
    ///     proofs came from another machine. The folder is walked recursively because the local layout is
    ///     <c>&lt;yyyy-MM&gt;/&lt;yyyy-MM-dd&gt;/*.srproof.json</c>, and an exported folder keeps it.
    /// </param>
    public IReadOnlyList<ProofReplayNode> LoadNodes(string? rootDirectory = null)
    {
        var nodes = new List<ProofReplayNode>();
        foreach (var path in EnumerateProofPaths(rootDirectory))
        {
            if (!proofExporter.TryRead(path, out var proof) || proof is null)
            {
                logger.LogWarning("证明重放：无法读取证明文件 {Path}", path);
                continue;
            }

            if (proof.Chain is null)
            {
                logger.LogWarning("证明重放：证明 {Path} 没有链信息，无法定位其在链中的位置", path);
                continue;
            }

            nodes.Add(Describe(path, proof));
        }

        return nodes.OrderBy(node => node.ChainIndex).ToArray();
    }

    private IEnumerable<string> EnumerateProofPaths(string? rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            return proofExporter.EnumerateProofPaths();

        return Directory.Exists(rootDirectory)
            ? Directory.EnumerateFiles(rootDirectory, "*.srproof.json", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>
    ///     Re-reads one proof file into the values the details dialog shows. Re-read rather than carried on
    ///     <see cref="ProofReplayNode" />: the node deliberately holds only what replay needs, and widening it for a
    ///     dialog that is opened far less often would make every list load pay for it.
    /// </summary>
    public ProofDetails? TryBuildDetails(string path)
    {
        if (!proofExporter.TryRead(path, out var proof) || proof is null)
        {
            logger.LogWarning("证明详情：无法读取证明文件 {Path}", path);
            return null;
        }

        var auditBytes = string.IsNullOrWhiteSpace(proof.AuditPayload)
            ? []
            : AnonymizedAuditPayload.DecodeBase64Url(proof.AuditPayload);

        var audit = auditBytes.Length == 0 ? null : AnonymizedAuditPayload.TryParse(auditBytes);

        // The audit commitment the public verifier displays is a hash of those bytes, not a stored field; hashing it
        // here is what keeps this view honest instead of restating a value nothing wrote down.
        var auditHash = auditBytes.Length == 0
            ? string.Empty
            : AnonymizedAuditPayload.EncodeBase64Url(SHA256.HashData(auditBytes));

        var seed = string.Empty;
        var requestedCount = 0;
        var candidateCount = 0;

        if (!string.IsNullOrWhiteSpace(proof.Payload))
        {
            var payloadBytes = AnonymizedAuditPayload.DecodeBase64Url(proof.Payload);

            if (VerificationWireCodec.TryDecodeProofPayload(payloadBytes, out var input, out var decodedSeed, out _))
            {
                seed = AnonymizedAuditPayload.EncodeBase64Url(decodedSeed);
                requestedCount = input.Count;
                candidateCount = input.Candidates.Count;
            }
        }

        var gateEvidence = gateEvidenceExporter.TryReadForUpPath(path);

        return new ProofDetails(
            proof.ProofId,
            proof.Mode,
            proof.AlgorithmId,
            proof.AlgorithmEngineVersion,
            VerificationWireCodec.RequestFormatVersion,
            proof.InputHash,
            auditHash,
            seed,
            !string.IsNullOrWhiteSpace(proof.Witness?.Receipt),
            requestedCount,
            candidateCount,
            audit,
            ProofConsistency.Check(proof, gateEvidence),
            DescribeGateEvidence(proof, gateEvidence));
    }

    /// <summary>
    ///     Projects the fork-local gate evidence beside this proof. Absence is normal — a draw whose gate removed nobody
    ///     writes none — so it returns null rather than an error, and the dialog has to say which reading applies
    ///     instead of showing a silent pass.
    /// </summary>
    private static ProofGateEvidenceInfo? DescribeGateEvidence(DrawProof proof, GateEvidence? evidence)
    {
        if (evidence is null)
            return null;

        var selfHashValid = string.Equals(
            GateEvidenceHashing.ComputeSelfHash(evidence),
            evidence.SelfHash,
            StringComparison.Ordinal);

        var bound = proof.Chain is { } chain
                    && string.Equals(chain.SelfHash, evidence.UpChainHash, StringComparison.Ordinal)
                    && chain.Index == evidence.UpChainIndex
                    && proof.ProofId == evidence.ProofId;

        return new ProofGateEvidenceInfo(
            evidence.Removed.Count,
            evidence.CandidateCountBefore,
            evidence.CandidateCountAfter,
            evidence.Threshold,
            selfHashValid,
            bound);
    }

    /// <summary>
    ///     Reads a roster the user selected. Student lists and prize pools are distinguished by their root property,
    ///     because the stored JSON carries no kind marker of its own.
    /// </summary>
    public ProofRosterContext? LoadRoster(string path, out string? error)
    {
        error = null;
        try
        {
            var json = File.ReadAllText(path);
            var options = ConfigServiceBase.JsonOptions;

            if (json.Contains("\"prizes\"", StringComparison.Ordinal))
            {
                var prizes = JsonSerializer.Deserialize<PrizeList>(json, options);
                return prizes is null ? null : new ProofRosterContext("prize", Names(prizes), RosterDigest.Compute(prizes));
            }

            var students = JsonSerializer.Deserialize<StudentList>(json, options);
            return students is null ? null : new ProofRosterContext("student", Names(students), RosterDigest.Compute(students));
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            error = exception.Message;
            logger.LogWarning(exception, "证明重放：读取名单文件 {Path} 失败", path);
            return null;
        }
    }

    /// <summary>
    ///     The roster that is active right now, taken for the draw kind of the selected proof.
    /// </summary>
    /// <remarks>
    ///     This reads live state on purpose, and it is the one place that may: the roster digest exists precisely to
    ///     answer "is the list I have now the one this draw was taken from", so comparing against the live list is the
    ///     check itself. The replay itself still never touches live state — its candidate pool, weights and seed come
    ///     from the proof.
    /// </remarks>
    public bool TryLoadActiveRoster(VerificationDrawKind kind, out ProofRosterSnapshot? snapshot)
    {
        snapshot = null;

        if (kind == VerificationDrawKind.Prize)
        {
            if (profileService.CurrentPrizeList is not { } prizes)
                return false;

            snapshot = new ProofRosterSnapshot(
                new ProofRosterContext("prize", Names(prizes), RosterDigest.Compute(prizes)),
                prizes.Name,
                prizes.Prizes.Select(prize => new ProofRosterMember(
                    prize.Id,
                    DisplayName(prize),
                    $"×{prize.Count}",
                    prize.Exists)).ToArray());
            return true;
        }

        if (profileService.CurrentStudentList is not { } students)
            return false;

        snapshot = new ProofRosterSnapshot(
            new ProofRosterContext("student", Names(students), RosterDigest.Compute(students)),
            students.Name,
            students.Students.Select(student => new ProofRosterMember(
                student.Id,
                DisplayName(student),
                string.Join(" · ", new[] { student.Group, student.Gender }
                    .Where(part => !string.IsNullOrWhiteSpace(part))),
                student.Exists)).ToArray());
        return true;
    }

    /// <summary>Replays one node and, when a roster was supplied, resolves both winner sets to names.</summary>
    public ProofReplayOutcome Replay(ProofReplayNode node, ProofRosterContext? roster)
    {
        var report = node.DecodeError is null
            ? ProofReplayService.Replay(node.Payload, node.Mode, node.EngineVersion)
            : new ProofReplayReport(ProofReplayStatus.UnsupportedFormat, [], [], node.DecodeError);

        bool? rosterMatches = null;
        if (roster is not null)
        {
            var recordedDigest = ReadRosterDigest(node.AuditPayloadBase64);
            rosterMatches = recordedDigest is not null
                && string.Equals(recordedDigest, roster.Digest, StringComparison.Ordinal);
        }

        return new ProofReplayOutcome(
            report,
            rosterMatches,
            ResolveNames(report.RecordedWinners, roster),
            ResolveNames(report.ReplayedWinners, roster));
    }

    /// <summary>
    ///     Grades one node against the sources the user chose to trust, reading only artifacts on disk: the Up file
    ///     supplies the chain position, the receipt and the time stamp, and the reference sibling supplies the beacon
    ///     pulse. Each input is verified rather than merely present — the time stamp must validate and the receipt
    ///     must be bound to this proof — so a node that carries a broken token cannot claim that factor.
    /// </summary>
    /// <remarks>
    ///     Chain intactness is scoped to this node, unlike the settings page it replaces: the window lets the user pick
    ///     any node, so the node whose own file is the corrupted one must be zeroed even while the rest of the chain
    ///     still verifies. Chain-level breaks (a gap, a broken link, a proof beyond the head) still zero everything,
    ///     because in those cases the chain as a whole can no longer explain itself.
    /// </remarks>
    public ProofTrustReport AssessTrust(
        ProofReplayNode node,
        IReadOnlyCollection<ProofTrustSource> trustedSources,
        DateTimeOffset? witnessedAtUtc,
        WitnessTimeConfidence witnessConfidence)
    {
        if (!proofExporter.TryRead(node.Path, out var proof) || proof is null)
            return ProofTrustScorer.Evaluate(new ProofTrustInput(
                false, false, false, false, null, null, node.CreatedAtUtc,
                trustedSources, witnessedAtUtc, witnessConfidence));

        DrawProofBeacon? beacon = null;
        if (ownProofExporter.TryRead(OwnProofPaths.FromUpPath(node.Path), out var own) && own is not null)
            beacon = own.Beacon;

        DateTimeOffset? timestampedAt = null;
        var timestampValid = false;
        if (proof.Witness?.TimestampToken is { Length: > 0 } token)
        {
            var validation = TimestampAuthorityClient.Validate(
                token, WitnessClient.FromBase64Url(WitnessClient.ComputeAttestedProofHash(proof)));
            timestampValid = validation.IsValid;
            if (validation.IsValid)
                timestampedAt = validation.Timestamp;
        }

        return ProofTrustScorer.Evaluate(new ProofTrustInput(
            IsChainIntactFor(node.Path, proof),
            beacon is not null,
            timestampValid,
            WitnessClient.TryValidateStoredReceipt(proof, out _),
            beacon?.PulseTimeStamp,
            timestampedAt,
            proof.CreatedAtUtc,
            trustedSources,
            witnessedAtUtc,
            witnessConfidence));
    }

    /// <summary>
    ///     Whether the selected proof can vouch for its own position in the chain.
    /// </summary>
    /// <remarks>
    ///     A proof from this installation is judged by the full local verdict — head, retention floor and removal
    ///     ledger included. A proof read from a folder the user picked is judged by its own self-hash only, because a
    ///     folder handed over from another machine cannot prove that it holds the whole chain: reporting the local
    ///     chain's health for a foreign file would be answering a different question.
    /// </remarks>
    /// <summary>
    ///     Re-checks the recorded signature when the payload it covers was kept; null when the proof holds no payload
    ///     (every source but random.org's signed endpoint, and every node written before the field existed).
    /// </summary>
    /// <remarks>
    ///     This is the only path that can legitimately say a signature is valid: it recomputes the check locally from
    ///     material the file carries, instead of repeating whatever was claimed at fetch time.
    /// </remarks>
    public RandomOrgSignatureCheck? VerifyRecordedSignature(ProofReplayNode node)
    {
        if (!ownProofExporter.TryRead(OwnProofPaths.FromUpPath(node.Path), out var own) || own?.Beacon is null)
            return null;

        var beacon = own.Beacon;
        if (string.IsNullOrWhiteSpace(beacon.SignatureValue) || string.IsNullOrWhiteSpace(beacon.SignaturePayload))
            return null;

        return IAppHost.TryGetService<RandomOrgSignatureVerifier>()?
            .Verify(beacon.SignaturePayload, beacon.SignatureValue);
    }

    /// <summary>
    ///     Whether the node recorded a signed entropy source, so the window can state exactly what it has and has not
    ///     established.
    /// </summary>
    /// <remarks>
    ///     Verification is deliberately not repeated here: the signature covers the raw JSON of random.org's response,
    ///     which the proof does not keep, so re-checking would need material that is gone. A claim we cannot re-derive
    ///     must not be shown as "verified" — the proof keeps the signature itself precisely so a third party holding
    ///     that response can check it.
    /// </remarks>
    public ProofSignatureNote SignatureNote(ProofReplayNode node)
    {
        if (!ownProofExporter.TryRead(OwnProofPaths.FromUpPath(node.Path), out var own) || own?.Beacon is null)
            return ProofSignatureNote.NoSignedSource;

        return string.IsNullOrWhiteSpace(own.Beacon.SignatureValue)
            ? ProofSignatureNote.UnsignedSource
            : ProofSignatureNote.SignatureRecorded;
    }

    private bool IsChainIntactFor(string path, DrawProof proof)
    {
        var isLocalProof = proofExporter.EnumerateProofPaths()
            .Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));

        if (isLocalProof)
        {
            var report = integrityVerifier.Verify();
            if (report.Gaps > 0 || report.BrokenLinks > 0 || report.BeyondHead > 0)
                return false;

            return report.Issues.All(issue =>
                issue.Kind is not (ProofIntegrityIssueKind.Modified or ProofIntegrityIssueKind.Unreadable
                    or ProofIntegrityIssueKind.UnsupportedVersion)
                || !string.Equals(issue.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        if (proof.Chain is not { } chain)
            return false;

        return string.Equals(
            ProofChainStore.ComputeSelfHash(
                chain.Index, chain.PrevHash, WitnessClient.ComputeAttestedProofHash(proof)),
            chain.SelfHash,
            StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ResolveNames(
        IReadOnlyList<VerificationWinner> winners,
        ProofRosterContext? roster)
    {
        if (roster is null)
            return [];

        return winners
            .Select(winner => roster.Names.TryGetValue(winner.RecordId, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : winner.RecordId.ToString("N"))
            .ToArray();
    }

    private ProofReplayNode Describe(string path, DrawProof proof)
    {
        var payload = TryFromBase64Url(proof.Payload);
        string? decodeError = null;
        var kind = VerificationDrawKind.Student;
        var candidateCount = 0;
        var winnerCount = proof.Result.WinnerRecordIds.Count;

        if (payload is null)
        {
            decodeError = "payload 不是有效的 base64url";
        }
        else if (VerificationWireCodec.TryDecodeProofPayload(payload, out var input, out _, out var recorded))
        {
            kind = input.Kind;
            candidateCount = input.Candidates.Count;
            winnerCount = recorded.Winners.Count;
        }
        else
        {
            decodeError = "payload 无法解码为本版本可识别的抽取输入";
        }

        return new ProofReplayNode(
            path,
            System.IO.Path.GetFileName(path),
            proof.Chain!.Index,
            proof.CreatedAtUtc,
            kind,
            proof.Mode,
            candidateCount,
            winnerCount,
            ReadRosterDigest(proof.AuditPayload) is not null,
            proof.AlgorithmEngineVersion,
            proof.Result.WinnerRecordIds,
            payload ?? [],
            proof.AuditPayload,
            decodeError,
            ProofConsistency.Check(proof, gateEvidenceExporter.TryReadForUpPath(path)));
    }

    /// <summary>
    ///     The roster digest is not a field of its own: it lives inside the anonymous audit payload, so it is read out
    ///     of that JSON rather than modelled. A missing digest is normal (the coordinator omits it when the drawn
    ///     list was not the active profile).
    /// </summary>
    private static string? ReadRosterDigest(string auditPayloadBase64)
    {
        var bytes = TryFromBase64Url(auditPayloadBase64);
        if (bytes is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.TryGetProperty("rosterDigest", out var digest)
                && digest.ValueKind == JsonValueKind.String
                ? digest.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<Guid, string> Names(StudentList list) =>
        list.Students
            .GroupBy(student => student.RecordId)
            .ToDictionary(group => group.Key, group => DisplayName(group.First()));

    private static Dictionary<Guid, string> Names(PrizeList list) =>
        list.Prizes
            .GroupBy(prize => prize.RecordId)
            .ToDictionary(group => group.Key, group => DisplayName(group.First()));

    // A record is a valid candidate with only one of number or name, so the name map falls back to the number
    // rather than resolving to an empty label.
    private static string DisplayName(Student student) =>
        string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name;

    private static string DisplayName(Prize prize) =>
        string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;

    /// <summary>base64url without padding, as the proof writer produced it.</summary>
    private static byte[]? TryFromBase64Url(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            0 => normalized,
            _ => string.Empty
        };

        if (normalized.Length == 0)
            return null;

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
