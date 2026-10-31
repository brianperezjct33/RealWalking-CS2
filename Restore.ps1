$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
if (Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue) { throw 'Close Cities Skylines II first.' }
$path=Join-Path $PSScriptRoot 'Release-Build-State.json'
if (-not (Test-Path -LiteralPath $path)) { throw 'No recorded local build to restore.' }
$state=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
$modDir=[string]$state.target
if (-not $modDir.Contains('CS2RealWalkingG1Bridge')) { throw 'Unsafe target; refusing.' }
Write-Host ('Restore target: '+$modDir) -ForegroundColor Cyan
if ([bool]$state.hadOriginal) {
  if (-not (Test-Path -LiteralPath $state.backup -PathType Container)) { throw 'Backup missing: cannot restore.' }
  Write-Host ('Backup folder: '+$state.backup)
} else { Write-Host 'No prior local folder; restore will remove the new local mod.' }
$dll=Join-Path $modDir 'CS2RealWalkingG1Bridge.dll'
if ((Test-Path -LiteralPath $dll) -and (-not [string]::IsNullOrEmpty($state.newHash))) {
  $current=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
  if ($current -ne [string]$state.newHash) { throw 'Local DLL has changed since this build. Refusing automatic restore; review manually.' }
}
if ((Read-Host 'Type RESTORE to undo this local build') -cne 'RESTORE') { Write-Host 'Cancelled.'; exit 0 }
if(Test-Path -LiteralPath $modDir) { Remove-Item -LiteralPath $modDir -Recurse -Force }
if([bool]$state.hadOriginal) {
 New-Item -ItemType Directory -Force -Path $modDir | Out-Null
 Get-ChildItem -LiteralPath $state.backup -Force | Copy-Item -Destination $modDir -Recurse -Force
}
Write-Host 'RESTORE PASS. Restart game to pick up the previous local DLL.' -ForegroundColor Green
