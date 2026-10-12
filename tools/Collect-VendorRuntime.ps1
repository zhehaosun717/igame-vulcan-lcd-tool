# Copies only the runtime used by this tool, preserving upstream binaries.
param(
    [Parameter(Mandatory=$true)][string]$Destination,
    [string]$Source = 'C:\Program Files\iGameCenter'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$files = @(
    'iGameAPI.LCD.CSharp.dll', 'iGameAPI.NVAPI.CSharp.dll',
    'iGameAPI.Contracts.dll', 'iGameCenter.Contracts.dll', 'License.txt',
    'iGameAPI\iGameAPI.LCD.dll', 'iGameAPI\iGameAPI.NVAPI.dll',
    'SAVIConverter\configs\giftosavi_singledisplay.xml'
)
foreach ($file in $files) {
    $target = Join-Path $Destination $file
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $Source $file) -Destination $target
}
# The converter tools include their own codec DLLs and crash-handler resources.
Copy-Item -LiteralPath (Join-Path $Source 'SAVIConverter\tools') -Destination (Join-Path $Destination 'SAVIConverter') -Recurse

# JpegEncoder is x86 and requires VC90 CRT through an embedded SxS manifest.
# Ship a private assembly and an explicit redirect; do not install system DLLs.
$crt = Get-ChildItem -LiteralPath "$env:WINDIR\WinSxS" -Directory -Filter 'x86_microsoft.vc90.crt_*' |
    Sort-Object { [version]($_.Name.Split('_')[3]) } -Descending | Select-Object -First 1
if (-not $crt) { throw 'VC90 x86 CRT not found. Install the official VC++ 2008 x86 runtime on the build machine.' }
$version = $crt.Name.Split('_')[3]
$toolDir = Join-Path $Destination 'SAVIConverter\tools'
$crtDir = Join-Path $toolDir 'Microsoft.VC90.CRT'
New-Item -ItemType Directory -Path $crtDir -Force | Out-Null
foreach ($name in 'msvcr90.dll','msvcp90.dll','msvcm90.dll') { Copy-Item -LiteralPath (Join-Path $crt.FullName $name) -Destination $crtDir }
Copy-Item -LiteralPath (Join-Path "$env:WINDIR\WinSxS\Manifests" ($crt.Name + '.manifest')) -Destination (Join-Path $crtDir 'Microsoft.VC90.CRT.manifest')
$config = @"
<?xml version="1.0" encoding="UTF-8"?>
<configuration><windows><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><dependentAssembly>
<assemblyIdentity type="win32" name="Microsoft.VC90.CRT" processorArchitecture="x86" publicKeyToken="1fc8b3b9a1e18e3b" />
<bindingRedirect oldVersion="9.0.0.0-9.0.65535.65535" newVersion="$version" />
</dependentAssembly></assemblyBinding></windows></configuration>
"@
Set-Content -LiteralPath (Join-Path $toolDir 'JpegEncoder.exe.config') -Value $config -Encoding UTF8
Write-Host "Bundled LCD runtime, SAVI converter and private x86 VC90 CRT $version"
