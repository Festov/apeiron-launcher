using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Apeiron.Services;

public enum SkinPreset
{
    Default,
    Steve,
    Alex,
    Ari,
    Efe,
    Kai,
    Makena,
    Noor,
    Sunny,
    Zuri,
    Custom
}

public enum SkinModel
{
    Classic,
    Slim
}

public sealed class StandardSkinInfo
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsSlim { get; init; }
    public BitmapImage? HeadPreview { get; set; }
}

public sealed class AccountSkinInfo
{
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    public string Variant { get; init; } = "CLASSIC";
    public string State { get; init; } = "INACTIVE";
    public string? Alias { get; init; }
    public bool IsActive => string.Equals(State, "ACTIVE", StringComparison.OrdinalIgnoreCase);
    public bool IsSlim => string.Equals(Variant, "SLIM", StringComparison.OrdinalIgnoreCase);
    public BitmapImage? HeadPreview { get; set; }
}

public class SkinService
{
    public static readonly IReadOnlyList<(string Id, bool IsSlim)> StandardSkinDefs =
    [
        ("steve", false),
        ("alex", true),
        ("ari", true),
        ("efe", true),
        ("kai", false),
        ("makena", true),
        ("noor", true),
        ("sunny", false),
        ("zuri", false)
    ];

    private readonly string _cacheDir;
    private readonly string _localSkinsDir;
    private BitmapImage? _steveAvatar;
    private readonly Dictionary<string, BitmapImage> _headCache = new(StringComparer.OrdinalIgnoreCase);

    public SkinService(string minecraftDir)
    {
        _cacheDir = Path.Combine(minecraftDir, "cache", "skins");
        Directory.CreateDirectory(_cacheDir);

        var launcherDir = AppDomain.CurrentDomain.BaseDirectory;
        _localSkinsDir = Path.Combine(launcherDir, "config", "skins");
        Directory.CreateDirectory(_localSkinsDir);
    }

    public string LocalCustomSkinPath => Path.Combine(_localSkinsDir, "custom.png");

    public BitmapImage GetSteveAvatar() =>
        _steveAvatar ??= LoadAssetAvatar("steve-head.png") ?? CreateFallbackSteveAvatar();

    public BitmapImage GetAlexAvatar() => GetStandardHead("alex");

    public BitmapImage GetStandardHead(string id)
    {
        if (_headCache.TryGetValue(id, out var cached))
            return cached;

        var head = LoadAssetAvatar($"{id}-head.png") ?? GetSteveAvatar();
        _headCache[id] = head;
        return head;
    }

    public IReadOnlyList<StandardSkinInfo> GetStandardSkins()
    {
        var list = new List<StandardSkinInfo>(StandardSkinDefs.Count);
        foreach (var (id, slim) in StandardSkinDefs)
        {
            list.Add(new StandardSkinInfo
            {
                Id = id,
                DisplayName = char.ToUpperInvariant(id[0]) + id[1..],
                IsSlim = slim,
                HeadPreview = GetStandardHead(id)
            });
        }

        return list;
    }

