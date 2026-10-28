using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartHome;

/// <summary>
/// Runs at server startup and asks the File Transformation plugin to patch index.html,
/// adding our client script to every page load of the web UI.
/// </summary>
public class StartupTask : IScheduledTask
{
    private const string IndexTransformId = "8f2b6c1e-3a4d-4e7f-9b2a-5c6d7e8f9a01";

    private readonly ILogger<StartupTask> _logger;

    public StartupTask(ILogger<StartupTask> logger)
    {
        _logger = logger;
    }

    public string Name => "Smart Home Startup";

    public string Key => "Jellyfin.Plugin.SmartHome.Startup";

    public string Description => "Registers the Smart Home script with File Transformation.";

    public string Category => "Startup Services";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // File Transformation lives in a different load context, so it can't be referenced directly
        Assembly? fileTransformation = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(assembly => assembly.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);

        MethodInfo? register = fileTransformation?
            .GetType("Jellyfin.Plugin.FileTransformation.PluginInterface")?
            .GetMethod("RegisterTransformation");

        if (register == null)
        {
            _logger.LogError("Smart Home needs the File Transformation plugin, which isn't installed or loaded");
            return Task.CompletedTask;
        }

        string json = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["id"] = IndexTransformId,
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = GetType().Assembly.FullName,
            ["callbackClass"] = typeof(IndexPatch).FullName,
            ["callbackMethod"] = nameof(IndexPatch.Apply),
        });

        // Build the JObject with File Transformation's own copy of Newtonsoft, so the types match
        // even when each plugin loads its own copy of the library
        Type jObjectType = register.GetParameters()[0].ParameterType;
        object? payload = jObjectType.GetMethod("Parse", new[] { typeof(string) })?.Invoke(null, new object[] { json });

        register.Invoke(null, new[] { payload });
        _logger.LogInformation("Smart Home registered its index.html transformation");
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
    }
}
