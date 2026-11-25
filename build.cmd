@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found in .NET Framework directory.
  exit /b 1
)

if not exist build mkdir build
if not exist dist mkdir dist

echo [1/3] building icon generator...
"%CSC%" /nologo /codepage:65001 /target:exe /out:build\IconGen.exe /r:System.dll /r:System.Drawing.dll tools\IconGen.cs || exit /b 1

echo [2/3] rendering app.ico...
build\IconGen.exe app.ico || exit /b 1

echo [3/3] building dist\ClickCounter.exe...
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu /win32icon:app.ico ^
  /out:dist\ClickCounter.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\Theme.cs src\KeyCatalog.cs src\StatsStore.cs src\InputLog.cs src\InputHook.cs src\Controls.cs ^
  src\RankList.cs src\ConfirmReset.cs src\SaveAsForm.cs src\FileExport.cs src\KeyLogForm.cs src\HistoryForm.cs src\TraySupport.cs src\MainForm.cs src\Program.cs || exit /b 1

echo.
echo Build OK: dist\ClickCounter.exe
