#!/usr/bin/env python3
"""Check release-candidate structure and essential gameplay contracts.

No download or game dependency is required. This is not a C# compiler.
"""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors = []
def require(ok: bool, message: str):
    if not ok: errors.append(message)

def source(path: str) -> str:
    return (ROOT / path).read_text(encoding='utf-8-sig')

def combined(folder: str) -> str:
    return '\n'.join(f.read_text(encoding='utf-8-sig')
                     for f in sorted((ROOT / 'src' / folder).glob('*.cs')))

project = ET.parse(ROOT / 'RealWalking.csproj').getroot()
includes = [e.attrib['Include'].replace('\\', '/')
            for e in project.findall('.//Compile')]
explicit = {i for i in includes if '*' not in i}
actual = {p.relative_to(ROOT).as_posix() for p in (ROOT / 'src').rglob('*.cs')}
require(len(includes) == len(set(includes)), 'duplicate compilation items')
require(explicit == actual, 'all src C# files must be included exactly once in the project')
require('FPCSource/**/*.cs' in includes, 'bundled FPC wildcard missing')
require('using FirstPersonCamera.Helpers;' not in source('FPCSource/CameraInput.cs'),
        'deleted upstream FPC helper namespace still imported (CS0246)')

# Integration cleanup: removed random follow and empty selection callbacks must stay removed.
fpc = '\n'.join(p.read_text(encoding='utf-8-sig') for p in (ROOT / 'FPCSource').rglob('*.cs'))
for removed in ('RandomFollow', 'RandomMode', 'FilteredRandomTypes',
                'OnToggleSelectionMode', 'ToggleRaycasting'):
    require(removed not in fpc, f'legacy FPC API reintroduced: {removed}')
require(not (ROOT / 'FPCSource/Enums/RandomMode.cs').exists(), 'legacy random mode file returned')
require(not (ROOT / 'Properties/PublishProfiles/PublishNewMod.pubxml').exists(),
        'do not ship a publish profile that can create a duplicate listing')
# FPC settings resources should contain only actively registered settings keys.
import csv
active_camera_keys = {
    'Options.SECTION:[cgameworld.FirstPersonCameraContinued',
    'FirstPersonCameraContinued.ToastTextEnter',
    'Options.TAB:GeneralSettingsTab',
    'Options.GROUP:CameraSettingsGroup',
    'Options.GROUP:KeybindingSettingsGroup',
    'Options.OPTION:FOV',
    'Options.OPTION_DESCRIPTION:FOV',
    'Options.OPTION:MovementSpeed',
    'Options.OPTION_DESCRIPTION:MovementSpeed',
    'Options.OPTION:RunSpeed',
    'Options.OPTION_DESCRIPTION:RunSpeed',
    'Options.OPTION:CimHeight',
    'Options.OPTION_DESCRIPTION:CimHeight',
    'Options.OPTION:TransitionSpeedFactor',
    'Options.OPTION_DESCRIPTION:TransitionSpeedFactor',
    'Options.OPTION:FreeModeKeybind',
    'Options.OPTION_DESCRIPTION:FreeModeKeybind',
    'Options.BINDING:EnterFreeModeBinding',
}
for locale_file in sorted((ROOT / 'FPCSource/l10n').glob('*.csv')):
    with locale_file.open(encoding='utf-8-sig', newline='') as source_file:
        rows = list(csv.reader(source_file))
    found = [row[0] for row in rows if len(row) == 2]
    require(len(found) == len(rows) and len(found) == len(set(found)) and
            set(found) == active_camera_keys,
            f'active embedded FPC translations: {locale_file.name}')

require(len(list((ROOT / 'FPCSource').rglob('*.cs'))) > 20, 'embedded FPC camera incomplete')
require('CS2RealWalkingG1Bridge' in source('RealWalking.csproj'), 'assembly identity changed')
require('RW_AREA' in source('RealWalking.csproj'), 'navigation-area compile symbol missing')
require('RWLocalization\\l10n\\*.csv' in source('RealWalking.csproj'), 'RW language resources missing')
require('FPCSource\\l10n\\*.csv' in source('RealWalking.csproj'), 'FPC language resources missing')

