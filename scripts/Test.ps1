$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot 'dist\Q版 Claude娘 Codex 额度查询-v1.2'
$exePath = Join-Path $outputPath 'Q版 Claude娘 Codex 额度查询-v1.2.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Run scripts\Build.ps1 first.' }
$resultPath = Join-Path $outputPath 'self-test.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$testProcess = Start-Process -FilePath $exePath -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
if ($testProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath)) { throw 'Self-test did not complete.' }
$result = Get-Content -LiteralPath $resultPath -Raw
if (-not $result.StartsWith('PASS:')) { throw $result }
Write-Output $result
