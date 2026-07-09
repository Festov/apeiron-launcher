using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Apeiron.Services;

public class LogService
{
    private readonly string _logsDir;
    private readonly object _lock = new();
    private string? _sessionFile;

    public LogService(string? logsDirectory = null)
    {
        _logsDir = string.IsNullOrWhiteSpace(logsDirectory)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
            : logsDirectory.Trim();
        Directory.CreateDirectory(_logsDir);
        StartSession();
    }

    public void StartSession()
    {
        _sessionFile = Path.Combine(_logsDir, $"launcher-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        WriteLine(LocalizationService.T("log.session_start"));
    }

    public void WriteLine(string message)
    {
        if (string.IsNullOrEmpty(_sessionFile)) return;

        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
            lock (_lock)
            {
                File.AppendAllText(_sessionFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
    }

    public string SaveGameLog(string buildName, IEnumerable<string> lines)
    {
        return SaveNamedLog("game", buildName, lines, "log.game_saved");
    }

    public string SaveInstallLog(string buildName, IEnumerable<string> lines)
    {
        return SaveNamedLog("install", buildName, lines, "log.install_saved");
    }

    public static void OpenLogFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public static void OpenLogsFolder(string logsDir)
    {
        Directory.CreateDirectory(logsDir);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = logsDir,
            UseShellExecute = true
        });
    }

    private string SaveNamedLog(string prefix, string buildName, IEnumerable<string> lines, string logKey)
    {
        var safeName = SanitizeLogFileName(buildName);
        var path = Path.Combine(_logsDir, $"{prefix}-{safeName}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

        try
        {
            var header = $"{LocalizationService.T("log.session_start")} | {buildName}{Environment.NewLine}";
            var content = header + string.Join(Environment.NewLine, lines);
            File.WriteAllText(path, content, Encoding.UTF8);
            WriteLine(LocalizationService.F(logKey, Path.GetFileName(path)));
            return path;
        }
        catch
        {
            return "";
        }
    }

    private static string SanitizeLogFileName(string buildName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = buildName
            .Trim()
            .Select(ch => invalid.Contains(ch) || ch == ' ' ? '-' : ch)
            .ToArray();
        var safeName = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(safeName) ? "build" : safeName;
    }

    public string? CurrentSessionFile => _sessionFile;

    public string LogsDirectory => _logsDir;
}
