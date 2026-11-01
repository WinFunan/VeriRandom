using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;

namespace SecRandom.Services.Verification;

/// <summary>
///     Owns the random.org API key this installation uses for signed random numbers.
/// </summary>
/// <remarks>
///     The key never goes into <c>settings.json</c>, an archive, a log line, or the Android documents provider: it is
///     a credential that grants quota on the account that applied for it, so it lives beside the other private stores
///     under <c>data/config/randomorg/</c> and is written by atomic replace like every other file this app persists.
///     The user applies for the key themselves in their own browser — this class only stores what they paste back.
/// </remarks>
public sealed class RandomOrgCredentialStore(ILogger<RandomOrgCredentialStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static string FilePath => Utils.GetFilePath("config", "randomorg", "key.json");

    /// <summary>The stored key, or null when none was configured or the file cannot be read.</summary>
    public string? Load()
    {
        var path = FilePath;
        try
        {
            if (!File.Exists(path))
                return null;

            var document = JsonSerializer.Deserialize<KeyDocument>(File.ReadAllText(path), JsonOptions);
            return string.IsNullOrWhiteSpace(document?.ApiKey) ? null : document.ApiKey.Trim();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            // A stored key that cannot be read simply means "not configured": the draw falls back to the unsigned
            // endpoint, and this line is the only trace, because the key itself must never reach a log.
            logger.LogWarning(exception, "读取 random.org API 密钥失败，将按未配置处理。");
            return null;
        }
    }

    public void Save(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var path = FilePath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new KeyDocument { ApiKey = apiKey.Trim() }, JsonOptions));
        File.Move(temporaryPath, path, true);

        logger.LogInformation("已保存 random.org API 密钥（长度 {Length}），未记录密钥本身。", apiKey.Trim().Length);
    }

    private sealed class KeyDocument
    {
        public string? ApiKey { get; set; }
    }
}
