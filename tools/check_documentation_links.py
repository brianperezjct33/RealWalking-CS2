#!/usr/bin/env python3
"""Check relative Markdown links in the public entry-point docs (not archived notes)."""
from pathlib import Path
from urllib.parse import unquote
import re, sys

ROOT=Path(__file__).resolve().parents[1]
docs=[p for p in ROOT.glob('*.md')]+[p for p in (ROOT/'docs').glob('*.md')]
errors=[]
for page in docs:
    content=page.read_text(encoding='utf-8-sig')
    for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)',content):
        target=target.split(' ')[0].split('#')[0]
        if not target or ':' in target or target.startswith('//'):continue
        if not (page.parent / unquote(target)).exists():
            errors.append(f'{page.relative_to(ROOT)}: missing {target}')
if errors:
    for e in errors:print('FAIL:',e)
    sys.exit(1)
print(f'PASS: internal Markdown links in {len(docs)} active documentation files resolve')
