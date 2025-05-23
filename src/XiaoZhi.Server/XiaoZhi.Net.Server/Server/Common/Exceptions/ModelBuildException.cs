using System;

namespace XiaoZhi.Net.Server.Common.Exceptions
{
    public class ModelBuildException : Exception
    {
        public ModelBuildException() { }
        public ModelBuildException(string errorMessage) : base(errorMessage)
        { }

    }
}
