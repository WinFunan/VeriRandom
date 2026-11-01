# Converts a release tag into a SemVer-safe version string.
#
# The display version stays the git tag itself: the app reads GlobalConstants.Version from the generated
# informational version, so a downstream build tagged "v3.1.5 Mu 1" still shows "v3.1.5 Mu 1". This script is
# only for the places that cannot carry a space or a bare "Mu": -p:Version, NuGet package versions, and
# artifact file names. The rule here must stay identical to SecRandom.Core/Helpers/ReleaseVersion.cs.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag
)

$version = $Tag.Trim().TrimStart('v', 'V')

# A downstream revision: "Mu" preceded by a separator and followed by the ordinal, at the very end.
$mu = [regex]::Match($version, '(?i)(?:^|[\s\-_+])(?:mu)[\s.\-_]?(?<n>\d+)$')
if ($mu.Success) {
    $core = $version.Substring(0, $mu.Index).TrimEnd([char[]]" `t-_+.")
    if (-not [string]::IsNullOrWhiteSpace($core)) {
        # "-mu.N" is a valid SemVer pre-release, which is what MSBuild and NuGet accept.
        $version = "$core-mu.$($mu.Groups['n'].Value)"
    }
}

Write-Output $version
