@echo off
setlocal ENABLEDELAYEDEXPANSION

:: Root folder where your .md diagrams live
set "INPUTDIR=docs\Data"
set "OUTFORMAT=svg"
set "THEME=default"
set "SCALE=2"
set "WIDTH=1600"

:: Find mmdc
set "MMDC="
for /f "delims=" %%p in ('where mmdc 2^>nul') do set "MMDC=%%p"
if not defined MMDC set "MMDC=C:\Users\%USERNAME%\AppData\Roaming\npm\mmdc.cmd"

if not exist "%MMDC%" (
  echo [ERROR] Mermaid CLI not found.
  exit /b 1
)

:: Options
set "OPTS=-t %THEME% -s %SCALE%"
if not "%WIDTH%"=="" set "OPTS=%OPTS% -w %WIDTH%"

echo === Mermaid Export ===
echo Using mmdc: %MMDC%
echo Input dir:  %INPUTDIR%
echo.

set "COUNT=0"
for %%f in ("%INPUTDIR%\*.md") do (
  echo [*] Exporting %%f...
  "%MMDC%" -i "%%f" -o "%%~dpnf.%OUTFORMAT%" %OPTS%
  if not errorlevel 1 set /a COUNT+=1
)

echo.
echo Done. Exported %COUNT% file^(s^).
endlocal
