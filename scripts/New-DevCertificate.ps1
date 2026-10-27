<#
.SYNOPSIS
    Generates a local development code-signing certificate for MSIX packaging.

.DESCRIPTION
    Creates a self-signed certificate with Code Signing EKU (1.3.6.1.5.5.7.3.3)
    and stores the private key strictly OUTSIDE the Git repository in
    $env:USERPROFILE\.teamsremote\certs\TeamsRemoteDevTest.pfx.
    Also imports the public .cer into Cert:\CurrentUser\TrustedPeople so Windows
    permits local MSIX installation without untrusted root errors.

.PARAMETER Subject
    Certificate subject DN. Defaults to "CN=TeamsRemoteDevTest, O=TeamsMobileRemote, C=US".

.PARAMETER Force
    Overwrites any existing certificate with the same subject.
#>
param(
    [string]$Subject = "CN=TeamsRemoteDevTest, O=TeamsMobileRemote, C=US",
    [string]$Password = "TeamsRemoteDev2026!",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$certDir = Join-Path $env:USERPROFILE ".teamsremote\certs"
if (-not (Test-Path $certDir)) {
    New-Item -ItemType Directory -Path $certDir -Force | Out-Null
}

$pfxPath = Join-Path $certDir "TeamsRemoteDevTest.pfx"
$cerPath = Join-Path $certDir "TeamsRemoteDevTest.cer"

if ((Test-Path $pfxPath) -and -not $Force) {
    Write-Host "[CERT] Existing development certificate found at: $pfxPath" -ForegroundColor Green
    Write-Host "[CERT] To regenerate, pass -Force."
    return [PSCustomObject]@{
        PfxPath = $pfxPath
        CerPath = $cerPath
        Subject = $Subject
    }
}

Write-Host "[CERT] Generating self-signed code-signing certificate: $Subject..." -ForegroundColor Cyan

# Code Signing EKU: 1.3.6.1.5.5.7.3.3
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -KeySpec Signature `
    -KeyLength 2048 `
    -KeyExportPolicy Exportable `
    -HashAlgorithm SHA256 `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(5)

$secPwd = ConvertTo-SecureString -String $Password -AsPlainText -Force

# Export PFX (private key + cert)
Export-PfxCertificate `
    -Cert $cert `
    -FilePath $pfxPath `
    -Password $secPwd | Out-Null

# Export public CER
Export-Certificate `
    -Cert $cert `
    -FilePath $cerPath | Out-Null

Write-Host "[CERT] Private key saved to: $pfxPath" -ForegroundColor Green
Write-Host "[CERT] Public certificate saved to: $cerPath" -ForegroundColor Green

# Install into TrustedPeople so local App Installer trusts the certificate
try {
    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
    Write-Host "[CERT] Public certificate imported to Cert:\CurrentUser\TrustedPeople." -ForegroundColor Green
} catch {
    Write-Warning "[CERT] Could not automatically import to TrustedPeople: $_"
}

return [PSCustomObject]@{
    PfxPath = $pfxPath
    CerPath = $cerPath
    Subject = $cert.Subject
    Thumbprint = $cert.Thumbprint
}
