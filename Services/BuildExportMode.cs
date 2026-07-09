namespace Apeiron.Services;

public enum BuildExportMode
{
    /// <summary>Mods, config, resource packs — no worlds.</summary>
    Modpack,

    /// <summary>Full instance backup including saves.</summary>
    FullBackup
}
