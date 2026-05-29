param(
    [Parameter(Mandatory=$true, HelpMessage="包含应用程序文件的文件夹路径")]
    [string]$SourceFolder,
    
    [Parameter(Mandatory=$false, HelpMessage="应用程序版本号")]
    [string]$Version = "1.0.0",
    
    [Parameter(Mandatory=$false, HelpMessage="输出文件夹路径")]
    [string]$OutputFolder = "",
    
    [Parameter(Mandatory=$false, HelpMessage="InnoSetup 编译器路径")]
    [string]$ISCCPath = "",
    
    [Parameter(Mandatory=$false, HelpMessage="InnoSetup 脚本路径")]
    [string]$IssPath = ""
)

$ErrorActionPreference = "Stop"

$AppName = "SyncClipboard"
$AppExe = "SyncClipboard.exe"

if (-not (Test-Path $SourceFolder)) {
    Write-Error "源文件夹不存在: $SourceFolder"
    exit 1
}

$SourceFolder = (Resolve-Path $SourceFolder).Path

$exePath = Join-Path $SourceFolder $AppExe
if (-not (Test-Path $exePath)) {
    Write-Error "在源文件夹中找不到可执行文件: $AppExe"
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
    Write-Error "找不到 InnoSetup 脚本文件: $IssPath"
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
        Write-Error "找不到 InnoSetup 编译器 (ISCC.exe)。请通过 -ISCCPath 参数指定路径。"
        Write-Host "可以从 https://jrsoftware.org/isdl.php 下载 InnoSetup"
        exit 1
    }
}

Write-Host "使用 InnoSetup: $ISCCPath" -ForegroundColor Green
Write-Host "使用脚本文件: $IssPath" -ForegroundColor Green
Write-Host "源文件夹: $SourceFolder" -ForegroundColor Cyan
Write-Host "版本号: $Version" -ForegroundColor Cyan
Write-Host "输出目录: $OutputFolder" -ForegroundColor Cyan
Write-Host "开始编译安装包..." -ForegroundColor Yellow

$arguments = @(
    "`"$IssPath`"",
    "/DSourceFolder=`"$SourceFolder`"",
    "/DAppVersion=$Version",
    "/DOutputDir=`"$OutputFolder`""
)

try {
    $process = Start-Process -FilePath $ISCCPath -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    
    if ($process.ExitCode -eq 0) {
        $installerPath = Join-Path $OutputFolder "$AppName-$Version-installer.exe"
        Write-Host "`n安装包创建成功!" -ForegroundColor Green
        Write-Host "安装包位置: $installerPath" -ForegroundColor Cyan
        
        if (Test-Path $installerPath) {
            $fileInfo = Get-Item $installerPath
            Write-Host "文件大小: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
        }
    }
    else {
        Write-Error "InnoSetup 编译失败，退出代码: $($process.ExitCode)"
    }
}
catch {
    Write-Error "编译过程中发生错误: $_"
}
