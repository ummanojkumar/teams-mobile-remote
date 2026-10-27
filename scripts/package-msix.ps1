<#
.SYNOPSIS
    Builds, packages, and signs the Teams Remote Companion as a self-contained win-x64 MSIX installer.

.DESCRIPTION
    Automates the full packaging pipeline:
    1. Reads version.json and maps SemVer (0.9.0-beta.1) to 4-part MSIX version (0.9.0.1).
    2. Builds React PWA (npm run build) and bundles static assets into Relay wwwroot/.
    3. Publishes self-contained Companion for win-x64.
    4. Resolves Windows SDK makeappx.exe and signtool.exe.
    5. Stamps Package.appxmanifest with version and certificate Subject.
    6. Packs the MSIX package and signs it with Authenticode.
    7. Generates release-manifest.json and optional TeamsRemote.appinstaller.

.PARAMETER Configuration
    Build configuration: "Release" (default) or "Debug".

.PARAMETER CertificatePath
    Path to code-signing PFX certificate. Defaults to $env:USERPROFILE\.teamsremote\certs\TeamsRemoteDevTest.pfx.

.PARAMETER CertificatePassword
    Password for PFX certificate if protected.

.PARAMETER UpdateUri
    Optional HTTPS root URI for App Installer distribution (e.g. https://releases.teamsremote.net/beta).

.PARAMETER SkipWebBuild
    Skips npm run build if web assets are already built.

.PARAMETER OutDir
    Output directory for the generated .msix and manifest. Defaults to "artifacts/dist".
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$CertificatePath,
    [string]$CertificatePassword = "TeamsRemoteDev2026!",
    [string]$UpdateUri,
    [switch]$SkipWebBuild,
    [string]$OutDir = "artifacts/dist"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Teams Remote Companion - MSIX Production Packaging Pipeline" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Read version.json
$versionJsonPath = Join-Path $repoRoot "version.json"
if (-not (Test-Path $versionJsonPath)) {
    throw "version.json not found at $versionJsonPath"
}

$versionInfo = Get-Content $versionJsonPath -Raw | ConvertFrom-Json
$major = $versionInfo.major
$minor = $versionInfo.minor
$patch = $versionInfo.patch
$build = $versionInfo.build
$prerelease = $versionInfo.prerelease
$channel = $versionInfo.channel

$productVersion = if ([string]::IsNullOrWhiteSpace($prerelease)) {
    "$major.$minor.$patch"
} else {
    "$major.$minor.$patch-$prerelease"
}
$msixVersion = "$major.$minor.$patch.$build"

Write-Host "[VERSION] Product Version (SemVer): $productVersion" -ForegroundColor Green
Write-Host "[VERSION] MSIX 4-Part Version:      $msixVersion" -ForegroundColor Green

# 2. Resolve Windows SDK tools (makeappx.exe, signtool.exe)
function Find-SdkTool([string]$toolName) {
    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kitsRoot) {
        $found = Get-ChildItem -Path $kitsRoot -Filter $toolName -Recurse -File |
            Where-Object { $_.DirectoryName -like "*\x64" } |
            Sort-Object { $_.Directory.Parent.Name } -Descending |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    # Check PATH as fallback
    $inPath = Get-Command $toolName -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }
    return $null
}

$makeAppxPath = Find-SdkTool "makeappx.exe"
if (-not $makeAppxPath) {
    throw "makeappx.exe not found in Windows Kits or PATH. Please install Windows SDK."
}
Write-Host "[SDK] makeappx.exe: $makeAppxPath" -ForegroundColor Gray

$signToolPath = Find-SdkTool "signtool.exe"
if (-not $signToolPath) {
    throw "signtool.exe not found in Windows Kits or PATH. Please install Windows SDK."
}
Write-Host "[SDK] signtool.exe: $signToolPath" -ForegroundColor Gray

# 3. Resolve Code Signing Certificate
if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    $CertificatePath = Join-Path $env:USERPROFILE ".teamsremote\certs\TeamsRemoteDevTest.pfx"
}

if (-not (Test-Path $CertificatePath)) {
    Write-Host "[CERT] No certificate found at $CertificatePath. Running New-DevCertificate.ps1..." -ForegroundColor Yellow
    $devCertScript = Join-Path $repoRoot "scripts\New-DevCertificate.ps1"
    & $devCertScript -Password $CertificatePassword | Out-Null
    if (-not (Test-Path $CertificatePath)) {
        throw "Failed to create or locate development certificate at $CertificatePath"
    }
}

