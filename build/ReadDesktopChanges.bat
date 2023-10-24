For /f "tokens=1* delims=:" %%i in ('Type ..\src\SyncClipboard.Desktop\Changes.md^|Findstr /n ".*"') do (
    if "%%j"=="" (
        goto :end
    )
    if "%%i"=="1" (
        echo | set /p dummyName="%%j">version.txt
    ) ^
    else (
        echo %%j>>feature.txt
    )
)
:end