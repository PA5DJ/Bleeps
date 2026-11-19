@echo off
rem ============================================================================
rem  Make sure this machine can build Bleeps: a .NET 10 SDK has to be present.
rem
rem  Called by the three build scripts. Sets DOTNET to a dotnet.exe that works and
rem  returns 0; returns 1 if it could not get there, with a reason on screen.
rem
rem  Deliberately no setlocal: the caller needs DOTNET afterwards.
rem
rem  This is the SDK, which is a different thing from what bleeps.iss fetches.
rem  That one installs the desktop RUNTIME on the machine of someone who is going
rem  to run Bleeps; this one installs the SDK on a machine that is going to
rem  build it. A machine can have either without the other.
rem ============================================================================

set "SDKURL=https://aka.ms/dotnet/10.0/dotnet-sdk-win-x64.exe"
set "SDKEXE=%TEMP%\dotnet-sdk-10-win-x64.exe"
set "DOTNETDIR=%ProgramFiles%\dotnet"

call :find_dotnet
if defined DOTNET call :have_sdk10 && goto :ok

echo.
echo ============================================================
echo  .NET 10 SDK not found - fetching it
echo ============================================================
if not defined DOTNET echo  No dotnet on this machine at all.
if defined DOTNET     echo  dotnet is here, but no 10.x SDK is installed.
echo.
echo  From : %SDKURL%
echo  To   : %SDKEXE%
echo.

rem curl ships with Windows 10 1803 and later; PowerShell is the fallback for
rem anything older.
where curl >nul 2>&1
if errorlevel 1 goto :ps_download

curl -L --fail --progress-bar -o "%SDKEXE%" "%SDKURL%"
if errorlevel 1 goto :download_failed
goto :install

:ps_download
echo  (no curl - using PowerShell)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ProgressPreference='SilentlyContinue'; try { Invoke-WebRequest -Uri '%SDKURL%' -OutFile '%SDKEXE%' -UseBasicParsing } catch { exit 1 }"
if errorlevel 1 goto :download_failed

:install
if not exist "%SDKEXE%" goto :download_failed

echo.
echo ============================================================
echo  Installing the .NET 10 SDK
echo ============================================================
echo  Windows will ask for administrator rights - the SDK installs
echo  for the whole machine, which is what Visual Studio expects.
echo.

rem /passive rather than /quiet on purpose: this takes a couple of minutes and a
rem window that shows nothing at all looks like a script that has hung. It still
rem needs no clicking beyond the elevation prompt.
"%SDKEXE%" /install /passive /norestart
set "RC=%ERRORLEVEL%"

rem 3010 means "installed, but Windows wants a restart". The SDK itself is usable.
if "%RC%"=="3010" echo  Installed. Windows would like a restart at some point.
if not "%RC%"=="0" if not "%RC%"=="3010" goto :install_failed

del /q "%SDKEXE%" >nul 2>&1

rem PATH is not refreshed inside a running command prompt, so look for the SDK
rem where the installer puts it rather than trusting the shell to find it.
if exist "%DOTNETDIR%\dotnet.exe" set "DOTNET=%DOTNETDIR%\dotnet.exe"
call :find_dotnet_if_unset
if not defined DOTNET goto :still_missing
call :have_sdk10 || goto :still_missing

echo.
echo  .NET 10 SDK installed.
goto :ok

rem ---- outcomes -----------------------------------------------------------

:ok
echo.
echo  Using: %DOTNET%
for /f "tokens=*" %%v in ('"%DOTNET%" --version 2^>nul') do echo  SDK   : %%v
exit /b 0

:download_failed
echo.
echo  ERROR: could not download the SDK.
echo  Check the internet connection, or install it by hand from
echo  https://dotnet.microsoft.com/en-us/download/dotnet/10.0
exit /b 1

:install_failed
echo.
echo  ERROR: the SDK installer returned %RC%.
echo  If that was a cancelled administrator prompt, run this script again and
echo  allow it. Otherwise install by hand from
echo  https://dotnet.microsoft.com/en-us/download/dotnet/10.0
exit /b 1

:still_missing
echo.
echo  ERROR: the installer finished but no 10.x SDK is visible.
echo  Close this window, open a new one and try again - a fresh command prompt
echo  picks up the PATH the installer added.
exit /b 1

rem ---- helpers ------------------------------------------------------------

:find_dotnet
set "DOTNET="
where dotnet >nul 2>&1 && set "DOTNET=dotnet"
if not defined DOTNET if exist "%DOTNETDIR%\dotnet.exe" set "DOTNET=%DOTNETDIR%\dotnet.exe"
exit /b 0

:find_dotnet_if_unset
if not defined DOTNET call :find_dotnet
exit /b 0

rem A machine can have the runtime and no SDK, and then dotnet answers but cannot
rem build. --list-sdks prints nothing at all in that case, which is the test.
:have_sdk10
"%DOTNET%" --list-sdks 2>nul | findstr /b /c:"10." >nul
exit /b %ERRORLEVEL%
