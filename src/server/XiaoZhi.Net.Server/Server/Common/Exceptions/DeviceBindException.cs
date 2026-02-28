using System;

namespace XiaoZhi.Net.Server.Common.Exceptions
{
    internal class DeviceBindException : Exception
    {
        public string BindCode { get; }

        public DeviceBindException(string bindCode) : base()
        {
            this.BindCode = bindCode;
        }
        public DeviceBindException(string bindCode, string message) : base(message)
        {
            this.BindCode = bindCode;
        }
        public DeviceBindException(string bindCode, string message, Exception innerException) : base(message, innerException)
        {
            this.BindCode = bindCode;
        }
    }
}
