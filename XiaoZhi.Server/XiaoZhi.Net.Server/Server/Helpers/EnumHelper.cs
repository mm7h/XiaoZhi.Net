using System;
using System.ComponentModel;
using System.Reflection;

namespace XiaoZhi.Net.Server.Helpers
{
    /// <summary>
    /// 枚举帮助类
    /// </summary>
    internal static class EnumHelper
    {
        /// <summary>
        /// 获得枚举字段的描述特性
        /// </summary>
        public static string GetDescription(this Enum thisValue)
        {
            FieldInfo field = thisValue.GetType().GetField(thisValue.ToString());
            var attr = (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute);
            if (attr == null) return string.Empty;
            return attr.Description;
        }

        /// <summary>
        /// 获得枚举字段的描述自定义特性(Attribute)
        /// </summary>
        public static T? GetAttribute<T>(this Enum thisValue) where T : class
        {
            FieldInfo field = thisValue.GetType().GetField(thisValue.ToString());
            var attr = (Attribute.GetCustomAttribute(field, typeof(T)) as T);
            return attr;
        }

        /// <summary>
        /// 获得枚举字段的名称。
        /// </summary>
        /// <returns></returns>
        public static string GetName(this Enum thisValue)
        {
            return Enum.GetName(thisValue.GetType(), thisValue);
        }

        /// <summary>
        /// 获得枚举字段的值。
        /// </summary>
        /// <returns></returns>
        public static T GetValue<T>(this Enum thisValue)
        {
            return (T)Enum.Parse(thisValue.GetType(), thisValue.ToString());
        }
    }

}
