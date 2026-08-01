using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Apeiron.Services;

/// <summary>
/// Downloads pack icons with the shared HttpClient (correct User-Agent) and decodes
/// PNG/JPEG/WebP via SkiaSharp — WPF BitmapImage cannot decode Modrinth WebP icons.
/// </summary>
public static class ModpackIconLoader
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxConcurrentDownloads = 8;
    private const int DecodeWidth = 80;

    public static async Task LoadAllAsync(
        IEnumerable<ModpackListItem> items,
        CancellationToken cancellationToken = default)
    {
        using var gate = new SemaphoreSlim(MaxConcurrentDownloads);
        var tasks = items
            .Where(i => !string.IsNullOrWhiteSpace(i.IconUrl))
            .Select(item => LoadOneAsync(item, gate, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private static async Task LoadOneAsync(
        ModpackListItem item,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        var url = item.IconUrl!;
        if (Cache.TryGetValue(url, out var cached))
        {
            SetIconOnUi(item, cached);
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Cache.TryGetValue(url, out cached))
            {
                SetIconOnUi(item, cached);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await AppHttp.Client.GetByteArrayAsync(url, cancellationToken);
            var image = Decode(bytes);
            if (image == null)
                return;

            Cache[url] = image;
            SetIconOnUi(item, image);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Keep placeholder; individual icon failures must not break the list.
        }
        finally
        {
            gate.Release();
        }
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var skBitmap = SKBitmap.Decode(bytes);
            if (skBitmap == null)
                return TryWicDecode(bytes);

            using var scaled = ScaleIfNeeded(skBitmap);
            using var image = SKImage.FromBitmap(scaled);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            if (encoded == null)
                return TryWicDecode(bytes);

            using var ms = new MemoryStream();
            encoded.SaveTo(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return TryWicDecode(bytes);
        }
    }

    private static SKBitmap ScaleIfNeeded(SKBitmap source)
    {
        if (source.Width <= DecodeWidth)
            return source.Copy() ?? source;

        var height = Math.Max(1, source.Height * DecodeWidth / source.Width);
        var info = new SKImageInfo(DecodeWidth, height);
        var resized = source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return resized ?? source.Copy() ?? source;
    }

    private static ImageSource? TryWicDecode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = DecodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static void SetIconOnUi(ModpackListItem item, ImageSource image)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            item.IconImage = image;
        else
            dispatcher.Invoke(() => item.IconImage = image);
    }
}
