using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartHome.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartHome.Shelves;

/// <summary>Creates, updates, and retires the shelf collections so they match the library.</summary>
public class ShelfSync
{
    private const int PosterTiles = 9;

    private readonly ILibraryManager _library;
    private readonly ICollectionManager _collections;
    private readonly IProviderManager _providers;
    private readonly ILogger<ShelfSync> _logger;

    public ShelfSync(ILibraryManager library, ICollectionManager collections, IProviderManager providers, ILogger<ShelfSync> logger)
    {
        _library = library;
        _collections = collections;
        _providers = providers;
        _logger = logger;
    }

    private IReadOnlyList<BaseItem> Query(BaseItemKind kind) => _library.GetItemList(new InternalItemsQuery
    {
        IncludeItemTypes = [kind],
        Recursive = true,
        IsVirtualItem = false,
        DtoOptions = new DtoOptions(true),
    });

    public async Task RunAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        Plugin plugin = Plugin.Instance!;
        PluginConfiguration config = plugin.Configuration;

        IReadOnlyList<BaseItem> movies = Query(BaseItemKind.Movie);
        IReadOnlyList<BaseItem> shows = Query(BaseItemKind.Series);
        Dictionary<Guid, BaseItem> items = movies.Concat(shows).ToDictionary(i => i.Id);
        List<ShelfResult> wanted = ShelfCatalog.Build(movies, shows, DateTime.UtcNow);
        Dictionary<Guid, BoxSet> boxSets = Query(BaseItemKind.BoxSet).OfType<BoxSet>().ToDictionary(b => b.Id);
        _logger.LogInformation("Smart Shelves: {Movies} movies, {Shows} shows -> {Shelves} shelves", movies.Count, shows.Count, wanted.Count);

        void Save() => plugin.SaveConfiguration();

        for (int i = 0; i < wanted.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (shelf, ids) = wanted[i];
            ShelfRecord? record = config.Shelves.FirstOrDefault(r => r.Key == shelf.Key);

            // adopt collections made by the old Python script (or a previous install) by name
            BoxSet? box = record != null && boxSets.TryGetValue(record.CollectionId, out var owned)
                ? owned
                : boxSets.Values.FirstOrDefault(b => b.Name == shelf.Name);

            if (box == null)
            {
                _logger.LogInformation("Smart Shelves: + {Name} with {Count}", shelf.Name, ids.Count);
                box = await _collections.CreateCollectionAsync(new CollectionCreationOptions
                {
                    Name = shelf.Name,
                    IsLocked = true,
                    ItemIdList = ids.Select(id => id.ToString("N")).ToArray(),
                }).ConfigureAwait(false);
            }
            else
            {
                var current = box.LinkedChildren.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value).ToHashSet();
                var add = ids.Where(id => !current.Contains(id)).ToList();
                var drop = current.Except(ids).ToList();
                if (add.Count > 0)
                {
                    await _collections.AddToCollectionAsync(box.Id, add).ConfigureAwait(false);
                }

                if (drop.Count > 0)
                {
                    await _collections.RemoveFromCollectionAsync(box.Id, drop).ConfigureAwait(false);
                }

                if (add.Count + drop.Count > 0)
                {
                    _logger.LogInformation("Smart Shelves: ~ {Name} +{Add} -{Drop}", shelf.Name, add.Count, drop.Count);
                }
            }

            if (record == null || record.CollectionId != box.Id)
            {
                config.Shelves.RemoveAll(r => r.Key == shelf.Key);
                record = new ShelfRecord { Key = shelf.Key, CollectionId = box.Id };
                config.Shelves.Add(record);
                Save();   // so a crash later in the run can't orphan this collection
            }

            await RefreshPosterAsync(box, record, shelf, ids, items, cancellationToken).ConfigureAwait(false);
            progress.Report(90.0 * (i + 1) / wanted.Count);
        }

        // shelves that no longer qualify (e.g. an era fell out of the top list)
        var keep = wanted.Select(w => w.Shelf.Key).ToHashSet();
        foreach (ShelfRecord stale in config.Shelves.Where(r => !keep.Contains(r.Key)).ToList())
        {
            if (boxSets.TryGetValue(stale.CollectionId, out var box))
            {
                _logger.LogInformation("Smart Shelves: - retire {Name}", box.Name);
                _library.DeleteItem(box, new DeleteOptions { DeleteFileLocation = true }, true);
            }

            config.Shelves.Remove(stale);
        }

        Save();
        progress.Report(100);
    }

    /// <summary>Rebuild the collage when the shelf's top items (or size) change.</summary>
    private async Task RefreshPosterAsync(BoxSet box, ShelfRecord record, Shelf shelf, IReadOnlyList<Guid> ids, Dictionary<Guid, BaseItem> items, CancellationToken cancellationToken)
    {
        string fingerprint = $"{ids.Count}:{string.Join(',', ids.Take(PosterTiles))}";
        if (record.Poster == fingerprint && box.HasImage(ImageType.Primary, 0))
        {
            return;
        }

        List<string> paths = ids.Take(PosterTiles * 2)
            .Select(id => items[id])
            .Where(item => item.HasImage(ImageType.Primary, 0))
            .Select(item => item.GetImagePath(ImageType.Primary, 0))
            .Where(File.Exists)
            .Take(PosterTiles)
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            byte[] jpeg = PosterRenderer.Render(shelf.Name, ids.Count, shelf.Unit, paths);
            using var stream = new MemoryStream(jpeg);
            await _providers.SaveImage(box, stream, "image/jpeg", ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
            await box.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
            record.Poster = fingerprint;
            Plugin.Instance!.SaveConfiguration();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // a poster is nice to have; never let it stop the sync
            _logger.LogWarning(e, "Smart Shelves: poster for {Name} failed", shelf.Name);
        }
    }
}
