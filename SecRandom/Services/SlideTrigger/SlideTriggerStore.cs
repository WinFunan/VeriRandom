using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.SlideTrigger;
using SecRandom.Core.Services.SlideTrigger;
using SecRandom.Shared;

namespace SecRandom.Services.SlideTrigger;

/// <summary>
///     Owns the marker definitions a teacher has created: what each id means, and which session it already fired in.
/// </summary>
/// <remarks>
///     <para>
///         Stored in <c>data/config/slide-trigger/triggers.json</c> rather than in <c>settings.json</c>. Markers are a
///         per-machine classroom setup with their own lifecycle (create, edit, delete, roll the session), and mixing
///         them into the main settings tree would put a growing list into every settings-diff and every policy
///         projection. They carry no secret — an id plus draw parameters — so they need no exclusion from archives.
///     </para>
///     <para>
///         The session string is what makes a marker reusable across lessons. Consumption is recorded against it, so
///         roll-call slide 12 fires once per lesson rather than once per installation.
///     </para>
/// </remarks>
public sealed class SlideTriggerStore(ILogger<SlideTriggerStore> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private SlideTriggerCatalog? _catalog;

    public IReadOnlyList<SlideTriggerDefinition> Triggers => Catalog.Triggers;

    /// <summary>The current lesson's session id; markers already fired in it are ignored.</summary>
    public string Session => Catalog.Session;

    /// <summary>
    ///     The marker a global shortcut copies, or null when none was picked. Kept as an id rather than a copy so a
    ///     later edit or deletion is reflected without another bookkeeping step.
    /// </summary>
    public SlideTriggerDefinition? QuickMarker =>
        string.IsNullOrWhiteSpace(Catalog.QuickMarkerId) ? null : Find(Catalog.QuickMarkerId);

    /// <summary>Points the shortcut at one marker; an unknown id clears the selection instead of persisting a dead one.</summary>
    public void SetQuickMarker(string? id)
    {
        var resolved = string.IsNullOrWhiteSpace(id) ? null : Find(id)?.Id;
        if (string.Equals(Catalog.QuickMarkerId, resolved, StringComparison.Ordinal))
            return;

        Catalog.QuickMarkerId = resolved;
        Save();
    }

    public SlideTriggerDefinition? Find(string id) =>
        Catalog.Triggers.FirstOrDefault(trigger => string.Equals(trigger.Id, id, StringComparison.Ordinal));

    public SlideTriggerDefinition Create(string name, SlideTriggerParameters parameters, DateTimeOffset? expiresAt)
    {
        var id = SlideTriggerId.New();
        var normalized = (parameters ?? new SlideTriggerParameters()).Normalize();

        var definition = new SlideTriggerDefinition
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "未命名标记" : name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            Parameters = normalized,
            ExpiresAt = expiresAt,
            MarkerText = SlideTriggerPayload.Encode(normalized, id, expiresAt)
        };

        Catalog.Triggers.Add(definition);

        // A marker is made right before it is used, so the shortcut points at it without a second click. The page still
        // lets the teacher point it somewhere else.
        Catalog.QuickMarkerId = definition.Id;

        Save();
        return definition;
    }

    /// <summary>
    ///     Applies an edited definition and re-encodes its marker text. Parameters are re-normalized here rather than at
    ///     every call site, so a page cannot persist an out-of-range count by forgetting to validate. The id stays the
    ///     same, which is what keeps the per-session consumption record meaningful across edits.
    /// </summary>
    public void Update(SlideTriggerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var existing = Find(definition.Id);
        if (existing is null)
            return;

        var normalized = (definition.Parameters ?? existing.Parameters).Normalize();

        // Encode before touching the stored record: it throws on an over-long list name, and a record that is already
        // half-updated but whose marker text still describes the old parameters is worse than a refused edit.
        var markerText = SlideTriggerPayload.Encode(normalized, existing.Id, definition.ExpiresAt);

        existing.Name = string.IsNullOrWhiteSpace(definition.Name) ? existing.Name : definition.Name.Trim();
        existing.Parameters = normalized;
        existing.ExpiresAt = definition.ExpiresAt;

        // The deck still holds the old PNG until the teacher replaces it; re-encoding here is what makes this record
        // say what the *new* marker would contain, and the page must therefore offer to export it again.
        existing.MarkerText = markerText;

        Save();
    }

    public void Remove(string id)
    {
        var existing = Find(id);
        if (existing is null)
            return;

        Catalog.Triggers.Remove(existing);

        // A dangling id would make the shortcut silently do nothing, which is indistinguishable from a broken hotkey.
        if (string.Equals(Catalog.QuickMarkerId, id, StringComparison.Ordinal))
            Catalog.QuickMarkerId = null;

        Save();
    }

    /// <summary>
    ///     Records that the marker fired. Persisted immediately: a crash right after a draw must not let the same slide
    ///     fire a second draw when the app comes back.
    /// </summary>
    public void MarkConsumed(SlideTriggerDefinition definition, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(definition);

        definition.MarkConsumed(Session, at);
        Save();
    }

    /// <summary>
    ///     Rolls the session so every marker can fire again in the next lesson. Deliberately a command, never
    ///     automatic: "the lesson ended" is not something the app can infer, and silently re-arming markers mid-lesson
    ///     would make a slide fire twice.
    /// </summary>
    public string StartNewSession()
    {
        Catalog.Session = NewSessionId();
        Save();
        return Catalog.Session;
    }

    private SlideTriggerCatalog Catalog => _catalog ??= Load();

    private SlideTriggerCatalog Load()
    {
        var path = CatalogPath;

        if (File.Exists(path))
        {
            try
            {
                var catalog = JsonSerializer.Deserialize<SlideTriggerCatalog>(
                    File.ReadAllText(path),
                    SerializerOptions);

                if (catalog is not null)
                {
                    // A catalog without a session would make every marker look unconsumed on every launch, so a
                    // missing value rolls a fresh one instead of being accepted.
                    if (string.IsNullOrWhiteSpace(catalog.Session))
                        catalog.Session = NewSessionId();

                    if (catalog.Triggers is null)
                        catalog.Triggers = [];

                    return catalog;
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                logger.LogWarning(exception, "无法读取课件标记配置，将按空配置继续：{Path}", path);
            }
        }

        return new SlideTriggerCatalog { Session = NewSessionId(), Triggers = [] };
    }

    private void Save()
    {
        try
        {
            var path = CatalogPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Catalog, SerializerOptions));
            File.Move(temporary, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "无法写入课件标记配置：{Path}", CatalogPath);
        }
    }

    private static string CatalogPath => Utils.GetFilePath("config", "slide-trigger", "triggers.json");

    private static string NewSessionId() => DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
}

/// <summary>On-disk shape. Nullable list because a hand-edited file may omit it.</summary>
internal sealed class SlideTriggerCatalog
{
    public string Session { get; set; } = string.Empty;

    /// <summary>Id of the marker the global shortcut copies; null when none was picked.</summary>
    public string? QuickMarkerId { get; set; }

    public List<SlideTriggerDefinition> Triggers { get; set; } = [];
}
