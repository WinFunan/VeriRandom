<#
.SYNOPSIS
    Generates an Android release keystore for this fork and prints the repository secrets that
    the Build & Publish workflow expects.

.DESCRIPTION
    The workflow signs Android APKs only when all four of these repository secrets are present:
      ANDROID_SIGNING_KEYSTORE_BASE64
      ANDROID_SIGNING_KEYSTORE_PASSWORD
      ANDROID_SIGNING_KEY_ALIAS
      ANDROID_SIGNING_KEY_PASSWORD

    Without them the workflow falls back to a debug-signed APK that is uploaded as
    "-unsigned" and kept out of the release output and update manifest. Run this script once to
    create a real release keystore, then set the four secrets.

    The keystore is the app's update identity: if you lose it, existing installs can never accept
    an update signed by a new key. Back it up somewhere safe and never commit it.

.EXAMPLE
    ./scripts/generate-android-keystore.ps1 -Alias verirandom

.NOTES
    Requires a JDK on PATH (or JAVA_HOME set) so that `keytool` is available.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts/android-keystore',
    [string]$Alias = 'verirandom',
    [string]$StorePassword,
    [string]$KeyPassword,
    [string]$DistinguishedName = 'CN=VeriRandom, OU=Android, O=VeriRandom, C=CN',
    [int]$ValidityDays = 10000,
    [int]$KeySize = 2048,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Read-RequiredPassword([string]$Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $plain = [System.Net.NetworkCredential]::new('', $secure).Password
    if ([string]::IsNullOrWhiteSpace($plain)) {
        throw "A non-empty password is required."
    }

    return $plain
}

$keytool = Get-Command keytool -ErrorAction SilentlyContinue
if (-not $keytool) {
    throw "keytool was not found on PATH. Install a JDK (for example Temurin 21) or set JAVA_HOME so that its bin directory is on PATH."
}

if ([string]::IsNullOrWhiteSpace($StorePassword)) {
    $StorePassword = Read-RequiredPassword "Keystore password (ANDROID_SIGNING_KEYSTORE_PASSWORD)"
}
if ([string]::IsNullOrWhiteSpace($KeyPassword)) {
    # PKCS12 keystores require the key password to match the store password, so reuse it by default.
    $KeyPassword = $StorePassword
}
elseif ($KeyPassword -cne $StorePassword) {
    Write-Warning 'PKCS12 keystores require the key password to match the store password; using the keystore password for both.'
    $KeyPassword = $StorePassword
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$keystorePath = Join-Path $outputRoot "$Alias.keystore"
$base64Path = Join-Path $outputRoot "$Alias.keystore.base64.txt"

if ((Test-Path -LiteralPath $keystorePath) -and -not $Force) {
    throw "'$keystorePath' already exists. Pass -Force to overwrite it, but be aware that overwriting a release keystore breaks update compatibility for existing installs."
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (Test-Path -LiteralPath $keystorePath) {
    Remove-Item -LiteralPath $keystorePath -Force
}

# Array splatting keeps each -J-* option a single argument; passing them as bare tokens makes
# PowerShell split "-J-Duser.language=en" into "-J-Duser" + ".language=en" (see AGENTS.md).
$keytoolArguments = @(
    '-J-Duser.language=en',
    '-J-Duser.country=US',
    '-genkeypair',
    '-keystore', $keystorePath,
    '-storetype', 'PKCS12',
    '-keyalg', 'RSA',
    '-keysize', $KeySize,
    '-validity', $ValidityDays,
    '-alias', $Alias,
    '-dname', $DistinguishedName,
    '-storepass', $StorePassword,
    '-keypass', $KeyPassword
)
# Java tools write their progress to stderr, and under $ErrorActionPreference='Stop' PowerShell turns
# that into a terminating NativeCommandError that aborts this script before the key pair is even written.
# Merge this one call's stderr into the host output, the same shape the workflow's keytool steps use.
$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & keytool @keytoolArguments 2>&1 | ForEach-Object { Write-Host "$_" }
    $keytoolExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorAction
}
if ($keytoolExitCode -ne 0) {
    throw "keytool failed with exit code $keytoolExitCode."
}

# Normalizes to standard base64 (no line breaks) so it can be pasted straight into the secret.
$keystoreBytes = [IO.File]::ReadAllBytes($keystorePath)
$base64 = [Convert]::ToBase64String($keystoreBytes)
Set-Content -LiteralPath $base64Path -Value $base64 -Encoding ascii -NoNewline

Write-Host ''
Write-Host 'Keystore generated.' -ForegroundColor Green
Write-Host "  Keystore : $keystorePath"
Write-Host "  Base64   : $base64Path"
Write-Host "  Alias    : $Alias"
Write-Host ''
Write-Host 'Create these repository secrets (Settings > Secrets and variables > Actions):'
Write-Host '  ANDROID_SIGNING_KEYSTORE_BASE64   <- contents of the *.base64.txt file on one line'
Write-Host '  ANDROID_SIGNING_KEYSTORE_PASSWORD  <- the keystore password you entered'
Write-Host "  ANDROID_SIGNING_KEY_ALIAS          <- $Alias"
Write-Host '  ANDROID_SIGNING_KEY_PASSWORD       <- the same password again (PKCS12 requires both to match)'
Write-Host ''
Write-Host 'Never commit the keystore or its base64 file: artifacts/ is ignored, but keep a separate, safe backup.' -ForegroundColor Yellow
