using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;

namespace InputshareLib
{
    public static class Extensions
    {
        public static string ToHashString(this byte[] hash)
        {
            string str = "";

            foreach (byte b in hash)
            {
                str = str + b.ToString() + " ";
            }

            return str;
        }

        
    }
}
