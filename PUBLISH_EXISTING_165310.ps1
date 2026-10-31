param([switch]$Upload)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = $here
# This helper is intended to be copied into the RealWalking-CS2 project root
# alongside RealWalking.csproj and Properties/PublishConfiguration.xml.
if (-not (Test-Path -LiteralPath (Join-Path $project 'RealWalking.csproj'))) {
  throw 'Place this script in the RealWalking-CS2 project root before using it.'
}
$metadata = Join-Path $project 'Properties\PublishConfiguration.xml'
if (-not (Test-Path -LiteralPath $metadata)) { throw 'Publish configuration missing.' }
[xml]$pub = Get-Content -LiteralPath $metadata -Raw
if ($pub.Publish.ModId.Value -ne '165310') { throw 'Wrong ModId: expected existing listing 165310. Aborting.' }
if ($pub.Publish.AccessLevel.Value -ne 'Unlisted') { throw 'This helper only uploads an Unlisted staging version. Aborting.' }
if ($pub.Publish.ModVersion.Value -ne '0.10.12-rc27') { throw 'Unexpected release version. Review version metadata before publishing.' }
if ($pub.Publish.GameVersion.Value -ne '1.6.*') { throw 'Game compatibility value differs from tested 1.6.* range.' }
if ($null -ne $pub.Publish.Dependency) { throw 'Unexpected external dependency in metadata.' }
if (Get-Process -Name Cities2 -ErrorAction SilentlyContinue) { throw 'Close Cities Skylines II before uploading.' }
$modsRoot = [Environment]::GetEnvironmentVariable('CSII_LOCALMODSPATH', 'User')
if ([string]::IsNullOrWhiteSpace($modsRoot)) { $modsRoot = $env:CSII_LOCALMODSPATH }
if ([string]::IsNullOrWhiteSpace($modsRoot)) {
  $modsRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II\Mods'
}
$modDir = Join-Path $modsRoot 'CS2RealWalkingG1Bridge'
$dll = Join-Path $modDir 'CS2RealWalkingG1Bridge.dll'
$harmony = Join-Path $modDir '0Harmony.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw ('Local tested DLL missing: ' + $dll + '. Run Build.cmd first.') }
if (-not (Test-Path -LiteralPath $harmony -PathType Leaf)) { throw ('0Harmony.dll missing: ' + $harmony + '. Do NOT upload an incomplete mod.') }
$dllHash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
$stateFile = Join-Path $project 'Release-Build-State.json'
if (Test-Path -LiteralPath $stateFile) {
  $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
  if (-not [string]::IsNullOrWhiteSpace([string]$state.newHash) -and $state.newHash -ne $dllHash) {
    throw 'DLL SHA256 differs from the recorded Build.cmd result; aborting to avoid uploading an unexpected binary.'
  }
}
Write-Host '=== Real Walking Existing Listing Release Review ===' -ForegroundColor Cyan
Write-Host 'Listing: 165310 (EXISTING, NOT NEW)'
Write-Host 'Version: 0.10.12-rc27'
Write-Host 'Visibility: Unlisted'
Write-Host ('Local package: ' + $modDir)
Write-Host ('Game DLL SHA256: ' + $dllHash)
Get-ChildItem -LiteralPath $modDir -File | Select-Object Name, Length | Format-Table -AutoSize
if (-not $Upload) {
  Write-Host 'Preflight only: NOTHING was uploaded.' -ForegroundColor Yellow
  Write-Host 'After checking the tested package, run: .\PUBLISH_EXISTING_165310.ps1 -Upload'
  exit 0
}
$publisher = [Environment]::GetEnvironmentVariable('CSII_MODPUBLISHERPATH', 'User')
if ([string]::IsNullOrWhiteSpace($publisher)) { $publisher = $env:CSII_MODPUBLISHERPATH }
$publisher = ([string]$publisher).Trim().Trim('"')
if (-not (Test-Path -LiteralPath $publisher -PathType Leaf)) {
  throw 'CSII_MODPUBLISHERPATH is missing or invalid. Use the official toolchain publishing profile or check the ModPublisher installation.'
}
Write-Host 'WARNING: This action WILL upload a new version to the existing Paradox Mods listing 165310.' -ForegroundColor Yellow
if ((Read-Host 'Type exactly: UPLOAD 165310') -cne 'UPLOAD 165310') {
  Write-Host 'Cancelled. No upload started.'
  exit 0
}
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
$arguments = @('NewVersion', $metadata, '-c', $modDir, '-v')
if ($publisher.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)) {
  & dotnet $publisher @arguments
} else {
  & $publisher @arguments
}
$code = $LASTEXITCODE
if ($code -ne 0) { throw ('Official ModPublisher failed, exit code: ' + $code) }
Write-Host 'Publisher returned success. Verify Mod ID, version, packaged files, and Unlisted visibility on Paradox Mods.' -ForegroundColor Green
