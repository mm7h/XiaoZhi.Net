using System;

namespace XiaoZhi.Net.Server.Common.Models
{
    /// <summary>
    /// IoT 方法参数信息，用于描述 IoT 工具的参数类型和元数据
    /// </summary>
    internal class IoTParameterInfo
    {
        public IoTParameterInfo(string name, string description, Type parameterType, bool isRequired = true)
        {
            this.Name = name;
            this.Description = description;
            this.ParameterType = parameterType;
            this.IsRequired = isRequired;
        }

        /// <summary>参数名称</summary>
        public string Name { get; }

        /// <summary>参数描述</summary>
        public string Description { get; }

        /// <summary>参数 .NET 类型</summary>
        public Type ParameterType { get; }

        /// <summary>是否必须</summary>
        public bool IsRequired { get; }
    }
}
