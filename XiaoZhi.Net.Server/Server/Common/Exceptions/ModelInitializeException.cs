using System;
using System.Collections.Generic;
using System.Text;

namespace XiaoZhi.Net.Server.Common.Exceptions
{
    public class ModelInitializeException : Exception
    {
        public ModelInitializeException() { }
        public ModelInitializeException(string errorMessage) : base(errorMessage)
        { }

    }
}
