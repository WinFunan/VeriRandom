using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Verification;
using SecRandom.Langs.MainPages.RawRandom;
using SecRandom.Services.Security;
using SecRandom.Services.Verification;

namespace SecRandom.ViewModels.MainPages;

/// <summary>
///     主界面「随机数」页：在用户给的范围里生成一个裸随机数。
/// </summary>
/// <remarks>
///     <para>
///         <b>为什么不用抽签内核</b>：<c>DrawEngine</c> 要求具体候选池，而这里的范围可以是 1..10⁹——把范围物化成候选
///         既浪费又没有意义。裸随机数的正确做法是<b>拒绝采样</b>（<see cref="RejectionSampler" />）：种子经 SHA-256
///         计数器模式展开，落在尾部不满一整块的值丢掉重来，因此不存在取模偏差。
///     </para>
///     <para>
///         <b>可复核性与"公平"是两件事</b>：只要参与派生的熵全部来自<b>单一</b>可验证源（NIST / drand），事后就能用那个
///         公开脉冲重算同一个数；一旦混入 random.org 或本地 CSPRNG，本次结果只能证明"输入产出这个数"，不能作为可复核
///         证据——界面对此给出阻断式说明，而不是脚注。无论哪种组合，它都<b>不能</b>证明抽中了某个人：没有名单就没有
///         roster digest。
///     </para>
///     <para>
///         <b>为什么不复用 <c>BeaconEntropyProvider</c></b>：那个服务按 <c>BeaconSourceId</c> 绑死单一来源，并为信标
///         序号写锚点文件——那是抽签的语义（一次抽取占一个序号）。本页允许一次勾多个源，走的是"就地取数 + 哈希合并"，
///         不占抽签序号，也不写抽签证明。
///     </para>
/// </remarks>
public partial class RawRandomPageViewModel : ViewModelBase
{
    /// <summary>Sentinel id for the local CSPRNG pseudo-source, which is not part of the beacon catalog.</summary>
    public const string LocalSourceId = RawNumberProofFormat.LocalSourceId;

    private readonly ExternalEntropySourceClient _entropySourceClient;
    private readonly RawNumberProofService _proofService;
    private readonly MainConfigHandler _configHandler;
    private readonly ISecurityService _securityService;
    private readonly ILogger<RawRandomPageViewModel> _logger;

    /// <summary>
    ///     True while the constructor seeds the observable properties from configuration: a first write would
    ///     otherwise persist the defaults back over the saved values before the user touched anything.
    /// </summary>
    private bool _isLoading;

    public RawRandomPageViewModel(
        MainConfigHandler configHandler,
        ExternalEntropySourceClient entropySourceClient,
        RawNumberProofService proofService,
        ISecurityService securityService,
        ILogger<RawRandomPageViewModel> logger) : base(configHandler)
    {
        _configHandler = configHandler;
        _entropySourceClient = entropySourceClient;
        _proofService = proofService;
        _securityService = securityService;
        _logger = logger;

        var more = configHandler.Data.MoreSettings;

        _isLoading = true;
        LowerBoundText = more.RawRandomLowerBound.ToString();
        UpperBoundText = more.RawRandomUpperBound.ToString();
        UseValues = more.RawRandomUseValues;
        ValuesText = more.RawRandomValuesText;
        ExclusionsText = more.RawRandomExclusionsText;
        GeneratorFromText = more.RawRandomGeneratorFromText;
        GeneratorToText = more.RawRandomGeneratorToText;
        PredicateText = more.RawRandomPredicateText;
        UseRawEntropy = more.RawRandomRawEntropyEnabled;
        BitCount = more.RawRandomBitCount;
        OutputFormat = more.RawRandomOutputFormat;
        AsciiCharCount = more.RawRandomAsciiCharCount;
        AllowEntropyConcat = more.RawRandomEntropyConcatEnabled;
        RefreshLockState();

        EntropySources = new ObservableCollection<EntropySourceOption>(
            BeaconSourceCatalog.All.Select(source => new EntropySourceOption(
                source.Id,
                DisplayNameOf(source),
                IsVerifiable(source))));
        EntropySources.Add(new EntropySourceOption(LocalSourceId, Resources.C_LocalCsprng, false));

        // The first verifiable source is the one preselected: a user who never opens the choice gets the reproducible
        // path rather than the one that silently cannot be checked afterwards.
        if (EntropySources.Count > 0)
            EntropySources[0].IsSelected = true;

        foreach (var option in EntropySources)
            option.PropertyChanged += OnEntropySourceChanged;

        _isLoading = false;

        StatusText = Resources.M_Ready;
        RefreshVerifiabilityNotice();
    }

