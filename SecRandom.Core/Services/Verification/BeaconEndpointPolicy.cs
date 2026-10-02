namespace SecRandom.Core.Services.Verification;

/// <summary>
///     Validates and normalizes the beacon endpoint. The default is the official NIST Beacon v2 base;
///     a mirror is allowed so the feature stays usable where the official host is unreachable, but remote
///     plain HTTP is rejected and the proof records the raw signed pulse, so a substituted pulse remains
///     auditable after the fact.
/// </summary>
public static class BeaconEndpointPolicy
{
    public const string DefaultEndpoint = "https://beacon.nist.gov/beacon/2.0/";

    public static Uri Normalize(string? endpoint)
    {
        var value = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new FormatException("Beacon endpoint must be an absolute URL.");

        var isLoopbackHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !isLoopbackHttp)
            throw new FormatException("Beacon endpoint must use HTTPS (HTTP is allowed only for loopback).");

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        if (!builder.Path.EndsWith('/'))
            builder.Path += "/";

        return builder.Uri;
    }
}
