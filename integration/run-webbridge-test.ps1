# 真机命令层测试（WebBridgeTest）。会真实写一次屏幕（亮度、动画、挂件），并覆盖屏幕上的 ccask.savi。
# 安全措施：编译失败就不运行；运行前后都把 settings.json / integration.json 还原成已知良好的备份；
#           托盘程序若在运行会先退出（串口只能被一个进程占用），测试后需要你自己重新启动它。
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$data = Join-Path $env:APPDATA 'GpuLcdTool'
$good = Join-Path $root 'backups\upload-mutex-20261009-104435'   # 含原始 settings.json / integration.json
foreach ($f in 'settings.json', 'integration.json') { if (-not (Test-Path (Join-Path $good $f))) { throw "missing known-good backup: $good\$f" } }

function Send-Pipe($line) {
  $j = Start-Job -ArgumentList $line { param($l) $p = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'GpuLcdTool', [System.IO.Pipes.PipeDirection]::InOut); $p.Connect(3000); $w = New-Object System.IO.StreamWriter($p, (New-Object System.Text.UTF8Encoding($false))); $w.AutoFlush = $true; $r = New-Object System.IO.StreamReader($p, [System.Text.Encoding]::UTF8); $w.WriteLine($l); $r.ReadLine() }
  if (Wait-Job $j -Timeout 15) { Receive-Job $j } else { 'TIMEOUT' }
}
function Restore-Settings { foreach ($f in 'settings.json', 'integration.json') { Copy-Item (Join-Path $good $f) (Join-Path $data $f) -Force } }
# The app writes HKCU\...\Run\GpuLcdTool = "<its own exe path>" --tray when autostart is set. The test exe runs from %TEMP%,
# so without this save/restore it would repoint the user's real autostart at the test harness.
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runBefore = (Get-ItemProperty $runKeyPath -Name GpuLcdTool -ErrorAction SilentlyContinue).GpuLcdTool
function Restore-Run {
  if ($null -ne $runBefore) { Set-ItemProperty $runKeyPath -Name GpuLcdTool -Value $runBefore }
  else { Remove-ItemProperty $runKeyPath -Name GpuLcdTool -ErrorAction SilentlyContinue }
}

if (Get-Process GpuLcd -ErrorAction SilentlyContinue) { Send-Pipe 'quit' | Out-Null; Start-Sleep 3 }
if (Get-Process GpuLcd -ErrorAction SilentlyContinue) { throw 'GpuLcd.exe is still running; it holds the serial port' }

$ig = 'C:\Program Files\iGameCenter'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$out = Join-Path $root 'integration\WebBridgeTest.exe'
Remove-Item $out -ErrorAction SilentlyContinue   # 防止编译失败后误用旧版本
Push-Location $root
& $csc /nologo /target:exe /platform:x64 /main:GpuLcd.WebBridgeTest "/out:$out" "/r:$ig\iGameAPI.LCD.CSharp.dll" "/r:$ig\iGameAPI.NVAPI.CSharp.dll" "/r:$ig\iGameAPI.Contracts.dll" "/r:$ig\iGameCenter.Contracts.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.Web.dll /r:System.IO.Compression.dll "/r:lib\Microsoft.Web.WebView2.Core.dll" "/r:lib\Microsoft.Web.WebView2.WinForms.dll" VendorRuntime.cs GpuLcd.cs Storage.cs Integrations.cs SignalBridge.cs Widgets.cs DisplayJobs.cs NightMode.cs WebUi.cs integration\LinkState.cs integration\CompletionReader.cs integration\DotMatrixScene.cs integration\DotMatrixClient.cs DotMatrixBridge.cs integration\WebBridgeTest.cs
$ok = ($LASTEXITCODE -eq 0) -and (Test-Path $out)
Pop-Location
if (-not $ok) { throw 'compile failed - test NOT run' }

$t = Join-Path $env:TEMP 'wbt'; New-Item -ItemType Directory $t -Force | Out-Null   # 从非工作目录运行，避免沙箱限制写 AppData
Copy-Item $out $t -Force; Copy-Item (Join-Path $root 'lib\*.dll') $t -Force
Restore-Settings
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$resultFile = Join-Path $root 'integration\web-bridge-test-result.txt'
try { & (Join-Path $t 'WebBridgeTest.exe') 2>&1 | Tee-Object $resultFile } finally { Restore-Settings; Restore-Run }
"exit=$LASTEXITCODE"
