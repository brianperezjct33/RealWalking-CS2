# Contributing

Issues, bug reports, translations and pull requests are welcome.

- Start with the [architecture](docs/ARCHITECTURE.md) and [code map](docs/SOURCE_MAP.md).
- Put Real Walking changes in `src/`, and preserve upstream license notices when touching `FPCSource/`.
- Keep navigation and transit queries read-only. Test changes to heights, path boundaries, vehicle following and safe exits in game.
- Run the checks listed in [BUILDING.md](BUILDING.md).
- Include the game version, reproduction steps and relevant logs in bug reports. Exclude personal paths and save files.

The combined project is [GPL-2.0-only](LICENSE). Do not commit proprietary game DLLs.
