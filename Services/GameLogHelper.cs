using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Apeiron.Services;

public static class GameLogHelper
{
    public static string GetLatestLogPath(BuildInfo build) =>
        Path.Combine(build.GetGameDir(), "logs", "latest.log");

    public static string GetCrashReportsDir(BuildInfo build) =>
        Path.Combine(build.GetGameDir(), "crash-reports");

    public static string? FindNewestCrashReport(BuildInfo build)
    {
        var dir = GetCrashReportsDir(build);
        if (!Directory.Exists(dir))
            return null;

        return Directory.EnumerateFiles(dir, "*.txt")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static string ReadTail(string path, int maxBytes = 64 * 1024)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "";

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length <= maxBytes)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        stream.Seek(-maxBytes, SeekOrigin.End);
        using var tailReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = tailReader.ReadToEnd();
        var firstNl = text.IndexOf('\n');
        if (firstNl >= 0 && firstNl + 1 < text.Length)
            text = text[(firstNl + 1)..];
        return "…\n" + text;
    }

    public static void OpenLatestLog(BuildInfo build)
    {
        var path = GetLatestLogPath(build);
        if (!File.Exists(path))
            throw new FileNotFoundException(LocalizationService.T("edit_build.game_log_missing"), path);
        LogService.OpenLogFile(path);
    }

    public static void OpenCrashReportsFolder(BuildInfo build)
    {
        var dir = GetCrashReportsDir(build);
        Directory.CreateDirectory(dir);
        LogService.OpenLogsFolder(dir);
    }
}
