using InputshareLib.Input;
using InputshareLib.Net.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static InputshareLib.Settings;

namespace InputshareLib.Server
{
    /// <summary>
    /// Represents a inputshare client
    /// </summary>
    class ConnectedClient : IDisposable
    {
        public event EventHandler ConnectionError;
        public event EventHandler<BoundEdge> ClientEdgeHit;
        public event EventHandler<string> ClipboardTextCopied;

        private Socket clientSocket;
        private Thread socketSendThread;    //Each socket has a dedicated thread for sending data
        private byte[] clientBuffer = new byte[ServerClientBufferSize];
        private Timer heartbeatTimer;
        public string ClientName { get; private set; }
        public Guid ClientGuid { get; private set; }
        public IPEndPoint ClientEndPoint { get; private set; }
        public bool Connected { get; private set; }

        public ConnectedClient LeftClient { get; set; }
        public ConnectedClient RightClient { get; set; }
        public ConnectedClient AboveClient { get; set; }
        public ConnectedClient BelowClient { get; set; }

        private List<ClipboardSetTextMessage> clipboardMsgBuffer = new List<ClipboardSetTextMessage>();

        private CancellationTokenSource cancelToken;
        private BlockingCollection<INetworkMessage> SendQueue = new BlockingCollection<INetworkMessage>();
        private BlockingCollection<INetworkMessage> LowPrioritySendQueue = new BlockingCollection<INetworkMessage>();
        private BlockingCollection<INetworkMessage>[] queueCollection;
        private ManualResetEventSlim fileTransferPartSentEvent = new ManualResetEventSlim(false);


        public ConnectedClient(Socket soc, string name, Guid id)
        {
            queueCollection = new BlockingCollection<INetworkMessage>[2] { SendQueue, LowPrioritySendQueue };
            clientSocket = soc;
            ClientName = name;
            ClientGuid = id;
            Connected = true;
            ClientEndPoint = soc.RemoteEndPoint as IPEndPoint;
            cancelToken = new CancellationTokenSource();
            heartbeatTimer = new Timer(HeartbeatTimerCallback, null, 0, ServerHeartbeatInterval);
            socketSendThread = new Thread(SocketSendThreadLoop);
            socketSendThread.Start();
            clientSocket.NoDelay = true;
            clientSocket.BeginReceive(clientBuffer, 0, 4, 0, SocketReceiveCallback, null);
        }

