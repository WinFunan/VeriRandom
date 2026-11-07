<#
.SYNOPSIS
    Checks out the draw-algorithm tier at a pinned ref into vendors/NewAlgorithm.

.DESCRIPTION
    Continuous integration does the same thing with actions/checkout, so this script is the local equivalent: a
    developer machine can build without initialising a submodule. The directory is git-ignored and referenced by
    SecRandom.Core as a project reference, which keeps the algorithm in its own repository (history, tags, releases)
    instead of copying its source into this one.

    The ref is a tag on purpose: both CI and this script then depend on one published version of the other repository
    rather than on whatever its default branch happens to contain.
#>
[CmdletBinding()]
param(
    [string]$Ref = 'v0.1.0',
    [string]$Repository = 'https://github.com/WinFunan/VeriRandom.NewAlgorithm.git',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot 'vendors/NewAlgorithm'
$project = Join-Path $target 'SecRandom.NewAlgorithm/SecRandom.NewAlgorithm.csproj'

if ((Test-Path $project) -and -not $Force) {
    $current = (& git -C $target rev-parse --short HEAD 2>$null)
    Write-Host "NewAlgorithm is already present at $current. Pass -Force to re-checkout $Ref."
    return
}

if (Test-Path $target) {
    Remove-Item $target -Recurse -Force
}

New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
& git clone --depth 1 --branch $Ref $Repository $target
if ($LASTEXITCODE -ne 0) {
    throw "git clone failed for $Repository at ref '$Ref'. Push that tag to the algorithm repository first."
}

Write-Host "NewAlgorithm checked out at $Ref."