meta = ET.parse(ROOT / 'Properties' / 'PublishConfiguration.xml').getroot()
def field(name: str):
    item = meta.find(name)
    return item.attrib.get('Value') if item is not None else None
require(field('ModId') == '165310', 'unexpected Paradox mod ID')
require(field('AccessLevel') == 'Unlisted', 'candidate must remain Unlisted')
require(field('ModVersion') == '0.10.12-rc27', 'candidate version mismatch')
require(meta.find('Dependency') is None, 'external camera dependency added')

entry = combined('Entry')
scan = combined('Navigation')
transit = combined('Transit')
keys = source('src/Bootstrap/RWHotkeys.cs')
manual = source('FPCSource/Transformer/FinalTransforms/ManualFinalTransform.cs')
require('CaptureOrbitCamera' in entry and 'TryPickNearest' in entry and 'ClosestTriangle3D' in entry,
        'tested camera-nearest 3D landing missing')
require('InitialScanRadius = 110f' in entry and 'ExpandedScanRadius = 240f' in entry,
        'bounded entry scan missing')
require('water.IsSafeEntry' in entry, 'water safety filter missing')
require('PedestrianStreetMultiplier=2.0f' in scan and 'IsVanillaPedestrianStreetName' in scan,
        'Pedestrian Street-only widening missing')
require('RWCameraHeight.Publish(targetY)' in scan, 'navigation direct eye height missing')
require('RWCameraHeight.TryGet(out float eyeY)' in manual and 'manualHeightDelta' in manual and
        'WaterUtils.SampleHeight' not in manual and 'TerrainUtils.SampleHeight' not in manual,
        'absolute-world-height camera contract broken')
require('WaterPlanarOnly' in transit and 'RoadExitNeedsStop' in transit and
        'MinObservedStop = 0.60f' in transit and 'CanShowExitPrompt' in transit,
        'transit stopped-state safeguards missing')
require('RWOverlayState.SetUnifiedDebug(enabled)' in keys and 'keyboard.jKey' in keys and
        'keyboard.vKey' in keys and 'keyboard.eKey' in keys,
        'unified controls missing')
require('RWTravelMode.Terrain' not in scan and 'RWTravelMode.Terrain' not in transit,
        'removed Terrain mode returned')
require('Short(' not in source('src/Routes/RWStopStripHud.cs'), 'station names are truncated')
require((ROOT / 'LICENSE').is_file() and (ROOT / 'UPSTREAM/FPC_LICENSE').is_file(),
        'upstream licensing files missing')
require((ROOT / 'CREDITS_AND_LICENSE.md').is_file(), 'credit document missing')
require((ROOT / 'Properties/Thumbnail.png').is_file(), 'publishing thumbnail missing')
require(not list(ROOT.glob('RW*.cs')) and not (ROOT / 'Mod.cs').exists(),
        'old duplicate root source detected')
# Prevent the cleaned project from accumulating obsolete diagnostics again.
for legacy in ('03-Collect-Performance.cmd', '03-Collect-Performance.ps1',
               '04-Collect-Transit.cmd', '04-Collect-Transit.ps1',
               'src/Debug/RWTransitRailAudit.cs', 'src/Debug/RWWalkingDiagnostics.cs',
               'tools/rc24_parity.json', 'tools/check_source_parity.py'):
    require(not (ROOT / legacy).exists(), f'obsolete diagnostic artifact reintroduced: {legacy}')
require((ROOT / 'docs/archive/development-history.zip').is_file(), 'single historical archive missing')
require('RWTransitRailAudit' not in keys + combined('Transit'), 'rail audit runtime still referenced')
if errors:
    for error in errors: print('FAIL:', error)
    sys.exit(1)
print(f'PASS: {len(actual)} Real Walking compilation files, embedded FPC, metadata and gameplay guards')
print('NOTE: source-only checks; Windows C# build / game regression / artwork rights not verified')
