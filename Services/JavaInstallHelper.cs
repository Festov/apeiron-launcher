using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Apeiron.Services;

public static class JavaInstallHelper
{
    public static bool IsValidOracleExe(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 1024 * 1024)
            return false;

        Span<byte> header = stackalloc byte[2];
        using var stream = File.OpenRead(path);
        return stream.Read(header) == 2 && header[0] == 0x4D && header[1] == 0x5A;
    }

    public static bool IsValidOracleZip(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 1024 * 1024)
            return false;

        Span<byte> header = stackalloc byte[2];
        using var stream = File.OpenRead(path);
        return stream.Read(header) == 2 && header[0] == 0x50 && header[1] == 0x4B;
    }

    public static async Task<bool> InstallOracleExeAsync(string exePath, Action<string>? log = null)
    {
        if (!IsValidOracleExe(exePath))
            return false;

        var installExe = Path.Combine(Path.GetTempPath(), $"apeiron-oracle-jdk-{Guid.NewGuid():N}.exe");
        File.Copy(exePath, installExe, overwrite: true);

        var variants = new[]
        {
            "/s INSTALL_SILENT=DisableAutoUpdate",
            "/s"
        };

        foreach (var variant in variants)
        {
            var exitCode = await RunElevatedAsync(installExe, variant);
            if (exitCode == 1223)
            {
                log?.Invoke(LocalizationService.T("log.java.install_cancelled"));
                return false;
            }

            if (exitCode == 0 || exitCode == 3010)
                return true;

            if (exitCode > 0)
                log?.Invoke(LocalizationService.F("log.java.install_exit_code", exitCode));
        }

        return false;
    }

    public static async Task<bool> InstallOracleZipAsync(string zipPath, int javaMajor, Action<string>? log = null)
    {
        if (!IsValidOracleZip(zipPath))
            return false;

        var extractDir = Path.Combine(Path.GetTempPath(), $"apeiron-oracle-jdk-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            var jdkRoot = JavaDownloadHelper.FindJdkRoot(extractDir);
            if (jdkRoot == null)
            {
                log?.Invoke(LocalizationService.T("log.java.extract_failed"));
                return false;
            }

            var folderName = Path.GetFileName(jdkRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java", folderName);

            log?.Invoke(LocalizationService.F("log.java.installing_zip", javaMajor, targetDir));

            var exitCode = await RunElevatedAsync(
                "robocopy",
                $"\"{jdkRoot}\" \"{targetDir}\" /E /NFL /NDL /NJH /NJS /nc /ns /np");

            if (exitCode == 1223)
            {
                log?.Invoke(LocalizationService.T("log.java.install_cancelled"));
                return false;
            }

            return exitCode is >= 0 and < 8;
        }
        finally
        {
            try { Directory.Delete(extractDir, true); } catch { }
        }
    }

    private static async Task<int> RunElevatedAsync(string fileName, string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            }
        };

        try
        {
            if (!process.Start())
                return -1;

            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return 1223;
        }
    }
}
