using System;
using System.IO;
using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LauncherUpdateServiceTests
{
    [Theory]
    [InlineData("1.4.0", "1.3.0", true)]
    [InlineData("1.3.0", "1.3.0", false)]
    [InlineData("1.3.1", "1.3.0", true)]
    [InlineData("1.2.9", "1.3.0", false)]
    public void IsNewerVersion_compares_semver_parts(string latest, string current, bool expected)
    {
        var result = LauncherUpdateService.IsNewerVersion(
            Version.Parse(latest),
            Version.Parse(current));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void VerifySha256_accepts_matching_hash()
    {
        var path = Path.Combine(Path.GetTempPath(), "apeiron-sha-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

            LauncherUpdateService.VerifySha256(path, expected);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void VerifySha256_rejects_mismatched_hash()
    {
        var path = Path.Combine(Path.GetTempPath(), "apeiron-sha-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

            Assert.Throws<InvalidDataException>(() =>
                LauncherUpdateService.VerifySha256(path, new string('A', 64)));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
