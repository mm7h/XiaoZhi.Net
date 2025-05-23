using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace XiaoZhi.Net.WebApi.Controllers
{
    [Route("/")]
    [ApiController]
    [AllowAnonymous]
    public class DefaultController : AbpController
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Redirect("/swagger");
        }
    }
}
