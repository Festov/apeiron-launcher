using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using ICSharpCode.SharpZipLib.Zip;

namespace Apeiron.Services;

public class MinecraftService
{
    private readonly string _launcherDir;
    public string MinecraftDir { get; }
    private readonly string _versionsDir;
    private readonly string _librariesDir;

    private const int MAX_RETRY = 3;

    public event Action<int, string>? ProgressChanged;
    public event Action<string>? Log;

    public MinecraftService(string? minecraftDir = null)
    {
        _launcherDir = AppDomain.CurrentDomain.BaseDirectory;
        MinecraftDir = string.IsNullOrWhiteSpace(minecraftDir)
            ? Path.Combine(_launcherDir, ".minecraft")
            : minecraftDir.Trim();
        _versionsDir = Path.Combine(MinecraftDir, "versions");
        _librariesDir = Path.Combine(MinecraftDir, "libraries");

        Directory.CreateDirectory(MinecraftDir);
        Directory.CreateDirectory(_versionsDir);
        Directory.CreateDirectory(_librariesDir);

        Log?.Invoke(LocalizationService.F("log.mc.launcher_dir", _launcherDir));
        Log?.Invoke(LocalizationService.F("log.mc.minecraft_dir", MinecraftDir));
    }

    public async Task<bool> DownloadVanillaMinecraft(string version = "26.1", CancellationToken cancellationToken = default)
    {
        var versionDir = Path.Combine(_versionsDir, version);
        var versionJsonPath = Path.Combine(versionDir, $"{version}.json");
        var versionJsonTempPath = versionJsonPath + ".tmp";
        var jarPath = Path.Combine(versionDir, $"{version}.jar");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log?.Invoke(LocalizationService.F("log.mc.downloading", version));

            Directory.CreateDirectory(versionDir);

            if (File.Exists(jarPath) && File.Exists(versionJsonPath))
            {
                Log?.Invoke(LocalizationService.F("log.mc.already_downloaded", version));
                return true;
            }

            Log?.Invoke(LocalizationService.T("log.mc.downloading_manifest"));
            var manifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
            var manifestJson = await AppHttp.Client.GetStringAsync(manifestUrl);
            var manifest = JObject.Parse(manifestJson);

            var versionEntry = manifest["versions"]?
                .FirstOrDefault(v => v["id"]?.ToString() == version);

            if (versionEntry == null)
            {
                Log?.Invoke(LocalizationService.F("log.mc.version_not_in_manifest", version));
                return false;
            }

            var versionUrl = versionEntry["url"]?.ToString();
            if (string.IsNullOrEmpty(versionUrl))
            {
                Log?.Invoke(LocalizationService.F("log.mc.version_url_not_found", version));
                return false;
            }

            Log?.Invoke(LocalizationService.F("log.mc.downloading_version_json", version));
            var versionJson = await AppHttp.Client.GetStringAsync(versionUrl);
            var versionData = JObject.Parse(versionJson);

            await File.WriteAllTextAsync(versionJsonTempPath, versionJson, cancellationToken);

            var clientUrl = versionData["downloads"]?["client"]?["url"]?.ToString();
            if (string.IsNullOrEmpty(clientUrl))
            {
                Log?.Invoke(LocalizationService.T("log.mc.client_jar_url_not_found"));
                return false;
            }

            Log?.Invoke(LocalizationService.T("log.mc.downloading_client_jar"));
            var clientSha1 = versionData["downloads"]?["client"]?["sha1"]?.ToString();
            await DownloadFileWithRetry(clientUrl, jarPath, clientSha1, cancellationToken);

            var libraries = versionData["libraries"] as JArray;
            if (libraries != null)
            {
                var total = libraries.Count;
                var processed = 0;

                foreach (var lib in libraries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processed++;
                    ProgressChanged?.Invoke(
                        (int)((double)processed / total * 100),
                        LocalizationService.F("log.mc.downloading_library", lib["name"]?.ToString() ?? "library"));

                    await LibraryHelper.DownloadFromVersionLibraryAsync(
                        lib,
                        _librariesDir,
                        (url, path, sha1, ct) => DownloadFileWithRetry(url, path, sha1, ct),
                        cancellationToken);
                }
            }

            await EnsureLwjglUnsafeLibrary(versionData, cancellationToken);

            var lwjglVersion = "3.4.1";
            var nativesToDownload = new[]
            {
                $"https://repo1.maven.org/maven2/org/lwjgl/lwjgl/{lwjglVersion}/lwjgl-{lwjglVersion}-natives-windows.jar",
                $"https://repo1.maven.org/maven2/org/lwjgl/lwjgl-glfw/{lwjglVersion}/lwjgl-glfw-{lwjglVersion}-natives-windows.jar",
                $"https://repo1.maven.org/maven2/org/lwjgl/lwjgl-openal/{lwjglVersion}/lwjgl-openal-{lwjglVersion}-natives-windows.jar",
                $"https://repo1.maven.org/maven2/org/lwjgl/lwjgl-stb/{lwjglVersion}/lwjgl-stb-{lwjglVersion}-natives-windows.jar",
                $"https://repo1.maven.org/maven2/org/lwjgl/lwjgl-tinyfd/{lwjglVersion}/lwjgl-tinyfd-{lwjglVersion}-natives-windows.jar",
                $"https://repo1.maven.org/maven2/org/lwjgl/lwjgl-jemalloc/{lwjglVersion}/lwjgl-jemalloc-{lwjglVersion}-natives-windows.jar",
                $"https://repo1.maven.org/maven2/org/lwjgl/lwjgl-opengl/{lwjglVersion}/lwjgl-opengl-{lwjglVersion}-natives-windows.jar"
            };

            foreach (var url in nativesToDownload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(url);
                var jarPathNative = Path.Combine(_librariesDir, fileName);

                if (!File.Exists(jarPathNative))
                    await DownloadFileWithRetry(url, jarPathNative, null, cancellationToken);
            }

            var assetIndex = versionData["assetIndex"];
            if (assetIndex != null)
            {
                var assetUrl = assetIndex["url"]?.ToString();
                var assetId = assetIndex["id"]?.ToString() ?? version;
                
                if (!string.IsNullOrEmpty(assetUrl))
                {
                    Log?.Invoke(LocalizationService.T("log.mc.downloading_asset_index"));
                    var assetJson = await AppHttp.Client.GetStringAsync(assetUrl);
                    var assetIndexPath = Path.Combine(MinecraftDir, "assets", "indexes", $"{assetId}.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(assetIndexPath)!);
                    await File.WriteAllTextAsync(assetIndexPath, assetJson);
                    
                    Log?.Invoke(LocalizationService.T("log.mc.downloading_all_assets"));
                    if (!await DownloadAllAssets(assetId, cancellationToken))
                        return false;
                }
            }

            await EnsureIndexAndCreateVirtual(version);

            File.Move(versionJsonTempPath, versionJsonPath, overwrite: true);

            Log?.Invoke(LocalizationService.F("log.mc.ready", version));

            return true;
        }
        catch (OperationCanceledException)
        {
            CleanupPartialVersion(versionDir, versionJsonTempPath, versionJsonPath, jarPath);
            Log?.Invoke(LocalizationService.T("log.mc.download_cancelled"));
            return false;
        }
        catch (Exception ex)
        {
            CleanupPartialVersion(versionDir, versionJsonTempPath, versionJsonPath, jarPath);
            Log?.Invoke(LocalizationService.F("log.mc.download_error", ex.Message));
            return false;
        }
    }

