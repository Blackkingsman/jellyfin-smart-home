using Jellyfin.Plugin.SmartHome.Shelves;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.SmartHome;

/// <summary>Keeps the smart shelf collections in sync. Runs nightly and shortly after new content arrives.</summary>
public class ShelfTask : IScheduledTask
{
    private readonly ShelfSync _sync;

    public ShelfTask(ShelfSync sync)
    {
        _sync = sync;
    }

    public string Name => "Update Smart Shelves";

    public string Key => "Jellyfin.Plugin.SmartHome.Shelves";

    public string Description => "Creates and updates the smart shelf collections and their posters.";

    public string Category => "Smart Home";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _sync.RunAsync(progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks };
    }
}
