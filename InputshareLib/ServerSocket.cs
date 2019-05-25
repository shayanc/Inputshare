using InputshareLib.Input;
using InputshareLib.Net;
using InputshareLib.Net.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static InputshareLib.Settings;

namespace InputshareLib
{
    /// <summary>
    /// Manages a connection to an inputshare server
    /// </summary>
    public class ServerSocket : IDisposable
    {
        private Socket tcpSocket;
        public ServerSocketState State { get; private set; } = ServerSocketState.Idle;

        /// <summary>
        /// The directory where received files will be stored
        /// </summary>
        public string FileReceivePath { get; set; } = @"C:\";

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler ConnectionFailed;
        public event EventHandler ConnectionError;

        public event EventHandler<ISInputData> InputReceived;
        public event EventHandler<MessageType> MessageReceived;
        public event EventHandler<string> ClipboardTextReceived;

        public IPEndPoint serverAddress { get; private set; }
        private byte[] socketBuffer = new byte[Settings.ClientSocketBuffer];

        private Thread socketReceiveThread;
        private CancellationTokenSource cancelToken;
        private Timer receiveProgressPrintTimer;

        private string cName;
        private Guid cId;
        private StringBuilder cbTextBuilder;

        private bool disconnecting = false;
        private int conId = 0;
        private List<FileReceiveHandler> receiveHandlers = new List<FileReceiveHandler>();

        public void Connect(IPEndPoint address, string name, Guid id)
        {
            if (IsStateConnected()){
                throw new InvalidOperationException($"Cannot connect when state is {State}");
            }

            cancelToken = new CancellationTokenSource();
            receiveProgressPrintTimer = new Timer(ReceiveProgressPrintTimer_Tick, 0, 2000, 2000);
            cName = name;
            disconnecting = false;
            cId = id;
            tcpSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            tcpSocket.NoDelay = true;
            serverAddress = address;
            ISLogger.Write($"ServerSocket->Connecting to {address.ToString()}");
            SetState(ServerSocketState.AttemptingConnection);
            conId++;
            tcpSocket.BeginConnect(address, ConnectCallback, conId);
        }

        //logs the progress of all file tranfers every 2000ms (if any)
        private void ReceiveProgressPrintTimer_Tick(object sync)
        {
            foreach(var handle in receiveHandlers)
            {
                ISLogger.Write($"{handle.FileName}: {handle.PercentComplete}%");
            }
        }

        public void Disconnect()
        {
            if(!IsStateConnected()){
                throw new InvalidOperationException($"Cannot disconnect when state is {State}");
            }
            ISLogger.Write($"ServerSocket->Disconnecting");
            disconnecting = true;
            tcpSocket.Dispose();
            SetState(ServerSocketState.Idle);
            Disconnected?.Invoke(this, null);
        }

        public void SendClipboardText(string text)
        {
            if (!IsStateConnected())
            {
                throw new InvalidOperationException($"Attempted to send clipboard data when connection state is {State}");
            }

            if(text == null)
            {
                ISLogger.Write("Warning: Attempted to send a null string");
                return;
            }

            int partsNeeded = (text.Length / ClipboardTextPartSize);
            int part = 0;
            int strPos = 0;
            if (partsNeeded == 1)
            {
                ClipboardSetTextMessage msg = new ClipboardSetTextMessage(text, 1, 1);
                byte[] data = msg.ToBytes();
                tcpSocket.BeginSend(data, 0, data.Length, 0, SendCallback, null);
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
                byte[] d = msg.ToBytes();
                tcpSocket.BeginSend(d, 0, d.Length, 0, SendCallback, null);
                part++;
            }
            text = "";
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                if ((int)ar.AsyncState != conId)
                    return;

                tcpSocket.EndConnect(ar);
                SetState(ServerSocketState.ConnectionEstablished);
                socketReceiveThread = new Thread(SocketReceiveThreadLoop);
                socketReceiveThread.Start();
                SendClientInfo(cName, cId);
            }catch(ObjectDisposedException)
            {
                ISLogger.Write("ServerSocket->Disposed");
            }
            catch (Exception ex)
            {
                if (!disconnecting)
                {
                    disconnecting = true;
                }
                else
                    return;

                OnConnectionError(ex, ServerSocketState.ConnectionFailed);
                return;
            }
        }
        
