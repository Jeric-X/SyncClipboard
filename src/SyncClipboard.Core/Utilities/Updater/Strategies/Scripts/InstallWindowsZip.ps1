param([Parameter(Mandatory=$true)][string]$Work, [switch]$Worker, [int]$SupervisorId = 0, [switch]$ElevatedRetry)
$ErrorActionPreference = 'Stop'
$task = Get-Content -LiteralPath (Join-Path $Work 'task.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$log = Join-Path $Work 'install.log'
$pidName = if ($Worker) { 'worker-pid' } else { 'helper-pid' }
[IO.File]::WriteAllText((Join-Path $Work $pidName), [string]$PID)
function Progress([string]$Phase, [int]$Percent) {
    try {
        [IO.File]::WriteAllText((Join-Path $Work 'progress.tmp'), "$Phase`n$Percent")
        Move-Item -LiteralPath (Join-Path $Work 'progress.tmp') -Destination (Join-Path $Work 'progress') -Force
    } catch {
        # Progress is optional; reporting failures must never interrupt replacement or rollback.
        Write-Verbose -Message $_.Exception.Message -ErrorAction SilentlyContinue
    }
}
function Mark([string]$Name) { [IO.File]::WriteAllText((Join-Path $Work $Name), '') }
function Exists([string]$Name) { Test-Path -LiteralPath (Join-Path $Work $Name) }
function Fail([string]$Message) {
    Add-Content -LiteralPath $log -Value $Message -Encoding UTF8
    [IO.File]::WriteAllText((Join-Path $Work 'failed.tmp'), $Message)
    Move-Item -LiteralPath (Join-Path $Work 'failed.tmp') -Destination (Join-Path $Work 'failed') -Force
}
function Launch {
    Start-Process -FilePath $task.Executable -WorkingDirectory ([IO.Path]::GetDirectoryName($task.Executable)) | Out-Null
}
function Get-Sha256([string]$Path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($Path)
        try { return [BitConverter]::ToString($hash.ComputeHash($stream)) }
        finally { $stream.Dispose() }
    } finally { $hash.Dispose() }
}
if (!$Worker) {
    try {
        $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + (Join-Path $Work 'InstallWindowsZip.ps1') + '" -Work "' + $Work + '" -Worker -SupervisorId ' + $PID
        $retryElevated = $false
        while ($true) {
            $parameters = @{ FilePath = (Join-Path $PSHOME 'powershell.exe'); ArgumentList = $arguments; PassThru = $true; WindowStyle = 'Hidden' }
            if ($task.Elevate -or $retryElevated) { $parameters.Verb = 'RunAs' }
            if ($retryElevated) { $parameters.ArgumentList += ' -ElevatedRetry' }
            try { $process = Start-Process @parameters }
            catch {
                # The previous worker restored everything. Canceling UAC must not leave the app closed.
                if ($retryElevated) { Mark 'restored'; Launch }
                throw
            }
            while (!$process.HasExited) {
                try {
                    $progressFile = Join-Path $Work 'progress'
                    if (Test-Path -LiteralPath $progressFile) {
                        $values = @(Get-Content -LiteralPath $progressFile -Encoding UTF8)
                        if ($values.Count -ge 2) {
                            $phase = $values[0]
                            if ($task.Language -like 'zh*') {
                                $labels = @{ waiting = '等待程序退出'; backup = '备份程序文件'; installing = '安装更新'; restoring = '恢复旧版本' }
                                if ($labels.ContainsKey($phase)) { $phase = $labels[$phase] }
                            }
                            Write-Progress -Activity 'SyncClipboard Update' -Status $phase -PercentComplete ([int]$values[1])
                        }
                    }
                } catch {
                    # The worker may replace the progress file while it is being read.
                    Write-Verbose -Message $_.Exception.Message -ErrorAction SilentlyContinue
                }
                Start-Sleep -Milliseconds 100
                $process.Refresh()
            }
            if (!$task.Elevate -and !$retryElevated -and (Exists 'needs-elevation') -and (Exists 'restored')) {
                if (Exists 'cancel') { throw 'Update handoff canceled.' }
                $retryElevated = $true
                Remove-Item -LiteralPath (Join-Path $Work 'needs-elevation')
                Remove-Item -LiteralPath (Join-Path $Work 'restored')
                continue
            }
            break
        }
        Write-Progress -Activity 'SyncClipboard Update' -Completed
        if ((Exists 'installed') -or (Exists 'restored')) { Launch }
        elseif (!(Exists 'failed')) { Fail 'The update helper exited unexpectedly.' }
    } catch {
        $cause = $_.Exception
        while ($cause) {
            if ($cause -is [ComponentModel.Win32Exception] -and $cause.NativeErrorCode -eq 1223) { Mark 'canceled' }
            $cause = $cause.InnerException
        }
        Fail $_.Exception.Message
    }
    if (Exists 'failed') {
        Write-Host (Get-Content -LiteralPath (Join-Path $Work 'failed') -Raw -Encoding UTF8)
        Write-Host "Log: $log"
        Read-Host 'Press Enter to close' | Out-Null
        exit 1
    }
    exit 0
}