    private static void CleanupPartialVersion(string versionDir, string versionJsonTempPath, string versionJsonPath, string jarPath)
    {
        try
        {
            if (File.Exists(versionJsonTempPath))
                File.Delete(versionJsonTempPath);

            if (File.Exists(versionJsonPath) && !File.Exists(jarPath))
                File.Delete(versionJsonPath);

            if (Directory.Exists(versionDir) &&
                Directory.GetFiles(versionDir, "*", SearchOption.AllDirectories).Length == 0 &&
                Directory.GetDirectories(versionDir, "*", SearchOption.AllDirectories).Length == 0)
            {
                Directory.Delete(versionDir, true);
            }
        }
        catch
        {
            // Ignore cleanup issues after cancellation/failure.
        }
    }

    private async Task DownloadFileWithRetry(
        string url,
        string path,
        string? expectedSha1 = null,
        CancellationToken cancellationToken = default,
        int retryCount = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await DownloadFile(url, path, cancellationToken);

            if (!LibraryHelper.VerifySha1(path, expectedSha1))
            {
                File.Delete(path);
                throw new IOException(LocalizationService.T("log.mc.sha1_mismatch"));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (retryCount < MAX_RETRY)
            {
                Log?.Invoke(LocalizationService.F("log.mc.retry", retryCount + 1, MAX_RETRY, ex.Message));
                await Task.Delay(1000 * (retryCount + 1), cancellationToken);
                await DownloadFileWithRetry(url, path, expectedSha1, cancellationToken, retryCount + 1);
            }
            else
            {
                throw;
            }
        }
    }

