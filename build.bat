@echo off
setlocal

set RUNTIME=%~1
if "%RUNTIME%"=="" set RUNTIME=win-x64

set PROJECT=src\Allure.GaugePlugin
set OUT_DIR=%PROJECT%\bin\Release\net10.0\%RUNTIME%
set PUBLISH_DIR=%OUT_DIR%\publish

if "%AOT%"=="" set AOT=true

echo Publishing for %RUNTIME% (AOT=%AOT%)...
dotnet publish %PROJECT% -r %RUNTIME% --self-contained -c Release -p:PublishAot=%AOT%
if errorlevel 1 exit /b 1

for /f "tokens=2 delims=:, " %%a in ('findstr /r "\"version\"" %PROJECT%\plugin.json') do set VERSION=%%~a
set ZIP_FILE=%OUT_DIR%\allure-report-%VERSION%-%RUNTIME%.zip
if exist "%ZIP_FILE%" del "%ZIP_FILE%"

echo Creating %ZIP_FILE%...
powershell -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_FILE%' -Force"
if errorlevel 1 exit /b 1

echo.
echo Done. Install with:
echo   gauge install allure-report --file %ZIP_FILE%