    public ObservableCollection<EntropySourceOption> EntropySources { get; }

    /// <summary>Advanced output is opt-in per machine. Read once when the page is built: flipping it is a settings change that
    ///     requires reopening the page, which is the right weight for a control that changes what "the result" means.
    /// </summary>
    public bool ShowAdvancedOptions => Config.MoreSettings.RawRandomAdvancedOptionsEnabled;

    /// <summary>The character-count row only makes sense for the ASCII format.</summary>
    public bool IsAsciiFormat => OutputFormat == 2;

    partial void OnOutputFormatChanged(int value) => OnPropertyChanged(nameof(IsAsciiFormat));

    [ObservableProperty] private string _lowerBoundText = "0";
    [ObservableProperty] private string _upperBoundText = "100";
    [ObservableProperty] private bool _useValues;
    [ObservableProperty] private string _valuesText = string.Empty;

    /// <summary>Exclusions subtracted from the candidates, e.g. <c>7,8</c> or <c>13-15</c>.</summary>
    [ObservableProperty] private string _exclusionsText = string.Empty;

    /// <summary>Enumeration generator bounds; empty disables it.</summary>
    [ObservableProperty] private string _generatorFromText = string.Empty;
    [ObservableProperty] private string _generatorToText = string.Empty;

    /// <summary>Predicate over <c>X</c> the generated values must satisfy.</summary>
    [ObservableProperty] private string _predicateText = string.Empty;

    /// <summary>Pool size as resolved, so the teacher can see what the expression actually selected.</summary>
    [ObservableProperty] private string _candidatesText = string.Empty;

    /// <summary>How many values one generation produces. Each value is independent; duplicates inside a batch are fine.</summary>
    [ObservableProperty] private int _count = 1;

    /// <summary>Advanced output: emit entropy instead of in-range values.</summary>
    [ObservableProperty] private bool _useRawEntropy;

    /// <summary>
    ///     Confidential mode: this generation writes no record at all.
    /// </summary>
    /// <remarks>
    ///     Deliberately <b>not</b> persisted in the settings file. Every other switch here is a preference; this one is a
    ///     promise that the number cannot be reconstructed later, and a persisted version of it would let a teacher who
    ///     forgot that it was on keep producing unrecorded numbers for weeks. A per-session choice makes the exception
    ///     explicit every time, which is the only honest shape for "do not keep evidence of this".
    /// </remarks>
    [ObservableProperty] private bool _confidentialMode;

    /// <summary>Whether the masked result is currently shown. Resets on every generation and on toggling the mode.</summary>
    [ObservableProperty] private bool _isResultRevealed;

    [ObservableProperty] private int _bitCount = 32;

    /// <summary>0 = decimal, 1 = hex, 2 = printable ASCII.</summary>
    [ObservableProperty] private int _outputFormat = 1;

    /// <summary>How many ASCII characters to produce; separate from the bit width because one character is one symbol.</summary>
    [ObservableProperty] private int _asciiCharCount = 16;

    /// <summary>Measured information content of the last output, shown beside it rather than kept in a log.</summary>
    [ObservableProperty] private string _entropyText = string.Empty;

    /// <summary>
    ///     A mask that reveals nothing at all, not even the value's length. Length is itself a hint for a decimal
    ///     output, and a fixed-width mask costs the reader nothing: the bit width is right there in the options.
    /// </summary>
    private const string ResultMask = "************";

    /// <summary>What the result area shows: the value, or the mask while confidential mode has it hidden.</summary>
    public string DisplayResult =>
        ConfidentialMode && !IsResultRevealed && !string.IsNullOrEmpty(ResultText) ? ResultMask : ResultText;

    /// <summary>Whether the result is currently covered, which is also when the reveal control is offered.</summary>
    public bool IsConfidentialMasked => ConfidentialMode && !IsResultRevealed && !string.IsNullOrEmpty(ResultText);

    public string RevealButtonText => IsResultRevealed ? Resources.C_HideResult : Resources.C_RevealResult;

    /// <summary>Concatenate several independent samples to fill an output wider than one seed.</summary>
    [ObservableProperty] private bool _allowEntropyConcat;
    [ObservableProperty] private bool _isSettingsLocked;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _verifiabilityNotice = string.Empty;

