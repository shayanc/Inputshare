﻿using InputshareLib.Input.Hotkeys;
using InputshareLib.Input;
using System.Diagnostics;

namespace InputshareLib
{
    public static class Settings
    {
        /// <summary>
        /// Prevents lag while debugging
        /// </summary>
        public const bool DEBUG_DISABLEHOOK = false;

        public readonly static int ClientListenerQueueSize = 6;
        public readonly static int ClientListenerMaxOpenSockets = 12;
        public readonly static bool ClientListenerSocketNoDelay = true;
        public readonly static int ClientListenerSocketSendBufferSize = 32768;
        public readonly static int ClientListenerSocketReceiveBufferSize = 32768;
        public readonly static int ClientListenerSocketBufferSize = 32768;
        public readonly static int ClientListenerDefaultPort = 44101;
        public readonly static int ClientListenerSocketTimeout = 1999;

        public readonly static int ServerDefaultMaxClients = 12;
        public readonly static int ServerClientBufferSize = 32768;  
        public readonly static int ServerClientMaxPacketSize = 32768;//max size of a packet received by a client
        public readonly static int ServerHeartbeatInterval = 2000;
        public readonly static ProcessPriorityClass ServerBasePriority = ProcessPriorityClass.RealTime;

        public readonly static int ClientSocketBuffer = 1024*40;
        public readonly static int ClientMaxPacketSize = 1024*39;
        public readonly static int ClientSocketReceiveBuffer = 1024 * 64;
        public readonly static bool ClientReleaseKeysOnFocus = true;
        public readonly static ProcessPriorityClass ClientBasePriority = ProcessPriorityClass.RealTime;

        public readonly static int ClipboardTextPartSize = 28000;
        public readonly static int FileTransferPartSize = 1024*32; //size of file chunks

        public readonly static Hotkey ServerDefaultExitHotkey = new Hotkey(ScanCode.Q, Hotkey.Modifiers.Ctrl | Hotkey.Modifiers.Shift);
        public readonly static Hotkey ServerDefaultSwitchLocalHotkey = new Hotkey(ScanCode.Z, Hotkey.Modifiers.Shift);
        public readonly static Hotkey ServerDefaultSASHotkey = new Hotkey(ScanCode.P, Hotkey.Modifiers.Ctrl | Hotkey.Modifiers.Alt);
    }
}
