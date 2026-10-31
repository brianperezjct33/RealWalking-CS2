param([string]$GameRoot='')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
if (Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue) { throw 'Close Cities Skylines II before building: official toolchain auto-deploys local mod.' }
& (Join-Path $PSScriptRoot 'Check-Environment.ps1') -GameRoot $GameRoot
Write-Host 'WARNING: The official toolchain will deploy the built mod to the local Mods folder.' -ForegroundColor Yellow
if ((Read-Host 'Type BUILD to back up the installed local mod and compile') -cne 'BUILD') { Write-Host 'Cancelled; nothing changed.'; exit 0 }
$localRoot=[Environment]::GetEnvironmentVariable('CSII_LOCALMODSPATH','User')
if ([string]::IsNullOrWhiteSpace($localRoot)) { $localRoot=$env:CSII_LOCALMODSPATH }
if ([string]::IsNullOrWhiteSpace($localRoot)) { $localRoot=Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II\Mods' }
$modDir=Join-Path $localRoot 'CS2RealWalkingG1Bridge'
$hadOriginal=Test-Path -LiteralPath $modDir -PathType Container
$backup=''
if ($hadOriginal) {
    $backup=Join-Path $PSScriptRoot ('backups\PreviousLocalMod_'+(Get-Date -Format 'yyyyMMdd_HHmmss'))
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Get-ChildItem -LiteralPath $modDir -Force | Copy-Item -Destination $backup -Recurse -Force -ErrorAction Stop
    Write-Host ('BACKED UP existing local mod to '+$backup)
}
$state=[pscustomobject]@{target=$modDir;backup=$backup;hadOriginal=$hadOriginal;originalHash='';newHash=''}
if ($hadOriginal) {
    $dll=Join-Path $modDir 'CS2RealWalkingG1Bridge.dll'
    if(Test-Path -LiteralPath $dll) { $state.originalHash=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash }
}
$state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Release-Build-State.json') -Encoding UTF8
$env:DOTNET_CLI_UI_LANGUAGE='en'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_ROLL_FORWARD='LatestMajor'
Write-Host 'STANDALONE S1 LOCAL BUILD; no Paradox Mods upload will run.' -ForegroundColor Yellow
Write-Host 'Build using the exact official toolchain project...' -ForegroundColor Cyan
$ErrorActionPreference='Continue'
& dotnet build '.\RealWalking.csproj' -c Release --nologo 2>&1 | Tee-Object -FilePath '.\BuildLog-Release.txt'
$code=$LASTEXITCODE
$ErrorActionPreference='Stop'
if($code -ne 0) { throw 'BUILD FAILED. Upload BuildLog-Release.txt and do not publish. Use Restore.cmd if the deployed mod was changed.' }
$out=Join-Path $modDir 'CS2RealWalkingG1Bridge.dll'
if(-not (Test-Path -LiteralPath $out -PathType Leaf)) { throw ('Build exited 0 but deployed DLL not found: '+$out+' . Upload BuildLog-Release.txt.') }
# Harmony is a library dependency of the embedded camera, not a separate FPC mod.
# Some official toolchain versions do not automatically deploy NuGet DLLs.
$harmonyBuild=Join-Path $PSScriptRoot 'bin\Release\net48\0Harmony.dll'
$harmonyDeploy=Join-Path $modDir '0Harmony.dll'
if (Test-Path -LiteralPath $harmonyBuild) {
    Copy-Item -LiteralPath $harmonyBuild -Destination $harmonyDeploy -Force
    Write-Host 'INFO: copied bundled 0Harmony.dll from build output to local mod folder'
} elseif (-not (Test-Path -LiteralPath $harmonyDeploy)) {
    Write-Warning 'Harmony assembly not found alongside local mod. Runtime may fail; inspect BuildLog-Release.txt and do not publish.'
}

$state.newHash=(Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash
$state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Release-Build-State.json') -Encoding UTF8
Write-Host ('PASS: official build and local deploy. DLL SHA256: '+$state.newHash) -ForegroundColor Green
Write-Host 'NOT PUBLISHED. Launch the game and test this build before any upload.' -ForegroundColor Yellow
