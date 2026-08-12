[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}

if (-not $outputRoot.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the repository: $outputRoot"
}

$packageName = "GuguPet-Windows-x64"
$packageDirectory = Join-Path $outputRoot $packageName
$watcherDirectory = Join-Path $outputRoot "watcher"
$archivePath = Join-Path $outputRoot "$packageName.zip"
$checksumPath = Join-Path $outputRoot "SHA256SUMS.txt"

foreach ($path in @($packageDirectory, $watcherDirectory, $archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $packageDirectory, $watcherDirectory | Out-Null

dotnet publish (Join-Path $repositoryRoot "GuguPet.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -o $packageDirectory

if ($LASTEXITCODE -ne 0) { throw "GuguPet publish failed with exit code $LASTEXITCODE" }

dotnet publish (Join-Path $repositoryRoot "launch-watcher\GuguPet.LaunchWatcher.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -o $watcherDirectory

if ($LASTEXITCODE -ne 0) { throw "Launch watcher publish failed with exit code $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $watcherDirectory "GuguPet.LaunchWatcher.exe") -Destination $packageDirectory -Force

foreach ($name in @("LICENSE", "ASSET_NOTICE.md", "PRIVACY.md", "SECURITY.md", "UNINSTALL.md", "README.md", "README.en.md", "README.ja.md")) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $name) -Destination $packageDirectory -Force
}

$unexpectedDebugFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Include *.pdb)
if ($unexpectedDebugFiles.Count -gt 0) {
    throw "Debug symbols must not be published: $($unexpectedDebugFiles.FullName -join ', ')"
}

$requiredFiles = @("GuguPet.exe", "GuguPet.dll", "GuguPet.LaunchWatcher.exe")
foreach ($name in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $name))) {
        throw "Required release file is missing: $name"
    }
}

Compress-Archive -Path (Join-Path $packageDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
"$hash  $packageName.zip" | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "Release archive: $archivePath"
Write-Host "SHA-256: $hash"
