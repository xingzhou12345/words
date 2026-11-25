' ============================================
'  IELTS word lookup - silent background launcher
'  Double-click to start. No window.
'  NOTE: keep this file pure ASCII. Windows Script Host reads
'  .vbs as ANSI unless it has a BOM, so non-ASCII text breaks it.
' ============================================
Option Explicit

Dim fso, sh, dir, exe
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")

dir = fso.GetParentFolderName(WScript.ScriptFullName)
exe = dir & "\IeltsHotkey.exe"

If Not fso.FileExists(exe) Then
    MsgBox "IeltsHotkey.exe not found in:" & vbCrLf & dir, 16, "IELTS Word Lookup"
    WScript.Quit 1
End If

sh.CurrentDirectory = dir
sh.Run """" & exe & """", 0, False
