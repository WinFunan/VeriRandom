using System;

namespace SecRandom.Core.Services.Security.Fido2;

/// <summary>
///     WebAuthn运送的所有二进制字段（challenge、credential id、公钥、签名）都是不带填充的 base64url，
///     不是普通 base64。用 Convert.ToBase64String 直接比较会因 <c>+</c>/<c>/</c> 与 <c>-</c>/<c>_</c>
///     的差别以及填充符长度不同而失配，所以编码与比较统一走这里。
/// </summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TryDecode(value, out var data))
            throw new FormatException("Value is not valid base64url.");
        return data;
    }

    public static bool TryDecode(string? value, out byte[] data)
    {
        data = [];
        if (string.IsNullOrEmpty(value))
            return false;

        var normalized = value.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
            case 1: return false;
        }

        try
        {
            data = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
