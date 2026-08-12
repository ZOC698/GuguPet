[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "docs\media"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$captureBuild = Join-Path $repositoryRoot "artifacts\demo-capture-build"
$previewPath = Join-Path $outputRoot "gugupet-for-codex-preview.png"
$videoPath = Join-Path $outputRoot "gugupet-for-codex-demo.mp4"
$gifPath = Join-Path $outputRoot "gugupet-for-codex-demo.gif"

if (-not $outputRoot.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the repository: $outputRoot"
}

$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
New-Item -ItemType Directory -Force -Path $outputRoot, $captureBuild | Out-Null

dotnet publish (Join-Path $repositoryRoot "GuguPet.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -o $captureBuild

if ($LASTEXITCODE -ne 0) { throw "Demo capture build failed with exit code $LASTEXITCODE" }

$demo = Start-Process -FilePath (Join-Path $captureBuild "GuguPet.exe") -ArgumentList "--demo-capture" -PassThru
try {
    Start-Sleep -Milliseconds 800
    & $ffmpeg.Source -y -f gdigrab -framerate 30 -i 'title=GuguPet for Codex' `
        -t 18.2 -vf 'scale=640:-2:flags=lanczos,format=yuv420p' -an `
        -c:v libx264 -preset medium -crf 20 -movflags '+faststart' $videoPath
    if ($LASTEXITCODE -ne 0) { throw "Demo recording failed with exit code $LASTEXITCODE" }
}
finally {
    if (-not $demo.HasExited) { Stop-Process -Id $demo.Id -Force }
}

$trimmedPath = Join-Path $repositoryRoot "artifacts\gugupet-for-codex-demo-trimmed.mp4"
& $ffmpeg.Source -y -ss 1.7 -i $videoPath -vf 'format=yuv420p' -an `
    -c:v libx264 -preset medium -crf 20 -movflags '+faststart' $trimmedPath
if ($LASTEXITCODE -ne 0) { throw "Demo trim failed with exit code $LASTEXITCODE" }
Move-Item -LiteralPath $trimmedPath -Destination $videoPath -Force

& $ffmpeg.Source -y -i $videoPath -filter_complex `
    'fps=12,scale=480:-2:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=sierra2_4a' `
    $gifPath
if ($LASTEXITCODE -ne 0) { throw "Demo GIF generation failed with exit code $LASTEXITCODE" }

& $ffmpeg.Source -y -ss 0.8 -i $videoPath -frames:v 1 -update 1 $previewPath
if ($LASTEXITCODE -ne 0) { throw "Demo preview generation failed with exit code $LASTEXITCODE" }

Write-Host "Gugu-only demo: $videoPath"
Write-Host "README preview: $previewPath"
Write-Host "Optional GIF: $gifPath"
