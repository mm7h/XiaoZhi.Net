using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Services;

namespace XiaoZhi.Net.Application.Services
{
    public class TestService : ApplicationService
    {
        /// <summary>
        /// 动态Api
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [HttpGet("hello-world")]
        public string GetHelloWorld(string? name)
        {
            return $"HelloWord - {name ?? "no name"}.";
        }
    }
}
