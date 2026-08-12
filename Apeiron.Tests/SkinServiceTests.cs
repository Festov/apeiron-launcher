using Apeiron.Services;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Media.Imaging;
using Xunit;

namespace Apeiron.Tests;

public class SkinServiceTests
{
    [Fact]
    public void GetStandardHead_loads_distinct_embedded_heads()
    {
        BitmapImage? steve = null;
        BitmapImage? alex = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var root = Path.Combine(Path.GetTempPath(), "apeiron-skin-heads-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    var skins = new SkinService(root);
                    steve = skins.GetStandardHead("steve");
                    alex = skins.GetStandardHead("alex");
                }
                finally
                {
                    try { Directory.Delete(root, recursive: true); } catch { }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.NotNull(steve);
        Assert.NotNull(alex);
        Assert.Equal(64, steve!.PixelWidth);
        Assert.Equal(64, alex!.PixelWidth);
        // Fallback Steve avatar is 8x8 — real embedded heads must not collapse to that.
        Assert.NotEqual(8, steve.PixelWidth);
    }

    [Fact]
    public void Embedded_standard_skin_assets_are_present()
    {
        var asm = typeof(SkinService).Assembly;
        foreach (var (id, _) in SkinService.StandardSkinDefs)
        {
            Assert.NotNull(asm.GetManifestResourceStream($"Apeiron.Assets.{id}-skin.png"));
            Assert.NotNull(asm.GetManifestResourceStream($"Apeiron.Assets.{id}-head.png"));
        }
    }

    [Fact]
    public void GetStandardSkinPath_materializes_embedded_skin_to_cache()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-skin-embed-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var skins = new SkinService(root);
            var path = skins.GetStandardSkinPath("steve");
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.Contains(Path.Combine("cache", "skins", "presets"), path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TryValidateSkinFile_accepts_64x64_png()
    {
        var path = Path.Combine(Path.GetTempPath(), "apeiron-skin-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bmp = new Bitmap(64, 64))
                bmp.Save(path, ImageFormat.Png);

            Assert.True(SkinService.TryValidateSkinFile(path, out var error));
            Assert.Equal("", error);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void TryValidateSkinFile_rejects_wrong_size()
    {
        var path = Path.Combine(Path.GetTempPath(), "apeiron-skin-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bmp = new Bitmap(40, 40))
                bmp.Save(path, ImageFormat.Png);

            Assert.False(SkinService.TryValidateSkinFile(path, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
