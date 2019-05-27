using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace InputshareLib
{
    /// <summary>
    /// To drag and drop to other machines, we put this window at the same edge of the display as the target machine
    /// when the file is dropped into this window, we can then retreive the name of the dropped files
    /// and start sending them to the target client
    /// 
    /// Dragging and dropping was very akward to implement, this hacky way was the only way I could find. Maybe a windows shell extension would work?
    /// TODO - window does not close properly
    /// TODO - File lengths need to be stored as long! current max file is ~2.2GB
    /// </summary>
    public class WinDropTargetWindow
    {
        #region native
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int metric);

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

        [DllImport("shell32.dll")]
        static extern uint DragQueryFileA(IntPtr hDrop, uint iFile, [Out]char[] lpszFile, uint cch);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfterWnd, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT pos);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int WM_QUIT = 0x0012;
        private const int WM_CLOSE = 0x0010;


        private readonly static IntPtr HWND_MESSAGE = new IntPtr(-3);

        #endregion

        private delegate IntPtr WndProcCallback(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private WNDCLASSEX wndClass;
        private WndProcCallback wndCallback;
        private IntPtr wndCallbackPointer;

        private IntPtr WindowHandle;

        private const int dropZoneSize = 25;

        public event EventHandler<FileDroppedEventArgs> FileDropped;

        public WinDropTargetWindow()
        {
            Task.Run(() => { CreateFullscreenWindow(); });
            
        }

        private void CreateFullscreenWindow()
        {
            CreateClass();

            WindowHandle = CreateWindowEx(0x00000010 | 0x00000008 | 0x00400000 | 0x00000080 | 0x00000020,   //Register the window as accepting files
                wndClass.lpszClassName,
                "InputshareDropper",
                0x10000000,
                0,
                0,
                50,
                50,
                IntPtr.Zero,
                IntPtr.Zero,
                Process.GetCurrentProcess().Handle,
                IntPtr.Zero
                );

            ISLogger.Write("Created drop window");

            WndMessageLoop();
        }

        private void CreateClass()
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
                lpszClassName = "isdropclass",

                //We just need to make sure these values are empty and not filled with random data
                hIcon = IntPtr.Zero,
                hIconSm = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                cbClsExtra = 0
            };

            if (RegisterClassEx(ref wndClass) == 0)  //Returns 0 if registerclassex fails
            {
                //throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register window class");  //Throw error with code if fails
            }
        }

        public void SetVisible(bool visible)
        {
            if (visible)
                ShowWindow(WindowHandle, 5); //SW_SHOW
            else
                ShowWindow(WindowHandle, 0); //SW_HIDE
        }

        private void WndMessageLoop()
        {
            ISLogger.Write("WinDropTargetWindow->Waiting");
            SetVisible(false);

            while (true)
            {
                if (GetMessage(out MSG message, WindowHandle, 0, 0) != 0)
                {
                    DispatchMessage(ref message);   //Dispatch the messages to wndproc / delegate callbacks
                }
                else
                {
                    break;
                }
            }
            ISLogger.Write($"WinDropTarget window exited");
        }

        private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == 563) //WM_FILEDROP
            {
                ISLogger.Write("FILE DROP");
                //Once we the file gets dropped, hide the window again
                ShowWindow(WindowHandle, 0); //SW_HIDE
                //Now we need to see what was being dropped
                ReadFileDrop(wParam);
                return IntPtr.Zero;
            }else if(message == WM_CLOSE)
            {
                PostQuitMessage(0);
                return IntPtr.Zero;
            }

            return DefWindowProcA(hwnd, message, wParam, lParam);
        }

        public void CloseWindow()
        {
            SendMessage(WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            WindowHandle = IntPtr.Zero;
        }

        public void MoveToCursorPosition()
        {
            GetCursorPos(out POINT pos);
            //We need to show the window to make the drop target active...
            ShowWindow(WindowHandle, 5); //SW_SHOW
            SetWindowPos(WindowHandle, new IntPtr(0), pos.X-25, pos.Y-25, 50, 50, 0x0040);
            ISLogger.Write("WinDropTargetWindow->Moved to cursor pos");
        }

        private void ReadFileDrop(IntPtr wParam)
        {
            //Passing 0xFFFFFFFF into this function will return the number of files dropped into the target
            uint ret = DragQueryFileA(wParam, 0xFFFFFFFF, null, 0);

            for(int i = 0; i < ret; i++)
            {
                char[] buff = new char[256];
                //We pass the Wparam (unkown internal struct) into the function,
                //give it a char buffer and tell it how long said buffer is
                //it will then return the name of the file at the specified index (i)

                //the return value of the function is the length of the returned filename
                uint _ret = DragQueryFileA(wParam, (uint)i, buff, 256);
                
                //We want to fire the event under a new task, to prevent the window from getting stuck visible
                Task.Run(() => { FileDropped?.Invoke(this, new FileDroppedEventArgs(new string(buff, 0, (int)_ret))); });
            }
        }
    }

    public class FileDroppedEventArgs : EventArgs
    {
        public FileDroppedEventArgs(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }
    }
}
