using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Apeiron.Services;

public static class SecureStorage
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Apeiron.Launcher.v1");

    public static void WriteText(string path, string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        AtomicFile.WriteAllBytes(path, encrypted);
    }

    public static string? ReadText(string path)
    {
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
            return null;

        if (bytes[0] == (byte)'{')
            return Encoding.UTF8.GetString(bytes);

        try
        {
            var decrypted = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsPlainTextFile(string path)
    {
        if (!File.Exists(path))
            return false;

        var bytes = File.ReadAllBytes(path);
        return bytes.Length > 0 && bytes[0] == (byte)'{';
    }
}