    public static bool IsStandardSkinId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        foreach (var (known, _) in StandardSkinDefs)
        {
            if (string.Equals(known, id, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsSlimStandard(string? id)
    {
        foreach (var (known, slim) in StandardSkinDefs)
        {
            if (string.Equals(known, id, StringComparison.OrdinalIgnoreCase))
                return slim;
        }

        return false;
    }

    public BitmapImage? LoadFullSkinBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        return LoadBitmapFromFile(path);
    }

    public BitmapImage? LoadAvatarFromSkinFile(string path)
    {
        if (!File.Exists(path))
            return null;

        var full = LoadBitmapFromFile(path);
        if (full == null)
            return null;

        var w = full.PixelWidth;
        var h = full.PixelHeight;
        var isSkinSheet =
            (w == 64 && (h == 64 || h == 32)) ||
            (w == 128 && (h == 128 || h == 64));

        return isSkinSheet ? (CropSkinHead(full) ?? full) : full;
    }

    public static bool TryValidateSkinFile(string path, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = LocalizationService.T("skin.file_missing");
            return false;
        }

        try
        {
            var bitmap = LoadBitmapFromFile(path);
            if (bitmap == null)
            {
                error = LocalizationService.T("skin.file_invalid");
                return false;
            }

            var w = bitmap.PixelWidth;
            var h = bitmap.PixelHeight;
            var valid =
                (w == 64 && (h == 64 || h == 32)) ||
                (w == 128 && (h == 128 || h == 64));

            if (!valid)
            {
                error = LocalizationService.T("skin.file_size");
                return false;
            }

            return true;
        }
        catch
        {
            error = LocalizationService.T("skin.file_invalid");
            return false;
        }
    }

    public string? GetPresetSkinPath(SkinPreset preset) =>
        preset switch
        {
            SkinPreset.Steve => FindAssetPath("steve-skin.png"),
            SkinPreset.Alex => FindAssetPath("alex-skin.png"),
            SkinPreset.Ari => FindAssetPath("ari-skin.png"),
            SkinPreset.Efe => FindAssetPath("efe-skin.png"),
            SkinPreset.Kai => FindAssetPath("kai-skin.png"),
            SkinPreset.Makena => FindAssetPath("makena-skin.png"),
            SkinPreset.Noor => FindAssetPath("noor-skin.png"),
            SkinPreset.Sunny => FindAssetPath("sunny-skin.png"),
            SkinPreset.Zuri => FindAssetPath("zuri-skin.png"),
            _ => null
        };

    public string? GetStandardSkinPath(string? id)
    {
        if (!IsStandardSkinId(id))
            return null;
        return FindAssetPath($"{id!.Trim().ToLowerInvariant()}-skin.png");
    }

    public string SaveCustomSkin(string sourcePath)
    {
        File.Copy(sourcePath, LocalCustomSkinPath, overwrite: true);
        return LocalCustomSkinPath;
    }

    public void InvalidateUuidCache(string? uuid)
    {
        var names = new List<string> { "account" };
        if (!string.IsNullOrEmpty(uuid))
        {
            names.Add(uuid);
            names.Add(NormalizeUuid(uuid, withDashes: true));
            names.Add(NormalizeUuid(uuid, withDashes: false));
        }

        foreach (var name in names)
        {
            var cachePath = Path.Combine(_cacheDir, $"{name}.png");
            try
            {
                if (File.Exists(cachePath))
                    File.Delete(cachePath);
            }
            catch
            {
                // ignore cache delete failures
            }
        }
    }

    public async Task UploadSkinAsync(string accessToken, string skinFilePath, SkinModel model)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException(LocalizationService.T("skin.not_signed_in"));

        if (!TryValidateSkinFile(skinFilePath, out var error))
            throw new InvalidOperationException(error);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(model == SkinModel.Slim ? "slim" : "classic"), "variant");

