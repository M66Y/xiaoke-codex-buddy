Set shell = CreateObject("WScript.Shell")
Set fs = CreateObject("Scripting.FileSystemObject")
folder = fs.GetParentFolderName(WScript.ScriptFullName)
exePath = fs.BuildPath(folder, fs.GetFileName(folder) & ".exe")
shell.Run Chr(34) & exePath & Chr(34), 0, False
