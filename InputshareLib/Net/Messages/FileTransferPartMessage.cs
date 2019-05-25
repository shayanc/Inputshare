using System;
using System.Collections.Generic;
using System.Text;

namespace InputshareLib.Net.Messages
{
    public class FileTransferPartMessage : INetworkMessage
    {
        public MessageType Type { get; }
        public Guid FileTransferId { get; }
        public byte[] PartData { get; }
        public int PartCount { get; }
        public int PartNumber { get; }
        public string FileName { get; }
        public long FileSize { get; }

        public FileTransferPartMessage(Guid transferId, int partCount, int part, byte[] data, string fileName, long fileLen)
        {
            Type = MessageType.FileTransferPart;
            PartNumber = part;
            PartCount = partCount;
            PartData = data;
            FileTransferId = transferId;
            FileName = fileName;
            FileSize = fileLen;
        }

        public byte[] ToBytes()
        {
            try
            {
                byte[] nameData = Encoding.ASCII.GetBytes(FileName);
                byte[] data = new byte[PartData.Length + nameData.Length + 33]; //int+byte+int+int+int+int+guid+part
                Buffer.BlockCopy(BitConverter.GetBytes(data.Length - 4), 0, data, 0, 4); //write data size
                data[4] = (byte)Type; //write message type
                Buffer.BlockCopy(BitConverter.GetBytes(FileSize), 0, data, 5, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(PartNumber), 0, data, 9, 4); //write part number
                Buffer.BlockCopy(BitConverter.GetBytes(PartCount), 0, data, 13, 4); //write part count
                Buffer.BlockCopy(BitConverter.GetBytes(nameData.Length), 0, data, 17, 4); //write name length in bytes
                Buffer.BlockCopy(nameData, 0, data, 21, nameData.Length); //write name string
                Buffer.BlockCopy(FileTransferId.ToByteArray(), 0, data, 21 + nameData.Length, 16); //write transfer ID
                Buffer.BlockCopy(PartData, 0, data, 21 + nameData.Length, PartData.Length);   //write file part
                return data;
            }catch(Exception ex)
            {
                throw new MessageSerializeError(ex.Message, ex);
            }
            
        }

        public static FileTransferPartMessage FromBytes(ref byte[] data)
        {
            try
            {
                int fileLen = BitConverter.ToInt32(data, 5);
                int part = BitConverter.ToInt32(data, 9);
                int partCount = BitConverter.ToInt32(data, 13);
                int nameLen = BitConverter.ToInt32(data, 17);
                byte[] nameData = new byte[nameLen];
                Buffer.BlockCopy(data, 21, nameData, 0, nameLen);


                byte[] transIdData = new byte[16];
                Buffer.BlockCopy(data, nameLen+25, transIdData, 0, 16);
                Guid transId = new Guid(transIdData);
                byte[] partData = new byte[data.Length - nameLen - 21]; //length of the data is packet size-25
                return new FileTransferPartMessage(transId, partCount, part, partData, Encoding.ASCII.GetString(nameData), fileLen);
            }catch(Exception ex)
            {
                throw new MessageUnreadableException(ex.Message);
            }
        }
    }
}