    /// <summary>Red warning styling is driven from here so the AXAML keeps a single selector to react to.</summary>
    [ObservableProperty] private bool _isVerifiabilityReduced;

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (IsBusy)
            return;

        long lower = 0;
        long upper = 0;
        RawCandidateSpec? space = null;

        if (UseValues)
        {
            // Candidates plus exclusions plus an optional predicate resolve to one interval space. The space — not the
            // teacher's text — is what the proof records, so a later parser change cannot alter an old record.
            var generatorFrom = string.IsNullOrWhiteSpace(GeneratorFromText) ? (long?)null : long.TryParse(GeneratorFromText, out var from) ? from : null;
            var generatorTo = string.IsNullOrWhiteSpace(GeneratorToText) ? (long?)null : long.TryParse(GeneratorToText, out var to) ? to : null;

            if (!RawCandidateSpec.TryCreate(
                    ValuesText,
                    ExclusionsText,
                    generatorFrom,
                    generatorTo,
                    PredicateText,
                    out space,
                    out var error))
            {
                StatusText = string.Format(Resources.M_ValuesInvalidFormat, error);
                CandidatesText = string.Empty;
                return;
            }

            lower = space!.Intervals[0].Start;
            upper = space.Intervals[^1].End;
            CandidatesText = string.Format(Resources.M_CandidatesFormat, space.Cardinality);

            // An exclusion that matches no candidate is otherwise a silent no-op, and "I excluded 7 and 7 still came up"
            // is then blamed on the draw. Resolving the same expression without exclusions is cheap and answers it.
            if (!string.IsNullOrWhiteSpace(ExclusionsText)
                && RawCandidateSpec.TryCreate(
                    ValuesText, string.Empty, generatorFrom, generatorTo, PredicateText,
                    out var withoutExclusions, out _)
                && withoutExclusions is not null
                && withoutExclusions.Cardinality == space.Cardinality)
            {
                CandidatesText += " · " + Resources.M_ExclusionsIneffective;
            }
        }
        else
        {
            CandidatesText = string.Empty;

            if (!TryReadRange(out lower, out upper))
            {
                StatusText = Resources.M_RangeInvalid;
                return;
            }
        }

        var selected = EntropySources.Where(option => option.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = Resources.M_NoEntropySource;
            return;
        }

        IsBusy = true;
        StatusText = Resources.M_Generating;

