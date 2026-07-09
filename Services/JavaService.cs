using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Apeiron.Services;

public class JavaService
{
    public event Action<string>? Log;
    public event Action<int, string>? ProgressChanged;

    public bool IsJavaInstalled(string mcVersion = "1.21") =>
        !string.IsNullOrEmpty(ResolveJavaPath(mcVersion));

    public string? ResolveJavaPath(string mcVersion)
    {
        var required = JavaVersionHelper.GetRequiredJavaMajor(mcVersion);
        var maximum = JavaVersionHelper.GetMaxJavaMajor(mcVersion);
        var candidates = new List<(string Path, int Major, int Score)>();

        foreach (var path in GetCandidateJavaPaths())
            AddCandidate(candidates, path);

        AddCandidate(candidates, "java");

        return candidates
            .Where(c => c.Major >= required && c.Major <= maximum)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Major)
            .Select(c => c.Path)
            .FirstOrDefault();
    }

    private static void AddCandidate(List<(string Path, int Major, int Score)> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (path != "java" && !File.Exists(path))
            return;

        var major = DetectJavaMajor(path);
        if (major <= 0)
            return;

        if (list.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        list.Add((path, major, GetJavaVendorScore(path, major)));
    }

    public static int GetJavaVendorScore(string javaPath, int major)
    {
        var lower = javaPath.ToLowerInvariant();

        if (IsOracleJavaPath(lower))
            return 1000;

        if (lower.Contains("temurin") || lower.Contains("adoptium"))
            return major == 8 ? 50 : 200;

        if (lower.Contains("microsoft"))
            return 150;

        if (lower.Contains(@"\program files\java\"))
            return 500;

        return 300;
    }

    private static bool IsOracleJavaPath(string lowerPath) =>
        lowerPath.Contains(@"\program files\java\jdk")
        && !lowerPath.Contains("adoptium")
        && !lowerPath.Contains("microsoft")
        && !lowerPath.Contains("temurin");

    public static int DetectJavaMajor(string javaPath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            var output = process.StandardError.ReadToEnd();
            process.WaitForExit(3000);

            var match = Regex.Match(output, @"version ""(\d+)(?:\.(\d+))?");
            if (!match.Success) return 0;

            var first = int.Parse(match.Groups[1].Value);
            if (first == 1 && match.Groups[2].Success)
                return int.Parse(match.Groups[2].Value);

            return first;
        }
        catch
        {
            return 0;
        }
    }

    private static string[] GetCandidateJavaPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new List<string>();

        foreach (var root in new[]
        {
            Path.Combine(programFiles, "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java")
        })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var java = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(java))
                    candidates.Add(java);
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<bool> InstallJava(string mcVersion = "1.21")
    {
        try
        {
            var javaMajor = JavaVersionHelper.GetPreferredJavaMajor(mcVersion);
            Log?.Invoke(LocalizationService.F("log.java.downloading", javaMajor));

            var downloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
            Directory.CreateDirectory(downloadDir);

            var installerPath = Path.Combine(downloadDir, $"oracle-jdk-{javaMajor}.download");
            var downloaded = await JavaDownloadHelper.DownloadJdkInstallerAsync(
                javaMajor,
                installerPath,
                (progress, text) => ProgressChanged?.Invoke(progress, text),
                msg => Log?.Invoke(msg));

            if (!downloaded)
            {
                Log?.Invoke(LocalizationService.F("log.java.download_failed", javaMajor));
                return false;
            }

            Log?.Invoke(LocalizationService.F("log.java.installing", javaMajor));
            var installed = JavaInstallHelper.IsValidOracleExe(installerPath)
                ? await JavaInstallHelper.InstallOracleExeAsync(installerPath, msg => Log?.Invoke(msg))
                : await JavaInstallHelper.InstallOracleZipAsync(installerPath, javaMajor, msg => Log?.Invoke(msg));

            if (!installed)
                return false;

            if (!IsJavaInstalled(mcVersion))
            {
                Log?.Invoke(LocalizationService.F("log.java.installed_restart_hint", javaMajor));
                return false;
            }

            Log?.Invoke(LocalizationService.F("log.java.installed_system", javaMajor));
            return true;
        }
        catch (Exception ex)
        {
            var details = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            Log?.Invoke(LocalizationService.F("main.error_with_message", details));
            return false;
        }
    }
}