        private void SocketReceiveThreadLoop()
        {
            try
            {
                ISLogger.Write("Socket receive thread started");
                byte[] header = new byte[4];
                while (!cancelToken.IsCancellationRequested)
                {
                    int hRem = 4;
                    int hPos = 0;
                    do
                    {
                        int hIn = tcpSocket.Receive(header, hPos, hRem, 0);   //make sure we read all 4 bytes of header
                        hPos += hIn;
                        hRem -= hIn;
                    } while (hRem > 0);
                    int pSize = BitConverter.ToInt32(header, 0);

                    if(pSize > Settings.ClientMaxPacketSize)
                    {
                        OnConnectionError(new Exception("Connection error: Server sent invalid packet size of " + pSize), ServerSocketState.ConnectionError);
                        return;
                    }
                    int dRem = pSize;
                    int bPos = 4;
                    do
                    {
                        int bIn = tcpSocket.Receive(socketBuffer, bPos, dRem, 0);
                        bPos += bIn;
                        dRem = pSize - bPos + 4;
                    } while (dRem > 0);
                    MessageType cmd = (MessageType)socketBuffer[4];
                    switch (cmd)
                    {
                        case MessageType.Input:
                            {
                                InputMessage msg = InputMessage.FromBytes(socketBuffer);
                                InputReceived?.Invoke(this, msg.Input);
                                break;
                            }
                        case MessageType.ServerOK:
                            ISLogger.Write("Server sent OK");
                            SetState(ServerSocketState.Connected);
                            MessageReceived?.Invoke(this, cmd);
                            break;
                        case MessageType.SetClipboardText:
                            ProcessCbCopy(ClipboardSetTextMessage.FromBytes(socketBuffer));
                            break;
                        case MessageType.FileTransferPart:
                            FileTransferPartMessage fileMsg = FileTransferPartMessage.FromBytes(ref socketBuffer);
                            ReadFilePart(fileMsg);
                            break;

                        default:
                            MessageReceived?.Invoke(this, cmd);
                            break;
                    }
                }
            }catch(ObjectDisposedException )
            {

            }catch(Exception ex)
            {
                if (!disconnecting)
                {
                    disconnecting = true;
                }
                else
                    return;

                if (cancelToken.IsCancellationRequested)
                    return;

                if (!ex.Message.Contains("WSACancelBlockingCall")){
                    //ISLogger.Write("Serversocket error: " + ex.Message);
                }
                OnConnectionError(ex, ServerSocketState.ConnectionError);
            }
        }

        private FileReceiveHandler ReceiveHandlerFromTransferId(Guid transferId)
        {
            foreach(FileReceiveHandler handler in receiveHandlers)
            {
                if (handler.TransferId == transferId)
                    return handler;
            }
            return null;
        }

        private void ReadFilePart(FileTransferPartMessage fileMsg)
        {
            if(fileMsg.PartNumber == 0)
            {
                FileReceiveHandler handler = new FileReceiveHandler(fileMsg, FileReceivePath);
                handler.ReceiveComplete += Handler_ReceiveComplete;
                receiveHandlers.Add(handler);
            }
            else
            {
                FileReceiveHandler handle = ReceiveHandlerFromTransferId(fileMsg.FileTransferId);
                if (handle != null)
                    handle.AddChunk(fileMsg);
            }
        }

        //Called when a file receive handler has complete
        private void Handler_ReceiveComplete(object sender, EventArgs e)
        {
            FileReceiveHandler handler = (FileReceiveHandler)sender;

            if (receiveHandlers.Contains(handler))
            {
                receiveHandlers.Remove(handler);
            }
        }

        public void SendCommand(MessageType type)
        {
            SingleCommandMessage msg = new SingleCommandMessage(type);
            byte[] data = msg.ToBytes();
            tcpSocket?.BeginSend(data, 0, data.Length, 0, SendCallback, null);
        }

        private void SetState(ServerSocketState state)
        {
            State = state;

            switch (state)
            {
                case ServerSocketState.Connected:
                    Connected?.Invoke(this, null);
                    break;
                case ServerSocketState.ConnectionError:
                    ConnectionError?.Invoke(this, null);
                    break;
                case ServerSocketState.ConnectionFailed:
                    ConnectionFailed?.Invoke(this, null);
                    break;
            }

        }

        private void ProcessCbCopy(ClipboardSetTextMessage msg)
        {
            if(msg.Part == 1)
            {
                cbTextBuilder = new StringBuilder();
            }

            cbTextBuilder.Append(msg.Text);

            if(msg.Part == msg.PartCount)
            {
                ClipboardTextReceived?.Invoke(this, cbTextBuilder.ToString());
            }
        }

        private void SendClientInfo(string name, Guid id)
        {
            ClientLoginMessage msg = new ClientLoginMessage(name, id);
            byte[] data = msg.ToBytes();
            tcpSocket.BeginSend(data, 0, data.Length, 0, SendCallback, null);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                int bOut = tcpSocket.EndSend(ar);
            }
            catch (SocketException ex)
            {
                if (!disconnecting)
                {
                    disconnecting = true;
                }
                else
                    return;

                OnConnectionError(ex, ServerSocketState.ConnectionError);
                return;
            }
            catch (ObjectDisposedException)
            {

            }
        }

        private void OnConnectionError(Exception ex, ServerSocketState newState)
        {
            if (cancelToken.IsCancellationRequested)
                return;

            foreach(var handler in receiveHandlers.ToArray())
            {
                handler.CancelTransfer();
            }

            cancelToken.Cancel();
            tcpSocket.Close();
            ISLogger.Write("Serversocket error: " + ex.Message);
            SetState(newState);
        }

        public bool IsStateConnected()
        {
            switch (State) {
                case ServerSocketState.Connected:
                case ServerSocketState.ConnectionEstablished:
                    return true;
                default:
                    return false;
            }
        }

        public enum ServerSocketState
        {
            Idle,
            AttemptingConnection,
            ConnectionEstablished,
            Connected,
            ConnectionFailed,
            ConnectionError,
            Closed
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    cancelToken.Cancel();
                    tcpSocket?.Dispose();
                    State = ServerSocketState.Closed;
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