        try
        {
            var entropy = await CollectEntropyAsync(selected).ConfigureAwait(true);

            var seed = RawNumberSeedDerivation.Derive(entropy
                .Select(item => new RawNumberEntropyInput(item.SourceId, item.OutputValue))
                .ToList());

            if (UseRawEntropy)
            {
                // Advanced mode: show entropy instead of an in-range value. The record still binds the *seed*, because
                // the text is a deterministic function of it — storing the presentation too would only duplicate it.
                var bits = Math.Clamp(BitCount, 8, 16384);

                // One seed is seed.Length bytes wide. A wider output can either be expanded from that one seed (no extra
                // entropy, which the measured line says openly) or filled by concatenating further independent samples.
                var seedBits = Math.Max(8, seed.Length * 8);
                var rounds = AllowEntropyConcat
                    ? Math.Max(1, (int)Math.Ceiling(bits / (double)seedBits))
                    : 1;

                var material = new List<byte>(seed);
                var allEntropy = new List<RawNumberProofEntropy>(entropy);
                var sourceBits = EntropyBitsOf(entropy);
                var firstRoundBits = sourceBits;

                for (var round = 1; round < rounds; round++)
                {
                    var extra = await CollectEntropyAsync(selected).ConfigureAwait(true);
                    allEntropy.AddRange(extra);
                    sourceBits += EntropyBitsOf(extra);

                    material.AddRange(RawNumberSeedDerivation.Derive(extra
                        .Select(item => new RawNumberEntropyInput(item.SourceId, item.OutputValue))
                        .ToList()));
                }

                var combined = material.ToArray();

                var output = OutputFormat switch
                {
                    // Decimal is the same bits as one unsigned integer: long by design, never truncated.
                    0 => RawEntropyOutput.ToDecimal(RawEntropyOutput.Expand(combined, bits)),
                    2 => RawEntropyOutput.ToPrintableAscii(combined, Math.Clamp(AsciiCharCount, 1, 4096)),
                    _ => RawEntropyOutput.ToHex(RawEntropyOutput.Expand(combined, bits))
                };

                ResultText = output;

                var report = RawEntropyOutput.Analyze(output, sourceBits);
                EntropyText = string.Format(
                    Resources.M_EntropyFormat,
                    report.SymbolCount,
                    report.BitsPerSymbol,
                    report.TotalBits,
                    report.AvailableSourceBits);

                // Concatenation is only worth anything when each round brings a beat the previous one did not have. A
                // source that has not published a new pulse yet hands back the same one, which is the common case when a
                // teacher generates twice in a row — saying "no new entropy" is what keeps the switch from looking like
                // it did something.
                if (AllowEntropyConcat && rounds > 1 && sourceBits == firstRoundBits)
                    EntropyText += " " + Resources.M_ConcatNoGain;

                if (ConfidentialMode)
                {
                    // Nothing is written, not even the record that would carry the seed: the seed and the text are this
                    // process's only copy, and closing the app destroys both.
                    IsResultRevealed = false;
                    StatusText = Resources.M_ConfidentialGenerated;
                    return;
                }

                var advancedProof = new RawNumberProof
                {
                    Value = 0,
                    AdvancedOutput = true,
                    EntropyRoundSize = Math.Max(1, selected.Count),
                    Verifiable = !IsVerifiabilityReduced,
                    SeedDigest = RawNumberSeedDerivation.Digest(combined),
                    Entropy = allEntropy
                };

                var advancedPath = await _proofService.SaveAsync(advancedProof).ConfigureAwait(true);
                StatusText = string.Format(Resources.M_ProofSavedFormat, Path.GetFileName(advancedPath));
                return;
            }
            var count = Math.Clamp(Count, 1, 50);
            var results = space is not null
                ? space.SampleMany(seed, count)
                : RejectionSampler.SampleMany(lower, upper, seed, count);

            var proof = new RawNumberProof
            {
                LowerBound = lower,
                UpperBound = upper,
                Value = results[0],
                Results = results.Length > 1 ? [.. results] : null,
                Intervals = space?.Intervals.Select(interval => new[] { interval.Start, interval.End }).ToList(),
                Cardinality = space?.Cardinality ?? 0,
                Verifiable = !IsVerifiabilityReduced,
                SeedDigest = RawNumberSeedDerivation.Digest(seed),
                Entropy = entropy
            };

            // The record is written on every generation, not on request: a number nobody wrote down is exactly the
            // case this page exists to avoid, and a "save this one" button invites saving only the results someone
            // liked. Confidential mode is the deliberate exception, and it writes nothing at all.
            ResultText = string.Format(Resources.M_ResultFormat, string.Join("、", results));

            if (ConfidentialMode)
            {
                IsResultRevealed = false;
                StatusText = Resources.M_ConfidentialGenerated;
                return;
            }

            var path = await _proofService.SaveAsync(proof).ConfigureAwait(true);
            var verification = _proofService.Verify(proof);

            StatusText = verification.Succeeded
                ? string.Format(Resources.M_ProofSavedFormat, Path.GetFileName(path))
                : Resources.M_ProofVerifyFailed;
        }
        catch (Exception exception)
        {
            // A fetch failure must read as "nothing was produced" rather than as a number: showing a value derived
            // from fewer sources than the user picked would misrepresent where the entropy came from.
            _logger.LogWarning(exception, "裸随机数生成失败");
            StatusText = string.Format(Resources.M_GenerateFailedFormat, exception.Message);
            ResultText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    ///     Fetches every selected source in the order the user sees them and keeps each contribution verbatim. Local
    ///     CSPRNG material is recorded as hex as well, so the proof holds the exact bytes the derivation consumed even
    ///     though nothing external can re-anchor them.
    /// </summary>
    /// <summary>
    ///     Real entropy contributed by one collection round. The source's own published width is what counts, not the
    ///     width of the derived seed: a 512-bit NIST pulse still yields a 256-bit key, and the pulse is the part that
    ///     carries the entropy.
    /// </summary>
    private static int EntropyBitsOf(IEnumerable<RawNumberProofEntropy> entries)
    {
        var bits = 0;
        var seenBeats = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.Equals(entry.SourceId, RawNumberProofFormat.LocalSourceId, StringComparison.Ordinal))
            {
                // Every local call returns fresh bytes, so each one really is new material.
                bits += 256;
                continue;
            }

            // One pulse is one beat. Fetching the same published pulse again adds no unpredictability, and counting it
            // twice would advertise entropy the record does not actually contain.
            if (seenBeats.Add($"{entry.SourceId}:{entry.PulseIndex}"))
                bits += entry.OutputValue.Length / 2 * 8;
        }

