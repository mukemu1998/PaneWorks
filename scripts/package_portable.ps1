param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "src\PaneWorks.App\PaneWorks.App.csproj"
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$portableReadme = Join-Path $repoRoot "docs\PORTABLE_README.md"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"

# Keep the portable bundle minimal and ready to unzip-run.
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

[xml]$props = Get-Content $propsPath
$version = $props.Project.PropertyGroup.VersionPrefix
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VersionPrefix was not found in Directory.Build.props."
}

$releaseNotes = Join-Path $repoRoot "docs\release-notes-v$version.zh-CN.md"

$releaseRoot = Join-Path $repoRoot "artifacts\releases\v$version"
$publishRoot = Join-Path $releaseRoot "publish"
$packageRoot = Join-Path $releaseRoot "PaneWorks-v$version-$Runtime-portable"
$zipPath = Join-Path $releaseRoot "PaneWorks-v$version-$Runtime-portable.zip"
$hashPath = Join-Path $releaseRoot "PaneWorks-v$version-$Runtime-portable.zip.sha256"
$publicExeName = "PaneWorks.exe"

Write-Host "==> Clean old artifacts"
if (Test-Path $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Path $packageRoot | Out-Null

Write-Host "==> Stop running PaneWorks"
Get-Process -Name "PaneWorks.App" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "==> Publish self-contained single-file build"
& $dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishRoot

Write-Host "==> Prepare portable package"
Copy-Item -LiteralPath (Join-Path $publishRoot "PaneWorks.App.exe") -Destination (Join-Path $packageRoot $publicExeName)
Copy-Item -LiteralPath $portableReadme -Destination (Join-Path $packageRoot "README.md")
Set-Content -LiteralPath (Join-Path $packageRoot "VERSION") -Value "v$version" -Encoding ASCII

Write-Host "==> Create zip archive"
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force

Write-Host "==> Generate SHA256"
$hash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashPath -Value "$hash *$(Split-Path -Leaf $zipPath)" -Encoding ASCII

if (Test-Path $releaseNotes) {
    Write-Host "==> Copy release notes"
    Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $releaseRoot (Split-Path -Leaf $releaseNotes)) -Force
}

Write-Host ""
Write-Host "Package created:"
Write-Host $zipPath
Write-Host $hashPath
