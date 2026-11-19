@echo off
rem ============================================================================
rem  Make sure this machine can compile the installer: Inno Setup 6 has to be here.
rem
rem  Called by build_bleeps_setup.bat. Sets ISCC to an ISCC.exe that exists
rem  and returns 0; returns 1 if it could not get there, with a reason on screen.
rem
rem  Deliberately no setlocal: the caller needs ISCC afterwards.
rem
rem  Fetched when missing (PA5DJ, v1.0.416) - this replaces the old rule that Inno
rem  was never fetched by a build script. It installs for THIS user only
rem  (/CURRENTUSER, into %LOCALAPPDATA%\Programs), so no administrator prompt.
rem
rem  NEVER expand one of these paths inside a parenthesised block: they can hold
rem  "(x86)", and that bracket ends the block early ("... was unexpected at this
rem  time"). Single-line ifs and gotos only.
rem ============================================================================

rem Pinned to Inno Setup 6 on purpose: bleeps.iss is written for 6, and
rem jrsoftware's own download page also offers 7 now. The link is the one that
rem page gives (GitHub release). download.php/is.exe is NOT a file - it redirects
rem to the HTML page, so curl would "succeed" and run a web page as an installer.
set "INNOURL=https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe"
set "INNOEXE=%TEMP%\innosetup-6-install.exe"

call :find_iscc
if defined ISCC goto :ok

echo.
echo ============================================================
echo  Inno Setup 6 not found - fetching it
echo ============================================================
echo  From : %INNOURL%
echo  To   : %INNOEXE%
echo.

rem curl ships with Windows 10 1803 and later; PowerShell is the fallback.
where curl >nul 2>&1
if errorlevel 1 goto :ps_download
curl -L --fail --progress-bar -o "%INNOEXE%" "%INNOURL%"
if errorlevel 1 goto :download_failed
goto :install

:ps_download
echo  (no curl - using PowerShell)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ProgressPreference='SilentlyContinue'; try { Invoke-WebRequest -Uri '%INNOURL%' -OutFile '%INNOEXE%' -UseBasicParsing } catch { exit 1 }"
if errorlevel 1 goto :download_failed

:install
if not exist "%INNOEXE%" goto :download_failed
rem A web page instead of the installer is a few KB; the installer is about 10 MB.
for %%A in ("%INNOEXE%") do if %%~zA LSS 1000000 goto :download_failed

echo.
echo ============================================================
echo  Installing Inno Setup 6 (this user only)
echo ============================================================
rem /SILENT rather than /VERYSILENT: a progress window, so a minute of nothing
rem does not look like a hung script. /SP- skips the "this will install" question.
"%INNOEXE%" /SILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CURRENTUSER
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :install_failed
del /q "%INNOEXE%" >nul 2>&1

call :find_iscc
if not defined ISCC goto :still_missing
echo.
echo  Inno Setup 6 installed.
goto :ok

rem ---- outcomes -----------------------------------------------------------

:ok
echo  Inno Setup: "%ISCC%"
exit /b 0

:download_failed
echo.
echo  ERROR: could not download Inno Setup.
echo  Check the internet connection, or install it by hand from
echo  https://jrsoftware.org/isdl.php
exit /b 1

:install_failed
echo.
echo  ERROR: the Inno Setup installer returned %RC%.
echo  Install it by hand from https://jrsoftware.org/isdl.php and run this again.
exit /b 1

:still_missing
echo.
echo  ERROR: the installer finished but ISCC.exe is not in any of the usual places.
echo  Installed somewhere else? Set ISCC by hand before calling the build script.
exit /b 1

rem ---- helpers ------------------------------------------------------------

rem The places Inno 6 installs to: for all users (32- and 64-bit Program Files),
rem for this user only, and anything on PATH. An ISCC set by the caller wins.
:find_iscc
if defined ISCC if exist "%ISCC%" exit /b 0
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if defined ISCC exit /b 0
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if defined ISCC exit /b 0
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if defined ISCC exit /b 0
for /f "delims=" %%i in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%i"
exit /b 0
