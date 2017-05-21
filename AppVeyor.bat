set FEATURE=####
setlocal enabledelayedexpansion
For /f "tokens=1* delims=:" %%i in ('Type Changes.md^|Findstr /n ".*"') do (
if "%%i"=="1" set VERSION=%%j
if "%%j"=="" goto :end
set CHILDFEATURE=%%j
set "FEATURE=!FEATURE!!CHILDFEATURE:#=^<br^>!"
)
:end
setlocal disabledelayedexpansion