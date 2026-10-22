$ErrorActionPreference = 'Stop'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Q版 Claude娘 Codex 额度查询-v1.2.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
Write-Output 'Orange Quota autostart disabled. Right-click the mascot to exit the current process.'
