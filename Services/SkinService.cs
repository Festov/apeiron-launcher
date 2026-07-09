using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Apeiron.Services;

public class SkinService
{
    private readonly string _cacheDir;

    public SkinService(string minecraftDir)
    {
        _cacheDir = Path.Combine(minecraftDir, "cache", "skins");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<BitmapImage?> LoadSkinAsync(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid))
            return null;

        var cachePath = Path.Combine(_cacheDir, $"{uuid}.png");

        if (File.Exists(cachePath))
        {
            var cached = LoadBitmapFromFile(cachePath);
            if (cached != null)
                return cached;
        }

        var services = new Func<Task<BitmapImage?>>[]
        {
            () => LoadSkinFromCrafatar(uuid, cachePath),
            () => LoadSkinFromMinotar(uuid, cachePath),
            () => LoadSkinFromMinecraftApi(uuid, cachePath),
            () => LoadSkinFromStala(uuid, cachePath)
        };

        foreach (var service in services)
        {
            var image = await service();
            if (image != null)
                return image;
        }

        return null;
    }

    private static BitmapImage? LoadBitmapFromFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> LoadSkinFromCrafatar(string uuid, string cachePath)
    {
        var skinUrl = $"https://crafatar.com/avatars/{uuid}?size=128&overlay";
        return await DownloadSkinImage(skinUrl, cachePath);
    }

    private static async Task<BitmapImage?> LoadSkinFromMinotar(string uuid, string cachePath)
    {
        var skinUrl = $"https://minotar.net/avatar/{uuid}/128.png";
        return await DownloadSkinImage(skinUrl, cachePath);
    }

    private static async Task<BitmapImage?> LoadSkinFromStala(string uuid, string cachePath)
    {
        var skinUrl = $"https://stala.skin/avatar/{uuid}/128";
        return await DownloadSkinImage(skinUrl, cachePath);
    }

    private static async Task<BitmapImage?> DownloadSkinImage(string url, string cachePath)
    {
        try
        {
            using var response = await AppHttp.Client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(cachePath, bytes);
            return CreateBitmapFromBytes(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> LoadSkinFromMinecraftApi(string uuid, string cachePath)
    {
        try
        {
            var apiUrl = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}";
            var json = await AppHttp.Client.GetStringAsync(apiUrl);

            var textureStart = json.IndexOf("\"textures\":", StringComparison.Ordinal);
            if (textureStart < 0)
                return null;

            var textureEnd = json.IndexOf('}', textureStart + 20);
            if (textureEnd < 0)
                return null;

            var textureBlock = json.Substring(textureStart, textureEnd - textureStart + 1);
            var skinUrlStart = textureBlock.IndexOf("\"url\":\"", StringComparison.Ordinal);
            if (skinUrlStart < 0)
                return null;

            var urlStart = skinUrlStart + 7;
            var urlEnd = textureBlock.IndexOf('"', urlStart);
            if (urlEnd < 0)
                return null;

            var skinUrl = textureBlock.Substring(urlStart, urlEnd - urlStart).Replace("\\/", "/");

            using var skinResponse = await AppHttp.Client.GetAsync(skinUrl);
            if (!skinResponse.IsSuccessStatusCode)
                return null;

            var bytes = await skinResponse.Content.ReadAsByteArrayAsync();
            var fullImage = CreateBitmapFromBytes(bytes);
            if (fullImage == null)
                return null;

            var cropped = CropSkinHead(fullImage);
            if (cropped == null)
                return null;

            await File.WriteAllBytesAsync(cachePath, bytes);
            return cropped;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? CreateBitmapFromBytes(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? CropSkinHead(BitmapImage fullSkin)
    {
        try
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(fullSkin));
            encoder.Save(ms);
            ms.Position = 0;

            using var bitmap = new System.Drawing.Bitmap(ms);

            var headSize = bitmap.Width / 8;
            if (headSize < 8) headSize = 8;

            using var headBitmap = new System.Drawing.Bitmap(headSize * 2, headSize * 2);
            using var g = System.Drawing.Graphics.FromImage(headBitmap);

            g.DrawImage(bitmap,
                new System.Drawing.Rectangle(0, 0, headSize * 2, headSize * 2),
                new System.Drawing.Rectangle(0, 0, headSize, headSize),
                System.Drawing.GraphicsUnit.Pixel);

            using var outputMs = new MemoryStream();
            headBitmap.Save(outputMs, System.Drawing.Imaging.ImageFormat.Png);
            outputMs.Position = 0;

            var result = new BitmapImage();
            result.BeginInit();
            result.StreamSource = outputMs;
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.EndInit();
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }
}
