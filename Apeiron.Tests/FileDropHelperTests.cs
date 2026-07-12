using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class FileDropHelperTests
{
    [Fact]
    public void GetZipFiles_filters_existing_zip_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var zipPath = Path.Combine(root, "modpack.zip");
        var txtPath = Path.Combine(root, "readme.txt");
        File.WriteAllText(zipPath, "zip");
        File.WriteAllText(txtPath, "txt");

        try
        {
            var result = FileDropHelper.GetZipFiles(new[] { zipPath, txtPath, Path.Combine(root, "missing.zip") });
            Assert.Single(result);
            Assert.Equal(zipPath, result[0]);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
