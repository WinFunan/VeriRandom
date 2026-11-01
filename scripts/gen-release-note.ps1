# Renders the release body from the tag changelog plus a SHA256 table.
#
# There is deliberately no download table: this fork has no relay/mirror service, so a mirror column
# would be a dead link, and the release page itself already lists every asset under "Assets".
#
# Default mode (build_publish.yml) hashes the freshly built files in ./artifacts/release/output.
# -InventoryPath describes release assets by name + SHA256 instead, which is what build_ios.yml needs
# when it refreshes the notes of a release that is already published. -BodyPath keeps the body that is
# already live and replaces only the generated tables in it, so hand written prose survives.
#
# Two traps this script is written around, both invisible until someone reads the generated note:
#   * The ChangeLog is split on "\r\n|\n|\r" here instead of letting Get-Content split it. This
#     repository's own files are LF-only while upstream's are CRLF, and Get-Content's own splitting
#     joined many of those LF breaks into single long lines, which silently merged two changelog
#     bullets into one line.
#   * Paths stay relative and go through Get-Content/Set-Content, because those honour PowerShell's
#     current location. System.IO would resolve the same relative path against the process working
#     directory instead, which Set-Location does not change.
# An explicit -Encoding utf8 is likewise deliberate: the default differs between Windows PowerShell and
# pwsh, and the default would mangle the Chinese text of a UTF-8 file with no BOM.
#
# This file is saved as UTF-8 *with* a BOM, unlike every other script in this repository, because it is
# the only one that contains Chinese literals: Windows PowerShell 5.1 decodes a no-BOM script as ANSI,
# which turns those literals into mojibake inside the generated note. pwsh reads UTF-8 either way, so the
# BOM exists only to protect someone running this script locally instead of through CI.
[CmdletBinding()]
param(
    [string]$InventoryPath = '',
    [string]$BodyPath = '',
    [string]$OutFile = './release-note.md'
)

# Still required even though the body no longer builds download URLs: the release workflows and the iOS
# backfill path all set it, and a caller that forgets it is better rejected here than silently producing
# a note without repository context.
$repo = $env:repoName
$tag = $env:tagName
if ([string]::IsNullOrWhiteSpace($repo)) {
    throw "Environment variable 'repoName' is required."
}
if ([string]::IsNullOrWhiteSpace($tag)) {
    throw "Environment variable 'tagName' is required."
}

$changelogPath = "./CHANGELOG/v3/${tag}/CHANGELOG.md"
$releaseNotePath = $OutFile
$outDir = "./artifacts/release/output"

if (-not [string]::IsNullOrWhiteSpace($InventoryPath)) {
    if (-not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) {
        throw "Inventory file not found: $InventoryPath"
    }

    $inventory = Get-Content -LiteralPath $InventoryPath -Raw -Encoding utf8 | ConvertFrom-Json
    $inventoryEntries = foreach ($entry in @($inventory)) {
        if ([string]::IsNullOrWhiteSpace($entry.name)) {
            throw "Every inventory entry needs a file name."
        }
        if ([string]::IsNullOrWhiteSpace($entry.sha256)) {
            throw "Inventory entry '$($entry.name)' has no SHA256 digest."
        }
        [pscustomobject]@{ Name = $entry.name; Sha256 = $entry.sha256.ToUpperInvariant() }
    }
    $files = @($inventoryEntries | Sort-Object Name)

    if (-not $files) {
        throw "No assets found in $InventoryPath"
    }
} else {
    if (-not (Test-Path $outDir)) {
        throw "Output directory not found: $outDir"
    }

    $localFiles = Get-ChildItem -Path $outDir -File | Sort-Object Name
    if (-not $localFiles) {
        throw "No files found in $outDir"
    }

    $files = foreach ($file in $localFiles) {
        [pscustomobject]@{ Name = $file.Name; Sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash }
    }
}

$md5Summary = @"
> [!important]
> 请在下方 **Assets** 中下载文件；下载后请核对 SHA256。

<details>
<summary>展开 SHA256 </summary>

| 文件名 | SHA256 |
| --- | --- |
"@

foreach ($file in $files) {
    $md5Summary += "`n| $($file.Name) | ``$($file.Sha256)`` |"
}
$md5Summary += "`n`n</details>"

# Fork: source the body from this fork's own ChangeLog.md section first. CHANGELOG/ is upstream's tree, so
# reading it would publish upstream's release notes (and its banner and links) under this fork's release,
# describing features this fork never shipped. The upstream body file stays as the fallback.
$forkSection = ''
if (Test-Path "./ChangeLog.md") {
    $lines = (Get-Content "./ChangeLog.md" -Raw -Encoding utf8) -split "\r\n|\n|\r"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match ("^#\s+" + [regex]::Escape($tag) + "(\s|$)")) { $start = $i; break }
    }

    if ($start -ge 0) {
        $end = $lines.Count
        for ($i = $start + 1; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^#\s') { $end = $i; break }
        }

        $forkSection = ($lines[$start..($end - 1)] -join "`n").TrimEnd()
    }
}

$body = $forkSection
if ([string]::IsNullOrWhiteSpace($body) -and -not [string]::IsNullOrWhiteSpace($BodyPath)) {
    if (-not (Test-Path -LiteralPath $BodyPath -PathType Leaf)) {
        throw "Release body file not found: $BodyPath"
    }

    $body = Get-Content -LiteralPath $BodyPath -Raw -Encoding utf8
}

$changelog = if (-not [string]::IsNullOrWhiteSpace($body)) {
    # Drop a download table the body already carries; a body written before this fork dropped its own
    # download table would otherwise duplicate one.
    $tableMarker = $body.LastIndexOf('**下载链接**')
    if ($tableMarker -ge 0) {
        $body.Substring(0, $tableMarker).TrimEnd()
    } else {
        $body.TrimEnd()
    }
} elseif (Test-Path $changelogPath) {
    Get-Content $changelogPath -Raw -Encoding utf8
} else {
    "- 发布说明待补充。`n---`n"
}

$fullContent = "$changelog`n`n$md5Summary"
# utf8 (not utf8NoBOM): the release note is consumed as Markdown and a BOM would render as a stray
# character. pwsh writes UTF-8 without a BOM for this spelling; Windows PowerShell 5.1 adds one, which
# only affects a maintainer running this locally. Keeping the portable spelling matters more, because
# utf8NoBOM does not exist in 5.1 and would make the script unparseable there.
Set-Content -Path $releaseNotePath -Value $fullContent -Encoding utf8

Write-Host "Release Note generated with $(@($files).Count) asset(s)"
