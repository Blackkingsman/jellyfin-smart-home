using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SmartHome;

[ApiController]
[Route("SmartHome")]
public class SmartHomeController : ControllerBase
{
    /// <summary>The web client script. Anonymous because it loads before the user signs in.</summary>
    [HttpGet("client.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public ActionResult ClientScript()
    {
        Assembly assembly = typeof(SmartHomeController).Assembly;
        Stream? script = assembly.GetManifestResourceStream("Jellyfin.Plugin.SmartHome.Web.client.js");
        if (script == null)
        {
            return NotFound();
        }

        return File(script, "application/javascript");
    }

    /// <summary>Row settings for the client.</summary>
    [HttpGet("config")]
    [Authorize]
    public ActionResult<object> Config()
    {
        Configuration.PluginConfiguration config = Plugin.Instance!.Configuration;
        return new { config.ShelfRows, config.ItemsPerRow };
    }
}
