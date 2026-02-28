namespace XiaoZhi.Net.Server.Resources
{
    internal interface IOnnxModel : IResource<ModelSetting>
    {
        public string ModelType { get; }
        public string ModelName { get; }
    }
}
