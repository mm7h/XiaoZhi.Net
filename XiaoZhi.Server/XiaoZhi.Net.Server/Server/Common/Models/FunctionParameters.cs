using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Common.Models
{
    public class FunctionParameters
    {
        public FunctionParameters()
        {
            this.Type = string.Empty;
            this.Required = new List<string>();
            this.Properties = new Dictionary<string, FunctionProperties>();
        }
        public FunctionParameters(string type, IList<string> required, IDictionary<string, FunctionProperties> properties)
        {
            this.Type = type;
            this.Required = required;
            this.Properties = properties;
        }

        public string Type { get; set; }
        public IList<string> Required { get; set; }
        public IDictionary<string, FunctionProperties> Properties { get; set; }
    }

    public class FunctionProperties
    {
        public FunctionProperties()
        {
            this.Type = string.Empty;
            this.Description = string.Empty;
        }
        public FunctionProperties(string type, string description)
        {
            this.Type = type;
            this.Description = description;
        }

        public string Type { get; set; }
        public string Description { get; set; }
    }
}
