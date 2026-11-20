$ErrorActionPreference = 'Stop'
$taskCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskWpf = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
$taskLauncher = Join-Path $PSScriptRoot 'OpenAIProfiles-V5.exe'
$taskAssets = Join-Path $PSScriptRoot 'ReleaseAssets'
$taskSources = @('CompanyChatGPT.cs','PremiumPicker.cs','LauncherServices.cs','LauncherFeatures.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $taskCompiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll /reference:System.Xaml.dll "/reference:$taskWpf\WindowsBase.dll" "/reference:$taskWpf\PresentationCore.dll" "/reference:$taskWpf\PresentationFramework.dll" "/resource:$taskAssets\OpenAI.ico,OpenAI.ico" "/resource:$taskAssets\OpenAI-logo-check.png,OpenAI.logo.png" "/resource:$taskAssets\OpenAI-premium.png,OpenAI.premium.png" "/win32icon:$taskAssets\OpenAI.ico" "/out:$taskLauncher" @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
$taskSetup = Join-Path $PSScriptRoot 'OpenAIProfiles-Setup.exe'
& $taskCompiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll "/resource:$taskLauncher,Launcher.exe" "/win32icon:$taskAssets\OpenAI.ico" "/out:$taskSetup" (Join-Path $PSScriptRoot 'Installer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }
Write-Output 'Built launcher and per-user setup. These files remain unsigned until Sign-Release.ps1 is run with your certificate.'
