using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LoaderServiceTests
{
    [Fact]
    public void GetForgeVersionId_formats_profile_id() =>
        Assert.Equal("1.20.1-forge-47.3.0", LoaderService.GetForgeVersionId("1.20.1", "47.3.0"));

    [Fact]
    public void GetNeoForgeVersionId_formats_profile_id() =>
        Assert.Equal("neoforge-21.1.0", LoaderService.GetNeoForgeVersionId("21.1.0"));

    [Fact]
    public async Task InstallLoader_skips_vanilla_build()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-loader-" + Guid.NewGuid().ToString("N"));
        try
        {
            var loader = new LoaderService(root);
            var build = new BuildInfo { IsModded = false, MinecraftVersion = "1.20.1" };

            Assert.True(await loader.InstallLoader(build));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task InstallLoader_fails_when_loader_version_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-loader-" + Guid.NewGuid().ToString("N"));
        try
        {
            var loader = new LoaderService(root);
            var build = new BuildInfo
            {
                IsModded = true,
                Loader = "fabric",
                MinecraftVersion = "1.20.1",
                LoaderVersion = ""
            };

            Assert.False(await loader.InstallLoader(build));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task InstallLoader_rejects_unsupported_loader()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-loader-" + Guid.NewGuid().ToString("N"));
        try
        {
            var loader = new LoaderService(root);
            var build = new BuildInfo
            {
                IsModded = true,
                Loader = "optifine",
                LoaderVersion = "1.0",
                MinecraftVersion = "1.20.1"
            };

            Assert.False(await loader.InstallLoader(build));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
