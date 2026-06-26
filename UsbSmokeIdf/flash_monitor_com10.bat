@echo off
setlocal

cd /d "%~dp0"
call "C:\Espressif\v6.0\esp-idf\export.bat"
idf.py -p COM10 -b 115200 -B build_init_fix flash monitor

endlocal
