using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class ManifestCacheTests
{
    [Fact]
    public void TryRead_returns_false_when_cache_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var cache = new ManifestCache(root);
            Assert.False(cache.TryRead(out var json));
            Assert.Null(json);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Write_and_TryRead_roundtrip_within_ttl()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var cache = new ManifestCache(root, TimeSpan.FromHours(1));
            const string manifest = """{"versions":[]}""";
            cache.Write(manifest);

            Assert.True(cache.TryRead(out var json));
            Assert.Equal(manifest, json);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void TryRead_returns_false_when_cache_expired()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var cache = new ManifestCache(root, TimeSpan.FromMilliseconds(1));
            cache.Write("""{"versions":[]}""");

            Thread.Sleep(5);

            Assert.False(cache.TryRead(out _));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
