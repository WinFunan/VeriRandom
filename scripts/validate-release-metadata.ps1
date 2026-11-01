# Release channel metadata gate.
#
# Update clients resolve a channel to a release tag from metadata.yaml and then reject the
# release manifest when its tag/channel do not match (the desktop UpdateCenterService and the
# mobile MobileUpdateService), so a stale channel entry publishes an update nobody can reach.
# The release tag must therefore already carry a metadata.yaml advertising itself. Run this
# before the build jobs so a mismatch fails in seconds instead of after a full build.
#
# Tag shapes this repository releases (the rule itself lives in SecRandom.Core/Helpers/ReleaseVersion.cs):
#   v3.1.5            upstream-synced stable release        -> channel release
#   v3.1.5 Mu 1       downstream rebuild of v3.1.5          -> channel release (NOT a preview: it is
#                     a finished build of the same upstream version, and "Mu 2" must reach users of
#                     "Mu 1"). The tagged commit must point channels.release at this exact tag.
#   v3.0.0-beta.1     upstream preview                       -> channel beta
#   v3.0.0-alpha.2    upstream preview                       -> channel alpha
[CmdletBinding()]
param(
    [string]$Tag = $env:RELEASE_TAG,
    [string]$MetadataPath = 'metadata.yaml'
)

if ([string]::IsNullOrWhiteSpace($Tag)) {
    throw 'A release tag is required. Set the RELEASE_TAG environment variable or pass -Tag.'
}

# "Mu" followed by its ordinal, spelled with any separator: "v3.1.5 Mu 1", "v3.1.5-Mu.1", "v3.1.5_Mu_1".
$downstreamPattern = '(?i)(?:^|[\s\-_+])(?:mu)[\s.\-_]?\d+$'

$channel = if ($Tag -match '(?i)-alpha(?:\.|$)') { 'alpha' }
    elseif ($Tag -match '(?i)-beta(?:\.|$)') { 'beta' }
    elseif ($Tag -match $downstreamPattern) { 'release' }
    elseif ($Tag -match '-') { throw "Unsupported prerelease tag: $Tag" }
    else { 'release' }

if (-not (Test-Path -LiteralPath $MetadataPath -PathType Leaf)) {
    throw "Release metadata file '$MetadataPath' was not found."
}

$metadata = Get-Content -LiteralPath $MetadataPath -Raw

# The tag is compared verbatim, so a downstream tag keeps its space: allow anything up to the line end
# instead of assuming a single token.
$channelMatch = [regex]::Match($metadata, "(?ms)^  ${channel}:\s*\r?\n\s+tag:\s*(?<tag>[^\r\n]+?)\s*$")
if (-not $channelMatch.Success -or $channelMatch.Groups['tag'].Value -ne $Tag) {
    throw "metadata.yaml channel '$channel' must point to release tag $Tag."
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "channel=$channel"
}

Write-Host "metadata.yaml channel '$channel' -> $Tag"
