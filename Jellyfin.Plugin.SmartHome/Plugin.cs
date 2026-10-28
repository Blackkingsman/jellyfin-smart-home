using Jellyfin.Plugin.SmartHome.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SmartHome;

public class Plugin : BasePlugin<PluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Smart Home";

    public override Guid Id => Guid.Parse("4c1f7a52-9d3e-4b8a-a6f1-2e5d8c7b9a13");

    public override string Description => "Netflix-style home screen rows: because you watched, top picks, and smart shelves.";
}