function Assert-UpdateActive {
    if ((Exists 'cancel') -or ($supervisor -and $supervisor.HasExited)) { throw 'Update canceled or its window was closed.' }
}

function Copy-Cancellable([string]$Source, [string]$Destination, [hashtable]$Entry = $null) {
    Assert-UpdateActive
    $inputStream = [IO.File]::OpenRead($Source)
    try {
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            # A failed open leaves the destination untouched, including when it is exclusively locked.
            if ($Entry) {
                $Entry.Modified = $true
                $journal | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Work 'journal.json') -Encoding UTF8
            }
            $buffer = New-Object byte[] (1024 * 1024)
            while (($count = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                Assert-UpdateActive
                $outputStream.Write($buffer, 0, $count)
            }
        } finally { $outputStream.Dispose() }
    } finally { $inputStream.Dispose() }
    Assert-UpdateActive
}

function Assert-SafeDestination([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($task.Target).TrimEnd('\') + '\'
    if (!$full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escapes the installation directory.' }
    if ([IO.Directory]::Exists($full)) { throw 'An update file conflicts with an installed directory.' }
    foreach ($protected in $task.ProtectedPaths) {
        $protected = [IO.Path]::GetFullPath($protected).TrimEnd('\')
        if ($full.Equals($protected, [StringComparison]::OrdinalIgnoreCase) -or
            $full.StartsWith($protected + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Update would overwrite user data.' }
    }
    $current = $full
    while ($current -and $current.Length -ge $root.TrimEnd('\').Length) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Update destination contains a link.' }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

$journal = [Collections.Generic.List[object]]::new()
$replacementStarted = $false
$parentExited = $false
$supervisor = $null
try {
    if ($SupervisorId -gt 0) {
        $supervisor = [Diagnostics.Process]::GetProcessById($SupervisorId)
        # Keep a handle to this instance so PID reuse cannot hide a closed update window.
        $supervisor.Handle | Out-Null
    }
    Assert-UpdateActive
    if ($task.Kind -eq 'WindowsPortable') {
        $files = @(Get-ChildItem -LiteralPath $task.Stage -Recurse -File -Force)
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($task.Stage.Length).TrimStart('\')
            Assert-SafeDestination (Join-Path $task.Target $relative)
        }
        if ($ElevatedRetry) { [IO.Directory]::CreateDirectory($task.Backup) | Out-Null }
        else { New-Item -ItemType Directory -Path $task.Backup -ErrorAction Stop | Out-Null }
    }
    # Acquire write permission before telling the running application to exit.
    $probe = Join-Path $task.Target ('.update-probe-' + [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($probe, '')
    Remove-Item -LiteralPath $probe
    Progress 'waiting' -1
    Mark 'ready'
    $deadline = [DateTime]::UtcNow.AddMinutes(5)
    while (!(Exists 'commit')) {
        Assert-UpdateActive
        if ((Exists 'cancel') -or [DateTime]::UtcNow -ge $deadline) { throw 'Update handoff canceled.' }
        Start-Sleep -Milliseconds 100
    }
    $parent = Get-Process -Id $task.ProcessId -ErrorAction SilentlyContinue
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ($parent -and !$parent.WaitForExit(100)) {
        Assert-UpdateActive
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for SyncClipboard to exit.' }
    }
    $parentExited = $true
    Assert-UpdateActive

    if ($task.Kind -eq 'WindowsPortable') {
        # Back up every existing destination before modifying any installed file.
        $index = 0
        foreach ($file in $files) {
            Assert-UpdateActive
            $relative = $file.FullName.Substring($task.Stage.Length).TrimStart('\')
            $destination = Join-Path $task.Target $relative
            $backup = Join-Path $task.Backup $relative
            Assert-SafeDestination $destination
            $existed = Test-Path -LiteralPath $destination
            if ($existed) {
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
                Copy-Cancellable $destination $backup
            }
            $journal.Add(@{ Destination = $destination; Backup = $backup; Existed = $existed; Source = $file.FullName; Modified = $false })
            $index++
            Progress 'backup' ([int](100 * $index / $files.Count))
        }
        $replacementStarted = $true
        $index = 0
        foreach ($entry in $journal) {
            Assert-UpdateActive
            Assert-SafeDestination $entry.Destination
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($entry.Destination)) | Out-Null
            Copy-Cancellable $entry.Source $entry.Destination $entry
            $index++
            Progress 'installing' ([int](100 * $index / $journal.Count))
        }
    } else { throw 'Unsupported update format.' }
    Assert-UpdateActive
    Mark 'installed'
} catch {
    $failure = $_.Exception.Message
    $accessDenied = $false
    $cause = $_.Exception
    while ($cause) {
        if ($cause -is [UnauthorizedAccessException]) { $accessDenied = $true }
        $cause = $cause.InnerException
    }
    if ($replacementStarted) {
        $restoreErrors = [Collections.Generic.List[string]]::new()
        for ($i = $journal.Count - 1; $i -ge 0; $i--) {
            try {
                $entry = $journal[$i]
                if (!$entry.Modified) { continue }
                Progress 'restoring' ([int](100 * ($journal.Count - $i) / $journal.Count))
                if ($entry.Existed) {
                    # A locked file can reject the copy without changing its bytes. Avoid writing it again.
                    $unchanged = (Test-Path -LiteralPath $entry.Destination) -and
                        ((Get-Sha256 $entry.Destination) -eq (Get-Sha256 $entry.Backup))
                    if (!$unchanged) { Copy-Item -LiteralPath $entry.Backup -Destination $entry.Destination -Force }
                }
                elseif (Test-Path -LiteralPath $entry.Destination) { Remove-Item -LiteralPath $entry.Destination -Force }
            } catch { $restoreErrors.Add($_.Exception.Message) }
        }
        if ($restoreErrors.Count -eq 0) { Mark 'restored' }
        else { $failure += ' Rollback failed: ' + ($restoreErrors -join '; ') }
    }
    if (!$replacementStarted -and $parentExited) { Mark 'restored' }
    # Root-directory create permission does not imply write access to existing files or subdirectories.
    # Retry only after complete rollback; sharing violations and canceled updates must not trigger UAC.
    if ($accessDenied -and $parentExited -and !$task.Elevate -and !$ElevatedRetry -and
        (Exists 'restored') -and !(Exists 'cancel') -and (!$supervisor -or !$supervisor.HasExited)) {
        Add-Content -LiteralPath $log -Value ("Retrying with administrator permission: " + $failure) -Encoding UTF8
        Mark 'needs-elevation'
        exit 1
    }
    Fail $failure
    exit 1
}
Mark 'completed'
# Cleanup belongs to the main application, gated by target version and helper exit.
