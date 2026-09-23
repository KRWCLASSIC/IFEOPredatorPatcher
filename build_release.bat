@echo off
setlocal enabledelayedexpansion
title IFEOPredatorPatcher - Release Builder

echo ==========================================================
echo           IFEOPredatorPatcher Release Builder
echo ==========================================================
echo.

:: 1. Build the solution in Release mode
echo [*] Building solution in Release configuration...
dotnet build "%~dp0IFEOPredatorPatcher.sln" -c Release --nologo
if %ERRORLEVEL% neq 0 (
    echo.
    echo [-] Build failed! Please resolve compiler errors above.
    pause
    exit /b %ERRORLEVEL%
)

:: 2. Setup staging directories
set "DIST_DIR=%~dp0dist"
set "PACKAGE_DIR=%DIST_DIR%\IFEOPredatorPatcher"
set "PLUGINS_DIR=%PACKAGE_DIR%\plugins"
set "ZIP_FILE=%DIST_DIR%\IFEOPredatorPatcher-Release.zip"

echo [*] Staging release package...
if exist "%PACKAGE_DIR%" rmdir /s /q "%PACKAGE_DIR%"
mkdir "%PACKAGE_DIR%" 2>nul
mkdir "%PLUGINS_DIR%" 2>nul

:: 3. Copy executable and dependencies
set "BIN_PATCHER=%~dp0src\IFEOPredatorPatcher\bin\Release\net48"
set "BIN_PLUGIN=%~dp0src\Plugins\OpenPredatorPlugin\bin\Release\net48"

if exist "%BIN_PATCHER%\win-x86\IFEOPredatorPatcher.exe" (
    copy /y "%BIN_PATCHER%\win-x86\IFEOPredatorPatcher.exe" "%PACKAGE_DIR%\" >nul
    copy /y "%BIN_PATCHER%\win-x86\Mono.Cecil*.dll" "%PACKAGE_DIR%\" >nul
) else (
    copy /y "%BIN_PATCHER%\IFEOPredatorPatcher.exe" "%PACKAGE_DIR%\" >nul
    copy /y "%BIN_PATCHER%\Mono.Cecil*.dll" "%PACKAGE_DIR%\" >nul
)

if exist "%~dp0README.md" (
    copy /y "%~dp0README.md" "%PACKAGE_DIR%\" >nul
)

:: 4. Copy OpenPredator plugin
if exist "%BIN_PLUGIN%\win-x86\OpenPredatorPlugin.dll" (
    copy /y "%BIN_PLUGIN%\win-x86\OpenPredatorPlugin.dll" "%PLUGINS_DIR%\" >nul
) else (
    copy /y "%BIN_PLUGIN%\OpenPredatorPlugin.dll" "%PLUGINS_DIR%\" >nul
)

:: 5. Create ZIP archive
echo [*] Creating Release ZIP archive...
if exist "%ZIP_FILE%" del /f /q "%ZIP_FILE%"

powershell -NoProfile -Command "Compress-Archive -Path '%PACKAGE_DIR%\*' -DestinationPath '%ZIP_FILE%' -Force"
if %ERRORLEVEL% neq 0 (
    echo [-] Failed to create ZIP archive.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==========================================================
echo  [+] Build and Packaging Complete!
echo  [+] Output ZIP: %ZIP_FILE%
echo ==========================================================
echo.

pause