# Inspect Certificate Subject
Add-Type -AssemblyName System.Security
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
$plainPwd = $CertificatePassword
$cert.Import($CertificatePath, $plainPwd, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::DefaultKeySet)

$publisherSubject = $cert.Subject
Write-Host "[CERT] Publisher Identity: $publisherSubject" -ForegroundColor Green

# 4. Build React PWA Assets
$webDir = Join-Path $repoRoot "src\TeamsRemote.Web"
$relayWwwroot = Join-Path $repoRoot "src\TeamsRemote.Relay\wwwroot"

if (-not $SkipWebBuild) {
    Write-Host "[WEB] Building React PWA assets (npm run build)..." -ForegroundColor Cyan
    Push-Location $webDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }
    } finally {
        Pop-Location
    }

    $webDist = Join-Path $webDir "dist"
    if (-not (Test-Path $webDist)) { throw "Web build dist folder not found at $webDist" }

    if (-not (Test-Path $relayWwwroot)) {
        New-Item -ItemType Directory -Path $relayWwwroot -Force | Out-Null
    }

    Write-Host "[WEB] Copying built assets to Relay wwwroot: $relayWwwroot..." -ForegroundColor Gray
    Copy-Item -Path "$webDist\*" -Destination $relayWwwroot -Recurse -Force
} else {
    Write-Host "[WEB] Skipping web build (-SkipWebBuild specified)." -ForegroundColor Yellow
}

if (-not (Test-Path (Join-Path $relayWwwroot "index.html"))) {
    throw "index.html missing from Relay wwwroot at $relayWwwroot."
}

