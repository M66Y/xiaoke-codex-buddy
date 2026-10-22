$ErrorActionPreference = 'Stop'
$exePath = Join-Path $PSScriptRoot 'Q版 Claude娘 Codex 额度查询-v1.2.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Q版 Claude娘 Codex 额度查询-v1.2.exe is missing' }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Q版 Claude娘 Codex 额度查询-v1.2.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.WindowStyle = 7
$shortcut.Description = 'Show Orange Quota mascot while Codex is open'
$shortcut.Save()
Write-Output $shortcutPath
