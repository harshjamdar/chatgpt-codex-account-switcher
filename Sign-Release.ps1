param(
 [Parameter(Mandatory=$true)][string]$CertificateThumbprint,
 [Parameter(Mandatory=$true)][uri]$ReleaseUrl,
 [Parameter(Mandatory=$true)][uri]$TimestampServer,
 [string]$ManifestPath = (Join-Path $PSScriptRoot 'release-manifest.json')
)
$ErrorActionPreference = 'Stop'
if ($ReleaseUrl.Scheme -ne 'https' -or $TimestampServer.Scheme -notin @('http','https')) { throw 'Provide an HTTPS release URL and a valid timestamp service URL.' }
$taskThumbprint = $CertificateThumbprint.Replace(' ','')
if ($taskThumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw 'Invalid publisher certificate thumbprint.' }
$taskCertificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$taskThumbprint"
if (-not $taskCertificate.HasPrivateKey -or $taskCertificate.NotAfter -le (Get-Date)) { throw 'A valid publisher certificate with its private key is required.' }
$taskLauncher = Join-Path $PSScriptRoot 'OpenAIProfiles-V5.exe'
$taskResult = Set-AuthenticodeSignature -LiteralPath $taskLauncher -Certificate $taskCertificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer.AbsoluteUri
if ($taskResult.Status -ne 'Valid') { throw 'Launcher signing failed. No release manifest was generated.' }
# Rebuild setup AFTER signing so its embedded payload is the signed launcher.
$taskCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskSetup = Join-Path $PSScriptRoot 'OpenAIProfiles-Setup.exe'
$taskSetupSource = Join-Path $PSScriptRoot 'Installer.cs'
$taskIcon = Join-Path $PSScriptRoot 'ReleaseAssets\OpenAI.ico'
& $taskCompiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll "/resource:$taskLauncher,Launcher.exe" "/win32icon:$taskIcon" "/out:$taskSetup" $taskSetupSource
if ($LASTEXITCODE -ne 0) { throw 'Rebuilding signed-payload setup failed.' }
$taskResult = Set-AuthenticodeSignature -LiteralPath $taskSetup -Certificate $taskCertificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer.AbsoluteUri
if ($taskResult.Status -ne 'Valid') { throw 'Setup signing failed. No release manifest was generated.' }
@{ Version=([Version](Get-Item -LiteralPath $taskLauncher).VersionInfo.FileVersion).ToString(3); Url=$ReleaseUrl.AbsoluteUri; Sha256=(Get-FileHash -LiteralPath $taskLauncher -Algorithm SHA256).Hash.ToLowerInvariant() } | ConvertTo-Json | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
Write-Output 'Signed launcher and setup. Publish the launcher at the supplied release URL and host the manifest over HTTPS. Configure its URL and this certificate thumbprint in the launcher.'
