param(
    [Parameter(Mandatory=$false, HelpMessage="应用程序版本号")]
    [string]$Version = "",
    
    [Parameter(Mandatory=$false, HelpMessage="CPU 架构 (x64 或 arm64)")]
    [string]$Architecture = "",
    
    [Parameter(Mandatory=$false, HelpMessage="是否包含 .NET 运行时")]
    [string]$SelfContained = "",
    
    [Parameter(Mandatory=$false, HelpMessage="是否包含 Windows App SDK")]
    [string]$IncludeAppSDK = "",
    
    [Parameter(Mandatory=$false, HelpMessage="输出目录")]
    [string]$OutputDir = "",
    
    [Parameter(Mandatory=$false, HelpMessage="是否清理构建输出")]
    [string]$Clean = ""
)

$ErrorActionPreference = "Stop"

function Read-InteractiveInput {
    param(
        [string]$Prompt,
        [string]$DefaultValue,
        [string[]]$ValidValues = $null,
        [scriptblock]$ValidationScript = $null
    )
    
    while ($true) {
        if ($DefaultValue) {
            Write-Host "${Prompt} [默认: ${DefaultValue}]: " -NoNewline -ForegroundColor Yellow
        }
        else {
            Write-Host "${Prompt}: " -NoNewline -ForegroundColor Yellow
        }
        
        $userInput = Read-Host
        
        if ([string]::IsNullOrWhiteSpace($userInput)) {
            if ($DefaultValue) {
                return $DefaultValue
            }
            else {
                Write-Host "此参数为必填项，请输入值" -ForegroundColor Red
                continue
            }
        }
        
        if ($ValidValues) {
            if ($ValidValues -contains $userInput) {
                return $userInput
            }
            else {
                Write-Host "无效的值。有效值为: $($ValidValues -join ', ')" -ForegroundColor Red
                continue
            }
        }
        
        if ($ValidationScript) {
            $isValid = & $ValidationScript $userInput
            if ($isValid) {
                return $userInput
            }
            else {
                continue
            }
        }
        
        return $userInput
    }
}

function Read-YesNoInput {
    param(
        [string]$Prompt,
        [bool]$DefaultValue
    )
    
    $defaultStr = if ($DefaultValue) { "Y" } else { "N" }
    
    while ($true) {
        Write-Host "${Prompt} [Y/N] [默认: ${defaultStr}]: " -NoNewline -ForegroundColor Yellow
        $userInput = Read-Host
        
        if ([string]::IsNullOrWhiteSpace($userInput)) {
            return $DefaultValue
        }
        
        switch ($userInput.ToUpper()) {
            "Y" { return $true }
            "N" { return $false }
            "YES" { return $true }
            "NO" { return $false }
            default {
                Write-Host "无效的输入。请输入 Y 或 N" -ForegroundColor Red
            }
        }
    }
}

$AppName = "SyncClipboard"
$scriptDir = $PSScriptRoot
$rootDir = Split-Path (Split-Path $scriptDir -Parent) -Parent
$ProjectPath = Join-Path $rootDir "src\SyncClipboard.WinUI3\SyncClipboard.WinUI3.csproj"
$BuildOutputPath = Join-Path $rootDir "build\temp"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SyncClipboard Installer Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "未提供版本号，请输入" -ForegroundColor Magenta
    $Version = Read-InteractiveInput `
        -Prompt "版本号" `
        -DefaultValue "1.0.0" `
        -ValidationScript {
            param($v)
            if ($v -match '^\d+\.\d+\.\d+(-[a-zA-Z0-9]+)?$') {
                return $true
            }
            Write-Host "无效的版本号格式。正确格式: 1.0.0 或 3.1.2-beta1" -ForegroundColor Red
            return $false
        }
}

if ([string]::IsNullOrWhiteSpace($Architecture)) {
    Write-Host "未提供架构，请选择" -ForegroundColor Magenta
    $Architecture = Read-InteractiveInput `
        -Prompt "CPU 架构" `
        -DefaultValue "x64" `
        -ValidValues @("x64", "arm64")
}

if ([string]::IsNullOrWhiteSpace($SelfContained)) {
    Write-Host "未指定是否包含 .NET 运行时，请选择" -ForegroundColor Magenta
    $SelfContained = Read-YesNoInput `
        -Prompt "是否包含 .NET 运行时" `
        -DefaultValue $true
}
else {
    $SelfContained = $SelfContained -eq "true" -or $SelfContained -eq "1"
}

