using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartHome.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>How many collection rows to show per day (rotates daily).</summary>
    public int ShelfRows { get; set; } = 8;

    /// <summary>Items per row.</summary>
    public int ItemsPerRow { get; set; } = 20;

    /// <summary>Collections this plugin created and keeps in sync.</summary>
    public List<ShelfRecord> Shelves { get; set; } = [];
}

public class ShelfRecord
{
    public string Key { get; set; } = string.Empty;

    public Guid CollectionId { get; set; }

    /// <summary>Which items the current poster was drawn from, so it's only redrawn when they change.</summary>
    public string? Poster { get; set; }
}
