using System;

namespace XiaoZhi.Net.Server.Common.Exceptions
{
    internal class DeviceNotFoundException : Exception
    {
        public DeviceNotFoundException() : base()
        {
            
        }
        public DeviceNotFoundException(string message) : base(message)
        {

        }
        public DeviceNotFoundException(string message, Exception innerException) : base(message, innerException)
        {

        }
    }
}
