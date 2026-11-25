# 开机自启设置：把启动脚本放进「启动」文件夹
# 用法：
#   powershell -File autostart.ps1              开启
#   powershell -File autostart.ps1 -Off         关闭

param(
    [switch]$Off,
    [string]$TargetDir = '',
    [switch]$NoWait
)

$Root = $PSScriptRoot
if (-not $Root) { $Root = Split-Path -Parent $MyInvocation.MyCommand.Definition }

$vbs = Join-Path $Root '启动划词查词.vbs'
if (-not $TargetDir) { $TargetDir = [Environment]::GetFolderPath('Startup') }
$lnkPath = Join-Path $TargetDir '雅思划词查词.lnk'

Write-Host '=============================================='
Write-Host ' 雅思划词查词 · 开机自启'
Write-Host '=============================================='
Write-Host (' 启动文件夹: ' + $TargetDir)
Write-Host ''

if ($Off) {
    if (Test-Path -LiteralPath $lnkPath) {
        Remove-Item -LiteralPath $lnkPath -Force
        Write-Host ' [OK] 已取消开机自启' -ForegroundColor Green
    } else {
        Write-Host ' [--] 本来就没有设置开机自启' -ForegroundColor Gray
    }
} else {
    if (-not (Test-Path -LiteralPath $vbs)) {
        Write-Host (' [!!] 找不到启动脚本: ' + $vbs) -ForegroundColor Red
        if (-not $NoWait -and -not [Console]::IsInputRedirected) { [void]$Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') }
        exit 1
    }
    try {
        $shell = New-Object -ComObject WScript.Shell
        $lnk = $shell.CreateShortcut($lnkPath)
        $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\wscript.exe'
        $lnk.Arguments = '"' + $vbs + '"'
        $lnk.WorkingDirectory = $Root
        $lnk.WindowStyle = 7
        $lnk.Description = '雅思划词查词 - 选中文字按 Ctrl+Alt+D'
        $lnk.Save()
        if (Test-Path -LiteralPath $lnkPath) {
            Write-Host ' [OK] 已设置开机自启，下次开机自动在后台运行' -ForegroundColor Green
        } else {
            Write-Host ' [!!] 快捷方式没有生成，可能被安全软件拦了' -ForegroundColor Red
        }
    } catch {
        Write-Host (' [!!] 创建失败: ' + $_) -ForegroundColor Red
    }
}

Write-Host ''
Write-Host '----------------------------------------------'
Write-Host ' 现在也可以直接双击 启动划词查词.vbs 立刻启动'
Write-Host '----------------------------------------------'
Write-Host ''

if (-not $NoWait -and -not [Console]::IsInputRedirected) {
    Write-Host '按任意键关闭...' -NoNewline
    try { [void]$Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') } catch { }
}