if ([string]::IsNullOrWhiteSpace($IncludeAppSDK)) {
    Write-Host "未指定是否包含 Windows App SDK，请选择" -ForegroundColor Magenta
    $IncludeAppSDK = Read-YesNoInput `
        -Prompt "是否包含 Windows App SDK" `
        -DefaultValue $true
}
else {
    $IncludeAppSDK = $IncludeAppSDK -eq "true" -or $IncludeAppSDK -eq "1"
}

if ([string]::IsNullOrWhiteSpace($Clean)) {
    $Clean = $false
}
else {
    $Clean = $Clean -eq "true" -or $Clean -eq "1"
}
Write-Host ""

Write-Host "配置信息:" -ForegroundColor Yellow
Write-Host "  版本号:          $Version" -ForegroundColor White
Write-Host "  架构:            $Architecture" -ForegroundColor White
Write-Host "  Self-Contained:  $SelfContained" -ForegroundColor White
Write-Host "  Include App SDK: $IncludeAppSDK" -ForegroundColor White
Write-Host ""

Write-Host "项目根目录: $rootDir" -ForegroundColor Cyan
Write-Host ""

if ($Clean) {
    Write-Host "清理构建输出..." -ForegroundColor Yellow
    if (Test-Path $BuildOutputPath) {
        Remove-Item -Path $BuildOutputPath -Recurse -Force
    }
    Write-Host "清理完成" -ForegroundColor Green
    Write-Host ""
}

if (-not (Test-Path $BuildOutputPath)) {
    New-Item -ItemType Directory -Path $BuildOutputPath -Force | Out-Null
}

Write-Host "步骤 1/3: 还原 NuGet 包..." -ForegroundColor Yellow
try {
    $srcPath = Join-Path $rootDir "src"
    Push-Location $srcPath
    & dotnet restore SyncClipboard.WinUI3
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet 包还原失败"
    }
    Pop-Location
    Write-Host "NuGet 包还原成功" -ForegroundColor Green
    Write-Host ""
}
catch {
    Pop-Location
    Write-Error "NuGet 包还原失败: $_"
    exit 1
}

Write-Host "步骤 2/3: 编译项目..." -ForegroundColor Yellow
$msbuildArgs = @(
    $ProjectPath,
    "/p:Platform=$Architecture",
    "/p:RuntimeIdentifier=win-$Architecture",
    "/p:Configuration=Release",
    "/p:WindowsAppSDKSelfContained=$IncludeAppSDK",
    "/p:SelfContained=$SelfContained",
    "/v:m",
    "-restore"
)

try {
    & msbuild $msbuildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "编译失败"
    }
    Write-Host "编译成功" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Error "编译失败: $_"
    exit 1
}

$binPath = Join-Path $rootDir "src\SyncClipboard.WinUI3\bin\$Architecture\Release\net9.0-windows10.0.19041.0\win-$Architecture"
if (-not (Test-Path $binPath)) {
    Write-Error "找不到编译输出目录: $binPath"
    exit 1
}

Write-Host "复制编译输出..." -ForegroundColor Yellow
$destPath = $BuildOutputPath
if (Test-Path $destPath) {
    Remove-Item -Path $destPath -Recurse -Force
}
New-Item -ItemType Directory -Path $destPath -Force | Out-Null

Copy-Item -Path "$binPath\*" -Destination $destPath -Recurse -Force
Write-Host "复制完成" -ForegroundColor Green
Write-Host ""

$exePath = Join-Path $destPath "$AppName.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "找不到可执行文件: $exePath"
    exit 1
}

Write-Host "步骤 3/3: 创建安装包..." -ForegroundColor Yellow
Write-Host ""

if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $rootDir "build\output"
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$createInstallerScript = Join-Path $scriptDir "CreateInstaller.ps1"
if (-not (Test-Path $createInstallerScript)) {
    Write-Error "找不到 CreateInstaller.ps1: $createInstallerScript"
    exit 1
}

try {
    & $createInstallerScript `
        -SourceFolder $destPath `
        -Version $Version `
        -OutputFolder $OutputDir `
        -TargetArch $Architecture
    
    if ($LASTEXITCODE -ne 0) {
        throw "创建安装包失败"
    }
}
catch {
    Write-Error "创建安装包失败: $_"
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  构建完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

$installerPath = Join-Path $OutputDir "$AppName-$Version-installer.exe"
if (Test-Path $installerPath) {
    $fileInfo = Get-Item $installerPath
    Write-Host "安装包信息:" -ForegroundColor Cyan
    Write-Host "  路径: $installerPath" -ForegroundColor White
    Write-Host "  大小: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor White
    Write-Host "  版本: $Version" -ForegroundColor White
    Write-Host "  架构: $Architecture" -ForegroundColor White
}

if (Test-Path $BuildOutputPath) {
    Remove-Item -Path $BuildOutputPath -Recurse -Force
}
