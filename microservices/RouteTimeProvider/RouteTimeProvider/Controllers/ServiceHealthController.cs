using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace RouteTimeProvider.Controllers
{
    [EnableCors("AllowOrigin")]
    [ApiController]
    [Route("[controller]/[action]")]
    public class ServiceHealthController(ILogger<RouteTimeController> logger) : ControllerBase
    {
        private const string AliveMessage = "isAlive";

        [HttpGet]
        [ActionName(nameof(Get))]
        public ActionResult<string> Get()
        {
            logger.LogInformation("ServiceHealth route reached");
            return Ok(AliveMessage);
        }
    }
}