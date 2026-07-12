using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class SecureStorageTests
{
    [Fact]
    public void WriteText_and_ReadText_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "apeiron-secure-" + Guid.NewGuid().ToString("N") + ".dat");

        try
        {
            const string plaintext = """{"access_token":"test","username":"Player"}""";
            SecureStorage.WriteText(path, plaintext);

            Assert.False(SecureStorage.IsPlainTextFile(path));
            Assert.Equal(plaintext, SecureStorage.ReadText(path));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void IsPlainTextFile_detects_unencrypted_json()
    {
        var path = Path.Combine(Path.GetTempPath(), "apeiron-plain-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            File.WriteAllText(path, """{"access_token":"legacy"}""");
            Assert.True(SecureStorage.IsPlainTextFile(path));
            Assert.Equal("""{"access_token":"legacy"}""", SecureStorage.ReadText(path));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void ReadText_returns_null_for_missing_file() =>
        Assert.Null(SecureStorage.ReadText(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".missing")));
}
