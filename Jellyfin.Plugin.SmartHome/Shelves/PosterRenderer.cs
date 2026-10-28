using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace Jellyfin.Plugin.SmartHome.Shelves;

/// <summary>Collection posters: a collage of the shelf's top items with the shelf name on top.</summary>
public static class PosterRenderer
{
    private const int W = 1000, H = 1500;   // 2:3, same shape as movie posters
    private const int Margin = 70;

    private static readonly string[] Accents = ["#2dd4bf", "#fbbf24", "#fb7185", "#a78bfa", "#38bdf8", "#4ade80", "#f97316"];

    // SkiaSharp comes from the server; the font ships inside this DLL so posters look the same on any host
    private static readonly Lazy<SKTypeface> Typeface = new(() =>
        SKTypeface.FromStream(typeof(PosterRenderer).Assembly.GetManifestResourceStream("Jellyfin.Plugin.SmartHome.Fonts.BebasNeue-Regular.ttf")!));

    /// <summary>Same accent for the same name across runs (and the same one the Python version picked).</summary>
    private static SKColor Accent(string name)
    {
        var hash = new BigInteger(MD5.HashData(Encoding.UTF8.GetBytes(name)), isUnsigned: true, isBigEndian: true);
        return SKColor.Parse(Accents[(int)(hash % Accents.Length)]);
    }

    /// <summary>The part of the source to show so it fills the tile without stretching.</summary>
    private static SKRect Cover(int width, int height, float tileW, float tileH)
    {
        float scale = Math.Max(tileW / width, tileH / height);
        float w = tileW / scale, h = tileH / scale;
        float left = (width - w) / 2, top = (height - h) / 2;
        return new SKRect(left, top, left + w, top + h);
    }

    private static List<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        string line = string.Empty;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string trial = line.Length == 0 ? word : $"{line} {word}";
            if (font.MeasureText(trial) <= maxWidth || line.Length == 0)
            {
                line = trial;
            }
            else
            {
                lines.Add(line);
                line = word;
            }
        }

        lines.Add(line);
        return lines;
    }

    public static byte[] Render(string name, int count, string unit, IReadOnlyList<string> posterPaths)
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse("#0b0d12"));

        var tiles = posterPaths.Select(SKImage.FromEncodedData).Where(t => t != null).ToList();
        int n = tiles.Count >= 9 ? 3 : 2;
        float tw = (float)W / n, th = (float)H / n;

        // soften the collage as a whole so tile seams stay crisp
        using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.2f, 1.2f) })
        {
            canvas.SaveLayer(blur);
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
            for (int i = 0; i < Math.Min(tiles.Count, n * n); i++)
            {
                var dest = SKRect.Create(i % n * tw, i / n * th, tw, th);
                canvas.DrawImage(tiles[i], Cover(tiles[i].Width, tiles[i].Height, tw, th), dest, sampling);
            }

            canvas.Restore();
        }

        tiles.ForEach(t => t.Dispose());

        // fade into a dark band where the title sits: alpha 90 at the top, easing to solid at the bottom
        float[] stops = [0f, 0.35f, 0.5f, 0.65f, 0.8f, 0.9f, 1f];
        SKColor[] colors = stops
            .Select(t => new SKColor(0x07, 0x08, 0x0b, (byte)(90 + 165 * Math.Pow(Math.Max(0, (t - 0.35) / 0.65), 1.4))))
            .ToArray();
        using (var shade = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, H), colors, stops, SKShaderTileMode.Clamp) })
        {
            canvas.DrawRect(0, 0, W, H, shade);
        }

        SKColor accent = Accent(name);
        string title = name.ToUpperInvariant();
        using var titleFont = new SKFont(Typeface.Value, 190);
        List<string> lines = Wrap(title, titleFont, W - 2 * Margin);
        while (lines.Count > 3 || lines.Any(l => titleFont.MeasureText(l) > W - 2 * Margin))
        {
            titleFont.Size -= 10;
            lines = Wrap(title, titleFont, W - 2 * Margin);
        }

        // baselines from the bottom up: count line, then the title lines above it
        float lineH = titleFont.Size * 0.92f;
        float capHeight = titleFont.Size * 0.7f;
        float baseline = H - 200 - lineH * (lines.Count - 1);
        using var white = new SKPaint { Color = SKColor.Parse("#f5f6f8"), IsAntialias = true };
        using var accentPaint = new SKPaint { Color = accent, IsAntialias = true };

        canvas.DrawRect(Margin, baseline - capHeight - 46, 120, 12, accentPaint);
        foreach (string line in lines)
        {
            canvas.DrawText(line, Margin, baseline, SKTextAlign.Left, titleFont, white);
            baseline += lineH;
        }

        using var countFont = new SKFont(Typeface.Value, 64);
        canvas.DrawText($"{count} {unit}", Margin + 2, H - 110, SKTextAlign.Left, countFont, accentPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
        return data.ToArray();
    }
}
