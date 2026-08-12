param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Get-ProjectVersion {
    [xml]$proj = Get-Content (Join-Path $root "Apeiron.csproj")
    $version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Version not found in Apeiron.csproj"
    }
    return $version.Trim()
}

$version = Get-ProjectVersion

$repo = ""
$url = git -C $root config --get remote.origin.url 2>$null
if ($url -match 'github\.com[:/](.+?)(?:\.git)?$') {
    $repo = $matches[1].Trim().TrimEnd('/')
}
if ([string]::IsNullOrWhiteSpace($repo) -and $env:GITHUB_REPOSITORY) {
    $repo = $env:GITHUB_REPOSITORY.Trim()
}
if ([string]::IsNullOrWhiteSpace($repo)) {
    $repo = "Festov/apeiron-launcher"
}

if (-not $SkipTests) {
    Write-Host "Running tests..."
    dotnet test Apeiron.Tests/Apeiron.Tests.csproj -c Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Publishing Apeiron $version..."
dotnet publish Apeiron.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:GitHubRepository="$repo"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishDir = Join-Path $root "bin\Release\net8.0-windows\win-x64\publish"
$exe = Join-Path $publishDir "Apeiron.exe"

if (-not (Test-Path $exe)) {
    throw "Publish failed: $exe not found"
}

Get-ChildItem $publishDir -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

$distDir = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$zipName = "Apeiron-$version-win-x64.zip"
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath)

$sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash
$shaPath = "$zipPath.sha256"
Set-Content -Path $shaPath -Value "$sha256  $zipName" -Encoding ASCII -NoNewline

$distExe = Join-Path $distDir "Apeiron.exe"
$previousExe = $null
$fromVer = $null
if (Test-Path $distExe) {
    try {
        $prevInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($distExe)
        $fromVer = ($prevInfo.ProductVersion -replace '[^\d\.].*$', '').Trim('.')
        if ([string]::IsNullOrWhiteSpace($fromVer)) {
            $fromVer = "$($prevInfo.FileMajorPart).$($prevInfo.FileMinorPart).$($prevInfo.FileBuildPart)"
        }
        if ($fromVer -and $fromVer -ne $version) {
            $previousExe = Join-Path $distDir "Apeiron.prev.exe"
            Copy-Item $distExe $previousExe -Force
        }
    } catch { }
}

Copy-Item $exe $distExe -Force
$exeSha = (Get-FileHash $distExe -Algorithm SHA256).Hash
Set-Content -Path "$distExe.sha256" -Value "$exeSha  Apeiron.exe" -Encoding ASCII -NoNewline

# Remove legacy versioned standalone copies
Get-ChildItem $distDir -Filter "Apeiron-*-win-x64.exe*" -ErrorAction SilentlyContinue | Remove-Item -Force

if ($previousExe -and (Test-Path $previousExe) -and $fromVer) {
    $patchName = "Apeiron-$fromVer-to-$version.patch"
    $patchPath = Join-Path $distDir $patchName
    Write-Host "Creating delta patch from $fromVer..."
    dotnet run --project (Join-Path $root "tools\Apeiron.PatchTool\Apeiron.PatchTool.csproj") -c Release -- `
        $previousExe $distExe $patchPath
    if ($LASTEXITCODE -eq 0 -and (Test-Path $patchPath)) {
        $patchSha = (Get-FileHash $patchPath -Algorithm SHA256).Hash
        Set-Content -Path "$patchPath.sha256" -Value "$patchSha  $patchName" -Encoding ASCII -NoNewline
        Write-Host "  PATCH: $patchPath"
    }
    Remove-Item $previousExe -Force -ErrorAction SilentlyContinue
}

$sizeMb = [math]::Round((Get-Item $distExe).Length / 1MB, 1)
$zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "Apeiron $version"
Write-Host "  EXE: $distExe ($sizeMb MB)"
Write-Host "  ZIP: $zipPath ($zipMb MB)"
Write-Host "  SHA256: $shaPath"
