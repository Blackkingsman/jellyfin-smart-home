using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartHome.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>How many collection rows to show per day (rotates daily).</summary>
    public int ShelfRows { get; set; } = 6;

    /// <summary>Items per row.</summary>
    public int ItemsPerRow { get; set; } = 20;
}
