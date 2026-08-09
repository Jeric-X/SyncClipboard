#!/usr/bin/env bash

set -Eeuo pipefail

APP_NAME="SyncClipboard"
BUNDLE_NAME="${APP_NAME}.app"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT_PATH="$ROOT_DIR/src/SyncClipboard.Desktop.MacOS/SyncClipboard.Desktop.MacOS.csproj"

ARCHITECTURE=""
CONFIGURATION="Release"
SELF_CONTAINED="true"
OUTPUT_DIR=""
EXISTING_BUNDLE=""
CLEAN="false"
SIGN_BUNDLE="true"
SIGNING_IDENTITY="-"

usage() {
    cat <<'EOF'
编译 SyncClipboard macOS 客户端并生成可直接运行的 .app bundle。

用法:
  ./build/macos/BundleTool.sh [选项]

选项:
  -a, --architecture <arm64|x64>  目标架构；默认使用当前 Mac 的架构
  -c, --configuration <配置>     构建配置，默认 Release
  -o, --output-dir <目录>        输出目录，默认 build/output/macos-<架构>
      --app-bundle <路径>        跳过编译，校验并重新签名已有的 .app
      --self-contained <布尔值>  是否包含 .NET 运行时，默认 true
      --clean                    构建前清理目标项目
      --signing-identity <名称>  签名证书名称，默认使用临时签名 "-"
      --no-sign                  不对 bundle 重新签名
  -h, --help                     显示帮助

示例:
  ./build/macos/BundleTool.sh
  ./build/macos/BundleTool.sh -a arm64 --clean
  ./build/macos/BundleTool.sh --app-bundle build/macos/SyncClipboard.app
  ./build/macos/BundleTool.sh --signing-identity "Developer ID Application: Example"
EOF
}

fail() {
    echo "错误: $*" >&2
    exit 1
}

require_value() {
    local option="$1"
    local value="${2:-}"
    [[ -n "$value" ]] || fail "$option 需要一个值"
}

