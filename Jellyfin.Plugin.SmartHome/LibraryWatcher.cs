using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.SmartHome;

/// <summary>Queues a shelf update once the library has been quiet for a few minutes after content changes.</summary>
public sealed class LibraryWatcher : IHostedService, IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    private readonly ILibraryManager _library;
    private readonly Timer _timer;

    public LibraryWatcher(ILibraryManager library, ITaskManager tasks)
    {
        _library = library;
        _timer = new Timer(_ => tasks.QueueIfNotRunning<ShelfTask>());
    }

    // a scan adds items one by one, so each change pushes the update back until the scan settles
    private void OnChange(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie or Series or Episode)
        {
            _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded += OnChange;
        _library.ItemRemoved += OnChange;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded -= OnChange;
        _library.ItemRemoved -= OnChange;
        return Task.CompletedTask;
    }

    public void Dispose() => _timer.Dispose();
}
