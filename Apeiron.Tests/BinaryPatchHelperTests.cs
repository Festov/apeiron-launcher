using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class BinaryPatchHelperTests
{
    [Fact]
    public void Create_and_Apply_roundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var oldPath = Path.Combine(root, "old.bin");
            var newPath = Path.Combine(root, "new.bin");
            var patchPath = Path.Combine(root, "delta.patch");
            var outPath = Path.Combine(root, "out.bin");

            var oldBytes = Enumerable.Range(0, 20_000).Select(i => (byte)(i % 251)).ToArray();
            var newBytes = (byte[])oldBytes.Clone();
            for (var i = 100; i < 500; i++)
                newBytes[i] = (byte)(255 - newBytes[i]);
            newBytes = newBytes.Concat(new byte[] { 1, 2, 3, 4, 5 }).ToArray();

            File.WriteAllBytes(oldPath, oldBytes);
            File.WriteAllBytes(newPath, newBytes);

            BinaryPatchHelper.CreatePatch(oldPath, newPath, patchPath);
            Assert.True(new FileInfo(patchPath).Length > 0);
            Assert.True(new FileInfo(patchPath).Length < new FileInfo(newPath).Length);

            BinaryPatchHelper.ApplyPatch(oldPath, patchPath, outPath);
            Assert.Equal(newBytes, File.ReadAllBytes(outPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
