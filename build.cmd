@echo off
rem Builds GpuLcd.exe (x64) with the Roslyn compiler that ships with Visual Studio 2022.
rem The front end (ui\dist, built with "npm run build" in ui\) is zipped and embedded into the exe.
set CSC="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
set IG=C:\Program Files\iGameCenter
if exist "%~dp0ui-dist.zip" del "%~dp0ui-dist.zip"
if exist "%~dp0ui\dist\index.html" tar -a -c -f "%~dp0ui-dist.zip" -C "%~dp0ui\dist" .
set RES=
if exist "%~dp0ui-dist.zip" set RES=/resource:"%~dp0ui-dist.zip",GpuLcd.ui-dist.zip
if "%RES%"=="" echo WARNING: ui\dist not found - building WITHOUT the embedded new UI
%CSC% /nologo /target:winexe /platform:x64 /optimize+ /out:"%~dp0GpuLcd.exe" ^
  /win32icon:"%~dp0app.ico" /win32manifest:"%~dp0app.manifest" %RES% ^
  /r:"%IG%\iGameAPI.LCD.CSharp.dll" /r:"%IG%\iGameAPI.NVAPI.CSharp.dll" /r:"%IG%\iGameAPI.Contracts.dll" /r:"%IG%\iGameCenter.Contracts.dll" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.Web.dll /r:System.IO.Compression.dll ^
  /r:"%~dp0lib\Microsoft.Web.WebView2.Core.dll" /r:"%~dp0lib\Microsoft.Web.WebView2.WinForms.dll" ^
  "%~dp0AssemblyInfo.cs" "%~dp0GpuLcd.cs" "%~dp0Storage.cs" "%~dp0Integrations.cs" "%~dp0SignalBridge.cs" "%~dp0Widgets.cs" "%~dp0DisplayJobs.cs" "%~dp0NightMode.cs" "%~dp0WebUi.cs" "%~dp0integration\LinkState.cs" "%~dp0integration\CompletionReader.cs"
if errorlevel 1 exit /b 1
copy /y "%~dp0lib\*.dll" "%~dp0" >nul
