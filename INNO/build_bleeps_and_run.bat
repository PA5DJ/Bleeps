@echo off
setlocal

set PROJ=%~dp0..\src\Bleeps\Bleeps.csproj
set PUBLISHDIR=%~dp0build

echo.
echo ============================================================
echo  Step 1 - Check the .NET 10 SDK
echo ============================================================
call "%~dp0ensure_dotnet_sdk.bat"
if errorlevel 1 (
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Step 2 - dotnet publish (framework-dependent, Release)
echo ============================================================
"%DOTNET%" publish "%PROJ%" -c Release -r win-x64 --self-contained false -o "%PUBLISHDIR%"
if errorlevel 1 (
    echo.
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Step 3 - Start Bleeps.exe
echo ============================================================
start "" "%PUBLISHDIR%\Bleeps.exe"
