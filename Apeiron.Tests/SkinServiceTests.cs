using Apeiron.Services;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace Apeiron.Tests;

public class SkinServiceTests
{
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
