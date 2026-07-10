using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public class LoaderService
{
    private readonly string _minecraftDir;
    private readonly string _versionsDir;

    private const string FabricInstallerVersion = "1.0.1";

    public event Action<string>? Log;
    public event Action<int, string>? ProgressChanged;

    public LoaderService(string minecraftDir)
    {
        _minecraftDir = minecraftDir;
        _versionsDir = Path.Combine(_minecraftDir, "versions");
    }

    public async Task<bool> InstallLoader(BuildInfo build, CancellationToken cancellationToken = default)
    {
        if (!build.IsModded || string.IsNullOrEmpty(build.Loader))
            return true;

        if (string.IsNullOrEmpty(build.LoaderVersion))
        {
            Log?.Invoke(LocalizationService.T("log.loader.version_not_specified"));
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return build.Loader.ToLowerInvariant() switch
        {
            "fabric" => await InstallFabric(build.MinecraftVersion, build.LoaderVersion, cancellationToken),
            "quilt" => await InstallQuilt(build.MinecraftVersion, build.LoaderVersion, cancellationToken),
            "forge" => await InstallForge(build.MinecraftVersion, build.LoaderVersion, cancellationToken),
            "neoforge" => await InstallNeoForge(build.MinecraftVersion, build.LoaderVersion, cancellationToken),
            _ => await InstallUnsupported(build.Loader)
        };
    }

    public static string GetForgeVersionId(string mcVersion, string forgeVersion) =>
        $"{mcVersion}-forge-{forgeVersion}";

    public static string GetNeoForgeVersionId(string neoVersion) =>
        $"neoforge-{neoVersion}";

    private Task<bool> InstallUnsupported(string loader)
    {
        Log?.Invoke(LocalizationService.F("log.loader.unsupported", loader));
        return Task.FromResult(false);
    }

    public async Task<bool> InstallForge(string mcVersion, string forgeVersion, CancellationToken cancellationToken = default)
    {
        var versionId = GetForgeVersionId(mcVersion, forgeVersion);
        var profileJson = Path.Combine(_versionsDir, versionId, $"{versionId}.json");

        if (File.Exists(profileJson))
        {
            Log?.Invoke(LocalizationService.F("log.loader.already_installed", "Forge", forgeVersion, mcVersion));
            return true;
        }

        var vanillaJson = Path.Combine(_versionsDir, mcVersion, $"{mcVersion}.json");
        if (!File.Exists(vanillaJson))
        {
            Log?.Invoke(LocalizationService.F("log.loader.minecraft_first", mcVersion));
            return false;
        }

        Log?.Invoke(LocalizationService.F("log.loader.installing", "Forge", forgeVersion, mcVersion));

        try
        {
            EnsureLauncherProfiles();

            var installerUrl =
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";
            var installerPath = Path.Combine(_minecraftDir, $"forge-installer-{mcVersion}-{forgeVersion}.jar");

            Log?.Invoke(LocalizationService.F("log.loader.downloading_installer", "Forge"));
            await DownloadFile(installerUrl, installerPath, cancellationToken);

            var args = $"-jar \"{installerPath}\" --installClient \"{_minecraftDir}\"";
            var process = StartJavaProcess(args, mcVersion);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                Log?.Invoke(LocalizationService.F("log.loader.installer_exit_code", "Forge", process.ExitCode));

            if (File.Exists(profileJson))
            {
                Log?.Invoke(LocalizationService.F("log.loader.installed", "Forge", forgeVersion));
                return true;
            }

            var resolved = FindForgeVersionId(mcVersion, forgeVersion);
            if (resolved != null)
            {
                Log?.Invoke(LocalizationService.F("log.loader.installed_resolved", "Forge", forgeVersion, resolved));
                return true;
            }

            Log?.Invoke(LocalizationService.F("log.loader.installer_no_profile", "Forge"));
            return false;
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.loader.install_error", "Forge", ex.Message));
            return false;
        }
    }

    public async Task<bool> InstallNeoForge(string mcVersion, string neoVersion, CancellationToken cancellationToken = default)
    {
        var versionId = GetNeoForgeVersionId(neoVersion);
        var profileJson = Path.Combine(_versionsDir, versionId, $"{versionId}.json");

        if (File.Exists(profileJson))
        {
            Log?.Invoke(LocalizationService.F("log.loader.already_installed", "NeoForge", neoVersion, mcVersion));
            return true;
        }

        var vanillaJson = Path.Combine(_versionsDir, mcVersion, $"{mcVersion}.json");
        if (!File.Exists(vanillaJson))
        {
            Log?.Invoke(LocalizationService.F("log.loader.minecraft_first", mcVersion));
            return false;
        }

        Log?.Invoke(LocalizationService.F("log.loader.installing", "NeoForge", neoVersion, mcVersion));

        try
        {
            EnsureLauncherProfiles();

            var installerUrl =
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{neoVersion}/neoforge-{neoVersion}-installer.jar";
            var installerPath = Path.Combine(_minecraftDir, $"neoforge-installer-{neoVersion}.jar");

            Log?.Invoke(LocalizationService.F("log.loader.downloading_installer", "NeoForge"));
            await DownloadFile(installerUrl, installerPath, cancellationToken);

            var args = $"-jar \"{installerPath}\" --installClient \"{_minecraftDir}\"";
            var process = StartJavaProcess(args, mcVersion);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                Log?.Invoke(LocalizationService.F("log.loader.installer_exit_code", "NeoForge", process.ExitCode));

            if (File.Exists(profileJson))
            {
                Log?.Invoke(LocalizationService.F("log.loader.installed", "NeoForge", neoVersion));
                return true;
            }

            var resolved = FindNeoForgeVersionId(neoVersion);
            if (resolved != null)
            {
                Log?.Invoke(LocalizationService.F("log.loader.installed_resolved", "NeoForge", neoVersion, resolved));
                return true;
            }

            Log?.Invoke(LocalizationService.F("log.loader.installer_no_profile", "NeoForge"));
            return false;
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.loader.install_error", "NeoForge", ex.Message));
            return false;
        }
    }

    private string? FindForgeVersionId(string mcVersion, string forgeVersion)
    {
        var expected = GetForgeVersionId(mcVersion, forgeVersion);
        return File.Exists(Path.Combine(_versionsDir, expected, $"{expected}.json")) ? expected : null;
    }

    private string? FindNeoForgeVersionId(string neoVersion)
    {
        var expected = GetNeoForgeVersionId(neoVersion);
        if (File.Exists(Path.Combine(_versionsDir, expected, $"{expected}.json")))
            return expected;

        if (!Directory.Exists(_versionsDir)) return null;

        foreach (var dir in Directory.GetDirectories(_versionsDir))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals(expected, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(dir, $"{name}.json")))
                return name;
        }

        return null;
    }

    public static string GetNeoForgeVersionPrefix(string mcVersion)
    {
        var parts = mcVersion.Split('.');
        if (parts.Length >= 3 && parts[0] == "1")
            return $"{parts[1]}.{parts[2]}.";

        if (parts.Length >= 2)
            return $"{parts[0]}.{parts[1]}.";

        return mcVersion + ".";
    }

    public async Task<List<string>> FetchFabricLoaderVersions(string mcVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}";
            var json = JArray.Parse(await AppHttp.Client.GetStringAsync(url, cancellationToken));
            return ParseLoaderVersionList(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<List<string>> FetchQuiltLoaderVersions(string mcVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}";
            var json = JArray.Parse(await AppHttp.Client.GetStringAsync(url, cancellationToken));
            return ParseLoaderVersionList(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<string> ParseLoaderVersionList(JArray json)
    {
        var result = new List<string>();
        foreach (var entry in json)
        {
            var ver = entry["loader"]?["version"]?.ToString();
            if (!string.IsNullOrEmpty(ver))
                result.Add(ver);
        }

        return result;
    }

    public async Task<List<string>> FetchForgeVersions(string mcVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xml = await AppHttp.Client.GetStringAsync(
                "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
                cancellationToken);
            var doc = XDocument.Parse(xml);
            var prefix = mcVersion + "-";

            return doc.Descendants()
                .Where(e => e.Name.LocalName == "version")
                .Select(e => e.Value)
                .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
                .Select(v => v[prefix.Length..])
                .Distinct()
                .OrderByDescending(v => v, StringComparer.Ordinal)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<List<string>> FetchNeoForgeVersions(string mcVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = GetNeoForgeVersionPrefix(mcVersion);
            var xml = await AppHttp.Client.GetStringAsync(
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
                cancellationToken);
            var doc = XDocument.Parse(xml);

            return doc.Descendants()
                .Where(e => e.Name.LocalName == "version")
                .Select(e => e.Value)
                .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
                .Distinct()
                .OrderByDescending(v => v, StringComparer.Ordinal)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<bool> InstallFabric(string mcVersion, string loaderVersion, CancellationToken cancellationToken = default)
    {
        var versionId = $"fabric-loader-{loaderVersion}-{mcVersion}";
        var profileJson = Path.Combine(_versionsDir, versionId, $"{versionId}.json");

        if (IsLoaderProfileReady(versionId))
        {
            Log?.Invoke(LocalizationService.F("log.loader.already_installed", "Fabric", loaderVersion, mcVersion));
            return true;
        }

        var vanillaJson = Path.Combine(_versionsDir, mcVersion, $"{mcVersion}.json");
        if (!File.Exists(vanillaJson))
        {
            Log?.Invoke(LocalizationService.F("log.loader.minecraft_first", mcVersion));
            return false;
        }

        Log?.Invoke(LocalizationService.F("log.loader.fabric_installing", loaderVersion, mcVersion));

        if (await RunFabricInstaller(mcVersion, loaderVersion, cancellationToken) && IsLoaderProfileReady(versionId))
            return true;

        Log?.Invoke(LocalizationService.T("log.loader.installer_fallback"));
        return await DownloadFabricProfile(mcVersion, loaderVersion, versionId, cancellationToken);
    }

    private async Task<bool> RunFabricInstaller(string mcVersion, string loaderVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureLauncherProfiles();

            var installerUrl =
                $"https://maven.fabricmc.net/net/fabricmc/fabric-installer/{FabricInstallerVersion}/fabric-installer-{FabricInstallerVersion}.jar";
            var installerPath = Path.Combine(_minecraftDir, "fabric-installer.jar");

            if (!File.Exists(installerPath))
            {
                Log?.Invoke(LocalizationService.F("log.loader.downloading_installer", "Fabric"));
                await DownloadFile(installerUrl, installerPath, cancellationToken);
            }

            var args = $"-jar \"{installerPath}\" client -dir \"{_minecraftDir}\" -mcversion {mcVersion} -loader {loaderVersion}";
            Log?.Invoke(LocalizationService.F("log.loader.running_installer", "Fabric"));

            var process = StartJavaProcess(args, mcVersion);
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.loader.installer_warning", "Fabric", ex.Message));
            return false;
        }
    }

    private async Task<bool> DownloadFabricProfile(string mcVersion, string loaderVersion, string versionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var fabricDir = Path.Combine(_versionsDir, versionId);
            Directory.CreateDirectory(fabricDir);

            var jsonUrl =
                $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
            var jsonContent = await AppHttp.Client.GetStringAsync(jsonUrl, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(fabricDir, $"{versionId}.json"), jsonContent, cancellationToken);

            var destJar = Path.Combine(fabricDir, $"{versionId}.jar");
            var profile = JObject.Parse(jsonContent);
            await EnsureProfileJarAsync(destJar, mcVersion, profile["downloads"]?["client"]?["url"]?.ToString(), cancellationToken);

            Log?.Invoke(LocalizationService.F("log.loader.installed", "Fabric", loaderVersion));
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.loader.install_error", "Fabric", ex.Message));
            return false;
        }
    }

    public async Task<bool> InstallQuilt(string mcVersion, string loaderVersion, CancellationToken cancellationToken = default)
    {
        var versionId = $"quilt-loader-{loaderVersion}-{mcVersion}";
        var profileJson = Path.Combine(_versionsDir, versionId, $"{versionId}.json");

        if (IsLoaderProfileReady(versionId))
        {
            Log?.Invoke(LocalizationService.F("log.loader.already_installed", "Quilt", loaderVersion, mcVersion));
            return true;
        }

        var vanillaJson = Path.Combine(_versionsDir, mcVersion, $"{mcVersion}.json");
        if (!File.Exists(vanillaJson))
        {
            Log?.Invoke(LocalizationService.F("log.loader.minecraft_first", mcVersion));
            return false;
        }

        Log?.Invoke(LocalizationService.F("log.loader.quilt_installing", loaderVersion, mcVersion));

        try
        {
            EnsureLauncherProfiles();

            var installers = JArray.Parse(
                await AppHttp.Client.GetStringAsync("https://meta.quiltmc.org/v3/versions/installer"));
            var installerVersion = installers[0]?["version"]?.ToString();
            if (string.IsNullOrEmpty(installerVersion))
            {
                Log?.Invoke(LocalizationService.T("log.loader.quilt_installer_version_error"));
                return false;
            }

            var installerUrl =
                $"https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/{installerVersion}/quilt-installer-{installerVersion}.jar";
            var installerPath = Path.Combine(_minecraftDir, "quilt-installer.jar");

            if (!File.Exists(installerPath))
            {
                Log?.Invoke(LocalizationService.F("log.loader.downloading_installer", "Quilt"));
                await DownloadFile(installerUrl, installerPath, cancellationToken);
            }

            var args =
                $"-jar \"{installerPath}\" install client {mcVersion} {loaderVersion} --install-dir=\"{_minecraftDir}\"";
            var process = StartJavaProcess(args, mcVersion);
            await process.WaitForExitAsync(cancellationToken);

            if (IsLoaderProfileReady(versionId))
            {
                Log?.Invoke(LocalizationService.F("log.loader.installed", "Quilt", loaderVersion));
                return true;
            }

            Log?.Invoke(LocalizationService.T("log.loader.installer_fallback"));
            return await DownloadQuiltProfile(mcVersion, loaderVersion, versionId, cancellationToken);
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.loader.install_error", "Quilt", ex.Message));
            return false;
        }
    }

    private async Task<bool> DownloadQuiltProfile(string mcVersion, string loaderVersion, string versionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var quiltDir = Path.Combine(_versionsDir, versionId);
            Directory.CreateDirectory(quiltDir);

            var jsonUrl =
                $"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
            var jsonContent = await AppHttp.Client.GetStringAsync(jsonUrl, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(quiltDir, $"{versionId}.json"), jsonContent, cancellationToken);

            var destJar = Path.Combine(quiltDir, $"{versionId}.jar");
            var profile = JObject.Parse(jsonContent);
            await EnsureProfileJarAsync(destJar, mcVersion, profile["downloads"]?["client"]?["url"]?.ToString(), cancellationToken);

            Log?.Invoke(LocalizationService.F("log.loader.installed", "Quilt", loaderVersion));
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.loader.install_error", "Quilt", ex.Message));
            return false;
        }
    }

    private bool IsLoaderProfileReady(string versionId)
    {
        var jsonPath = Path.Combine(_versionsDir, versionId, $"{versionId}.json");
        var jarPath = Path.Combine(_versionsDir, versionId, $"{versionId}.jar");
        return File.Exists(jsonPath) && BuildInstallService.IsValidVersionJar(jarPath);
    }

    private async Task EnsureProfileJarAsync(
        string destJar,
        string mcVersion,
        string? downloadUrl,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(downloadUrl))
            await DownloadFile(downloadUrl, destJar, cancellationToken);

        var parentJar = Path.Combine(_versionsDir, mcVersion, $"{mcVersion}.jar");
        if (!BuildInstallService.IsValidVersionJar(destJar) && File.Exists(parentJar))
            File.Copy(parentJar, destJar, overwrite: true);

        if (!BuildInstallService.IsValidVersionJar(destJar))
            throw new IOException(LocalizationService.F("log.loader.invalid_version_jar", Path.GetFileName(destJar)));
    }

    private void EnsureLauncherProfiles()
    {
        var profilesPath = Path.Combine(_minecraftDir, "launcher_profiles.json");
        if (File.Exists(profilesPath))
            return;

        Directory.CreateDirectory(_minecraftDir);

        const string profilesJson = """
            {
              "profiles": {
                "(Default)": {
                  "name": "(Default)",
                  "type": "latest-release"
                }
              },
              "selectedProfile": "(Default)",
              "clientToken": "00000000000000000000000000000000",
              "authenticationDatabase": {},
              "settings": {
                "enableSnapshots": true,
                "enableBetas": true,
                "enableHistorical": true,
                "enableReleases": true,
                "profileGroups": {}
              },
              "version": 3
            }
            """;

        File.WriteAllText(profilesPath, profilesJson);
        Log?.Invoke(LocalizationService.T("log.loader.launcher_profiles_created"));
    }

    private Process StartJavaProcess(string arguments, string mcVersion)
    {
        var javaPath = new JavaService().ResolveJavaPath(mcVersion);
        if (string.IsNullOrEmpty(javaPath))
            throw new InvalidOperationException(LocalizationService.T("log.launch.java_not_found"));

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = arguments,
                WorkingDirectory = _minecraftDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            if (IsVerboseInstallerLine(e.Data)) return;
            Log?.Invoke($"[Installer] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            if (IsVerboseInstallerLine(e.Data)) return;
            Log?.Invoke($"[Installer] {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static bool IsVerboseInstallerLine(string line)
    {
        if (line.StartsWith("  ", StringComparison.Ordinal))
            return true;

        return line.StartsWith("Considering library", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("Downloading library", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("Reading patch", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("Patching ", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("  Extracting", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DownloadFile(string url, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var response = await AppHttp.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var downloaded = 0L;
        var buffer = new byte[8192];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(path);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;

            if (total > 0)
            {
                var progress = (int)(downloaded * 100 / total);
                ProgressChanged?.Invoke(progress, LocalizationService.F("log.download_file", Path.GetFileName(path)));
            }
        }
    }
}
