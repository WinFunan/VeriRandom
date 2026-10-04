using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SecRandom.Core.Models;

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
    /// <summary>Identifies the covered field set. Adding or removing a covered setting must change this id.</summary>
    public const string ScopeId = "verirandom-config-policy/v1";

    /// <summary>
    ///     Security fields that describe the integrity check itself. Covering them would make switching the
    ///     check on or off invalidate the very signature that verifies it.
    /// </summary>
    private static readonly string[] SelfReferentialFieldNames =
    [
        "SettingsIntegrityCheckEnabled",
        "SettingsIntegrityAction",
        "SettingsIntegrityRestoreSource",
        "ConfigIntegrityMode"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] Compute(MainConfigModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var scope = new JsonObject
        {
            ["general"] = new JsonObject
            {
                ["securitySettings"] = Project(model.SecuritySettings, SelfReferentialFieldNames),
                ["verification"] = Project(model.General.Verification),
                ["privacySettings"] = Project(model.General.PrivacySettings)
            },
            ["linkageSettings"] = Project(model.LinkageSettings),
            ["fairDrawSettings"] = Project(model.FairDrawSettings),
            ["lotteryEnabled"] = JsonValue.Create(model.MoreSettings.LotteryEnabled)
        };

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