        var bytes = await File.ReadAllBytesAsync(skinFilePath);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", Path.GetFileName(skinFilePath));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.minecraftservices.com/minecraft/profile/skins");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = content;

        using var response = await AppHttp.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(LocalizationService.F("skin.upload_failed", body));
    }

    public async Task ActivateSkinByUrlAsync(string accessToken, string skinUrl, SkinModel model)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException(LocalizationService.T("skin.not_signed_in"));
        if (string.IsNullOrWhiteSpace(skinUrl))
            throw new InvalidOperationException(LocalizationService.T("skin.file_missing"));

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            variant = model == SkinModel.Slim ? "slim" : "classic",
            url = skinUrl
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.minecraftservices.com/minecraft/profile/skins")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await AppHttp.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(LocalizationService.F("skin.upload_failed", body));
    }

    public async Task<IReadOnlyList<AccountSkinInfo>> GetAccountSkinsAsync(string accessToken)
    {
        var list = new List<AccountSkinInfo>();
        if (string.IsNullOrWhiteSpace(accessToken))
            return list;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await AppHttp.Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return list;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("skins", out var skins) ||
                skins.ValueKind != System.Text.Json.JsonValueKind.Array)
                return list;

            foreach (var skin in skins.EnumerateArray())
            {
                var info = new AccountSkinInfo
                {
                    Id = skin.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Url = skin.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
                    Variant = skin.TryGetProperty("variant", out var variant) ? variant.GetString() ?? "CLASSIC" : "CLASSIC",
                    State = skin.TryGetProperty("state", out var state) ? state.GetString() ?? "INACTIVE" : "INACTIVE",
                    Alias = skin.TryGetProperty("alias", out var alias) ? alias.GetString() : null
                };

                if (string.IsNullOrWhiteSpace(info.Url))
                    continue;

                info.HeadPreview = await DownloadHeadFromSkinUrlAsync(info.Url);
                list.Add(info);
            }
        }
        catch
        {
            // ignore profile fetch errors
        }

        return list;
    }

    public async Task<BitmapImage?> LoadAccountAvatarAsync(string? accessToken, string? uuid)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://api.minecraftservices.com/minecraft/profile");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await AppHttp.Client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var skinUrl = FindActiveSkinUrl(doc.RootElement);
                    if (!string.IsNullOrWhiteSpace(skinUrl))
                    {
                        var compact = string.IsNullOrWhiteSpace(uuid)
                            ? "account"
                            : NormalizeUuid(uuid, withDashes: false);
                        var cachePath = Path.Combine(_cacheDir, $"{compact}.png");
                        var head = await DownloadHeadFromSkinUrlAsync(skinUrl);
                        if (head != null)
                        {
                            await SaveBitmapAsPngAsync(head, cachePath);
                            return head;
                        }
                    }
                }
            }
            catch
            {
                // fall through to public providers
            }
        }

        return await LoadSkinAsync(uuid);
    }

    public async Task<(BitmapImage Skin, bool IsSlim)?> TryLoadActiveAccountFullSkinAsync(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await AppHttp.Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!TryFindActiveSkin(doc.RootElement, out var skinUrl, out var isSlim) ||
                string.IsNullOrWhiteSpace(skinUrl))
                return null;

            var full = await DownloadFullSkinFromUrlAsync(skinUrl);
            if (full == null)
                return null;

            return (full, isSlim);
        }
        catch
        {
            return null;
        }
    }

    public async Task<BitmapImage?> DownloadFullSkinFromUrlAsync(string skinUrl)
    {
        try
        {
            if (skinUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                skinUrl = "https://" + skinUrl[7..];

            using var response = await AppHttp.Client.GetAsync(skinUrl);
            if (!response.IsSuccessStatusCode)
                return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return CreateBitmapFromBytes(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindActiveSkinUrl(System.Text.Json.JsonElement root) =>
        TryFindActiveSkin(root, out var url, out _) ? url : null;

    private static bool TryFindActiveSkin(System.Text.Json.JsonElement root, out string? url, out bool isSlim)
    {
        url = null;
        isSlim = false;
        if (!root.TryGetProperty("skins", out var skins) ||
            skins.ValueKind != System.Text.Json.JsonValueKind.Array)
            return false;

        string? fallbackUrl = null;
        var fallbackSlim = false;
        foreach (var skin in skins.EnumerateArray())
        {
            if (!skin.TryGetProperty("url", out var urlEl))
                continue;
            var candidate = urlEl.GetString();
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var slim = skin.TryGetProperty("variant", out var variant) &&
                       string.Equals(variant.GetString(), "SLIM", StringComparison.OrdinalIgnoreCase);

            fallbackUrl ??= candidate;
            if (fallbackUrl == candidate)
                fallbackSlim = slim;

            if (skin.TryGetProperty("state", out var state) &&
                string.Equals(state.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                url = candidate;
                isSlim = slim;
                return true;
            }
        }

        if (fallbackUrl == null)
            return false;

        url = fallbackUrl;
        isSlim = fallbackSlim;
        return true;
    }

    private async Task<BitmapImage?> DownloadHeadFromSkinUrlAsync(string skinUrl)
    {
        try
        {
            if (skinUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                skinUrl = "https://" + skinUrl[7..];

            using var response = await AppHttp.Client.GetAsync(skinUrl);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var full = CreateBitmapFromBytes(bytes);
            if (full == null)
                return null;

            return IsFullSkinSheet(full) ? (CropSkinHead(full) ?? full) : full;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsFullSkinSheet(BitmapSource image)
    {
        var w = image.PixelWidth;
        var h = image.PixelHeight;
        return (w == 64 && (h == 64 || h == 32)) ||
               (w == 128 && h == 64); // HD skin sheet (not square avatar renders)
    }

    public BitmapImage ResolveLocalAvatar(string? preset, string? customPath)
    {
        var id = preset?.Trim().ToLowerInvariant();
        if (id == "custom")
            return LoadAvatarFromSkinFile(customPath ?? LocalCustomSkinPath) ?? GetSteveAvatar();
        if (IsStandardSkinId(id))
            return GetStandardHead(id!);
        return GetSteveAvatar();
    }

    public BitmapImage ResolveLocalAvatar(SkinPreset preset, string? customPath) =>
        ResolveLocalAvatar(preset switch
        {
            SkinPreset.Alex => "alex",
            SkinPreset.Ari => "ari",
            SkinPreset.Efe => "efe",
            SkinPreset.Kai => "kai",
            SkinPreset.Makena => "makena",
            SkinPreset.Noor => "noor",
            SkinPreset.Sunny => "sunny",
            SkinPreset.Zuri => "zuri",
            SkinPreset.Custom => "custom",
            SkinPreset.Steve => "steve",
            _ => "steve"
        }, customPath);

    public async Task<BitmapImage?> LoadSkinAsync(string? uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        var dashed = NormalizeUuid(uuid, withDashes: true);
        var compact = NormalizeUuid(uuid, withDashes: false);
        var cachePath = Path.Combine(_cacheDir, $"{compact}.png");

        if (File.Exists(cachePath))
        {
            // Avatar cache stores ready-to-show heads — do not re-crop.
            var cached = LoadBitmapFromFile(cachePath);
            if (cached != null)
            {
                if (IsFullSkinSheet(cached))
                    return CropSkinHead(cached) ?? cached;
                return cached;
            }
        }

        var services = new Func<Task<BitmapImage?>>[]
        {
            () => LoadSkinFromMcHeads(compact, cachePath),
            () => LoadSkinFromMinotar(compact, cachePath),
            () => LoadSkinFromCrafatar(dashed, cachePath),
            () => LoadSkinFromMinecraftApi(compact, cachePath),
            () => LoadSkinFromStala(compact, cachePath)
        };

        foreach (var service in services)
        {
            var image = await service();
            if (image != null)
                return image;
        }

        return null;
    }

    private static string NormalizeUuid(string uuid, bool withDashes)
    {
        var hex = uuid.Replace("-", "").Trim();
        if (hex.Length != 32)
            return withDashes ? uuid.Trim() : hex;

        if (!withDashes)
            return hex.ToLowerInvariant();

        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}".ToLowerInvariant();
    }

    private static BitmapImage? LoadAssetAvatar(string fileName)
    {
        var path = FindAssetPath(fileName);
        return path == null ? null : LoadBitmapFromFile(path);
    }

    private static string? FindAssetPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName),
            Path.Combine(AppContext.BaseDirectory, "Assets", fileName)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static BitmapImage? LoadBitmapFromFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Path.GetFullPath(path));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> LoadSkinFromMcHeads(string uuid, string cachePath)
    {
        var skinUrl = $"https://mc-heads.net/avatar/{uuid}/128";
        return await DownloadSkinImage(skinUrl, cachePath);
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

            await SaveBitmapAsPngAsync(cropped, cachePath);
            return cropped;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveBitmapAsPngAsync(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static BitmapImage CreateFallbackSteveAvatar()
    {
        var pixels = new uint[]
        {
            0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F,
            0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F, 0xFF2F2F2F,
            0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A,
            0xFFB9825A, 0xFFFFFFFF, 0xFF3C3C8C, 0xFFB9825A, 0xFFB9825A, 0xFF3C3C8C, 0xFFFFFFFF, 0xFFB9825A,
            0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFA06B42, 0xFFA06B42, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A,
            0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A, 0xFFB9825A,
            0xFFB9825A, 0xFF6B442A, 0xFF6B442A, 0xFF6B442A, 0xFF6B442A, 0xFF6B442A, 0xFF6B442A, 0xFFB9825A,
            0xFFB9825A, 0xFF6B442A, 0xFF6B442A, 0xFF6B442A, 0xFF6B442A, 0xFF6B442A, 0xFF6B442A, 0xFFB9825A,
        };

        var bitmap = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 8, 8), pixels, 8 * 4, 0);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = ms;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
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
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

            g.DrawImage(bitmap,
                new System.Drawing.Rectangle(0, 0, headSize * 2, headSize * 2),
                new System.Drawing.Rectangle(headSize, headSize, headSize, headSize),
                System.Drawing.GraphicsUnit.Pixel);

            // Hat overlay layer at (40, 8) on classic skins
            if (bitmap.Width >= headSize * 8 && bitmap.Height >= headSize * 2)
            {
                g.DrawImage(bitmap,
                    new System.Drawing.Rectangle(0, 0, headSize * 2, headSize * 2),
                    new System.Drawing.Rectangle(headSize * 5, headSize, headSize, headSize),
                    System.Drawing.GraphicsUnit.Pixel);
            }

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
