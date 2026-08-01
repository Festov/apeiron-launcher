using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apeiron.Services;

public sealed class ModpackInstallService
{
    private const int ParallelDownloads = 4;
    private const int CurseForgeParallelDownloads = 3;
    private static readonly TimeSpan PerFileTimeout = TimeSpan.FromSeconds(120);

    private readonly ModrinthModpackService _modrinth = new();
    private readonly string _instancesRoot;
    private readonly string _curseForgeApiKey;

    public ModpackInstallService(string instancesRoot, string curseForgeApiKey)
    {
        _instancesRoot = instancesRoot;
        _curseForgeApiKey = curseForgeApiKey ?? "";
    }

    /// <summary>Creates an instance shell; pack files download later on Play.</summary>
    public BuildInfo CreatePendingInstance(ModpackListItem pack, string? displayName)
    {
        var id = Guid.NewGuid().ToString();
        var name = string.IsNullOrWhiteSpace(displayName) ? pack.Name.Trim() : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "Modpack";

        var build = new BuildInfo
        {
            Id = id,
            Name = name,
            IsModded = true,
            ModsEnabled = true,
            InstallFabricApi = false,
            PendingModpackInstall = true,
            ModpackSource = pack.Source.ToString(),
            ModpackProjectId = pack.Id,
            InstancePath = Path.Combine(_instancesRoot, id)
        };
        build.EnsureInstanceFolders();
        return build;
    }

    public async Task CompletePendingInstallAsync(
        BuildInfo build,
        IProgress<ModpackInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!build.NeedsModpackContentInstall)
            return;

        if (!Enum.TryParse<ModpackSource>(build.ModpackSource, ignoreCase: true, out var source))
            throw new InvalidOperationException(LocalizationService.T("add_build.modpack.invalid_id"));

        var pack = new ModpackListItem
        {
            Source = source,
            Id = build.ModpackProjectId,
            Name = build.Name
        };

        switch (source)
        {
            case ModpackSource.Modrinth:
                await InstallModrinthIntoAsync(build, pack, progress, cancellationToken);
                break;
            case ModpackSource.CurseForge:
                await InstallCurseForgeIntoAsync(build, pack, progress, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }

        build.PendingModpackInstall = false;
    }

    private async Task InstallModrinthIntoAsync(
        BuildInfo build,
        ModpackListItem pack,
        IProgress<ModpackInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, LocalizationService.T("add_build.modpack.resolving"), percent: 2);

        var (_, downloadUrl, fileName) = await _modrinth.ResolveLatestPackFileAsync(pack.Id, cancellationToken);
        var tempRoot = CreateTempDir("apeiron-mrpack-");
        var packPath = Path.Combine(tempRoot, SanitizeFileName(fileName));

