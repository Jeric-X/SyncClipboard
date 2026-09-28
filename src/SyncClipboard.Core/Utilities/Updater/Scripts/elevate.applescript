on run argv
    set scriptPath to item 1 of argv
    set workPath to item 2 of argv
    try
        do shell script "/bin/sh " & quoted form of scriptPath & " " & quoted form of workPath & " worker" with administrator privileges
    on error errorMessage number errorNumber
        if errorNumber is -128 then
            do shell script "/usr/bin/touch " & quoted form of (workPath & "/canceled")
        end if
        error errorMessage number errorNumber
    end try
end run
