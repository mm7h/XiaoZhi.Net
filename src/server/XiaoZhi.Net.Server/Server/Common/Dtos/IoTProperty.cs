using System;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class IoTProperty
    {
        public IoTProperty(string name, string iotComponentName, string type)
        {
            this.Name = name;
            this.IoTComponentName = iotComponentName;
            this.TypeDescription = type;
            this.Type = IoTTypeMappingHelper.GetIoTType(type);
            this.StatusValue = IoTTypeMappingHelper.GetDefaultValue(type);
        }

        public string Name { get; }
        public string IoTComponentName { get; }
        public Type Type { get; }
        public string TypeDescription { get; }
        public object? StatusValue { get; set; }
    }
}
