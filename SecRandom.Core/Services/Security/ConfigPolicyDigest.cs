using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Services.Security;

/// <summary>
///     The canonical projection of the settings a signed policy covers, and its digest.
///     Only policy-level settings are covered. Everything the draw machine rewrites during ordinary use —
///     window geometry, recent lists, timer presets, histories, statistics — is deliberately excluded,
///     because a signature over those would fail on the first normal interaction and the feature would be
///     unusable rather than protective.
/// </summary>
public static class ConfigPolicyDigest
{
    /// <summary>
    ///     Identifies the covered field set. Adding or removing a covered setting must change this id.
    ///     v3 added the beacon-prefetch switch to the verification subtree, which the policy projects wholesale;
    ///     v4 added the periodic beacon-prefetch switch, and v5 the entropy-source id, to the same subtree;
    ///     v6 added the four per-draw-type setting trees, which decide how a draw samples and were previously outside
    ///     the signature — a signed policy that ignored "默认抽取设置" was not locking what it claimed to lock.
    /// </summary>
    public const string ScopeId = "verirandom-config-policy/v6";

    /// <summary>
    ///     Security fields that describe the integrity check itself. Covering them would make switching the
    ///     check on or off invalidate the very signature that verifies it. The roster-coverage switch belongs
    ///     here too: whether rosters are covered is already recorded by the <c>rosters</c> key's presence, so
    ///     hashing the switch as well would only lock the operator out of a control they must be able to flip
    ///     before re-signing.
    /// </summary>
    private static readonly string[] SelfReferentialFieldNames =
    [
        "SettingsIntegrityCheckEnabled",
        "SettingsIntegrityAction",
        "SettingsIntegrityRestoreSource",
        "ConfigIntegrityMode",
        "ConfigIntegrityIncludeRosters"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    ///     Computes the digest of the policy this machine is running.
    /// </summary>
    /// <param name="securitySettings">
    ///     The security settings, which deliberately do <em>not</em> come from <paramref name="model" />: they
    ///     live in the encrypted security-settings store (<c>data/config/security/settings.json</c>) rather than
    ///     in <c>settings.json</c>, so whoever owns both sources passes them in side by side.
    /// </param>
    /// <param name="rosterDigest">
    ///     Digest of the roster/list files, or null when the operator did not opt into covering them. The
    ///     member's <em>presence</em> is what records that choice: a signature made with rosters covered must
    ///     not verify against a policy that no longer covers them, and turning the option on must invalidate
    ///     the previous signature rather than silently widening the scope.
    /// </param>
    public static byte[] Compute(MainConfigModel model, SecuritySettingsConfig securitySettings, string? rosterDigest = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(securitySettings);

        var scope = new JsonObject
        {
            ["general"] = new JsonObject
            {
                ["securitySettings"] = Project(securitySettings, SelfReferentialFieldNames),
                ["verification"] = Project(model.General.Verification),
                ["privacySettings"] = Project(model.General.PrivacySettings)
            },
            ["linkageSettings"] = Project(model.LinkageSettings),
            ["fairDrawSettings"] = Project(model.FairDrawSettings),
            // The per-draw-type trees hold repeat mode, sample count, animation and the quick-draw list; leaving them
            // out let a signed policy vouch for a draw it did not actually constrain.
            ["defaultDrawSettings"] = Project(model.DefaultDrawSettings),
            ["rollCallSettings"] = Project(model.RollCallSettings),
            ["quickDrawSettings"] = Project(model.QuickDrawSettings),
            ["lotterySettings"] = Project(model.LotterySettings),
            ["lotteryEnabled"] = JsonValue.Create(model.MoreSettings.LotteryEnabled)
        };

        if (!string.IsNullOrWhiteSpace(rosterDigest))
            scope["rosters"] = JsonValue.Create(rosterDigest);

        return SHA256.HashData(Encoding.UTF8.GetBytes(scope.ToJsonString(SerializerOptions)));
    }

    /// <summary>Serializes one sub-config and canonicalizes it.</summary>
    private static JsonNode? Project<T>(T value, IEnumerable<string>? excludedMembers = null)
    {
        var node = JsonSerializer.SerializeToNode(value, SerializerOptions);
        if (node is null)
            return null;

        var excluded = excludedMembers?.ToHashSet(StringComparer.Ordinal);
        return Canonicalize(node, excluded);
    }

    /// <summary>
    ///     Sorts object members by name and drops excluded ones, so reordering a declaration is not a change
    ///     of policy. Every returned node is freshly built, because a node cannot be attached to two parents.
    /// </summary>
    private static JsonNode? Canonicalize(JsonNode? node, HashSet<string>? excluded)
    {
        switch (node)
        {
            case JsonObject source:
            {
                var result = new JsonObject();
                foreach (var pair in source
                             .Where(pair => excluded is null || !excluded.Contains(pair.Key))
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    result[pair.Key] = Canonicalize(pair.Value, excluded);
                }

                return result;
            }

            case JsonArray source:
            {
                var result = new JsonArray();
                foreach (var item in source)
                    result.Add(Canonicalize(item, excluded));

                return result;
            }

            default:
                return node?.DeepClone();
        }
    }
}
