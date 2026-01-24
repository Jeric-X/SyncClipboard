For /f "tokens=1* delims=:" %%i in ('Type Changes.md^|Findstr /n ".*"') do (
    if "%%i"=="1" (
        echo %%j>>version.txt
    )
    if "%%j"=="" (
        goto :end
    )
    echo %%j>>feature.txt
)
:end