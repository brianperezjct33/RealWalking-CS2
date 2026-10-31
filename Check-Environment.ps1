param([string]$GameRoot='')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
function Check($ok, $what) {
  if (-not $ok) { throw ('CHECK FAILED: '+$what) }
  Write-Host ('PASS: '+$what) -ForegroundColor Green
}
Write-Host '=== Real Walking - Windows build environment ===' -ForegroundColor Cyan
$tool=[Environment]::GetEnvironmentVariable('CSII_TOOLPATH','User')
if ([string]::IsNullOrWhiteSpace($tool)) { $tool=$env:CSII_TOOLPATH }
Check (-not [string]::IsNullOrWhiteSpace($tool)) 'CSII_TOOLPATH is set'
Check (Test-Path -LiteralPath (Join-Path $tool 'Mod.props') -PathType Leaf) 'Official Mod.props exists'
Check (Test-Path -LiteralPath (Join-Path $tool 'Mod.targets') -PathType Leaf) 'Official Mod.targets exists'
Check ($null -ne (Get-Command dotnet -ErrorAction SilentlyContinue)) 'dotnet CLI installed'
# Resolve game location without embedding any developer-specific path.
# Priority: explicit argument, environment variable, local-only cache, Steam defaults,
# and finally an interactive prompt. This cache is excluded from Git by .gitignore.
function Normalize-GameRoot([string]$Path) {
  if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
  return $Path.Trim().Trim('"').Trim()
}
function Test-GameRoot([string]$Path) {
  if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
  return Test-Path -LiteralPath (Join-Path $Path 'Cities2_Data\Managed\Game.dll') -PathType Leaf -ErrorAction SilentlyContinue
}
$cacheFile = Join-Path $PSScriptRoot '.realwalking-build.local.json'
$resolvedRoot = ''
if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
  $resolvedRoot = Normalize-GameRoot $GameRoot
  if (-not (Test-GameRoot $resolvedRoot)) {
    throw ('Game.dll not found under the specified game folder: ' + $resolvedRoot)
  }
} else {
  $candidates = @(
    $env:CSII_GAMEROOT,
    [Environment]::GetEnvironmentVariable('CSII_GAMEROOT', 'User')
  )
  if (Test-Path -LiteralPath $cacheFile -PathType Leaf) {
    try {
      $saved = Get-Content -LiteralPath $cacheFile -Raw -ErrorAction Stop | ConvertFrom-Json
      $candidates += [string]$saved.gameRoot
    } catch {
      Write-Warning 'Local game-path cache could not be read; asking for a path if needed.'
    }
  }
  # Standard Steam locations (including the current user's Steam installation).
  $steamRoots = @()
  $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
  if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
    $steamRoots += Join-Path $programFilesX86 'Steam'
  }
  try {
    $steamInstall = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    if (-not [string]::IsNullOrWhiteSpace($steamInstall)) { $steamRoots += $steamInstall }
  } catch { }
  foreach ($steamRoot in ($steamRoots | Select-Object -Unique)) {
    $candidates += Join-Path $steamRoot 'steamapps\common\Cities Skylines II'
    $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $libraryFile -PathType Leaf) {
      try {
        $libraryText = Get-Content -LiteralPath $libraryFile -Raw
        foreach ($match in [regex]::Matches($libraryText, '"path"\s*"([^"]+)"')) {
          $libraryRoot = $match.Groups[1].Value.Replace('\\', '\')
          $candidates += Join-Path $libraryRoot 'steamapps\common\Cities Skylines II'
        }
      } catch { }
    }
  }
  foreach ($candidate in $candidates) {
    $candidate = Normalize-GameRoot $candidate
    if (Test-GameRoot $candidate) { $resolvedRoot = $candidate; break }
  }
  if ([string]::IsNullOrWhiteSpace($resolvedRoot)) {
    Write-Host 'Cities Skylines II was not found automatically.' -ForegroundColor Yellow
    Write-Host 'Open Steam > Cities: Skylines II > Properties > Installed Files > Browse.'
    Write-Host 'Copy the game folder path into this window (the folder containing Cities2.exe).'
    for ($attempt = 1; $attempt -le 3; $attempt++) {
      $entered = Normalize-GameRoot (Read-Host 'Game installation folder')
      if ([string]::IsNullOrWhiteSpace($entered)) { throw 'No path entered. Build cancelled without changing the local mod.' }
      if (Test-GameRoot $entered) { $resolvedRoot = $entered; break }
      Write-Warning ('Game.dll not found at: ' + (Join-Path $entered 'Cities2_Data\Managed\Game.dll'))
    }
    if ([string]::IsNullOrWhiteSpace($resolvedRoot)) {
      throw 'Could not find Game.dll after three attempts. Build cancelled without changing the local mod.'
    }
  }
}
$GameRoot = $resolvedRoot
# This setting stays in the local PowerShell process and is not committed.
$env:CSII_GAMEROOT = $GameRoot
Write-Host ('Game installation folder: ' + $GameRoot) -ForegroundColor Cyan
$gameDll=Join-Path $GameRoot 'Cities2_Data\Managed\Game.dll'
Check (Test-Path -LiteralPath $gameDll -PathType Leaf) ('Game.dll at '+$gameDll)
$hash=(Get-FileHash -LiteralPath $gameDll -Algorithm SHA256).Hash
Write-Host ('Game.dll SHA256: '+$hash)
Check ($hash -eq 'AAEE15C4FA41C130ABAA1183840667E4FAAE531D67E8A9FA7536618EFFA2F86A') 'Game.dll matches the tested API version'
foreach ($f in @('src\Bootstrap\Mod.cs','src\Navigation\RWOverlay.cs','src\Navigation\RWWalking.cs','src\Transit\RWTransitBoarding.cs','src\Routes\RWStopStrip.cs','src\Routes\RWStopNames.cs','FPCSource\Mod.cs','FPCSource\MonoBehaviours\FirstPersonCameraController.cs','FPCSource\EntityFollower.cs','UPSTREAM\FPC_LICENSE','RealWalking.csproj','Properties\PublishConfiguration.xml','Properties\Thumbnail.png')) {
  Check (Test-Path -LiteralPath (Join-Path $PSScriptRoot $f) -PathType Leaf) ('Project file: '+$f)
}
[xml]$pub=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Properties\PublishConfiguration.xml') -Raw
Check ($pub.Publish.ModVersion.Value -eq '0.10.12-rc27') 'Mod version 0.10.12-rc27'
Check ($pub.Publish.ModId.Value -eq '165310') 'Existing Unlisted Paradox ID = 165310'
Check ($pub.Publish.AccessLevel.Value -eq 'Unlisted') 'Upload visibility still Unlisted'
Check ($null -eq $pub.Publish.Dependency) 'No external FPC dependency in metadata'
# Remember this machine's installation folder for future builds. Never commit this file.
try {
  @{ gameRoot = $GameRoot } | ConvertTo-Json | Set-Content -LiteralPath $cacheFile -Encoding UTF8
} catch {
  Write-Warning 'Could not save the local game path; you may need to enter it next time.'
}
Write-Host 'PASS: environment and publish metadata checks' -ForegroundColor Green
