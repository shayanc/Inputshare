using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace InputshareLib.Net.Messages
{
    class ContinueFileDragMessage : INetworkMessage
    {
        public MessageType Type { get; }

        public ContinueFileDragMessage(string fName, Bitmap fIcon, int fSize)
        {
            Type = MessageType.ContinueFileDrag;
            FileName = fName;
            FileIcon = fIcon;
            FileSize = fSize;
        }

        public string FileName { get; }
        public Bitmap FileIcon { get; }
        public int FileSize { get; }

        public byte[] ToBytes()
        {
            try
            {
                //TODO - file size should be stored as a LONG!
                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter bw = new BinaryWriter(ms))
                    {
                        byte[] iconData = ImageToArray(FileIcon);
                        byte[] nameData = Encoding.ASCII.GetBytes(FileName);

                        bw.Write(iconData.Length + nameData.Length + 13); //write size of packet ignoring first 4 bytes
                        bw.Write((byte)Type); //write file type
                        bw.Write(iconData.Length); //write length of icon data
                        bw.Write(iconData); //write icon
                        bw.Write(nameData.Length); //write length of file name
                        bw.Write(nameData); //write name data
                        bw.Write(BitConverter.GetBytes(FileSize)); //write file size
                        return ms.ToArray();
                    }
                }
            }catch(Exception ex)
            {
                throw new MessageSerializeError(ex.Message, ex);
            }
        }

        public static ContinueFileDragMessage FromBytes(ref byte[] data)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                {
                    using (BinaryReader br = new BinaryReader(ms))
                    {
                        br.ReadInt32(); //ingore packet size
                        br.ReadByte(); //ignore packet type
                        int iconDataLen = br.ReadInt32();
                        Bitmap fIcon = ArrayToBitmap(br.ReadBytes(iconDataLen));
                        int nameDataLen = br.ReadInt32();
                        string fName = Encoding.ASCII.GetString(br.ReadBytes(nameDataLen));
                        int fSize = br.ReadInt32();
                        return new ContinueFileDragMessage(fName, fIcon, fSize);
                    }
                }
            }
            catch(Exception ex)
            {
                throw new MessageUnreadableException(ex.Message);
            }
        }

        private static Bitmap ArrayToBitmap(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                ms.Seek(0, SeekOrigin.Begin);
                return (Bitmap)Bitmap.FromStream(ms);
            }
        }

        private byte[] ImageToArray(Bitmap bMap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bMap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
}
