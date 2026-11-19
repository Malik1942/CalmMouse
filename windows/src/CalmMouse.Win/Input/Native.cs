// Hand-written Win32 declarations for the input thread and device lookups. Everything is
// blittable (BOOL as int, strings as char*, callbacks as unmanaged function pointers), so no
// call on the hook path marshals or allocates. The few APIs with large structs (display
// timing, last input) come from CsWin32 instead (NativeMethods.txt).

using System.Runtime.InteropServices;

namespace CalmMouse.Win.Input;

internal static unsafe class Native
{
    // MARK: Messages and hooks

    public const uint WM_QUIT = 0x0012;
    public const uint WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const uint WM_INPUT = 0x00FF;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_MOUSEHWHEEL = 0x020E;
    public const uint WM_APP = 0x8000;

    public const int WH_MOUSE_LL = 14;
    public const uint LLMHF_INJECTED = 0x1;

    public const uint PM_REMOVE = 0x1;
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint MWMO_INPUTAVAILABLE = 0x4;
    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 0x102;
    public const uint WAIT_FAILED = 0xFFFFFFFF;

    public static readonly nint HWND_MESSAGE = -3;
    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: hook points and WindowFromPoint both in physical pixels.</summary>
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // MARK: Raw Input

    public const uint RIDEV_REMOVE = 0x1;
    public const uint RIDEV_INPUTSINK = 0x100;
    public const uint RIDEV_DEVNOTIFY = 0x2000;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEHID = 2;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000B;
    public const nint GIDC_ARRIVAL = 1;
    public const nint GIDC_REMOVAL = 2;

    public const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001, RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
    public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004, RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010, RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040, RI_MOUSE_BUTTON_4_UP = 0x0080;
    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100, RI_MOUSE_BUTTON_5_UP = 0x0200;
    public const ushort RI_MOUSE_WHEEL = 0x0400, RI_MOUSE_HWHEEL = 0x0800;

    // MARK: SendInput and keys

    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x1000;

    public const int VK_MBUTTON = 0x04;
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    public const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    // MARK: Windows, processes, tokens

    public const uint GA_ROOT = 2;
    public const uint SPI_GETMOUSEWHEELROUTING = 0x201C;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    /// <summary>QueryFullProcessImageName: the native \Device\HarddiskVolumeN\… path, which needs no drive-letter lookup.</summary>
    public const uint PROCESS_NAME_NATIVE = 0x1;
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;
    public const int TokenIntegrityLevel = 25;
    public const int ERROR_ACCESS_DENIED = 5;

    // MARK: Timers, files, devices

    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2;
    public const uint TIMER_ALL_ACCESS = 0x1F0003;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    public static readonly nint INVALID_HANDLE_VALUE = -1;
    public const uint CR_SUCCESS = 0;
    public const uint CR_BUFFER_SMALL = 0x1A;
    public const uint DEVPROP_TYPE_STRING = 0x12;
    public const uint DEVPROP_TYPE_STRING_LIST = 0x2012;

