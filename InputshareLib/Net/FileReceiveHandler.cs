using InputshareLib.Net.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InputshareLib.Net
{
    class FileReceiveHandler
    {
        public double PercentComplete { get; private set; }
        public bool Complete { get; private set; }
        public string FileName { get; private set; }
        public Guid TransferId { get; private set; }
        public string FolderPath { get; private set; }

        public event EventHandler ReceiveComplete;
        public CancellationTokenSource ReceiveCancelToken { get; } = new CancellationTokenSource();
        private BlockingCollection<FileTransferPartMessage> messageQueue = new BlockingCollection<FileTransferPartMessage>();

        private FileStream fileWriteStream;
        private Task receiveTask;

        public FileReceiveHandler(FileTransferPartMessage firstPart, string folderPath)
        {
            FolderPath = folderPath;
            TransferId = firstPart.FileTransferId;
            FileName = firstPart.FileName;
            Complete = false;
            PercentComplete = 0;
            messageQueue.Add(firstPart);
            receiveTask = new Task(ReceiveLoop);
            receiveTask.Start();
        }

        private void ReceiveLoop()
        {
            try
            {
                ISLogger.Write($"Downloading {FileName} to {FolderPath + "\\" + FileName}");
                fileWriteStream = new FileStream(FolderPath + "\\" + FileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                fileWriteStream.Seek(0, SeekOrigin.Begin);

                while (!Complete && !ReceiveCancelToken.IsCancellationRequested)
                {
                    FileTransferPartMessage msg = messageQueue.Take(ReceiveCancelToken.Token);
                    if (msg.PartNumber == 0)
                        fileWriteStream.Position = 0;
                    else
                        fileWriteStream.Position = msg.PartNumber * Settings.FileTransferPartSize;

                    fileWriteStream.Write(msg.PartData, 0, msg.PartData.Length);

                    if (msg.PartNumber != 0)
                        PercentComplete = ((double)msg.PartNumber / (double)msg.PartCount) * 100;

                    if (msg.PartNumber == msg.PartCount)
                    {
                        Complete = true;
                        fileWriteStream.Close();
                        OnReceiveComplete();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                ISLogger.Write($"File tranfer {FileName} cancelled");
                fileWriteStream.Dispose();

                //we want to delete the file if cancelled
                try {
                    if (File.Exists(FolderPath + "\\" + FileName))
                    {
                        File.Delete(FolderPath + "\\" + FileName);
                    }
                }catch(Exception) { }

            }catch(Exception ex) {
                
                ISLogger.Write($"FileReceiveHandler->An error occurred while receiving file '{FileName}': {ex.Message}");
                ISLogger.Write(ex.StackTrace);
                fileWriteStream.Dispose();
                ReceiveComplete?.Invoke(this, null);
            }
        }

        private void OnReceiveComplete()
        {
            ISLogger.Write($"Received file {FileName}");

            using(FileStream fs = new FileStream(FolderPath + "\\" + FileName, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read))
            {
                ISLogger.Write($"File hash: " + MD5.Create().ComputeHash(fs).ToHashString());
            }

            ReceiveComplete?.Invoke(this, null);
        }

        public void CancelTransfer()
        {
            ReceiveCancelToken.Cancel();
            ReceiveComplete?.Invoke(this, null);
        }

        public void AddChunk(FileTransferPartMessage chunk)
        {
            if (!ReceiveCancelToken.IsCancellationRequested)
            {
                messageQueue.Add(chunk);
            }
        }
    }
}