resolve_root_path() {
    local path="$1"
    if [[ "$path" == /* ]]; then
        printf '%s\n' "$path"
    else
        printf '%s\n' "$ROOT_DIR/$path"
    fi
}

resolve_working_path() {
    local path="$1"
    if [[ "$path" == /* ]]; then
        printf '%s\n' "$path"
    else
        printf '%s\n' "$PWD/$path"
    fi
}

validate_bundle() {
    local app_bundle="$1"
    local executable_path="$app_bundle/Contents/MacOS/SyncClipboard.Desktop.MacOS"

    [[ -d "$app_bundle/Contents" ]] || fail "无效的 app bundle: $app_bundle"
    [[ -f "$app_bundle/Contents/Info.plist" ]] || fail "App bundle 中缺少 Info.plist"
    [[ -f "$app_bundle/Contents/Resources/icon.icns" ]] || fail "App bundle 中缺少应用图标"
    [[ -f "$executable_path" ]] || fail "App bundle 中缺少主可执行文件"
    plutil -lint "$app_bundle/Contents/Info.plist"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -a|--architecture)
            require_value "$1" "${2:-}"
            ARCHITECTURE="$2"
            shift 2
            ;;
        -c|--configuration)
            require_value "$1" "${2:-}"
            CONFIGURATION="$2"
            shift 2
            ;;
        -o|--output-dir)
            require_value "$1" "${2:-}"
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --app-bundle)
            require_value "$1" "${2:-}"
            EXISTING_BUNDLE="$2"
            shift 2
            ;;
        --self-contained)
            require_value "$1" "${2:-}"
            SELF_CONTAINED="$2"
            shift 2
            ;;
        --clean)
            CLEAN="true"
            shift
            ;;
        --signing-identity)
            require_value "$1" "${2:-}"
            SIGNING_IDENTITY="$2"
            shift 2
            ;;
        --no-sign)
            SIGN_BUNDLE="false"
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            fail "未知选项: $1（使用 --help 查看帮助）"
            ;;
    esac
done

[[ "$(uname -s)" == "Darwin" ]] || fail "此脚本只能在 macOS 上运行"
command -v plutil >/dev/null 2>&1 || fail "找不到 macOS 系统命令 plutil"
command -v lipo >/dev/null 2>&1 || fail "找不到 macOS 系统命令 lipo"
if [[ "$SIGN_BUNDLE" == "true" ]]; then
    command -v codesign >/dev/null 2>&1 || fail "找不到 macOS 系统命令 codesign"
fi

if [[ -z "$EXISTING_BUNDLE" && -z "$ARCHITECTURE" ]]; then
    case "$(uname -m)" in
        arm64) ARCHITECTURE="arm64" ;;
        x86_64) ARCHITECTURE="x64" ;;
        *) fail "无法识别当前 Mac 的 CPU 架构: $(uname -m)" ;;
    esac
fi

if [[ -n "$ARCHITECTURE" ]]; then
    case "$ARCHITECTURE" in
        arm64|x64) ;;
        *) fail "架构必须是 arm64 或 x64" ;;
    esac
fi

case "$SELF_CONTAINED" in
    true|false) ;;
    *) fail "--self-contained 必须是 true 或 false" ;;
esac

[[ "$CONFIGURATION" =~ ^[A-Za-z0-9._-]+$ ]] || fail "无效的构建配置: $CONFIGURATION"

if [[ -n "$EXISTING_BUNDLE" ]]; then
    [[ "$CLEAN" == "false" ]] || fail "--clean 不能与 --app-bundle 同时使用"
    [[ -z "$OUTPUT_DIR" ]] || fail "--output-dir 不能与 --app-bundle 同时使用"
    DESTINATION_BUNDLE="$(resolve_working_path "$EXISTING_BUNDLE")"
else
    command -v dotnet >/dev/null 2>&1 || fail "找不到 dotnet，请先安装 .NET SDK"
    command -v ditto >/dev/null 2>&1 || fail "找不到 macOS 系统命令 ditto"
    [[ -f "$PROJECT_PATH" ]] || fail "找不到项目文件: $PROJECT_PATH"

    if [[ -z "$OUTPUT_DIR" ]]; then
        OUTPUT_DIR="$ROOT_DIR/build/output/macos-$ARCHITECTURE"
    else
        OUTPUT_DIR="$(resolve_root_path "$OUTPUT_DIR")"
    fi

    [[ "$OUTPUT_DIR" != "/" ]] || fail "输出目录不能是文件系统根目录"

    RUNTIME_IDENTIFIER="osx-$ARCHITECTURE"
    TARGET_FRAMEWORK="$(dotnet msbuild "$PROJECT_PATH" -nologo -getProperty:TargetFramework)"
    SOURCE_BUNDLE="$ROOT_DIR/src/SyncClipboard.Desktop.MacOS/bin/$CONFIGURATION/$TARGET_FRAMEWORK/$RUNTIME_IDENTIFIER/SyncClipboard.Desktop.MacOS.app"
    DESTINATION_BUNDLE="$OUTPUT_DIR/$BUNDLE_NAME"
fi

echo "========================================"
echo "  SyncClipboard macOS App Builder"
echo "========================================"
echo
echo "配置信息:"
if [[ -n "$EXISTING_BUNDLE" ]]; then
    echo "  模式:             已有 App Bundle"
else
    echo "  模式:             编译并创建 App Bundle"
    echo "  架构:             $ARCHITECTURE"
    echo "  构建配置:         $CONFIGURATION"
    echo "  Self-Contained:   $SELF_CONTAINED"
fi
echo "  重新签名:         $SIGN_BUNDLE"
echo "  输出:             $DESTINATION_BUNDLE"
echo

if [[ -z "$EXISTING_BUNDLE" ]]; then
    if [[ "$CLEAN" == "true" ]]; then
        echo "清理目标项目..."
        dotnet clean "$PROJECT_PATH" \
            -c "$CONFIGURATION" \
            -p:RuntimeIdentifiers="$RUNTIME_IDENTIFIER"
        echo
    fi

    echo "步骤 1/3: 还原 NuGet 包..."
    dotnet restore "$PROJECT_PATH" -p:RuntimeIdentifiers="$RUNTIME_IDENTIFIER"
    echo

    echo "步骤 2/3: 发布 macOS 应用..."
    dotnet publish "$PROJECT_PATH" \
        -c "$CONFIGURATION" \
        -p:RuntimeIdentifiers="$RUNTIME_IDENTIFIER" \
        --self-contained "$SELF_CONTAINED" \
        --no-restore

    [[ -d "$SOURCE_BUNDLE" ]] || fail "找不到发布生成的 app bundle: $SOURCE_BUNDLE"
    echo

    echo "步骤 3/3: 创建 $BUNDLE_NAME..."
    mkdir -p "$OUTPUT_DIR"
    if [[ -e "$DESTINATION_BUNDLE" ]]; then
        rm -rf "$DESTINATION_BUNDLE"
    fi
    ditto "$SOURCE_BUNDLE" "$DESTINATION_BUNDLE"
fi

echo "校验 App Bundle..."
validate_bundle "$DESTINATION_BUNDLE"
chmod +x "$DESTINATION_BUNDLE/Contents/MacOS/SyncClipboard.Desktop.MacOS"

if [[ "$SIGN_BUNDLE" == "true" ]]; then
    echo "重新签名 App Bundle..."
    if [[ "$SIGNING_IDENTITY" == "-" ]]; then
        codesign --force --deep --sign - --timestamp=none "$DESTINATION_BUNDLE"
    else
        codesign --force --deep --options runtime --sign "$SIGNING_IDENTITY" --timestamp "$DESTINATION_BUNDLE"
    fi
    codesign --verify --deep --strict "$DESTINATION_BUNDLE"
fi

EXECUTABLE_PATH="$DESTINATION_BUNDLE/Contents/MacOS/SyncClipboard.Desktop.MacOS"
EXECUTABLE_ARCHITECTURES="$(lipo -archs "$EXECUTABLE_PATH")"
if [[ -n "$ARCHITECTURE" ]]; then
    EXPECTED_ARCHITECTURE="$ARCHITECTURE"
    if [[ "$ARCHITECTURE" == "x64" ]]; then
        EXPECTED_ARCHITECTURE="x86_64"
    fi
    [[ " $EXECUTABLE_ARCHITECTURES " == *" $EXPECTED_ARCHITECTURE "* ]] || \
        fail "生成的可执行文件架构不正确: $EXECUTABLE_ARCHITECTURES"
fi

echo
echo "========================================"
echo "  构建完成"
echo "========================================"
echo "Bundle: $DESTINATION_BUNDLE"
echo "架构:   $EXECUTABLE_ARCHITECTURES"
echo "大小:   $(du -sh "$DESTINATION_BUNDLE" | awk '{print $1}')"
