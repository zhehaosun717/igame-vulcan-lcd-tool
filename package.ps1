# Builds the finished product into .\release :
#   GpuLcd-Setup-<ver>.exe        installer (per-user, no admin) - embeds the portable payload
#   GpuLcd-<ver>-portable.zip     portable folder
#   SHA256SUMS.txt
# Usage:  .\package.ps1            (rebuilds the UI too)
#         .\package.ps1 -SkipUi    (reuse ui\dist as is)
# ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
param([switch]$SkipUi)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ver = '1.0.0'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Step($t) { Write-Host "== $t" }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" } }

if (-not $SkipUi) {
  Step 'build UI (vue-tsc + vite)'
  Push-Location (Join-Path $root 'ui')
  cmd /c "npx vue-tsc --noEmit" ; Check 'vue-tsc'
  cmd /c "npm run build" ; Check 'vite build'
  Pop-Location
}
if (-not (Test-Path (Join-Path $root 'ui\dist\index.html'))) { throw 'ui\dist\index.html missing - build the UI first' }

Step 'build GpuLcd.exe (UI embedded)'
cmd /c (Join-Path $root 'build.cmd') ; Check 'build.cmd'
$exe = Join-Path $root 'GpuLcd.exe'
if (-not (Test-Path (Join-Path $root 'ui-dist.zip'))) { throw 'ui-dist.zip was not produced; the exe would not contain the UI' }

Step 'build GpuLcdSend.exe (command line helper)'
& $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$root\GpuLcdSend.exe" /r:System.Web.Extensions.dll /r:System.Drawing.dll "$root\integration\DisplaySend.cs" "$root\DisplayJobs.cs" ; Check 'GpuLcdSend'

$rel = Join-Path $root 'release'
$stage = Join-Path $rel 'stage\GpuLcd'
if (Test-Path (Join-Path $rel 'stage')) { Remove-Item (Join-Path $rel 'stage') -Recurse -Force }
New-Item -ItemType Directory $stage -Force | Out-Null
Step 'stage files'
foreach ($f in 'GpuLcd.exe', 'GpuLcdSend.exe') { Copy-Item (Join-Path $root $f) $stage }
Copy-Item (Join-Path $root 'lib\*.dll') $stage
Copy-Item (Join-Path $root 'installer\README.txt') $stage
Get-ChildItem $stage | ForEach-Object { '{0,-40} {1,10:N0}' -f $_.Name, $_.Length }

Step 'portable zip'
$portable = Join-Path $rel "GpuLcd-$ver-portable.zip"
if (Test-Path $portable) { Remove-Item $portable }
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $rel 'stage'), $portable, [IO.Compression.CompressionLevel]::Optimal, $false)

Step 'installer payload + Setup.exe'
$payload = Join-Path $rel 'payload.zip'
if (Test-Path $payload) { Remove-Item $payload }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)
$setup = Join-Path $rel "GpuLcd-Setup-$ver.exe"
if (Test-Path $setup) { Remove-Item $setup }
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$setup" "/win32icon:$root\app.ico" "/win32manifest:$root\app.manifest" "/resource:$payload,payload.zip" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$root\installer\Setup.cs" ; Check 'Setup'
Remove-Item $payload

Step 'checksums'
$lines = foreach ($f in $setup, $portable) { '{0}  {1}' -f (Get-FileHash $f -Algorithm SHA256).Hash.ToLower(), (Split-Path $f -Leaf) }
Set-Content (Join-Path $rel 'SHA256SUMS.txt') $lines -Encoding ASCII
Get-ChildItem $rel -File | ForEach-Object { '{0,-36} {1,12:N0} bytes' -f $_.Name, $_.Length }