    // MARK: Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public nint hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public nint hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public nint hDevice;
        public nint wParam;
    }

    /// <summary>RAWMOUSE: usFlags, then the union of ulButtons and (usButtonFlags, usButtonData) at offset 4.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort usFlags;
        [FieldOffset(4)] public ushort usButtonFlags;
        [FieldOffset(6)] public ushort usButtonData;
        [FieldOffset(8)] public uint ulRawButtons;
        [FieldOffset(12)] public int lLastX;
        [FieldOffset(16)] public int lLastY;
        [FieldOffset(20)] public uint ulExtraInformation;
    }

    /// <summary>RID_DEVICE_INFO: the mouse and HID members of the union (the keyboard one sets the size).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint dwType;
        [FieldOffset(8)] public uint mouse_dwId;
        [FieldOffset(12)] public uint mouse_dwNumberOfButtons;
        [FieldOffset(16)] public uint mouse_dwSampleRate;
        [FieldOffset(20)] public int mouse_fHasHorizontalWheel;
        [FieldOffset(8)] public uint hid_dwVendorId;
        [FieldOffset(12)] public uint hid_dwProductId;
        [FieldOffset(16)] public uint hid_dwVersionNumber;
        [FieldOffset(20)] public ushort hid_usUsagePage;
        [FieldOffset(22)] public ushort hid_usUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    /// <summary>INPUT with only its mouse member: MOUSEINPUT is the union's largest, so the size (40 on x64) is right.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
        public DEVPROPKEY(string guid, uint id) { fmtid = new Guid(guid); pid = id; }
    }

    public static readonly DEVPROPKEY DEVPKEY_Device_InstanceId = new("78c34fc8-104a-4aca-9ea4-524d52996e57", 256);
    public static readonly DEVPROPKEY DEVPKEY_Device_DeviceDesc = new("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
    public static readonly DEVPROPKEY DEVPKEY_Device_HardwareIds = new("a45c254e-df1c-4efd-8020-67d146a850e0", 3);
    public static readonly DEVPROPKEY DEVPKEY_Device_FriendlyName = new("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
    public static readonly DEVPROPKEY DEVPKEY_Device_BusReportedDeviceDesc = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);

    // MARK: user32

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nint, nint, nint> lpfn, nint hmod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern ushort RegisterClassExW(WNDCLASSEXW* lpwcx);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CreateWindowExW(uint dwExStyle, char* lpClassName, char* lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    public static extern int DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern int PeekMessageW(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessageW(MSG* lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint MsgWaitForMultipleObjectsEx(uint nCount, nint* pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int RegisterRawInputDevices(RAWINPUTDEVICE* pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(nint hRawInput, uint uiCommand, void* pData, uint* pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList(RAWINPUTDEVICELIST* pRawInputDeviceList, uint* puiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceInfoW(nint hDevice, uint uiCommand, void* pData, uint* pcbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern int GetCursorPos(POINT* lpPoint);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, uint* lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern int EnumChildWindows(nint hWndParent, delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SystemParametersInfoW(uint uiAction, uint uiParam, void* pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    public static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

    // MARK: kernel32

    [DllImport("kernel32.dll")]
    public static extern nint GetModuleHandleW(char* lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    public static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, int bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern int CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, uint* lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint CreateWaitableTimerExW(nint lpTimerAttributes, char* lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int SetWaitableTimer(nint hTimer, long* lpDueTime, int lPeriod, nint pfnCompletionRoutine, nint lpArgToCompletionRoutine, int fResume);

    [DllImport("kernel32.dll")]
    public static extern int CancelWaitableTimer(nint hTimer);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint CreateFileW(char* lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    // MARK: advapi32

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int OpenProcessToken(nint processHandle, uint desiredAccess, nint* tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int GetTokenInformation(nint tokenHandle, int tokenInformationClass, void* tokenInformation, uint length, uint* returnLength);

    [DllImport("advapi32.dll")]
    public static extern byte* GetSidSubAuthorityCount(nint pSid);

    [DllImport("advapi32.dll")]
    public static extern uint* GetSidSubAuthority(nint pSid, uint nSubAuthority);

    // MARK: hid

    [DllImport("hid.dll")]
    public static extern byte HidD_GetAttributes(nint hidDeviceObject, HIDD_ATTRIBUTES* attributes);

    [DllImport("hid.dll")]
    public static extern byte HidD_GetProductString(nint hidDeviceObject, void* buffer, uint bufferLength);

    // MARK: cfgmgr32

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_Device_Interface_PropertyW(char* pszDeviceInterface, DEVPROPKEY* propertyKey, uint* propertyType,
        byte* propertyBuffer, uint* propertyBufferSize, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Locate_DevNodeW(uint* pdnDevInst, char* pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_Parent(uint* pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_Child(uint* pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_Sibling(uint* pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_DevNode_PropertyW(uint dnDevInst, DEVPROPKEY* propertyKey, uint* propertyType,
        byte* propertyBuffer, uint* propertyBufferSize, uint ulFlags);
}
