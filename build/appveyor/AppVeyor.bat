setlocal enabledelayedexpansion
set newline=^<br^>
For /f "tokens=1* delims=:" %%i in ('Type Changes.md^|Findstr /n ".*"') do (
if "%%i"=="1" set VERSION=%%j
if "%%j"=="" (
echo !FEATURE!>feature.txt
echo !VERSION!>version.txt
goto :end
)
set CHILDFEATURE=%%j
set "FEATURE=!FEATURE!!CHILDFEATURE!!newline!"

)
:end
endlocal
For /f "tokens=1* delims=:" %%i in ('Type version.txt^|Findstr /n ".*"') do (
if "%%i"=="1" set VERSION=%%j
)
For /f "tokens=1* delims=:" %%i in ('Type feature.txt^|Findstr /n ".*"') do (
if "%%i"=="1" set FEATURE=%%j
)