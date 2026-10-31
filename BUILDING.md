# Build on Windows

Requires *Cities: Skylines II*, the official CSII modding toolchain, .NET, and a user-level `CSII_TOOLPATH` pointing to the folder containing `Mod.props` and `Mod.targets`.

1. Close the game. Run **`Build.cmd`** and type `BUILD`. The script checks the environment, backs up the currently installed local Real Walking build, compiles with the official toolchain and deploys the new DLL to the local Mods folder. **It does not upload to Paradox Mods.**
2. Launch the game and test the new build. If you need to revert, close the game and run **`Restore.cmd`**, then type `RESTORE`.

The build log is saved to `BuildLog-Release.txt`. Local backups and build state are excluded from Git.

**Game installation path:** `Build.cmd` first checks `CSII_GAMEROOT`, a local-only saved choice, and common Steam installations (including Steam libraries). If it cannot find the game, it **asks you to paste the game installation folder**; the validated location is saved to `.realwalking-build.local.json` for subsequent builds. This private file is ignored by Git and should never be uploaded. 

To specify a folder manually without a prompt, open Command Prompt and run:

```bat
Build.cmd -GameRoot "D:\SteamLibrary\steamapps\common\Cities Skylines II"
```

The game folder contains `Cities2.exe`; do not enter `Cities2_Data\Managed` or the `Game.dll` file itself. A game update may require rebuilding against a newly tested `Game.dll` version.

To validate source structure, translations and documentation, run these commands with Python 3 (they are also run by GitHub Actions):

```shell
python tools/check_release_structure.py
python tools/check_rw_localization.py
python tools/check_documentation_links.py
```

See [the in-game test matrix](docs/TEST_MATRIX.md).
