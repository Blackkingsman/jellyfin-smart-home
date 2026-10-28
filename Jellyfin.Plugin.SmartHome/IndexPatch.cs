using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartHome;

/// <summary>Shape File Transformation sends to the callback.</summary>
public class PatchRequestPayload
{
    [JsonPropertyName("contents")]
    public string? Contents { get; set; }
}

public static class IndexPatch
{
    private const string Tag = "<script defer src=\"../SmartHome/client.js\"></script>";

    /// <summary>Called by File Transformation each time index.html is served.</summary>
    public static string Apply(PatchRequestPayload payload)
    {
        string html = payload.Contents ?? string.Empty;
        if (html.Contains(Tag, StringComparison.Ordinal))
        {
            return html;
        }

        return html.Replace("</body>", Tag + "</body>", StringComparison.Ordinal);
    }
}