# 5. Staging Directory Setup
$stagingDir = Join-Path $repoRoot "artifacts\staging\msix"
if (Test-Path $stagingDir) {
    Remove-Item -Path $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

$outDirPath = Join-Path $repoRoot $OutDir
if (-not (Test-Path $outDirPath)) {
    New-Item -ItemType Directory -Path $outDirPath -Force | Out-Null
}

# 6. Publish Self-Contained Companion (win-x64)
Write-Host "[DOTNET] Publishing self-contained Companion for win-x64 ($Configuration)..." -ForegroundColor Cyan
$companionProj = Join-Path $repoRoot "src\TeamsRemote.Companion\TeamsRemote.Companion.csproj"
dotnet publish $companionProj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $stagingDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Ensure wwwroot is present in staging
$stagingWwwroot = Join-Path $stagingDir "wwwroot"
if (-not (Test-Path (Join-Path $stagingWwwroot "index.html"))) {
    Write-Host "[STAGING] Copying wwwroot to staging folder..." -ForegroundColor Gray
    Copy-Item -Path $relayWwwroot -Destination $stagingDir -Recurse -Force
}

# 7. Copy Visual Assets & Stamp AppxManifest.xml
$assetsSource = Join-Path $repoRoot "packaging\msix\Assets"
if (-not (Test-Path $assetsSource)) {
    throw "Assets directory not found at $assetsSource. Run packaging/msix/generate-assets.ps1."
}

$stagingAssets = Join-Path $stagingDir "Assets"
Copy-Item -Path $assetsSource -Destination $stagingDir -Recurse -Force

$manifestTemplatePath = Join-Path $repoRoot "packaging\msix\Package.appxmanifest.template"
if (-not (Test-Path $manifestTemplatePath)) {
    throw "Package.appxmanifest.template not found at $manifestTemplatePath"
}

$manifestContent = Get-Content $manifestTemplatePath -Raw
$manifestContent = $manifestContent.Replace("{{MSIX_VERSION}}", $msixVersion)
$manifestContent = $manifestContent.Replace("{{PUBLISHER_IDENTITY}}", $publisherSubject)

$targetManifest = Join-Path $stagingDir "AppxManifest.xml"
Set-Content -Path $targetManifest -Value $manifestContent -Encoding UTF8
Write-Host "[MANIFEST] AppxManifest.xml generated with Version=$msixVersion" -ForegroundColor Green

# 8. Pack MSIX with makeappx.exe
$msixFileName = "TeamsRemote-$msixVersion-x64.msix"
$msixPath = Join-Path $outDirPath $msixFileName

if (Test-Path $msixPath) {
    Remove-Item $msixPath -Force
}

Write-Host "[MAKEAPPX] Packing MSIX: $msixPath..." -ForegroundColor Cyan
& $makeAppxPath pack /d $stagingDir /p $msixPath /o /nv
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed with exit code $LASTEXITCODE" }

# 9. Sign MSIX with signtool.exe
Write-Host "[SIGNTOOL] Signing MSIX package..." -ForegroundColor Cyan
$signArgs = @("sign", "/f", $CertificatePath, "/fd", "SHA256")
if ($plainPwd) {
    $signArgs += @("/p", $plainPwd)
}

# Attempt timestamping; if timestamp fails (e.g. offline), fall back to untimestamped sign
$timestampArgs = $signArgs + @("/tr", "http://timestamp.digicert.com", "/td", "SHA256", $msixPath)
$timestampExit = 1
try {
    & $signToolPath @timestampArgs
    $timestampExit = $LASTEXITCODE
} catch {
    $timestampExit = 1
}

if ($timestampExit -ne 0) {
    Write-Warning "[SIGNTOOL] Timestamp server unreachable; signing without timestamp for offline/local build..."
    $localArgs = $signArgs + @($msixPath)
    & $signToolPath @localArgs
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed with exit code $LASTEXITCODE" }
}

Write-Host "[SIGNTOOL] MSIX signed successfully!" -ForegroundColor Green

# 10. Optional App Installer Generation
$appInstallerPath = $null
if (-not [string]::IsNullOrWhiteSpace($UpdateUri)) {
    $appInstallerTemplate = Join-Path $repoRoot "packaging\msix\TeamsRemote.appinstaller.template"
    if (Test-Path $appInstallerTemplate) {
        $aiContent = Get-Content $appInstallerTemplate -Raw
        $aiContent = $aiContent.Replace("{{MSIX_VERSION}}", $msixVersion)
        $aiContent = $aiContent.Replace("{{PUBLISHER_IDENTITY}}", $publisherSubject)
        $aiContent = $aiContent.Replace("{{UPDATE_URI}}", $UpdateUri.TrimEnd('/'))
        $appInstallerPath = Join-Path $outDirPath "TeamsRemote.appinstaller"
        Set-Content -Path $appInstallerPath -Value $aiContent -Encoding UTF8
        Write-Host "[APPINSTALLER] Generated $appInstallerPath" -ForegroundColor Green
    }
}

# 11. Generate Release Manifest
$fileItem = Get-Item $msixPath
$sha256 = (Get-FileHash -Path $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
$sizeMb = [Math]::Round($fileItem.Length / 1MB, 2)

$releaseManifest = [ordered]@{
    productName = "Teams Remote Companion"
    productVersion = $productVersion
    msixVersion = $msixVersion
    channel = $channel
    buildDate = (Get-Date).ToUniversalTime().ToString("o")
    architecture = "x64"
    targetPlatform = "win-x64"
    framework = "net10.0-windows"
    hasAppInstaller = (-not [string]::IsNullOrWhiteSpace($UpdateUri))
    files = @(
        [ordered]@{
            name = $msixFileName
            type = "msix"
            sizeBytes = $fileItem.Length
            sha256 = $sha256
        }
    )
}

if ($appInstallerPath -and (Test-Path $appInstallerPath)) {
    $aiItem = Get-Item $appInstallerPath
    $aiHash = (Get-FileHash -Path $appInstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $releaseManifest.files += [ordered]@{
        name = "TeamsRemote.appinstaller"
        type = "appinstaller"
        sizeBytes = $aiItem.Length
        sha256 = $aiHash
    }
}

$manifestJsonPath = Join-Path $outDirPath "release-manifest.json"
$releaseManifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestJsonPath -Encoding UTF8
Write-Host "[MANIFEST] Release manifest written to $manifestJsonPath" -ForegroundColor Green

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " MSIX Packaging Succeeded!" -ForegroundColor Green
Write-Host " Package:  $msixPath" -ForegroundColor Green
Write-Host " SHA-256:  $sha256" -ForegroundColor Green
Write-Host " Size:     $sizeMb MB" -ForegroundColor Green
Write-Host " Manifest: $manifestJsonPath" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan

return $releaseManifest

