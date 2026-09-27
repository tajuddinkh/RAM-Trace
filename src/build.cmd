@echo off
setlocal
cd /d "%~dp0"

echo RAM Trace V2.0 - local source build

echo This build script uses the .NET Framework 4.8 C# compiler already installed with Windows.
echo.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: .NET Framework C# compiler was not found.
  exit /b 1
)

if exist RAM-Trace.exe (
  echo NOTE: Exit an existing RAM Trace from its tray before rebuilding.
  echo.
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:app.manifest /win32icon:RAM-Trace.ico /out:RAM-Trace.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll RAMTrace.cs PerformanceDiagnostics.cs ProcessGuideContent.cs ProcessClassification.cs

if errorlevel 1 (
  echo.
  echo BUILD FAILED.
  echo If RAM-Trace.exe is in use, exit RAM Trace from the tray and run build.cmd again.
  exit /b 1
)

echo.
echo BUILD SUCCESSFUL: %CD%\RAM-Trace.exe
echo.
echo SHA-256 of this exact build:
where certutil >nul 2>&1
if errorlevel 1 (
  echo certutil is unavailable, so the hash could not be printed.
) else (
  certutil -hashfile RAM-Trace.exe SHA256
)

echo.
echo RAM Trace was NOT started automatically.
echo Keep antivirus real-time protection ON. If this unsigned build is quarantined,
echo restore/allow this exact build or submit it as a false positive to your antivirus vendor.
echo Do not disable protection globally.
endlocal


