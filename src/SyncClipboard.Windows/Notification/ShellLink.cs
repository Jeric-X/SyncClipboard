using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace SyncClipboard.Windows.Notification
{
    // Modified from https://github.com/pr8x/DesktopNotifications
    // Originally Modified from http://smdn.jp/programming/tips/createlnk/
    // Originally from http://www.vbaccelerator.com/home/NET/Code/Libraries/Shell_Projects
    // /
    // Partly based on Sending toast notifications from desktop apps sample
    public partial class ShellLink : IDisposable
    {
        #region Win32 and COM

        // IShellLink Interface
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            uint GetPath([Out] [MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszFile,
                int cchMaxPath, ref WIN32_FIND_DATAW pfd, uint fFlags);

            uint GetIDList(out IntPtr ppidl);
            uint SetIDList(IntPtr pidl);

            uint GetDescription([Out] [MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszName,
                int cchMaxName);

            uint SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

            uint GetWorkingDirectory([Out] [MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszDir,
                int cchMaxPath);

            uint SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

            uint GetArguments([Out] [MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszArgs,
                int cchMaxPath);

            uint SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            uint GetHotKey(out ushort pwHotkey);
            uint SetHotKey(ushort wHotKey);
            uint GetShowCmd(out int piShowCmd);
            uint SetShowCmd(int iShowCmd);

            uint GetIconLocation([Out] [MarshalAs(UnmanagedType.LPWStr)]
                StringBuilder pszIconPath,
                int cchIconPath, out int piIcon);

            uint SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

            uint SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
                uint dwReserved);

            uint Resolve(IntPtr hwnd, uint fFlags);
            uint SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        // ShellLink CoClass (ShellLink object)
        [ComImport]
        [ClassInterface(ClassInterfaceType.None)]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink
        {
        }

        // WIN32_FIND_DATAW Structure
        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
        private readonly struct WIN32_FIND_DATAW
        {
            public readonly uint dwFileAttributes;
            public readonly FILETIME ftCreationTime;
            public readonly FILETIME ftLastAccessTime;
            public readonly FILETIME ftLastWriteTime;
            public readonly uint nFileSizeHigh;
            public readonly uint nFileSizeLow;
            public readonly uint dwReserved0;
            public readonly uint dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public readonly string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public readonly string cAlternateFileName;
        }

        // IPropertyStore Interface
        [GeneratedComInterface]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        internal partial interface IPropertyStore
        {
            internal uint GetCount(out uint cProps);
            internal uint GetAt(uint iProp, out PropertyKey pkey);
            internal uint GetValue(in PropertyKey key, ref PropVariant pv);
            internal uint SetValue(in PropertyKey key, ref PropVariant pv);
            internal uint Commit();
        }

        // PropertyKey Structure
        // Narrowed down from PropertyKey.cs of Windows API Code Pack 1.1
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal readonly struct PropertyKey
        {
            #region Fields

            #endregion

            #region Public Properties

            public Guid FormatId { get; }

            public int PropertyId { get; }

            #endregion

            #region Constructor

            public PropertyKey(Guid formatId, int propertyId)
            {
                FormatId = formatId;
                PropertyId = propertyId;
            }

            public PropertyKey(string formatId, int propertyId)
            {
                FormatId = new Guid(formatId);
                PropertyId = propertyId;
            }

            #endregion
        }

        // PropVariant Class (only for string value)
        // Narrowed down from PropVariant.cs of Windows API Code Pack 1.1
        // Originally from http://blogs.msdn.com/b/adamroot/archive/2008/04/11
        // /interop-with-propvariants-in-net.aspx
        [StructLayout(LayoutKind.Explicit)]
        public struct PropVariant
        {
            #region Fields

            [FieldOffset(0)] public ushort valueType; // Value type

            // [FieldOffset(2)]
            // ushort wReserved1; // Reserved field
            // [FieldOffset(4)]
            // ushort wReserved2; // Reserved field
            // [FieldOffset(6)]
            // ushort wReserved3; // Reserved field

            [FieldOffset(8)] public IntPtr ptr; // Value

            #endregion

            #region Public Properties

            // Value type (System.Runtime.InteropServices.VarEnum)
            public VarEnum VarType
            {
                readonly get => (VarEnum)valueType;
                set => valueType = (ushort)value;
            }

            // Whether value is empty or null
            public readonly bool IsNullOrEmpty =>
                valueType == (ushort)VarEnum.VT_EMPTY ||
                valueType == (ushort)VarEnum.VT_NULL;

            // Value (only for string value)
            public readonly string? Value => Marshal.PtrToStringUni(ptr);

            #endregion
        }

        [LibraryImport("Ole32.dll")]
        private static partial int PropVariantClear(ref PropVariant pvar);

        #endregion

        #region Fields

        private IShellLinkW? shellLinkW;

        // Name = System.AppUserModel.ID
        // ShellPKey = PKEY_AppUserModel_ID
        // FormatID = 9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3
        // PropID = 5
        // Type = String (VT_LPWSTR)
        private readonly PropertyKey AppUserModelIDKey =
            new("{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}", 5);

        private const int MAX_PATH = 260;
        private const int INFOTIPSIZE = 1024;

        private const int STGM_READ = 0x00000000; // STGM constants
        private const uint SLGP_UNCPRIORITY = 0x0002; // SLGP flags

        #endregion

        #region Private Properties (Interfaces)

        private IPersistFile PersistFile
        {
            get
            {
                if (shellLinkW is not IPersistFile persistFile)
                {
                    throw new COMException("Failed to create IPersistFile.");
                }

                return persistFile;
            }
        }

        private IPropertyStore PropertyStore
        {
            get
            {
                if (shellLinkW is not IPropertyStore PropertyStore)
                {
                    throw new COMException("Failed to create IPropertyStore.");
                }

                return PropertyStore;
            }
        }

        #endregion

        #region Public Properties (Minimal)

        // Path of loaded shortcut file
        public string ShortcutFile
        {
            get
            {
                PersistFile.GetCurFile(out string shortcutFile);
                return shortcutFile;
            }
        }

        // Path of target file
        public string TargetPath
        {
            get
            {
                // No limitation to length of buffer string in the case of Unicode though.
                StringBuilder targetPath = new(MAX_PATH);

                var data = new WIN32_FIND_DATAW();

                VerifySucceeded(shellLinkW!.GetPath(targetPath, targetPath.Capacity, ref data,
                    SLGP_UNCPRIORITY));

                return targetPath.ToString();
            }
            set => VerifySucceeded(shellLinkW!.SetPath(value));
        }

        public string Arguments
        {
            get
            {
                // No limitation to length of buffer string in the case of Unicode though.
                StringBuilder arguments = new(INFOTIPSIZE);

                VerifySucceeded(shellLinkW!.GetArguments(arguments, arguments.Capacity));

                return arguments.ToString();
            }
            set => VerifySucceeded(shellLinkW!.SetArguments(value));
        }

        // AppUserModelID to be used for Windows 7 or later.
        public string AppUserModelID
        {
            get
            {
                PropVariant pv = new();
                VerifySucceeded(PropertyStore.GetValue(in AppUserModelIDKey, ref pv));
                var valueStr = pv.Value ?? "Null";
                Marshal.ThrowExceptionForHR(PropVariantClear(ref pv));
                return valueStr;
            }
            set
            {
                PropVariant pv = new()
                {
                    valueType = (ushort)VarEnum.VT_LPWSTR,
                    ptr = Marshal.StringToCoTaskMemUni(value)
                };
                VerifySucceeded(PropertyStore.SetValue(in AppUserModelIDKey, ref pv));
                VerifySucceeded(PropertyStore.Commit());
                Marshal.ThrowExceptionForHR(PropVariantClear(ref pv));
            }
        }

        #endregion

        #region Constructor

        public ShellLink()
            : this(null)
        {
        }

        // Construct with loading shortcut file.
        public ShellLink(string? file)
        {
            try
            {
                shellLinkW = (IShellLinkW)new CShellLink();
            }
            catch
            {
                throw new COMException("Failed to create ShellLink object.");
            }

            if (file != null)
            {
                Load(file);
            }
        }

        #endregion

        #region Destructor

        ~ShellLink()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (shellLinkW != null)
            {
                // Release all references.
                Marshal.FinalReleaseComObject(shellLinkW);
                shellLinkW = null;
            }
        }

        #endregion

        #region Methods

        // Save shortcut file.
        public void Save()
        {
            string file = ShortcutFile ?? throw new InvalidOperationException("File name is not given.");
            Save(file);
        }

        public void Save(string file)
        {
            ArgumentNullException.ThrowIfNull(file);

            PersistFile.Save(file, true);
        }

        // Load shortcut file.
        public void Load(string file)
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("File is not found.", file);
            }

            PersistFile.Load(file, STGM_READ);
        }

        // Verify if operation succeeded.
        public static void VerifySucceeded(uint hresult)
        {
            if (hresult > 1)
            {
                throw new InvalidOperationException("Failed with HRESULT: " + hresult.ToString("X"));
            }
        }

        #endregion
    }
}