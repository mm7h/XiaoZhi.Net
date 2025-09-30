using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Media.Common.Models
{
    internal struct OutputBufferFrame
    {
        public float[] Data;
        public bool IsFirst;
        public bool IsLast;

        public OutputBufferFrame(float[] data, bool isFirst, bool isLast)
        {
            Data = data;
            IsFirst = isFirst;
            IsLast = isLast;
        }
    }
}
