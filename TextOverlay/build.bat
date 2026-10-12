@echo off
rem Builds the 32-bit cm3text.dll (needs the Visual Studio Build Tools with the C++ workload).
rem Run from any folder; the DLL is written next to this script.
setlocal
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat" -arch=x86 -host_arch=x64 >nul 2>&1
if errorlevel 1 (
    echo Could not find Visual Studio Build Tools - edit the path in build.bat
    exit /b 1
)
pushd "%~dp0"
cl /nologo /O2 /LD /MT /EHsc /W3 cm3text.cpp /Fe:cm3text.dll /link /DEF:cm3text.def user32.lib gdi32.lib
set RESULT=%errorlevel%
del /q cm3text.obj cm3text.lib cm3text.exp 2>nul
popd
exit /b %RESULT%
