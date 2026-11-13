using System;

namespace SecRandom.Core.Services.Security.Fido2;

/// <summary>
///     Every ceremony this app runs happens on a loopback page, so the browser records an origin like
///     <c>http://localhost:51234</c>. The port is chosen per ceremony, which is why an origin cannot be pinned
///     to a constant — but the shape can, and that shape is what tells a real in-app ceremony apart from an
///     assertion produced by some public site.
/// </summary>
public static class WebAuthnOriginPolicy
{
    public static bool IsLoopback(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        // A path, query, or fragment never belongs to an origin, so its presence means this string was not
        // produced by clientDataJSON for a page root.
        if (uri.AbsolutePath is not ("" or "/") || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        if (uri.Host is "localhost")
            return true;

        return uri.Host is "127.0.0.1" or "::1";
    }
}
