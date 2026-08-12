# Creates Start Menu and Desktop shortcuts to Apeiron.exe.
# Usage:
#   .\scripts\install-shortcut.ps1
#   .\scripts\install-shortcut.ps1 -ExePath "D:\path\to\Apeiron.exe"

param(
    [Parameter(Mandatory = $false)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

function Resolve-ApeironExe {
    param([string]$PathHint)

    if (-not [string]::IsNullOrWhiteSpace($PathHint)) {
        $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PathHint)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Executable not found: $resolved"
        }
        return (Resolve-Path -LiteralPath $resolved).Path
    }

    $scriptDir = Split-Path -Parent $PSCommandPath
    $repoRoot = Split-Path -Parent $scriptDir
    $candidates = @(
        (Join-Path $repoRoot "Apeiron.exe"),
        (Join-Path $repoRoot "bin\Release\net8.0-windows\win-x64\Apeiron.exe"),
        (Join-Path $repoRoot "bin\Debug\net8.0-windows\win-x64\Apeiron.exe"),
        (Join-Path $repoRoot "publish\Apeiron.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Apeiron.exe not found. Pass -ExePath or build the launcher first."
}

$exe = Resolve-ApeironExe -PathHint $ExePath
$workDir = Split-Path -Parent $exe
$shortcutName = "Apeiron Launcher.lnk"

$shell = New-Object -ComObject WScript.Shell

function New-Shortcut {
    param(
        [string]$LinkPath,
        [string]$Target,
        [string]$WorkingDirectory
    )

    $shortcut = $shell.CreateShortcut($LinkPath)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = "Apeiron Minecraft Launcher"
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Save()
}

$desktop = [Environment]::GetFolderPath("Desktop")
$startMenu = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs"
if (-not (Test-Path -LiteralPath $startMenu)) {
    New-Item -ItemType Directory -Path $startMenu -Force | Out-Null
}

$desktopLink = Join-Path $desktop $shortcutName
$startMenuLink = Join-Path $startMenu $shortcutName

New-Shortcut -LinkPath $desktopLink -Target $exe -WorkingDirectory $workDir
New-Shortcut -LinkPath $startMenuLink -Target $exe -WorkingDirectory $workDir

Write-Host "Created shortcuts:"
Write-Host "  $desktopLink"
Write-Host "  $startMenuLink"
Write-Host "Target: $exe"
