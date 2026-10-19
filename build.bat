@echo off
setlocal

REM ==========================================================================
REM  TxtConverter build script (GUI + CLI in one executable)
REM
REM  Produces both release flavours and verifies that the command line
REM  interface works in the published output:
REM    _ReleaseBuilds\Full_Standalone   - includes the .NET runtime (~60-100 MB)
REM    _ReleaseBuilds\Lite_NoRuntime    - requires .NET installed (~2-5 MB)
REM ==========================================================================

echo ==========================================
echo 0. PREPARATION
echo ==========================================
REM Close the app if it is running so the executable is not locked.
taskkill /F /IM TxtConverter.exe /T 2>nul
echo.

echo ==========================================
echo 1. CLEANING UP (Hard Reset)
echo ==========================================
if exist "_ReleaseBuilds" rd /s /q "_ReleaseBuilds"
if exist "TxtConverter\bin" rd /s /q "TxtConverter\bin"
if exist "TxtConverter\obj" rd /s /q "TxtConverter\obj"
echo.

echo ==========================================
echo 2. BUILDING: STANDALONE (FULL)
echo Includes .NET Runtime. Expected size: ~60-100 MB
echo ==========================================
dotnet publish TxtConverter/TxtConverter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o "_ReleaseBuilds/Full_Standalone"
if errorlevel 1 goto :fail
echo.

echo ==========================================
echo 3. CLEANING UP AGAIN
echo ==========================================
REM Clean so the Standalone settings cannot leak into the Lite build.
if exist "TxtConverter\bin" rd /s /q "TxtConverter\bin"
if exist "TxtConverter\obj" rd /s /q "TxtConverter\obj"
echo.

echo ==========================================
echo 4. BUILDING: FRAMEWORK-DEPENDENT (LITE)
echo Requires .NET installed. Expected size: ~2-5 MB
echo ==========================================
dotnet publish TxtConverter/TxtConverter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o "_ReleaseBuilds/Lite_NoRuntime"
if errorlevel 1 goto :fail
echo.

echo ==========================================
echo 5. CLI SMOKE TEST + SELF TEST
echo ==========================================
set "CLI=_ReleaseBuilds\Lite_NoRuntime\TxtConverter.exe"

if not exist "%CLI%" goto :fail

"%CLI%" version
if errorlevel 1 goto :fail

"%CLI%" help
if errorlevel 1 goto :fail

"%CLI%" presets list
if errorlevel 1 goto :fail

"%CLI%" config path
if errorlevel 1 goto :fail

REM Self test: sample project -> scan -> convert -> merged file -> PDF.
"%CLI%" test
if errorlevel 1 goto :fail

echo.
echo CLI smoke test and self test passed.
echo.

echo ==========================================
echo DONE! Check "_ReleaseBuilds" folder.
echo   GUI : "_ReleaseBuilds\Full_Standalone\TxtConverter.exe"
echo   CLI : "_ReleaseBuilds\Full_Standalone\TxtConverter.exe" help
echo ==========================================
exit /b 0

:fail
echo.
echo ==========================================
echo BUILD OR CLI TEST FAILED (errorlevel %errorlevel%)
echo ==========================================
exit /b 1