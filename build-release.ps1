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

$releaseNotes = Join-Path $root "RELEASE.txt"
if (Test-Path $releaseNotes) {
    Copy-Item $releaseNotes $publishDir -Force
}

$distDir = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$zipName = "Apeiron-$version-win-x64.zip"
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath)

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "Apeiron $version"
Write-Host "  EXE: $exe ($sizeMb MB)"
Write-Host "  ZIP: $zipPath ($zipMb MB)"
