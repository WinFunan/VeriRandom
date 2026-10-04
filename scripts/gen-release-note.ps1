$repo = $env:repoName
$tag = $env:tagName
if ([string]::IsNullOrWhiteSpace($repo)) {
    throw "Environment variable 'repoName' is required."
}
if ([string]::IsNullOrWhiteSpace($tag)) {
    throw "Environment variable 'tagName' is required."
}

$changelogPath = "./CHANGELOG/v3/${tag}/CHANGELOG.md"
$releaseNotePath = "./release-note.md"
$outDir = "./artifacts/release/output"

if (-not (Test-Path $outDir)) {
    throw "Output directory not found: $outDir"
}

$files = Get-ChildItem -Path $outDir -File | Sort-Object Name
if (-not $files) {
    throw "No files found in $outDir"
}

$downloadSummary = @"
**下载链接**

| 文件名 | GitHub | GitHub 镜像 |
| --- | --- | --- |
"@

foreach ($file in $files) {
    $gh = "https://github.com/${repo}/releases/download/${tag}/$($file.Name)"
    $mirror = "https://ghproxy.sectl.cn/${gh}"
    $downloadSummary += "`n| $($file.Name) | [下载](${gh}) | [下载]($mirror) |"
}

$md5Summary = @"
> [!important]
> 下载时请核对文件 SHA256。

<details>
<summary>展开 SHA256 </summary>

| 文件名 | SHA256 |
| --- | --- |
"@

foreach ($file in $files) {
    $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
    $md5Summary += "`n| $($file.Name) | ``$hash`` |"
}
$md5Summary += "`n`n</details>"

# The release body comes from this fork's own ChangeLog.md first. CHANGELOG/ is upstream's tree: reading it
# would publish upstream's release notes (and upstream's banner/links) under this fork's release, describing
# features this fork never shipped. Fall back to the upstream file, then to a placeholder, so a missing
# section never breaks the release.
$forkSection = ''
if (Test-Path "./ChangeLog.md") {
    $lines = Get-Content "./ChangeLog.md"
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

$changelog = if (-not [string]::IsNullOrWhiteSpace($forkSection)) {
    $forkSection
} elseif (Test-Path $changelogPath) {
    Get-Content $changelogPath -Raw
} else {
    "- 发布说明待补充。`n---`n"
}

$fullContent = "$changelog`n`n$downloadSummary`n`n$md5Summary"
Set-Content -Path $releaseNotePath -Value $fullContent -Encoding utf8

Write-Host "Release Note generated"
