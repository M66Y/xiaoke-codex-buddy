$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appName = 'Q版 Claude娘 Codex 额度查询-v1.2'
$outputPath = Join-Path (Join-Path $repoRoot 'dist') $appName
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& $compiler /nologo /target:winexe /platform:anycpu "/win32manifest:$repoRoot\src\app.manifest" "/out:$outputPath\$appName.exe" @refs "$repoRoot\src\OrangeQuota.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'assets\doll.png') -Destination $outputPath -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\OrangeQuota.exe.config') -Destination (Join-Path $outputPath ($appName + '.exe.config')) -Force
foreach ($file in @('Enable-Autostart.ps1','Disable-Autostart.ps1','Start.vbs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $outputPath -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $outputPath -Force
Write-Output $outputPath
