using System.IO.Compression;
using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class ZipExtractHelperTests
{
    [Fact]
    public void ExtractZipFile_rejects_zip_slip_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-zipslip-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(root, "evil.zip");
        var extractDir = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(root);

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open());
                writer.Write("evil");
            }

            Assert.Throws<InvalidDataException>(() => ZipExtractHelper.ExtractZipFile(zipPath, extractDir));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ExtractZipFile_extracts_safe_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-zipsafe-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(root, "safe.zip");
        var extractDir = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(root);

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("folder/file.txt").Open());
                writer.Write("ok");
            }

            ZipExtractHelper.ExtractZipFile(zipPath, extractDir);

            Assert.True(File.Exists(Path.Combine(extractDir, "folder", "file.txt")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
