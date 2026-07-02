# setup-bepinex.ps1 - Install the vendored BepInEx (third_party/bepinex) into the
# Disco Elysium folder. Idempotent: safe to re-run (e.g. after a game update wipes it).
# After this, launch the game once through Steam (the first launch generates the
# BepInEx\interop proxy assemblies the build compiles against; it takes a few
# minutes and the game may look frozen), then run build.ps1 to deploy the mod.

$ErrorActionPreference = "Stop"

# --- Locate the game install ---
# DISCO_ELYSIUM_DIR env var wins; otherwise auto-detect from Steam library folders;
# otherwise fall back to the default location.
$Game = $env:DISCO_ELYSIUM_DIR
if (-not $Game) {
    $RegSteam = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -Name InstallPath -ErrorAction SilentlyContinue).InstallPath
    $DefaultSteam = if ($RegSteam) { $RegSteam } else { "C:\Program Files (x86)\Steam" }
    $SteamPaths = @()
    if (Test-Path "$DefaultSteam\steamapps") { $SteamPaths += $DefaultSteam }
    $LibFolders = "$DefaultSteam\steamapps\libraryfolders.vdf"
    if (Test-Path $LibFolders) {
        $content = Get-Content $LibFolders -Raw
        [regex]::Matches($content, '"path"\s+"([^"]+)"') | ForEach-Object {
            $p = $_.Groups[1].Value -replace '\\\\', '\'
            if ($p -ne $DefaultSteam -and (Test-Path "$p\steamapps")) { $SteamPaths += $p }
        }
    }
    foreach ($steam in $SteamPaths) {
        $candidate = "$steam\steamapps\common\Disco Elysium"
        if (Test-Path "$candidate\disco.exe") { $Game = $candidate; break }
    }
    if (-not $Game) { $Game = "C:\Program Files (x86)\Steam\steamapps\common\Disco Elysium" }
}
if (-not (Test-Path "$Game\disco.exe")) {
    Write-Host "ERROR: Disco Elysium not found at: $Game" -ForegroundColor Red
    Write-Host "Set the DISCO_ELYSIUM_DIR environment variable to the game folder." -ForegroundColor Red
    exit 1
}

$Zip = "$PSScriptRoot\third_party\bepinex\BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip"
if (-not (Test-Path $Zip)) {
    Write-Host "ERROR: vendored BepInEx not found at $Zip" -ForegroundColor Red
    exit 1
}

Write-Host "Installing BepInEx into $Game ..." -ForegroundColor Cyan

# The zip root is the game-folder layout (winhttp.dll, doorstop_config.ini, BepInEx\, dotnet\),
# so it extracts straight into the game folder.
Expand-Archive -Path $Zip -DestinationPath $Game -Force

Write-Host ""
Write-Host "BepInEx installed. Now launch the game once through Steam and wait for the" -ForegroundColor Cyan
Write-Host "main menu; the first launch generates the interop assemblies the build needs" -ForegroundColor Cyan
Write-Host "and can take a few minutes. Then quit and run build.ps1 to deploy the mod." -ForegroundColor Cyan
