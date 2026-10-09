@echo off
rem Builds GpuLcd.exe (x64) with the Roslyn compiler that ships with Visual Studio 2022.
set CSC="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
set IG=C:\Program Files\iGameCenter
%CSC% /nologo /target:winexe /platform:x64 /optimize+ /out:"%~dp0GpuLcd.exe" ^
  /r:"%IG%\iGameAPI.LCD.CSharp.dll" /r:"%IG%\iGameAPI.NVAPI.CSharp.dll" /r:"%IG%\iGameAPI.Contracts.dll" /r:"%IG%\iGameCenter.Contracts.dll" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.Web.dll ^
  "%~dp0GpuLcd.cs" "%~dp0Storage.cs" "%~dp0Integrations.cs" "%~dp0SignalBridge.cs" "%~dp0Widgets.cs" "%~dp0DisplayJobs.cs" "%~dp0NightMode.cs" "%~dp0integration\LinkState.cs" "%~dp0integration\CompletionReader.cs"