        return bits;
    }

    private async Task<List<RawNumberProofEntropy>> CollectEntropyAsync(
        IReadOnlyList<EntropySourceOption> selected)
    {
        var entries = new List<RawNumberProofEntropy>();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        foreach (var option in selected)
        {
            if (option.Id == RawNumberProofFormat.LocalSourceId)
            {
                entries.Add(new RawNumberProofEntropy
                {
                    SourceId = RawNumberProofFormat.LocalSourceId,
                    OutputValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()
                });
                continue;
            }

            var source = BeaconSourceCatalog.All.First(candidate => candidate.Id == option.Id);
            var pulse = await _entropySourceClient
                .FetchLatestAsync(source, new Uri(source.Endpoint), cancellation.Token)
                .ConfigureAwait(true);

            entries.Add(new RawNumberProofEntropy
            {
                SourceId = source.Id,
                OutputValue = pulse.OutputValue,
                PulseIndex = pulse.PulseIndex,
                TimeStamp = pulse.TimeStamp,
                SignatureValue = pulse.SignatureValue,
                SignatureVerified = pulse.SignatureVerified
            });
        }

        return entries;
    }

    private bool TryReadRange(out long lower, out long upper)
    {
        lower = 0;
        upper = 0;

        return long.TryParse(LowerBoundText, out lower)
               && long.TryParse(UpperBoundText, out upper)
               && upper >= lower;
    }

    private static string DisplayNameOf(BeaconSource source) => source.Kind switch
    {
        BeaconSourceKind.NistBeaconV2 => Resources.C_SourceNist,
        BeaconSourceKind.DrandChain => Resources.C_SourceDrand,
        BeaconSourceKind.RandomOrgEntropy => Resources.C_SourceRandomOrg,
        _ => source.Id
    };

    /// <summary>
    ///     random.org publishes neither timeline nor signature over a fetched block, so a verifier cannot tell
    ///     which block was used or how many were fetched — the very property NIST and drand provide.
    /// </summary>
    private static bool IsVerifiable(BeaconSource source) => source.Kind is BeaconSourceKind.NistBeaconV2
        or BeaconSourceKind.DrandChain;

    private void OnEntropySourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EntropySourceOption.IsSelected))
            RefreshVerifiabilityNotice();
    }

    private void RefreshVerifiabilityNotice()
    {
        // Confidential mode outranks every source combination: the question "can this be re-checked" is already
        // answered no, because there is nothing on disk to re-check.
        if (ConfidentialMode)
        {
            IsVerifiabilityReduced = true;
            VerifiabilityNotice = Resources.M_ConfidentialNotice;
            return;
        }

        var selected = EntropySources.Where(option => option.IsSelected).ToList();

        IsVerifiabilityReduced = selected.Count != 1 || !selected[0].IsVerifiable;
        VerifiabilityNotice = selected.Count switch
        {
            0 => Resources.M_NoEntropySource,
            _ when !IsVerifiabilityReduced => Resources.M_VerifiableNotice,
            _ => Resources.M_NotVerifiableNotice
        };
    }

    partial void OnLowerBoundTextChanged(string value) => PersistRange();

    partial void OnUpperBoundTextChanged(string value) => PersistRange();

    partial void OnUseValuesChanged(bool value)
    {
        if (_isLoading)
            return;

        _configHandler.Data.MoreSettings.RawRandomUseValues = value;
        _configHandler.Save();
    }

    partial void OnValuesTextChanged(string value)
    {
        if (_isLoading)
            return;

        _configHandler.Data.MoreSettings.RawRandomValuesText = value ?? string.Empty;
        _configHandler.Save();
    }

    partial void OnExclusionsTextChanged(string value) =>
        PersistFilterText(value, text => _configHandler.Data.MoreSettings.RawRandomExclusionsText = text);

    partial void OnGeneratorFromTextChanged(string value) =>
        PersistFilterText(value, text => _configHandler.Data.MoreSettings.RawRandomGeneratorFromText = text);

    partial void OnGeneratorToTextChanged(string value) =>
        PersistFilterText(value, text => _configHandler.Data.MoreSettings.RawRandomGeneratorToText = text);

    partial void OnPredicateTextChanged(string value) =>
        PersistFilterText(value, text => _configHandler.Data.MoreSettings.RawRandomPredicateText = text);

    [RelayCommand]
    private void ToggleResultVisibility() => IsResultRevealed = !IsResultRevealed;

    partial void OnResultTextChanged(string value) => NotifyMaskChanged();

    partial void OnIsResultRevealedChanged(bool value) => NotifyMaskChanged();

    partial void OnConfidentialModeChanged(bool value)
    {
        // A freshly enabled mode must never leave the previous result on screen: the user asked for a covered value, not
        // for whatever was already there to stay visible.
        IsResultRevealed = false;
        NotifyMaskChanged();
        RefreshVerifiabilityNotice();
    }

    private void NotifyMaskChanged()
    {
        OnPropertyChanged(nameof(DisplayResult));
        OnPropertyChanged(nameof(IsConfidentialMasked));
        OnPropertyChanged(nameof(RevealButtonText));
    }

    /// <summary>
    ///     One save path for the four filter strings. They behave identically and all four have to survive a restart, so
    ///     a separate copy per property would be four places to forget the <see cref="_isLoading" /> guard in — which is
    ///     exactly how the constructor's own assignments would get written back over the saved values.
    /// </summary>
    private void PersistFilterText(string? value, Action<string> assign)
    {
        if (_isLoading)
            return;

        assign(value ?? string.Empty);
        _configHandler.Save();
    }

    /// <summary>
    ///     The security settings live in the encrypted store, which is <c>internal</c> to this assembly — so it is
    ///     resolved here rather than injected: exposing it in a public constructor makes this view model's
    ///     accessibility inconsistent (CS0051), and the store cannot be widened just for this page.
    /// </summary>
    private static SecuritySettingsStore? SecuritySettings => IAppHost.TryGetService<SecuritySettingsStore>();

    /// <summary>
    ///     Re-reads the lock from the encrypted security settings. It is deliberately not a freely settable
    ///     preference on this view model: the only way to change it is <see cref="SetLockedAsync" />, which goes
    ///     through the password-gated boundary.
    /// </summary>
    public void RefreshLockState() =>
        IsSettingsLocked = SecuritySettings?.Data.ProtectRawRandomSettings ?? false;

    /// <summary>
    ///     Turns the range/entropy lock on or off through <c>ISecurityService</c>. Unlocking counts as a protection
    ///     downgrade, so it always needs a fresh password: neither the settings-window sudo nor the global timer
    ///     can satisfy it, which is the whole point of locking something with a security password.
    /// </summary>
    public async Task<bool> SetLockedAsync(TopLevel? root, bool locked)
    {
        if (SecuritySettings is not { } store)
            return false;

        // Without protection there is nothing to verify against, and the lock is a plain preference: sending the user
        // to a password prompt that cannot be satisfied would make the switch unusable on a fresh install.
        if (!store.Data.SecurityEnabled)
        {
            store.Data.ProtectRawRandomSettings = locked;
            store.Save();
            RefreshLockState();
            StatusText = locked ? Resources.M_Locked : Resources.M_Unlocked;
            return true;
        }

        if (root is null)
            return false;

        var updated = await _securityService
            .UpdateSecuritySettingsAsync(root, () => store.Data.ProtectRawRandomSettings = locked)
            .ConfigureAwait(true);

        RefreshLockState();
        StatusText = updated
            ? locked ? Resources.M_Locked : Resources.M_Unlocked
            : Resources.M_UnlockCancelled;

        return updated;
    }

    /// <summary>
    ///     Saves both bounds together on every edit, but only when both parse and the range is consistent: a
    ///     half-typed "1" on the way to "100" must not overwrite the stored upper bound with a smaller one.
    /// </summary>
    private void PersistRange()
    {
        if (_isLoading)
            return;

        if (!TryReadRange(out var lower, out var upper))
            return;

        var more = _configHandler.Data.MoreSettings;
        if (more.RawRandomLowerBound == lower && more.RawRandomUpperBound == upper)
            return;

        more.RawRandomLowerBound = lower;
        more.RawRandomUpperBound = upper;
        _configHandler.Save();
    }
}

/// <summary>One selectable entropy source on the raw-number page.</summary>
public partial class EntropySourceOption(string id, string displayName, bool isVerifiable) : ObservableObject
{
    public string Id { get; } = id;

    public string DisplayName { get; } = displayName;

    /// <summary>False for random.org and the local CSPRNG, which cannot be re-checked after the fact.</summary>
    public bool IsVerifiable { get; } = isVerifiable;

    [ObservableProperty] private bool _isSelected;
}
