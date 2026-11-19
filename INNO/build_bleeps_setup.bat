@echo off
setlocal

set PROJ=%~dp0..\src\Bleeps\Bleeps.csproj
set PUBLISHDIR=%~dp0build
set ISS=%~dp0bleeps.iss
rem ISCC comes from ensure_inno_setup.bat (step 1), which also fetches Inno Setup when it is missing.
rem Its path can hold "(x86)": NEVER expand it inside a parenthesised block.

echo.
echo ============================================================
echo  Step 1 - Check the build tools
echo ============================================================
call "%~dp0ensure_dotnet_sdk.bat"
if errorlevel 1 (
    pause
    exit /b 1
)

call "%~dp0ensure_inno_setup.bat"
if errorlevel 1 goto :no_inno
goto :tools_ok

:no_inno
pause
exit /b 1

:tools_ok

echo.
echo ============================================================
echo  Step 2 - Clear old setup EXEs and the old build
echo ============================================================
if exist "%~dp0BleepsSetup_*.exe" del /q "%~dp0BleepsSetup_*.exe"
if exist "%~dp0BleepsSetup_*.zip" del /q "%~dp0BleepsSetup_*.zip"
if exist "%~dp0BleepsSetup_*.sha256.txt" del /q "%~dp0BleepsSetup_*.sha256.txt"
if exist "%PUBLISHDIR%" rd /s /q "%PUBLISHDIR%"

echo.
echo ============================================================
echo  Step 3 - dotnet publish (framework-dependent, Release)
echo ============================================================
"%DOTNET%" publish "%PROJ%" -c Release -r win-x64 --self-contained false -o "%PUBLISHDIR%"
if errorlevel 1 (
    echo.
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)
rem Hamlib stays in build\hamlib: the installer puts it next to the exe, where Bleeps looks first.
if not exist "%PUBLISHDIR%\hamlib\libhamlib-4.dll" goto :no_hamlib

echo.
echo ============================================================
echo  Step 4 - Inno Setup compile Bleeps
echo ============================================================
"%ISCC%" "%ISS%"
if errorlevel 1 (
    echo.
    echo ERROR: Bleeps Inno Setup compile failed.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Step 5 - Pack the setup EXE into a ZIP of the same name
echo ============================================================
set "SETUPEXE="
for %%F in ("%~dp0BleepsSetup_*.exe") do set "SETUPEXE=%%~nxF"
if not defined SETUPEXE goto :no_setup
"%SystemRoot%\System32\tar.exe" -a -c -f "%~dp0%SETUPEXE:.exe=.zip%" -C "%~dp0." --options zip:compression=store "%SETUPEXE%"
if errorlevel 1 goto :zip_failed
echo Packed: "%~dp0%SETUPEXE:.exe=.zip%"

echo.
echo ============================================================
echo  Step 6 - SHA-256 of the setup and the ZIP
echo ============================================================
set "B_DIR=%~dp0"
set "B_EXE=%SETUPEXE%"
set "B_ZIP=%SETUPEXE:.exe=.zip%"
set "B_SHA=%SETUPEXE:.exe=.sha256.txt%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $l = foreach ($n in $env:B_EXE, $env:B_ZIP) { (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $env:B_DIR $n)).Hash.ToLower() + '  ' + $n }; Set-Content -LiteralPath (Join-Path $env:B_DIR $env:B_SHA) -Value $l -Encoding ascii; $l"
if errorlevel 1 echo WARNING: the SHA-256 could not be worked out. The setup and the ZIP are fine.

echo.
echo ============================================================
echo  Done!
echo  Setup EXE  : %~dp0%SETUPEXE%
echo  Setup ZIP  : same folder, same name, .zip
echo  Build files: %PUBLISHDIR%\
echo ============================================================
pause
exit /b 0

:no_hamlib
echo.
echo ERROR: hamlib\libhamlib-4.dll is missing in the build. Is the hamlib folder of this repository there?
pause
exit /b 1

:no_setup
echo.
echo ERROR: no BleepsSetup_*.exe found to pack.
pause
exit /b 1

:zip_failed
echo.
echo ERROR: packing the setup into a ZIP failed.
pause
exit /b 1
