@echo off
setlocal

cd /d "%~dp0"
if errorlevel 1 goto error

if not exist "C:\Espressif\v6.0\esp-idf\export.bat" (
    echo ERROR: No se encontro ESP-IDF en C:\Espressif\v6.0\esp-idf.
    goto error
)

echo Inicializando ESP-IDF...
call "C:\Espressif\v6.0\esp-idf\export.bat"
if errorlevel 1 goto error

echo Compilando UsbSmokeIdf...
call idf.py build
if errorlevel 1 goto error

echo.
echo Compilacion completada. Firmware: %~dp0build\UsbSmokeIdf.bin
if /i not "%~1"=="--no-pause" pause
exit /b 0

:error
echo.
echo ERROR: La compilacion no pudo completarse. Revisa los mensajes anteriores.
if /i not "%~1"=="--no-pause" pause
exit /b 1
