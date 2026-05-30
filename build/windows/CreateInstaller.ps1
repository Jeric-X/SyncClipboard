param(
    [Parameter(Mandatory=$true, HelpMessage="Path to the folder containing application files")]
    [string]$SourceFolder,
    
    [Parameter(Mandatory=$false, HelpMessage="Application version number")]
    [string]$Version = "1.0.0",
    
    [Parameter(Mandatory=$false, HelpMessage="Output folder path")]
    [string]$OutputFolder = "",
    
    [Parameter(Mandatory=$false, HelpMessage="InnoSetup compiler path")]
    [string]$ISCCPath = "",
    
    [Parameter(Mandatory=$false, HelpMessage="InnoSetup script path")]
    [string]$IssPath = "",

    [Parameter(Mandatory=$false, HelpMessage="Target architecture (x64 or arm64)")]
    [string]$TargetArch = "x64"
)

$ErrorActionPreference = "Stop"

$AppName = "SyncClipboard"
$AppExe = "SyncClipboard.exe"

if (-not (Test-Path $SourceFolder)) {
    Write-Error "Source folder not found: $SourceFolder"
    exit 1
}

$SourceFolder = (Resolve-Path $SourceFolder).Path

$exePath = Join-Path $SourceFolder $AppExe
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found in source folder: $AppExe"
    exit 1
}

if ([string]::IsNullOrEmpty($OutputFolder)) {
    $OutputFolder = Join-Path $PSScriptRoot "output"
}

if (-not (Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
}

$OutputFolder = (Resolve-Path $OutputFolder).Path

if ([string]::IsNullOrEmpty($IssPath)) {
    $IssPath = Join-Path $PSScriptRoot "setup.iss"
}

if (-not (Test-Path $IssPath)) {
    Write-Error "InnoSetup script not found: $IssPath"
    exit 1
}

$IssPath = (Resolve-Path $IssPath).Path

if ([string]::IsNullOrEmpty($ISCCPath)) {
    $commonPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 5\ISCC.exe"
    )
    
    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            $ISCCPath = $path
            break
        }
    }
    
    if ([string]::IsNullOrEmpty($ISCCPath)) {
        Write-Error "InnoSetup compiler (ISCC.exe) not found. Specify the path via -ISCCPath parameter."
        Write-Host "Download InnoSetup from https://jrsoftware.org/isdl.php"
        exit 1
    }
}

Write-Host "Using InnoSetup: $ISCCPath" -ForegroundColor Green
Write-Host "Using script: $IssPath" -ForegroundColor Green
Write-Host "Source folder: $SourceFolder" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "Output folder: $OutputFolder" -ForegroundColor Cyan
Write-Host "Target arch: $TargetArch" -ForegroundColor Cyan
Write-Host "Building installer..." -ForegroundColor Yellow

$arguments = @(
    "`"$IssPath`"",
    "/DSourceFolder=`"$SourceFolder`"",
    "/DAppVersion=$Version",
    "/DOutputDir=`"$OutputFolder`"",
    "/DTargetArch=$TargetArch"
)

try {
    $process = Start-Process -FilePath $ISCCPath -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    
    if ($process.ExitCode -eq 0) {
        $installerPath = Join-Path $OutputFolder "$AppName-$Version-installer.exe"
        Write-Host "`nInstaller created successfully!" -ForegroundColor Green
        Write-Host "Installer path: $installerPath" -ForegroundColor Cyan
        
        if (Test-Path $installerPath) {
            $fileInfo = Get-Item $installerPath
            Write-Host "File size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
        }
    }
    else {
        Write-Error "InnoSetup compilation failed with exit code: $($process.ExitCode)"
    }
}
catch {
    Write-Error "Error during compilation: $_"
}