    private async Task<bool> DownloadAllAssets(string version, CancellationToken cancellationToken = default)
    {
        try
        {
            Log?.Invoke(LocalizationService.T("log.mc.downloading_all_assets_start"));
            
            var assetIndexPath = Path.Combine(MinecraftDir, "assets", "indexes", $"{version}.json");
            if (!File.Exists(assetIndexPath))
            {
                Log?.Invoke(LocalizationService.F("log.mc.index_file_not_found", assetIndexPath));
                return false;
            }

            var assetIndexJson = await File.ReadAllTextAsync(assetIndexPath);
            var assetData = JObject.Parse(assetIndexJson);
            var objects = assetData["objects"] as JObject;

            if (objects == null)
            {
                Log?.Invoke(LocalizationService.T("log.mc.no_objects_in_index"));
                return false;
            }

            var total = objects.Count;
            var downloaded = 0;
            var failures = 0;
            var assetsDir = Path.Combine(MinecraftDir, "assets", "objects");

            Log?.Invoke(LocalizationService.F("log.mc.assets_total", total));

            var options = new ParallelOptions { MaxDegreeOfParallelism = 20, CancellationToken = cancellationToken };
            var properties = objects.Properties().ToList();

            await Task.Run(() =>
            {
                Parallel.ForEach(properties, options, (prop) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hash = prop.Value?["hash"]?.ToString();
                    if (string.IsNullOrEmpty(hash)) return;

                    var subDir = hash.Substring(0, 2);
                    var objectDir = Path.Combine(assetsDir, subDir);
                    var objectPath = Path.Combine(objectDir, hash);

                    if (File.Exists(objectPath))
                    {
                        Interlocked.Increment(ref downloaded);
                        return;
                    }

                    var url = $"https://resources.download.minecraft.net/{subDir}/{hash}";
                    
                    try
                    {
                        Directory.CreateDirectory(objectDir);
                        DownloadFileSync(url, objectPath);
                    }
                    catch
                    {
                        Interlocked.Increment(ref failures);
                    }

                    var current = Interlocked.Increment(ref downloaded);
                    
                    if (current % 50 == 0 || current == total)
                    {
                        ProgressChanged?.Invoke(
                            (int)((double)current / total * 100),
                            LocalizationService.F("log.mc.downloading_assets", current, total)
                        );
                    }
                });
            }, cancellationToken);

            if (failures > 0)
            {
                Log?.Invoke(LocalizationService.F("log.mc.assets_partial_fail", failures, total));
                return false;
            }

            Log?.Invoke(LocalizationService.F("log.mc.assets_done", downloaded, total));
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.mc.assets_error", ex.Message));
            return false;
        }
    }

    private void DownloadFileSync(string url, string path)
    {
        try
        {
            using var response = AppHttp.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var fileStream = File.Create(path);
            stream.CopyTo(fileStream);
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.mc.download_file_error", Path.GetFileName(path), ex.Message));
            throw;
        }
    }

    private async Task EnsureIndexAndCreateVirtual(string version)
    {
        var indexFile = Path.Combine(MinecraftDir, "assets", "indexes", $"{version}.json");
        
        if (!File.Exists(indexFile))
        {
            try
            {
                var manifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
                var manifestJson = await AppHttp.Client.GetStringAsync(manifestUrl);
                var manifest = JObject.Parse(manifestJson);
                
                var versionEntry = manifest["versions"]?
                    .FirstOrDefault(v => v["id"]?.ToString() == version);
                    
                if (versionEntry == null)
                {
                    Log?.Invoke(LocalizationService.F("log.mc.version_not_in_manifest", version));
                    return;
                }
                
                var versionUrl = versionEntry["url"]?.ToString();
                if (string.IsNullOrEmpty(versionUrl))
                {
                    Log?.Invoke(LocalizationService.F("log.mc.version_url_not_found", version));
                    return;
                }
                
                var versionJson = await AppHttp.Client.GetStringAsync(versionUrl);
                var versionData = JObject.Parse(versionJson);
                
                var assetIndex = versionData["assetIndex"];
                if (assetIndex == null)
                {
                    Log?.Invoke(LocalizationService.F("log.mc.no_asset_index", version));
                    return;
                }
                
                var assetUrl = assetIndex["url"]?.ToString();
                if (string.IsNullOrEmpty(assetUrl))
                {
                    Log?.Invoke(LocalizationService.T("log.mc.no_asset_index_url"));
                    return;
                }
                
                var assetJson = await AppHttp.Client.GetStringAsync(assetUrl);
                
                Directory.CreateDirectory(Path.GetDirectoryName(indexFile)!);
                await File.WriteAllTextAsync(indexFile, assetJson);
            }
            catch (Exception ex)
            {
                Log?.Invoke(LocalizationService.F("log.mc.index_download_error", ex.Message));
                return;
            }
        }
        
        await CreateVirtualAssetsProperly(version);
    }

    private async Task CreateVirtualAssetsProperly(string version)
    {
        try
        {
            var objectsDir = Path.Combine(MinecraftDir, "assets", "objects");
            var virtualDir = Path.Combine(MinecraftDir, "assets", "virtual");

            if (!Directory.Exists(objectsDir))
            {
                return;
            }

            if (Directory.Exists(virtualDir))
            {
                Directory.Delete(virtualDir, true);
            }
            
            Directory.CreateDirectory(virtualDir);

            var assetIndexPath = Path.Combine(MinecraftDir, "assets", "indexes", $"{version}.json");
            var virtualIndexDir = Path.Combine(virtualDir, "indexes");
            Directory.CreateDirectory(virtualIndexDir);
            var virtualIndexPath = Path.Combine(virtualIndexDir, $"{version}.json");
            File.Copy(assetIndexPath, virtualIndexPath, true);

            var virtualObjectsDir = Path.Combine(virtualDir, "objects");
            
            if (!TryCreateDirectoryLink(virtualObjectsDir, objectsDir))
                await Task.Run(() => CopyDirectory(objectsDir, virtualObjectsDir));
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.mc.virtual_assets_error", ex.Message));
        }
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            if (Directory.Exists(linkPath))
                Directory.Delete(linkPath);

            Directory.CreateSymbolicLink(linkPath, targetPath);
            return Directory.Exists(linkPath);
        }
        catch
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(psi);
                process?.WaitForExit();
                return process?.ExitCode == 0 && Directory.Exists(linkPath);
            }
            catch
            {
                return false;
            }
        }
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    private async Task EnsureLwjglUnsafeLibrary(JObject versionData, CancellationToken cancellationToken = default)
    {
        var libraries = versionData["libraries"] as JArray;
        if (libraries == null) return;

        var needsUnsafe = libraries.Any(lib =>
        {
            var name = lib["name"]?.ToString() ?? "";
            return name.StartsWith("org.lwjgl:lwjgl:", StringComparison.Ordinal) &&
                   !name.Contains(":unsafe", StringComparison.Ordinal);
        });

        if (!needsUnsafe) return;

        const string lwjglVersion = "3.4.1";
        var unsafePath = Path.Combine(_librariesDir, "org", "lwjgl", "lwjgl", lwjglVersion, $"lwjgl-{lwjglVersion}-unsafe.jar");
        if (File.Exists(unsafePath)) return;

        var url = $"https://repo1.maven.org/maven2/org/lwjgl/lwjgl/{lwjglVersion}/lwjgl-{lwjglVersion}-unsafe.jar";
        Log?.Invoke(LocalizationService.T("log.mc.downloading_lwjgl_unsafe"));
        Directory.CreateDirectory(Path.GetDirectoryName(unsafePath)!);
        await DownloadFileWithRetry(url, unsafePath, null, cancellationToken);
    }

    private async Task DownloadFile(string url, string path, CancellationToken cancellationToken = default)
    {
        using var response = await AppHttp.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var downloaded = 0L;
        var buffer = new byte[8192];

        using var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(path);

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;

            if (total > 0)
            {
                var progress = (int)((double)downloaded / total * 100);
                ProgressChanged?.Invoke(progress, LocalizationService.F("log.download_file", Path.GetFileName(path)));
            }
        }
    }
}