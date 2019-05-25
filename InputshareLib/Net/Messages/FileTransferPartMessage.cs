using System;
using System.Collections.Generic;
using System.IO;
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
                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter bw = new BinaryWriter(ms))
                    {
                        byte[] nameData = Encoding.ASCII.GetBytes(FileName);
                        byte[] transIdData = FileTransferId.ToByteArray();

                        bw.Write(nameData.Length + transIdData.Length + PartData.Length + 25);
                        bw.Write((byte)Type);
                        bw.Write(FileSize);
                        bw.Write(PartNumber);
                        bw.Write(PartCount);
                        bw.Write(nameData.Length);
                        bw.Write(nameData);
                        bw.Write(transIdData);
                        bw.Write(PartData.Length);
                        bw.Write(PartData);
                    }
                    return ms.ToArray();
                }
            }catch(Exception ex)
            {
                throw new MessageSerializeError(ex.Message, ex);
            }
        }

        public static FileTransferPartMessage FromBytes(ref byte[] data)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                {
                    using (BinaryReader br = new BinaryReader(ms))
                    {
                        br.ReadInt32(); //ingore packet size
                        br.ReadByte(); //ignore packet type
                        long fileLen = br.ReadInt64();
                        int part = br.ReadInt32();
                        int partCount = br.ReadInt32();
                        int nameSize = br.ReadInt32();
                        byte[] nameData = br.ReadBytes(nameSize);
                        Guid transId = new Guid(br.ReadBytes(16));
                        int pLen = br.ReadInt32();
                        byte[] partData = br.ReadBytes(pLen);
                        return new FileTransferPartMessage(transId, partCount, part, partData, Encoding.ASCII.GetString(nameData), fileLen);
                    }
                } 
            }catch(Exception ex)
            {
                throw new MessageUnreadableException(ex.Message);
            }
        }
    }
}
