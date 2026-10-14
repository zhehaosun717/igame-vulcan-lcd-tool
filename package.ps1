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
$ver = '1.0.2'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Step($t) { Write-Host "== $t" }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" } }

if (-not $SkipUi) {
  Step 'build UI (vue-tsc + vite)'
  Push-Location (Join-Path $root 'ui')
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
$stageRoot = [IO.Path]::GetFullPath((Join-Path $rel 'stage'))
$releaseRoot = [IO.Path]::GetFullPath($rel).TrimEnd('\') + '\'
if (-not $stageRoot.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path' }
if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory $stage -Force | Out-Null
Step 'stage files'
foreach ($f in 'GpuLcd.exe', 'GpuLcdSend.exe') { Copy-Item (Join-Path $root $f) $stage }
Copy-Item (Join-Path $root 'lib\*.dll') $stage
Copy-Item (Join-Path $root 'installer\README.txt') $stage
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $stage
& (Join-Path $root 'tools\Collect-VendorRuntime.ps1') -Destination (Join-Path $stage 'vendor\iGameCenter')
$integration = Join-Path $stage 'integration'
New-Item -ItemType Directory -Path $integration -Force | Out-Null
& $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$integration\GpuLcdHook.exe" /r:System.Web.Extensions.dll "$root\integration\Hook.cs" "$root\integration\LinkState.cs"; Check 'GpuLcdHook'
foreach ($name in 'CodexLCD.template.html','hooks.example.json','codex-hooks.example.json') { Copy-Item -LiteralPath (Join-Path $root "integration\$name") -Destination $integration }
Copy-Item -LiteralPath (Join-Path $root 'anims') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $root 'integration\anims') -Destination $integration -Recurse
$licenses = Join-Path $stage 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
$ffmpeg = Join-Path $stage 'vendor\iGameCenter\SAVIConverter\tools\ffmpeg.exe'
function Capture-ToolText($exe, $arguments, $destination) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$exe; $info.Arguments=$arguments; $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'Dependency notice command timed out' }
        if ($process.ExitCode -ne 0) { throw 'Dependency notice command failed' }
        [IO.File]::WriteAllText($destination, $stdout.Result + $stderr.Result, [Text.UTF8Encoding]::new($false))
    } finally { $process.Dispose() }
}
Capture-ToolText $ffmpeg '-version' (Join-Path $licenses 'ffmpeg-version.txt')
Capture-ToolText $ffmpeg '-L' (Join-Path $licenses 'ffmpeg-license.txt')
$manifest = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
    [ordered]@{ path=$_.FullName.Substring($stage.Length + 1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLower() }
})
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $stage 'bundle-manifest.json') -Encoding UTF8
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
