using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Services.Ipc;

namespace SecRandom.Services.Desktop;

public static class ProtocolActivation
{
    private static string? _startupUri;

    public static void SetStartupArguments(IReadOnlyList<string> arguments)
    {
        _startupUri = ExtractUri(arguments);
    }

    public static string? ConsumeStartupUri()
    {
        var uri = _startupUri;
        _startupUri = null;
        return uri;
    }

    private static string? ExtractUri(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "--url", StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Count)
                return Validate(arguments[index + 1]);

            var uri = Validate(argument);
            if (uri is not null)
                return uri;
        }

        return null;
    }

    private static string? Validate(string value)
    {
        // Both the fork's own scheme and upstream's compatibility scheme must survive here: dropping
        // `verirandom://` leaves the second instance with no URI to forward, so it falls back to "show the
        // main window" and can surface the duplicate-instance prompt instead of opening the link.
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && ProtocolRequestParser.IsSupportedScheme(uri.Scheme)
               && !value.Contains('\r')
               && !value.Contains('\n')
            ? uri.AbsoluteUri
            : null;
    }
}
