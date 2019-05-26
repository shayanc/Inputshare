using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace InputshareLib
{

    /// <summary>
    /// Manages a message only win32 window to receive&send window messages
    /// CreateWindow() must be called before calling any other method
    /// </summary>
    public class WinWindow
    {
        #region native
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr CreateWindowEx(int dwExStyle,
       //UInt16 regResult,
       [MarshalAs(UnmanagedType.LPStr)]
       string lpClassName,
       [MarshalAs(UnmanagedType.LPStr)]
       string lpWindowName,
       UInt32 dwStyle,
       int x,
       int y,
       int nWidth,
       int nHeight,
       IntPtr hWndParent,
       IntPtr hMenu,
       IntPtr hInstance,
       IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern UInt16 RegisterClassEx(ref WNDCLASSEX classEx);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WNDCLASSEX
        {
            [MarshalAs(UnmanagedType.U4)]
            public int cbSize;
            [MarshalAs(UnmanagedType.U4)]
            public int style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public int time;
            public POINT pt;
        }
        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess,
          uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern int GetMessage(out MSG message, IntPtr hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        static extern void DispatchMessage(ref MSG message);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook,
            LLHookCallback lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook,
                    IntPtr lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern IntPtr SetClipboardViewer(IntPtr newWin);

        [DllImport("user32.dll")]
        static extern bool ChangeClipboardChain(IntPtr hwndRemove, IntPtr hwndNext);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr window);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProcA(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr intPtr, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool OpenClipboard(IntPtr newWnd);

        [DllImport("user32.dll")]
        static extern bool CloseClipboard();

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostThreadMessage(uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern void PostQuitMessage(int code);

        [DllImport("user32.dll")]
        static extern bool UnregisterClassA(string className, IntPtr hInstance);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int WM_QUIT = 0x0012;
        private const int WM_CLOSE = 0x0010;

        public delegate IntPtr LLHookCallback(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr WndProcCallback(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        private readonly static IntPtr HWND_MESSAGE = new IntPtr(-3);

        #endregion

        public IntPtr WindowHandle { get; private set; }
        public WinWindowConfig WindowConfig { get; }
        public bool DeadWindow { get; private set; } //This is true if the window is closed

        //Events
        public event EventHandler ClipboardContentChanged;
        public event EventHandler DesktopSwitched;

        private const string wndName = "ismsgwnd";  //Name of the message only window

        private IntPtr wndCallbackPointer;  //pointer to wndCallback
        private WndProcCallback wndCallback; //Callback to WndProc(intpr, uint, inptr, inptr)
        private WNDCLASSEX wndClass; //Window class
        private uint wndThreadId;  //Unamanged thread ID of window message thread

        //Wineventhook variables
        public bool MonitoringDesktopSwitches { get; private set; }
        private IntPtr hWinEventHook; //Pointer to the wineventhook
        private WinEventDelegate winEventCallback;

        //Clipboard variables
        public bool MonitoringClipboard { get; private set; }

        //Mouse hook variables
        public bool MouseHookAssigned { get; private set; }
        private LLHookCallback mouseProcCallback;
        private IntPtr mouseProcID = IntPtr.Zero;

        //Keyboard hook variables
        public bool KeyboardHookAssigned { get; private set; }
        private LLHookCallback keyboardProcCallback;
        private IntPtr keyboardProcID = IntPtr.Zero;

        private CancellationToken windowCloseToken;

        private Thread wndDedicatedThread;

        public struct WinWindowConfig
        {
            public LLHookCallback mouseCallback;
            public LLHookCallback keyboardCallback;
            public bool monitorClipboard;
            public bool monitorDesktopSwitches;
        }

        /// <summary>
        /// Creates a message only window
        /// </summary>
        /// <param name="newThread">If true, the window will run on its own thread</param>
        public WinWindow(WinWindowConfig conf, bool newThread)
        {
            windowCloseToken = new CancellationToken();
            WindowConfig = conf;

            if (!newThread)
            {
                WindowStart();
            }
            else
            {
                wndDedicatedThread = new Thread(WindowStart);
                wndDedicatedThread.SetApartmentState(ApartmentState.STA); //Window threads must be marked as STA
                wndDedicatedThread.IsBackground = true;
                wndDedicatedThread.Name = "MessageOnlyWindowThread";
                wndDedicatedThread.Start();
            }
        }

        public void CloseWindow()
        {
            if (DeadWindow)
                throw new InvalidOperationException("Window is already closed");

            SendMessage(WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            Thread.Sleep(100);
            if (MouseHookAssigned) UnhookWindowsHookEx(mouseProcID);
            if (KeyboardHookAssigned) UnhookWindowsHookEx(keyboardProcID);
            UnregisterClassA(wndClass.lpszClassName, Process.GetCurrentProcess().Handle);
            WindowHandle = IntPtr.Zero;
        }

        private void WindowStart()
        {
            RegisterWindowClass();
            WindowHandle = CreateMessageOnlyWindow();

            //Assign hooks from config
            if (WindowConfig.mouseCallback != null)
                WndAssignMouseProc(WindowConfig.mouseCallback);

            if (WindowConfig.keyboardCallback != null)
                WndAssignKeyboardProc(WindowConfig.keyboardCallback);

            if (WindowConfig.monitorDesktopSwitches)
                WndMonitorDesktopSwitches();

            if (WindowConfig.monitorClipboard)
                WndMonitorClipboard();

            WndMessageLoop();
        }

        private IntPtr CreateMessageOnlyWindow()
        {
            IntPtr hWnd = CreateWindowEx(0,
                wndClass.lpszClassName, //The name of the window class to use, we defined this in RegisterWindowClass()
                wndName,  //name of window
                0,  //Window style
                0,  //X pos
                0,  //Y pos
                0,  //Width
                0,  //height
                HWND_MESSAGE,   //Specify the parent window as HWND_MESSAGE which will prevent a full window from being created
                IntPtr.Zero,
                Process.GetCurrentProcess().Handle,
                IntPtr.Zero
                );

            if (hWnd == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create window");
            }

            return hWnd;
        }

        private void RegisterWindowClass()
        {
            //We need to create a win32 compatible pointer to the WndProc function
            //Stored as a private variable to prevent garbage collection
            wndCallback = WndProc;
            wndCallbackPointer = Marshal.GetFunctionPointerForDelegate(wndCallback);

            //Creating the window class to register
            wndClass = new WNDCLASSEX()
            {
                cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)),
                lpfnWndProc = wndCallbackPointer,
                cbWndExtra = 0,
                hInstance = Process.GetCurrentProcess().Handle,
                lpszClassName = "isclass",

                //We just need to make sure these values are empty and not filled with random data
                hIcon = IntPtr.Zero,
                hIconSm = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                cbClsExtra = 0
            };

            //Unregister class incase it has already been registered
            UnregisterClassA("isclass", IntPtr.Zero);

            if (RegisterClassEx(ref wndClass) == 0)  //Returns 0 if registerclassex fails
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register window class");  //Throw error with code if fails
            }
        }

        private void WndMessageLoop()
        {
            wndThreadId = GetCurrentThreadId();
            ISLogger.Write("Thread ID: " + wndThreadId);

            while (!windowCloseToken.IsCancellationRequested)
            {
                if (GetMessage(out MSG message, WindowHandle, 0, 0) != 0)
                {
                    DispatchMessage(ref message);   //Dispatch the messages to wndproc / delegate callbacks
                }
                else
                {
                    ISLogger.Write($"WinWindow received WM_QUIT");
                    break;
                }
            }

            DeadWindow = true;
            ISLogger.Write($"WinWindow exited");
        }

        private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            switch (message)
            {
                case WM_CLIPBOARDUPDATE:
                    ClipboardContentChanged?.Invoke(this, null);
                    break;
                case WM_CLOSE:
                    PostQuitMessage(0);
                    break;
            }


            return DefWindowProcA(hwnd, message, wParam, lParam);
        }

        private void WndWinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            DesktopSwitched?.Invoke(this, null);
        }
        private void WndAssignMouseProc(LLHookCallback callback)
        {
            if (MouseHookAssigned)
                throw new InvalidOperationException("Mouse hook is already assigned");

            mouseProcCallback = callback;
            using (Process proc = Process.GetCurrentProcess())
            {
                using (ProcessModule mod = proc.MainModule)
                {
                    mouseProcID = SetWindowsHookEx(WH_MOUSE_LL, mouseProcCallback, GetModuleHandle(mod.ModuleName), 0);

                    if (mouseProcID == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, "Failed to create mouse hook (" + error + ")");
                    }
                    else
                    {
                        ISLogger.Write("Mouse hook created");
                        MouseHookAssigned = true;
                    }
                }
            }
        }

        private void WndAssignKeyboardProc(LLHookCallback callback)
        {
            keyboardProcCallback = callback;
            using (Process proc = Process.GetCurrentProcess())
            {
                using (ProcessModule mod = proc.MainModule)
                {
                    keyboardProcID = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProcCallback, GetModuleHandle(mod.ModuleName), 0);

                    if (keyboardProcID == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, "Failed to create keyboard hook (" + error + ")");
                    }
                    else
                    {
                        ISLogger.Write("keyboard hook created");
                        KeyboardHookAssigned = true;
                    }
                }
            }
        }

        private void WndMonitorDesktopSwitches()
        {
            winEventCallback = WndWinEventProc;
            hWinEventHook = SetWinEventHook(0x0020, 0x0020, IntPtr.Zero, winEventCallback, 0, 0, 0);

            if (hWinEventHook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create WinEventHook");
            }

            MonitoringDesktopSwitches = true;
        }

        private void WndMonitorClipboard()
        {
            if (!AddClipboardFormatListener(WindowHandle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add clipboard format listener");
            }

            MonitoringClipboard = true;
        }
    }
}
