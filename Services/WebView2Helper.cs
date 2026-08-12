using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Apeiron.Services;

public static class WebView2Helper
{
    public const string EvergreenRuntimeDownloadUrl =
        "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static string GetUserDataFolder(string? minecraftDir = null)
    {
        var mc = string.IsNullOrWhiteSpace(minecraftDir)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft")
            : minecraftDir.Trim();
        return Path.Combine(mc, "cache", "webview2");
    }

    public static bool IsRuntimeAvailable()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<CoreWebView2Environment> CreateEnvironmentAsync(string? minecraftDir = null)
    {
        var userData = GetUserDataFolder(minecraftDir);
        Directory.CreateDirectory(userData);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
    }

    /// <summary>Warm the WebView2 user-data folder so the first Skin/Auth window opens faster.</summary>
    public static async Task WarmupAsync(string? minecraftDir = null)
    {
        if (!IsRuntimeAvailable())
            return;

        try
        {
            _ = await CreateEnvironmentAsync(minecraftDir);
        }
        catch
        {
            // Warmup is best-effort.
        }
    }
}
