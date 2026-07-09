using System;
using System.IO;

namespace Apeiron.Services;

/// <summary>Imports third-party modpack zips as new instances.</summary>
public static class ModpackImportService
{
    public static BuildInfo ImportAsNewInstance(string zipPath, string instancesRoot) =>
        BuildExportService.Import(zipPath, instancesRoot);
}
