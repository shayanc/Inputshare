using System;
using System.Collections.Generic;
using System.Text;

namespace InputshareLib.Net.Messages
{
    class MessageSerializeError : Exception
    {
        public MessageSerializeError(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