        private void SocketSendThreadLoop()
        {
            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    INetworkMessage msg;

                    if (SendQueue.Count != 0)                                           //if there are items waiting in the normal priority queue, send that item
                        msg = SendQueue.Take();
                    else if (LowPrioritySendQueue.Count != 0)                           //else if normal queue is empty, and there is an item in the low priorty queue, send that item
                        msg = LowPrioritySendQueue.Take();
                    else
                        BlockingCollection<INetworkMessage>.TakeFromAny(queueCollection, out msg);       //if both queues are empty, wait for either queue to receive an item

                    byte[] data = msg.ToBytes();
                    clientSocket.Send(data);

                    if (msg.Type == MessageType.FileTransferPart)   //tell the send file thread that a part has been sent
                        fileTransferPartSentEvent.Set();
                }
            }
            catch (ObjectDisposedException)
            {

            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                ISLogger.Write($"{ClientName} send thread error: {ex.Message}");
                OnConnectionError();
            }

        }

        public static ConnectedClient LocalHost;
        public ConnectedClient(bool DUMMY)
        {
            Connected = true;
            ClientName = "LocalHost";
            ClientGuid = Guid.Empty;
            ClientEndPoint = new IPEndPoint(IPAddress.Any, 0);
        }

        private void HeartbeatTimerCallback(object obj)
        {
            if (!Connected)
                return;

            SendMessage(MessageType.Heartbeat);
        }

        public void SendFile(string source)
        {
            if (!Connected)
                throw new InvalidOperationException("Client not connected");

            //we will need to create a new task to send the file so that the entire file is not written to memory at once
            //The next part of the file is only loaded when the previous part has been sent
            Task.Run(new Action(() => { SendFileTask(source); }));
        }

        private void SendFileTask(string source)
        {
            try
            {
                bool transComplete = false;
                FileInfo sourceInfo = new FileInfo(source.ToString());
                int part = 0;
                int partCount = ((int)sourceInfo.Length / Settings.FileTransferPartSize);

                int filePos = 0;
                int fileRem = (int)sourceInfo.Length; //this will track how many bytes of the file we still need to send

                Guid transferId = Guid.NewGuid();
                byte[] chunkBuffer = new byte[Settings.FileTransferPartSize];

                ISLogger.Write($"Debug: Sending file {sourceInfo.Name} ({sourceInfo.Length / 1024}KB) to {ClientName} in {partCount} parts");

                using (FileStream sourceStream = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    ISLogger.Write("File hash: " + MD5.Create().ComputeHash(sourceStream).ToHashString()); 
                    sourceStream.Seek(0, SeekOrigin.Begin); //md5.computhash actually moves the position of the filestream, so we need to reset it
                    while (fileRem > 0)   //Keep reading in chunks until the last part of file is reached
                    {
                        int pSize = fileRem;

                        if (fileRem > Settings.FileTransferPartSize)
                            pSize = Settings.FileTransferPartSize;

                        sourceStream.Position = filePos;

                        chunkBuffer = new byte[pSize];
                        int bRead = sourceStream.Read(chunkBuffer, 0, pSize);
                        filePos += pSize;
                        fileRem -= pSize;
                        LowPrioritySendQueue.Add(new FileTransferPartMessage(transferId, partCount, part, chunkBuffer, sourceInfo.Name, sourceInfo.Length));
                        fileTransferPartSentEvent.Wait();   //Wait until the current part has been sent before sending the next part
                        fileTransferPartSentEvent.Reset();
                        part++;
                    }

                    ISLogger.Write($"Sent file {sourceInfo.FullName} ({sourceInfo.Length / 1024}KB) to {ClientName}");
                }
            }
            catch (Exception ex)
            {
                ISLogger.Write($"An error occurred while sending file {source.ToString()} to {ClientName}: \n {ex.Message}");
            }

        }

        public void SendMessage(MessageType message)
        {
            if (!Connected)
                throw new InvalidOperationException("Client not connected");

            SendQueue.Add(new SingleCommandMessage(message));
        }

        public void SendInput(ISInputData input)
        {
            if (!Connected)
                return;

            SendQueue.Add(new InputMessage(input));
        }

        public void SetClipboardText(string text)
        {
            if (!Connected)
                throw new InvalidOperationException("Client not connected");

            if (text == null)
            {
                ISLogger.Write("Warning: Attempted to send null clipboard text");
                return;
            }

            ISLogger.Write("Socket->Sending clipboard text to {0}", ClientName);

            int partsNeeded = (text.Length / ClipboardTextPartSize);
            int part = 0;
            int strPos = 0;
            if (partsNeeded == 1)
            {
                ClipboardSetTextMessage msg = new ClipboardSetTextMessage(text, 1, 1);
                SendQueue.Add(msg);
                return;
            }

            while (part <= partsNeeded)
            {
                int copyLen = ClipboardTextPartSize;
                if (part == partsNeeded)
                {
                    if (strPos + copyLen > text.Length)
                    {
                        copyLen = text.Length - strPos;
                    }
                }
                string str = text.Substring(strPos, copyLen);
                ClipboardSetTextMessage msg = new ClipboardSetTextMessage(str, part + 1, partsNeeded + 1);
                strPos = strPos + copyLen;
                SendQueue.Add(msg);
                part++;
            }

            text = "";
        }

        private int dRem;
        private int bPos;
        private void SocketReceiveCallback(IAsyncResult ar)
        {
            try
            {
                if (!Connected)
                    return;

                int bytesIn = clientSocket.EndReceive(ar);
                int pSize = BitConverter.ToInt32(clientBuffer, 0);

                if (bytesIn == 0)
                {
                    ISLogger.Write("Client {0} lost connection", ClientName);
                    OnConnectionError();
                    return;
                }

                if (pSize > ServerClientMaxPacketSize)
                {
                    ISLogger.Write("Client {0} attempted to sent a packet that was too large ({1})", ClientName, pSize);
                    OnConnectionError();
                    return;
                }

                dRem = pSize;
                bPos = 4;
                do
                {
                    int bIn = clientSocket.Receive(clientBuffer, bPos, dRem, 0);
                    bPos += bIn;
                    dRem = pSize - bPos + 4;
                } while (dRem > 0);
                MessageType msg = (MessageType)clientBuffer[4];

                switch (msg)
                {
                    case MessageType.SetClipboardText:
                        {
                            ClipboardSetTextMessage cbData = ClipboardSetTextMessage.FromBytes(clientBuffer);

                            if (cbData.Part == 1 && cbData.PartCount == 1)
                            {
                                ClipboardTextCopied?.Invoke(this, cbData.Text);
                                break;
                            }

                            if (cbData.Part == 1)
                            {
                                clipboardMsgBuffer = new List<ClipboardSetTextMessage>();
                                clipboardMsgBuffer.Add(cbData);
                            }
                            else if (cbData.Part == cbData.PartCount)
                            {
                                clipboardMsgBuffer.Add(cbData);
                                string str = "";

                                foreach (var part in clipboardMsgBuffer)
                                {
                                    str = str + part.Text;
                                }
                                ClipboardTextCopied?.Invoke(this, str);
                            }
                            else
                            {
                                clipboardMsgBuffer.Add(cbData);
                            }

                            break;
                        }

                    case MessageType.ClientBoundsBottom:
                        ClientEdgeHit?.Invoke(this, BoundEdge.Bottom);
                        break;
                    case MessageType.ClientBoundsLeft:
                        ClientEdgeHit?.Invoke(this, BoundEdge.Left);
                        break;
                    case MessageType.ClientBoundsRight:
                        ClientEdgeHit?.Invoke(this, BoundEdge.Right);
                        break;
                    case MessageType.ClientBoundsTop:
                        ClientEdgeHit?.Invoke(this, BoundEdge.Top);
                        break;
                }

                clientSocket.BeginReceive(clientBuffer, 0, 4, 0, SocketReceiveCallback, null);
            }
            catch (ObjectDisposedException)
            {
                //This just means that the socket was disposed from elsewhere
                return;
            }
            catch (SocketException ex)
            {
                ISLogger.Write("Connection error on client {0}: {1}", ClientName, ex.Message);
                OnConnectionError();
                return;
            }


        }

        private void OnConnectionError()
        {
            if (!Connected)
                return;

            Connected = false;
            cancelToken.Cancel();
            ConnectionError?.Invoke(this, null);
        }

        #region IDisposable Support
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    cancelToken?.Cancel();
                    Connected = false;
                    clientBuffer = null;
                    clientSocket?.Dispose();
                    heartbeatTimer?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
