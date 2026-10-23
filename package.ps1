# Builds the finished product into .\release :
#   GpuLcd-Setup-<ver>.exe        installer (per-user, no admin) - embeds the portable payload
#   GpuLcd-<ver>-portable.zip     portable folder
#   dsh-gpulcd-status-<ver>.zip   standalone runtime plugin
#   SHA256SUMS.txt
# Usage: .\package.ps1 (always rebuilds the UI; never launches the app)
# ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
param()
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ver = '1.0.5'
$pluginVer = '0.1.1'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Step($t) { Write-Host "== $t" }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" } }

Step 'check release metadata and frozen plugin build'
$pkg = Get-Content -LiteralPath (Join-Path $root 'dsh-plugin\package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$plugin = Get-Content -LiteralPath (Join-Path $root 'dsh-plugin\dsh.plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($pkg.version -ne $pluginVer -or $plugin.version -ne $pluginVer) { throw 'Plugin version mismatch' }
if ($pkg.engines.dsh -ne '>=0.2.1-rc.2' -or $plugin.engines.dsh -ne $pkg.engines.dsh -or $pkg.engines.node -ne '>=24') { throw 'Plugin compatibility mismatch' }
foreach ($name in 'AssemblyInfo.cs','installer\Setup.cs') {
  $text = Get-Content -LiteralPath (Join-Path $root $name) -Raw -Encoding UTF8
  if (-not $text.Contains('AssemblyVersion("' + $ver + '.0")') -or -not $text.Contains('AssemblyFileVersion("' + $ver + '.0")')) { throw "Host version mismatch: $name" }
}
& node (Join-Path $root 'dsh-plugin\build.mjs') --check; Check 'plugin build --check'

Step 'build UI (vue-tsc + vite)'
  Push-Location (Join-Path $root 'ui')
  try { cmd /c "npm run build -- --emptyOutDir --configLoader runner"; Check 'vite build' } finally { Pop-Location }
if (-not (Test-Path (Join-Path $root 'ui\dist\index.html'))) { throw 'ui\dist\index.html missing - build the UI first' }

Step 'build GpuLcd.exe (UI embedded)'
cmd /c (Join-Path $root 'build.cmd') ; Check 'build.cmd'
$exe = Join-Path $root 'GpuLcd.exe'
if (-not (Test-Path (Join-Path $root 'ui-dist.zip'))) { throw 'ui-dist.zip was not produced; the exe would not contain the UI' }

Step 'build GpuLcdSend.exe (command line helper)'
& $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$root\GpuLcdSend.exe" /r:System.Web.Extensions.dll /r:System.Drawing.dll "$root\integration\DisplaySend.cs" "$root\DisplayJobs.cs" ; Check 'GpuLcdSend'

$rel = Join-Path $root 'release'
$stage = Join-Path $rel 'stage\GpuLcd'
New-Item -ItemType Directory -Path $rel -Force | Out-Null
# Preserve previous outputs in a fresh directory. Never replace an old backup.
$backup = Join-Path $rel ('backups\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N'))
foreach ($name in "GpuLcd-Setup-$ver.exe", "GpuLcd-$ver-portable.zip", "dsh-gpulcd-status-$pluginVer.zip", 'SHA256SUMS.txt') {
  $old = Join-Path $rel $name
  if (Test-Path -LiteralPath $old) {
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath $old -Destination (Join-Path $backup $name)
  }
}
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
# Only the plugin runtime is distributed; no src/tests/dependencies/profile files.
$dshStage = Join-Path $stage 'dsh-plugin'
New-Item -ItemType Directory -Path $dshStage -Force | Out-Null
foreach ($name in 'package.json','dsh.plugin.json','cordis.patch.yml','README.md','CONTRACTS.md') {
  Copy-Item -LiteralPath (Join-Path $root "dsh-plugin\$name") -Destination $dshStage
}
Copy-Item -LiteralPath (Join-Path $root 'dsh-plugin\lib') -Destination $dshStage -Recurse
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $dshStage
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
foreach ($name in 'dswork.gif','dsask.gif','dsdone.gif') {
  if (-not (Test-Path -LiteralPath (Join-Path $stage "anims\$name"))) { throw "Missing DSH animation: $name" }
}
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
[IO.File]::WriteAllText((Join-Path $stage 'bundle-manifest.json'), ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Get-ChildItem $stage | ForEach-Object { '{0,-40} {1,10:N0}' -f $_.Name, $_.Length }

Step 'portable zip'
$portable = Join-Path $rel "GpuLcd-$ver-portable.zip"
if (Test-Path $portable) { Remove-Item $portable }
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $rel 'stage'), $portable, [IO.Compression.CompressionLevel]::Optimal, $false)

Step 'standalone plugin zip (same bytes as the portable plugin)'
$pluginZip = Join-Path $rel "dsh-gpulcd-status-$pluginVer.zip"
if (Test-Path -LiteralPath $pluginZip) { Remove-Item -LiteralPath $pluginZip }
$archive = [IO.Compression.ZipFile]::Open($pluginZip, [IO.Compression.ZipArchiveMode]::Create)
try {
  Get-ChildItem -LiteralPath $dshStage -File -Recurse | Sort-Object FullName | ForEach-Object {
    $entry = 'dsh-gpulcd-status/' + $_.FullName.Substring($dshStage.Length + 1).Replace('\','/')
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
} finally { $archive.Dispose() }

Step 'installer payload + Setup.exe'
$payload = Join-Path $rel 'payload.zip'
if (Test-Path $payload) { Remove-Item $payload }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)
$setup = Join-Path $rel "GpuLcd-Setup-$ver.exe"
if (Test-Path $setup) { Remove-Item $setup }
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$setup" "/win32icon:$root\app.ico" "/win32manifest:$root\app.manifest" "/resource:$payload,payload.zip" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$root\installer\Setup.cs" ; Check 'Setup'
Remove-Item $payload

Step 'checksums'
$lines = foreach ($f in $setup, $portable, $pluginZip) { '{0}  {1}' -f (Get-FileHash $f -Algorithm SHA256).Hash.ToLower(), (Split-Path $f -Leaf) }
Set-Content (Join-Path $rel 'SHA256SUMS.txt') $lines -Encoding ASCII
Get-ChildItem $rel -File | ForEach-Object { '{0,-36} {1,12:N0} bytes' -f $_.Name, $_.Length }
