#!/usr/bin/env python3
"""Validate Real Walking UI localization resources. Uses Python standard library only.

Run from any cwd: python tools/check_rw_localization.py
"""
import csv
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
L10N = ROOT / 'RWLocalization' / 'l10n'
SUPPORTED = {'en-US', 'de-DE', 'es-ES', 'fr-FR', 'it-IT', 'ja-JP',
             'ko-KR', 'pl-PL', 'pt-BR', 'ru-RU', 'zh-HANS', 'zh-HANT'}
PLACEHOLDER = re.compile(r'(?<!\{)\{\d+\}(?!\})')
KEY_USAGE = re.compile(r'RWLocalization\.T\("([\w.]+)"')
errors = []
entries = {}

for locale in sorted(SUPPORTED):
    path = L10N / f'{locale}.csv'
    if not path.is_file():
        errors.append(f'MISSING: {locale}.csv')
        continue
    table = {}
    try:
        with path.open(encoding='utf-8-sig', newline='') as file:
            for line, row in enumerate(csv.reader(file), 1):
                if not row: continue
                if len(row) != 2:
                    errors.append(f'{locale}:{line}: expected exactly 2 CSV cells; got {len(row)}')
                    continue
                key, value = row
                if key in table: errors.append(f'{locale}:{line}: duplicate key: {key}')
                if not key or not value.strip(): errors.append(f'{locale}:{line}: blank key or translation')
                table[key] = value
    except (OSError, UnicodeError, csv.Error) as exc:
        errors.append(f'{locale}: {exc}')
    entries[locale] = table

reference = entries.get('en-US', {})
if not reference: errors.append('English fallback cannot be empty')
for locale, table in entries.items():
    missing = reference.keys() - table.keys()
    extra = table.keys() - reference.keys()
    if missing: errors.append(f'{locale}: missing keys: {sorted(missing)}')
    if extra: errors.append(f'{locale}: unexpected keys: {sorted(extra)}')
    for key in reference.keys() & table.keys():
        expected = sorted(PLACEHOLDER.findall(reference[key]))
        actual = sorted(PLACEHOLDER.findall(table[key]))
        if expected != actual:
            errors.append(f'{locale}:{key}: placeholders {actual}, expected {expected}')

# Check common static-key calls; dynamic calls are reviewed separately.
source = '\n'.join(path.read_text(encoding='utf-8-sig') for path in (ROOT / 'src').rglob('RW*.cs'))
used = set(KEY_USAGE.findall(source))
used.update('rw.vehicle.' + name for name in ('generic', 'bus', 'tram', 'train', 'subway', 'ferry', 'ship', 'airplane'))
used.update(('rw.transit.exit_no_platform','rw.transit.exit_no_quay','rw.transit.exit_no_path'))
missing = used - reference.keys()
if missing: errors.append(f'Source references missing from English: {sorted(missing)}')

csproj = (ROOT/'RealWalking.csproj').read_text(encoding='utf-8-sig')
if 'RWLocalization\\l10n\\*.csv' not in csproj or 'CS2RealWalkingG1Bridge.rw_l10n.' not in csproj:
    errors.append('RealWalking.csproj does not embed Real Walking UI CSV resources')
if '<Compile Include="src\\Localization\\RWLocalization.cs" />' not in csproj:
    errors.append('RWLocalization.cs is missing from compile items')

if errors:
    print('\n'.join('FAIL: ' + error for error in errors))
    sys.exit(1)
print(f'PASS: {len(entries)}/{len(SUPPORTED)} supported locales; '
      f'{len(reference)} keys per locale; {len(used)} source keys referenced; '
      'UTF-8, duplicates, placeholders, and project resource paths verified.')
