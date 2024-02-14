using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Attributes;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace SyncClipboard.Core.Models.Keyboard;

[JsonConverter(typeof(JsonStringEnumConverterEx<Key>))]
public enum Key
{
    #region Windows And Common
    Ctrl,
    Shift,
    [PlatformString(nameof(Alt), Mac = "Option")]
    Alt,
    [PlatformString(nameof(Meta), Windows = "Win", Mac = "Cmd")]
    Meta,
    Backspace,
    Tab,
    Clear,
    Enter,
    Pause,
    Cancel,
    [EnumMember(Value = "Caps Lock")]
    Capital,
    Kana,
    Hanguel,
    Hangul,
    [EnumMember(Value = "IME On")]
    ImeOn,
    Junja,
    Final,
    Hanja,
    Kanji,
    [EnumMember(Value = "IME Off")]
    ImeOff,
    Esc,
    Convert,
    Nonconvert,
    Accept,
    [EnumMember(Value = "Mode Change")]
    Modechange,
    Space,
    PgUp,
    PgDn,
    End,
    Home,
    [EnumMember(Value = "←")]
    Left,
    [EnumMember(Value = "↑")]
    Up,
    [EnumMember(Value = "→")]
    Right,
    [EnumMember(Value = "↓")]
    Down,
    Select,
    Print,
    Execute,
    [EnumMember(Value = "Print Screen")]
    PrintScreen,
    Insert,
    Delete,
    Help,
    [EnumMember(Value = "0")]
    _0,
    [EnumMember(Value = "1")]
    _1,
    [EnumMember(Value = "2")]
    _2,
    [EnumMember(Value = "3")]
    _3,
    [EnumMember(Value = "4")]
    _4,
    [EnumMember(Value = "5")]
    _5,
    [EnumMember(Value = "6")]
    _6,
    [EnumMember(Value = "7")]
    _7,
    [EnumMember(Value = "8")]
    _8,
    [EnumMember(Value = "9")]
    _9,
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    [EnumMember(Value = "Menu")]
    Apps,
    Sleep,
    NumPad0,
    NumPad1,
    NumPad2,
    NumPad3,
    NumPad4,
    NumPad5,
    NumPad6,
    NumPad7,
    NumPad8,
    NumPad9,
    [EnumMember(Value = "* (NumPad)")]
    Multiply,
    [EnumMember(Value = "+ (NumPad)")]
    Add,
    Separator,
    [EnumMember(Value = "- (NumPad)")]
    Subtract,
    [EnumMember(Value = ". (NumPad)")]
    Decimal,
    [EnumMember(Value = "/ (NumPad)")]
    Divide,
    [EnumMember(Value = "= (NumPad)")]
    NumPadEqual,
    [EnumMember(Value = "NumPad Return")]
    NumPadReturn,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,
    [EnumMember(Value = "Num Lock")]
    NumLock,
    Scroll,
    [EnumMember(Value = "Browser Back")]
    BrowserBack,
    [EnumMember(Value = "Browser Forward")]
    BrowserForward,
    [EnumMember(Value = "Browser Refresh")]
    BrowserRefresh,
    [EnumMember(Value = "Browser Stop")]
    BrowserStop,
    [EnumMember(Value = "Browser Search")]
    BrowserSearch,
    [EnumMember(Value = "Browser Favorites")]
    BrowserFavorites,
    [EnumMember(Value = "Browser Home")]
    BrowserHome,
    [EnumMember(Value = "Volume Mute")]
    VolumeMute,
    [EnumMember(Value = "Volume Down")]
    VolumeDown,
    [EnumMember(Value = "Volume Up")]
    VolumeUp,
    [EnumMember(Value = "Next Track")]
    MediaNextTrack,
    [EnumMember(Value = "Previous Track")]
    MediaPrevTrack,
    [EnumMember(Value = "Stop Media")]
    MediaStop,
    [EnumMember(Value = "Play/Pause Media")]
    MediaPlayPause,
    [EnumMember(Value = "Start Mail")]
    LaunchMail,
    [EnumMember(Value = "Select Media")]
    LaunchMediaSelect,
    [EnumMember(Value = "Start App 1")]
    LaunchApp1,
    [EnumMember(Value = "Start App 2")]
    LaunchApp2,
    [EnumMember(Value = ";")]
    Semicolon,
    [EnumMember(Value = "=")]
    Equal,
    [EnumMember(Value = ",")]
    Comma,
    [EnumMember(Value = "-")]
    Minus,
    [EnumMember(Value = ".")]
    Period,
    [EnumMember(Value = "/")]
    Slash,
    [EnumMember(Value = "`")]
    BackQuote,
    [EnumMember(Value = "[")]
    OpenBracket,
    [EnumMember(Value = "\\")]
    BackSlash,
    [EnumMember(Value = "]")]
    CloshBracket,
    [EnumMember(Value = "'")]
    Quote,
    OEM_FJ_JISHO,
    OEM_FJ_MASSHOU,
    OEM_FJ_TOUROKU,
    OEM_FJ_LOYA,
    OEM_FJ_ROYA,
    GAMEPAD_A,
    GAMEPAD_B,
    GAMEPAD_X,
    GAMEPAD_Y,
    GAMEPAD_RIGHT_SHOULDER,
    GAMEPAD_LEFT_SHOULDER,
    GAMEPAD_LEFT_TRIGGER,
    GAMEPAD_RIGHT_TRIGGER,
    GAMEPAD_DPAD_UP,
    GAMEPAD_DPAD_DOWN,
    GAMEPAD_DPAD_LEFT,
    GAMEPAD_DPAD_RIGHT,
    GAMEPAD_MENU,
    GAMEPAD_VIEW,
    GAMEPAD_LEFT_THUMBSTICK_BUTTON,
    GAMEPAD_RIGHT_THUMBSTICK_BUTTON,
    GAMEPAD_LEFT_THUMBSTICK_UP,
    GAMEPAD_LEFT_THUMBSTICK_DOWN,
    GAMEPAD_LEFT_THUMBSTICK_RIGHT,
    GAMEPAD_LEFT_THUMBSTICK_LEFT,
    GAMEPAD_RIGHT_THUMBSTICK_UP,
    GAMEPAD_RIGHT_THUMBSTICK_DOWN,
    GAMEPAD_RIGHT_THUMBSTICK_RIGHT,
    GAMEPAD_RIGHT_THUMBSTICK_LEFT,
    OEM_8,
    OEM_AX,
    [PlatformString("<>", Mac = "§")]
    OEM_102,
    PROCESSKEY,
    ICO_CLEAR,
    PACKET,
    OEM_RESET,
    OEM_JUMP,
    OEM_PA1,
    OEM_PA2,
    OEM_PA3,
    OEM_WSCTRL,
    OEM_CUSEL,
    OEM_ATTN,
    OEM_FINISH,
    OEM_COPY,
    OEM_AUTO,
    OEM_ENLW,
    OEM_BACKTAB,
    ATTN,
    CRSEL,
    EXSEL,
    EREOF,
    PLAY,
    ZOOM,
    NONAME,
    PA1,
    OEM_CLEAR,
    #endregion Windows And Common
    #region MacOS and Linux
    Function,
    ChangeInputSource,
    Power,
    [EnumMember(Value = "Media Eject")]
    MediaEject,
    [EnumMember(Value = "Start App 3")]
    LaunchApp3,
    [EnumMember(Value = "Start App 4")]
    LaunchApp4,
    [EnumMember(Value = "Start Browser")]
    LaunchBrowser,
    [EnumMember(Value = "Start Calculator")]
    LaunchCalculator,
    KatakanaHiragana,
    Katakana,
    Hiragana,
    Alphanumeric,
    Underscore,
    Yen,
    JpComma
    #endregion MacOS and Linux
}