        try
        {
            await DownloadWithPulseAsync(
                () => DownloadWithTimeoutAsync(
                    ct => _modrinth.DownloadFileAsync(downloadUrl, packPath, ct),
                    TimeSpan.FromMinutes(10),
                    cancellationToken),
                progress,
                LocalizationService.T("add_build.modpack.downloading_pack"),
                percentFrom: 5,
                percentTo: 15,
                cancellationToken);

            Report(progress, LocalizationService.T("add_build.modpack.resolving"), percent: 16);
            var extractDir = Path.Combine(tempRoot, "extract");
            ZipExtractHelper.ExtractZipFile(packPath, extractDir);

            var indexPath = Path.Combine(extractDir, "modrinth.index.json");
            if (!File.Exists(indexPath))
                throw new InvalidOperationException(LocalizationService.T("add_build.modpack.invalid_mrpack"));

            var index = ModpackManifestParser.ParseMrpackIndex(File.ReadAllText(indexPath));
            ApplyMetadata(build, index.Metadata);
            build.EnsureInstanceFolders();
            var gameDir = build.GetGameDir();

            await DownloadFilesParallelAsync(
                index.Files,
                progress,
                cancellationToken,
                async (entry, ct) =>
                {
                    var dest = Path.GetFullPath(Path.Combine(gameDir, entry.Path));
                    EnsureUnderRoot(dest, gameDir);
                    if (IsUsableFile(dest))
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    Exception? lastError = null;
                    foreach (var url in entry.Downloads)
                    {
                        try
                        {
                            await DownloadWithTimeoutAsync(
                                token => _modrinth.DownloadFileAsync(url, dest, token),
                                PerFileTimeout,
                                ct);
                            return;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            lastError = ex;
                        }
                    }

                    if (entry.ClientRequired)
                        throw lastError ?? new InvalidOperationException(LocalizationService.F("add_build.modpack.file_failed", entry.Path));
                },
                fileLabel: entry => LocalizationService.F("add_build.modpack.downloading_file", Path.GetFileName(entry.Path)));

            Report(progress, LocalizationService.T("add_build.modpack.applying_overrides"), percent: 96);
            CopyOverrides(Path.Combine(extractDir, "overrides"), gameDir);
            CopyOverrides(Path.Combine(extractDir, "client-overrides"), gameDir);
            Report(progress, LocalizationService.T("add_build.modpack.done"), percent: 100);
        }
        finally
        {
            TryDeleteDir(tempRoot);
        }
    }

    private async Task InstallCurseForgeIntoAsync(
        BuildInfo build,
        ModpackListItem pack,
        IProgress<ModpackInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(pack.Id, out var modId))
            throw new InvalidOperationException(LocalizationService.T("add_build.modpack.invalid_id"));

        var cf = new CurseForgeModpackService(_curseForgeApiKey);
        Report(progress, LocalizationService.T("add_build.modpack.resolving"), percent: 2);

        var (packModId, packFileId, fileName, downloadUrl) = await cf.ResolveLatestPackFileAsync(modId, cancellationToken);
        var tempRoot = CreateTempDir("apeiron-cfpack-");
        var packPath = Path.Combine(tempRoot, SanitizeFileName(fileName));

        try
        {
            await DownloadWithPulseAsync(
                () => DownloadWithTimeoutAsync(
                    ct => cf.DownloadModFileAsync(packModId, packFileId, packPath, downloadUrl, fileName, ct),
                    TimeSpan.FromMinutes(10),
                    cancellationToken),
                progress,
                LocalizationService.T("add_build.modpack.downloading_pack"),
                percentFrom: 5,
                percentTo: 15,
                cancellationToken);

            Report(progress, LocalizationService.T("add_build.modpack.resolving"), percent: 16);
            var extractDir = Path.Combine(tempRoot, "extract");
            ZipExtractHelper.ExtractZipFile(packPath, extractDir);

            var manifestPath = FindManifest(extractDir);
            if (manifestPath == null)
                throw new InvalidOperationException(LocalizationService.T("add_build.modpack.invalid_curseforge"));

            var manifest = ModpackManifestParser.ParseCurseForgeManifest(File.ReadAllText(manifestPath));
            ApplyMetadata(build, manifest.Metadata);
            build.EnsureInstanceFolders();
            var modsDir = build.GetModsDir();
            var gameDir = build.GetGameDir();

            var files = manifest.Files.Where(f => f.Required).ToList();
            var fileIds = files.Select(f => f.FileId).Distinct().ToList();
            Report(progress, LocalizationService.F("add_build.modpack.downloading_mod", files.Count), percent: 18);

            var bulk = await cf.GetFilesBulkAsync(fileIds, cancellationToken);
            Report(progress, LocalizationService.T("add_build.modpack.resolving"), percent: 20);

            await DownloadFilesParallelAsync(
                files,
                progress,
                cancellationToken,
                async (entry, ct) =>
                {
                    string? preferredUrl = null;
                    string? apiFileName = null;
                    var destName = $"{entry.ProjectId}-{entry.FileId}.jar";

                    if (bulk.TryGetValue(entry.FileId, out var info))
                    {
                        if (!string.IsNullOrWhiteSpace(info.FileName))
                        {
                            apiFileName = info.FileName;
                            destName = SanitizeFileName(info.FileName);
                        }

                        if (!string.IsNullOrWhiteSpace(info.DownloadUrl))
                            preferredUrl = info.DownloadUrl;
                        else if (!string.IsNullOrWhiteSpace(apiFileName))
                            preferredUrl = CurseForgeModpackService.BuildCdnDownloadUrl(entry.FileId, apiFileName);
                    }

                    if (!string.IsNullOrWhiteSpace(preferredUrl) &&
                        Uri.TryCreate(preferredUrl, UriKind.Absolute, out var uri))
                    {
                        var urlName = Path.GetFileName(uri.AbsolutePath);
                        if (!string.IsNullOrWhiteSpace(urlName) &&
                            (urlName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                             urlName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                            destName = SanitizeFileName(Uri.UnescapeDataString(urlName));
                    }

                    var dest = Path.Combine(modsDir, destName);
                    if (IsUsableFile(dest))
                        return;

                    await DownloadWithTimeoutAsync(
                        token => cf.DownloadModFileAsync(
                            entry.ProjectId,
                            entry.FileId,
                            dest,
                            preferredUrl,
                            apiFileName,
                            token),
                        PerFileTimeout,
                        ct);
                },
                fileLabel: entry => LocalizationService.F("add_build.modpack.downloading_mod", entry.ProjectId),
                maxDegree: CurseForgeParallelDownloads);
            Report(progress, LocalizationService.T("add_build.modpack.applying_overrides"), percent: 96);
            var overridesName = string.IsNullOrWhiteSpace(manifest.OverridesFolder) ? "overrides" : manifest.OverridesFolder;
            var overridesDir = Path.Combine(Path.GetDirectoryName(manifestPath)!, overridesName);
            CopyOverrides(overridesDir, gameDir);
            Report(progress, LocalizationService.T("add_build.modpack.done"), percent: 100);
        }
        finally
        {
            TryDeleteDir(tempRoot);
        }
    }

    private static async Task DownloadFilesParallelAsync<T>(
        IReadOnlyList<T> files,
        IProgress<ModpackInstallProgress>? progress,
        CancellationToken cancellationToken,
        Func<T, CancellationToken, Task> downloadOne,
        Func<T, string> fileLabel,
        int? maxDegree = null)
    {
        var total = Math.Max(1, files.Count);
        var done = 0;
        var failures = new System.Collections.Concurrent.ConcurrentBag<(T Item, Exception Error)>();
        Report(progress, LocalizationService.T("add_build.modpack.downloading_pack"), percent: MapFilesPercent(0, total), 0, total);

        await RunParallelAsync(files, maxDegree ?? ParallelDownloads, cancellationToken, async item =>
        {
            try
            {
                await downloadOne(item, cancellationToken);
                var completed = Interlocked.Increment(ref done);
                Report(
                    progress,
                    fileLabel(item),
                    percent: MapFilesPercent(completed, total),
                    completed,
                    total);
            }
            catch (Exception ex) when (!HttpRetryHelper.IsCancellation(ex, cancellationToken))
            {
                failures.Add((item, ex));
            }
        });

        // One serial pass for files that failed under parallel CDN pressure (common 403 near the end).
        foreach (var (item, _) in failures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, fileLabel(item), percent: MapFilesPercent(done, total), done, total);
            await downloadOne(item, cancellationToken);
            var completed = Interlocked.Increment(ref done);
            Report(
                progress,
                fileLabel(item),
                percent: MapFilesPercent(completed, total),
                completed,
                total);
        }
    }

    private static int MapFilesPercent(int completed, int total)
    {
        // File downloads occupy 20% → 95% of the overall bar.
        var fraction = total <= 0 ? 1.0 : (double)completed / total;
        return (int)Math.Clamp(20 + fraction * 75, 20, 95);
    }

    private static async Task DownloadWithPulseAsync(
        Func<Task> download,
        IProgress<ModpackInstallProgress>? progress,
        string message,
        int percentFrom,
        int percentTo,
        CancellationToken cancellationToken)
    {
        Report(progress, message, percent: percentFrom);
        var pulse = percentFrom;
        using var pulseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pulseTask = Task.Run(async () =>
        {
            try
            {
                while (!pulseCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(1500, pulseCts.Token);
                    if (pulse < percentTo - 1)
                    {
                        pulse++;
                        Report(progress, message, percent: pulse);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected when download finishes
            }
        }, CancellationToken.None);

        try
        {
            await download();
        }
        finally
        {
            pulseCts.Cancel();
            try { await pulseTask; } catch { /* ignore */ }
            Report(progress, message, percent: percentTo);
        }
    }

    private static async Task DownloadWithTimeoutAsync(
        Func<CancellationToken, Task> download,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            try
            {
                await download(linked.Token);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException($"Download timed out after {timeout.TotalSeconds:0}s");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                if (attempt == 0)
                    await Task.Delay(HttpRetryHelper.GetDownloadBackoff(1), cancellationToken);
            }
        }

        throw lastError ?? new TimeoutException("Download failed");
    }

    private static void ApplyMetadata(BuildInfo build, ParsedModpackMetadata meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.MinecraftVersion))
            build.MinecraftVersion = meta.MinecraftVersion;
        if (!string.IsNullOrWhiteSpace(meta.Loader))
            build.Loader = meta.Loader;
        if (!string.IsNullOrWhiteSpace(meta.LoaderVersion))
            build.LoaderVersion = meta.LoaderVersion;
        build.IsModded = !string.IsNullOrWhiteSpace(build.Loader);
        build.InstallFabricApi = false;
    }

    private static async Task RunParallelAsync<T>(
        IReadOnlyList<T> items,
        int maxDegree,
        CancellationToken cancellationToken,
        Func<T, Task> work)
    {
        if (items.Count == 0)
            return;

        using var gate = new SemaphoreSlim(Math.Max(1, maxDegree));
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await work(item);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex) when (HttpRetryHelper.IsCancellation(ex, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }

    private static bool IsUsableFile(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 1024;

    private static string? FindManifest(string extractDir)
    {
        var direct = Path.Combine(extractDir, "manifest.json");
        if (File.Exists(direct))
            return direct;

        return Directory.EnumerateFiles(extractDir, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static void CopyOverrides(string sourceDir, string gameDir)
    {
        if (!Directory.Exists(sourceDir))
            return;

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.GetFullPath(Path.Combine(gameDir, relative));
            EnsureUnderRoot(dest, gameDir);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void EnsureUnderRoot(string fullPath, string root)
    {
        var rootFull = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes instance root: {fullPath}");
    }

    private static void Report(
        IProgress<ModpackInstallProgress>? progress,
        string message,
        int percent,
        int completed = 0,
        int total = 0) =>
        progress?.Report(new ModpackInstallProgress
        {
            Message = message,
            Percent = Math.Clamp(percent, 0, 100),
            Completed = completed,
            Total = total
        });

    private static string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "pack.zip" : name;
    }
}
