using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apeiron.Services;

public class ManifestCache
{
    public const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    private readonly string _cachePath;
    private readonly TimeSpan _ttl;

    public ManifestCache(string minecraftDir, TimeSpan? ttl = null)
    {
        _cachePath = Path.Combine(minecraftDir, "cache", "version_manifest.json");
        _ttl = ttl ?? TimeSpan.FromHours(1);
    }

    public bool TryRead(out string? json)
    {
        json = null;
        if (!File.Exists(_cachePath))
            return false;

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(_cachePath);
        if (age > _ttl)
            return false;

        json = File.ReadAllText(_cachePath);
        return true;
    }

    public void Write(string json)
    {
        var dir = Path.GetDirectoryName(_cachePath)!;
        Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(_cachePath, json);
    }

    public async Task<string> GetOrFetchAsync(CancellationToken cancellationToken = default)
    {
        if (TryRead(out var cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var json = await HttpRetryHelper.GetStringAsync(AppHttp.Client, ManifestUrl, cancellationToken);
        Write(json);
        return json;
    }
}
