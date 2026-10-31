# Localization

Real Walking's UI strings are in `RWLocalization/l10n/`; embedded-camera translations are in `FPCSource/l10n/`.

Supported Real Walking locales: `en-US`, `de-DE`, `es-ES`, `fr-FR`, `it-IT`, `ja-JP`, `ko-KR`, `pl-PL`, `pt-BR`, `ru-RU`, `zh-HANS`, `zh-HANT`. English is the fallback.

Each locale is a UTF-8, two-column CSV of `key,value` pairs. Keep the existing keys and positional placeholders (such as `{0}`) unchanged. Stop and line names may come from the game's own localization.

After editing, run `python tools/check_rw_localization.py`, then check the text in game, including long station names.
