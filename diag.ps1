# 雅思划词查词 · 诊断工具
# 双击「诊断.bat」运行，或者：powershell -File diag.ps1

$ErrorActionPreference = 'Continue'
$Root = $PSScriptRoot
if (-not $Root) { $Root = Split-Path -Parent $MyInvocation.MyCommand.Definition }
try { Add-Type -AssemblyName System.Windows.Forms } catch { }

function Line([string]$s) { Write-Host $s }
function Ok([string]$s)   { Write-Host ('  [OK]   ' + $s) -ForegroundColor Green }
function Bad([string]$s)  { Write-Host ('  [!!]   ' + $s) -ForegroundColor Red }
function Info([string]$s) { Write-Host ('  [--]   ' + $s) -ForegroundColor Gray }

Line '=============================================='
Line ' 雅思划词查词 · 诊断'
Line '=============================================='

Info ('PowerShell ' + $PSVersionTable.PSVersion.ToString() + ' / ' + [System.Threading.Thread]::CurrentThread.GetApartmentState())
Info ('目录 ' + $Root)
Line ''

$hotkey = Join-Path $Root 'hotkey.ps1'
$vbs    = Join-Path $Root '启动划词查词.vbs'
$html   = Join-Path $Root 'index.html'
$log    = Join-Path $Root 'hotkey.log'

# ---- 1. 文件 ----
Line '1. 关键文件'
foreach ($pair in @(@('IeltsHotkey.exe', (Join-Path $Root 'IeltsHotkey.exe')), @('启动划词查词.vbs', $vbs), @('index.html', $html))) {
    if (Test-Path -LiteralPath $pair[1]) { Ok $pair[0] } else { Bad ($pair[0] + ' 缺失') }
}

# ---- 2. 脚本语法 ----
Line ''
Line '2. 主程序'
$exePath = Join-Path $Root 'IeltsHotkey.exe'
if (Test-Path -LiteralPath $exePath) {
    $size = [Math]::Round((Get-Item -LiteralPath $exePath).Length / 1KB, 1)
    Ok ('IeltsHotkey.exe 存在（' + $size + ' KB）')
} else {
    Bad 'IeltsHotkey.exe 不存在'
}
$ini = Join-Path $Root 'hotkey.ini'
if (Test-Path -LiteralPath $ini) {
    $cfg = (Get-Content -LiteralPath $ini -Encoding UTF8 | Where-Object { $_ -match '^\s*(mods|key)\s*=' }) -join ' '
    Ok ('热键配置: ' + $cfg)
} else {
    Info '没有 hotkey.ini，用默认 Ctrl+Alt+D'
}

# ---- 3. 浏览器 ----
Line ''
Line '3. 浏览器'
$progId = ''
try {
    $progId = (Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice' -ErrorAction Stop).ProgId
} catch { }
if ($progId) { Info ('默认浏览器 ProgId: ' + $progId) } else { Info '读不到默认浏览器（不影响，会按安装路径找）' }

$found = @()
foreach ($p in @(
    (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
    (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'))) {
    if ($p -and (Test-Path -LiteralPath $p)) { $found += $p }
}
if ($found.Count) { $found | ForEach-Object { Ok $_ } } else { Bad '没找到 Chrome / Edge，弹窗打不开' }

# ---- 4. 常驻进程 ----
Line ''
Line '4. 常驻进程'
$proc = Get-Process IeltsHotkey -ErrorAction SilentlyContinue
if ($proc) {
    foreach ($p in $proc) {
        Ok ('正在运行 PID ' + $p.Id + ' · 内存 ' + [Math]::Round($p.WorkingSet64 / 1MB, 1) + ' MB')
    }
} else {
    Bad '没有运行 → 双击「启动划词查词.vbs」'
}
$old = Get-Process powershell -ErrorAction SilentlyContinue
if ($old) { Info ('注意：还有 ' + $old.Count + ' 个 powershell 进程，若是旧版请点 停止划词查词.bat') }

# ---- 5. 剪贴板 ----
Line ''
Line '5. 剪贴板'
try {
    $c = [System.Windows.Forms.Clipboard]::GetText()
    if ($c) { Ok ('可读，当前内容: ' + $c.Substring(0, [Math]::Min(30, $c.Length))) } else { Info '可读，但是空的' }
} catch { Bad ('读不了剪贴板: ' + $_) }

# ---- 6. 日志 ----
Line ''
Line '6. 最近日志'
if (Test-Path -LiteralPath $log) {
    Get-Content -LiteralPath $log -Encoding UTF8 -Tail 15 | ForEach-Object { Info $_ }
} else {
    Bad '还没有 hotkey.log —— 说明脚本从没成功跑起来过'
}

Line ''
Line '=============================================='
Line ' 如果第 4 步显示没运行：双击 启动划词查词.vbs'
Line ' 如果显示运行中但热键没反应：可能被别的软件占用，'
Line '   改 hotkey.ini 里的 key（比如换成 Q），重启程序'
Line '=============================================='
Write-Host ''
Write-Host '按任意键关闭...' -NoNewline
try { [void]$Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') } catch { Start-Sleep -Seconds 10 }
