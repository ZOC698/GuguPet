[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "artifacts-local-update",
    [string]$InstallDirectory = ""
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
[xml]$versionProps = Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Build.props")
$version = [string]$versionProps.Project.PropertyGroup.Version
$packageName = "GuguPet-Windows-x64-v$version"
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$installRoot = if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot "release\GuguPet-current"))
} else {
    [IO.Path]::GetFullPath($InstallDirectory)
}

$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "release"))
if (-not $installRoot.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    -not [IO.Path]::GetFileName($installRoot).StartsWith("GuguPet", [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($installRoot).Equals("GuguPet-rollback", [StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallDirectory must be a GuguPet application folder inside $releaseRoot"
}
if ((Test-Path -LiteralPath $installRoot) -and
    -not (Test-Path -LiteralPath (Join-Path $installRoot "GuguPet.exe"))) {
    throw "The existing local installation is not valid: $installRoot"
}

& (Join-Path $PSScriptRoot "build-release.ps1") `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -OutputDirectory $outputRoot
if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE" }

$packageDirectory = Join-Path $outputRoot $packageName
$archivePath = Join-Path $outputRoot "$packageName.zip"
$updaterPath = Join-Path $outputRoot "updater\GuguPet.Updater.exe"
foreach ($path in @($packageDirectory, $archivePath, $updaterPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Local update input is missing: $path" }
}

$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "GuguPet.CodexWatcher"
$runValue = (Get-ItemProperty -LiteralPath $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue).$runValueName
$watcherEnabled = $runValue -is [string] -and $runValue.Contains("GuguPet.LaunchWatcher.exe", [StringComparison]::OrdinalIgnoreCase)

$managedReleasePrefix = [IO.Path]::TrimEndingDirectorySeparator($releaseRoot) + [IO.Path]::DirectorySeparatorChar
$installedProcesses = @(Get-CimInstance Win32_Process | Where-Object {
    # Every GuguPet watcher uses one mutex, including legacy copies outside the
    # fixed launcher directory. Stop all exact-name watcher processes so an old
    # binary cannot retain ownership and silently reject the new watcher.
    $_.Name -eq "GuguPet.LaunchWatcher.exe" -or
    ($_.ExecutablePath -is [string] -and
     $_.ExecutablePath.StartsWith($managedReleasePrefix, [StringComparison]::OrdinalIgnoreCase) -and
     $_.Name -eq "GuguPet.exe")
})
$petWasRunning = @($installedProcesses | Where-Object Name -eq "GuguPet.exe").Count -gt 0

foreach ($process in $installedProcesses) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
}
foreach ($process in $installedProcesses) {
    Wait-Process -Id $process.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
}

$updateProcess = Start-Process -FilePath $updaterPath -ArgumentList @(
    "--package", "`"$archivePath`"",
    "--target", "`"$installRoot`"",
    "--no-restart"
) -WorkingDirectory $outputRoot -WindowStyle Hidden -Wait -PassThru
if ($updateProcess.ExitCode -ne 0) {
    throw "Local updater failed with exit code $($updateProcess.ExitCode)."
}

$sourceFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File)
$installedFiles = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File)
if ($sourceFiles.Count -ne $installedFiles.Count) {
    throw "Installed file count does not match the freshly built package."
}
foreach ($sourceFile in $sourceFiles) {
    $relative = [IO.Path]::GetRelativePath($packageDirectory, $sourceFile.FullName)
    $installedFile = Join-Path $installRoot $relative
    if (-not (Test-Path -LiteralPath $installedFile)) {
        throw "Installed file is missing: $relative"
    }
    $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
    $installedHash = (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash
    if ($sourceHash -ne $installedHash) {
        throw "Installed file failed verification: $relative"
    }
}

$watcherExecutable = Join-Path $installRoot "GuguPet.LaunchWatcher.exe"
$petExecutable = Join-Path $installRoot "GuguPet.exe"
$launcherRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "GuguPet\Launcher"
$stableWatcherExecutable = Join-Path $launcherRoot "GuguPet.LaunchWatcher.exe"
if ($watcherEnabled) {
    New-Item -ItemType Directory -Force -Path $launcherRoot | Out-Null
    Copy-Item -LiteralPath $watcherExecutable -Destination $stableWatcherExecutable -Force
    $sourceWatcherHash = (Get-FileHash -LiteralPath $watcherExecutable -Algorithm SHA256).Hash
    $stableWatcherHash = (Get-FileHash -LiteralPath $stableWatcherExecutable -Algorithm SHA256).Hash
    if ($sourceWatcherHash -ne $stableWatcherHash) {
        throw "Stable launch watcher failed verification."
    }

    Set-ItemProperty -LiteralPath $runKeyPath -Name $runValueName `
        -Value "`"$stableWatcherExecutable`" --pet `"$petExecutable`""
    Start-Process -FilePath $stableWatcherExecutable `
        -ArgumentList @("--pet", "`"$petExecutable`"") `
        -WorkingDirectory $launcherRoot -WindowStyle Hidden

    $watcherVerified = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $watcherVerified = @(Get-CimInstance Win32_Process | Where-Object {
            $_.Name -eq "GuguPet.LaunchWatcher.exe" -and
            $_.ExecutablePath -is [string] -and
            [IO.Path]::GetFullPath($_.ExecutablePath).Equals(
                [IO.Path]::GetFullPath($stableWatcherExecutable),
                [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
        if ($watcherVerified) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $watcherVerified) {
        throw "The stable launch watcher did not remain running."
    }
}
if ($petWasRunning) {
    Start-Process -FilePath $petExecutable -ArgumentList "--codex-startup" -WorkingDirectory $installRoot
}

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath = Join-Path $desktop "咕嘎桌宠.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $petExecutable
$shortcut.WorkingDirectory = $installRoot
$shortcut.IconLocation = "$petExecutable,0"
$shortcut.Save()

$watcherHash = (Get-FileHash -LiteralPath $watcherExecutable -Algorithm SHA256).Hash
$updaterHash = (Get-FileHash -LiteralPath (Join-Path $installRoot "GuguPet.Updater.exe") -Algorithm SHA256).Hash
Write-Host "Local GuguPet installation updated: $installRoot"
Write-Host "Rollback directory: $(Join-Path $releaseRoot 'GuguPet-rollback\previous')"
Write-Host "Verified files: $($sourceFiles.Count)"
Write-Host "Packaged launch watcher SHA-256: $watcherHash"
if ($watcherEnabled) { Write-Host "Stable launch watcher: $stableWatcherExecutable" }
Write-Host "Updater SHA-256: $updaterHash"
Write-Host "Desktop shortcut: $shortcutPath"
