# Real Walking — Standalone

**Explore the city you built — on foot.** Real Walking adds path-aware first-person walking, stairs and station exploration, public transport rides, and free flight to *Cities: Skylines II*.

![Real Walking cover illustration](Properties/Thumbnail.png)

*Cover illustration generated with GPT.*

## Features

- **Walk the city:** follow pedestrian roads, bridges, stairs, station platforms and waterfront paths using the game's pedestrian Net and Navigation Area geometry.
- **Easy entry:** press **Alt+F** to enter at the closest suitable walkable position to the current camera in 3D space.
- **Stay on the path:** the controller tracks walking height, stops at detected path boundaries and slides along them when moving diagonally.
- **Ride public transport:** board stopped buses, trams, metros, passenger trains, ferries and passenger ships with **E**. Leave them at a suitable stop with a valid walking surface.
- **Two modes:** switch between Navigation walking and free flight, with independent absolute flight altitude.
- **Route display:** see line and stop information while riding. The interface supports 12 languages.

## Controls

| Key | Action |
| --- | --- |
| **Alt+F** | Enter / leave first-person view |
| **WASD / Mouse** | Move / look |
| **Shift** | Walk faster |
| **E** | Board or leave a supported vehicle when allowed |
| **Ctrl+Shift+J** | Switch Navigation / Free; release vehicle follow into Free |
| **R / F** | Raise / lower altitude in Free mode |
| **Ctrl+Shift+V** | Show / hide navigation overlays |
| **Esc** | Exit first-person view |

## Installation

Subscribe to **Real Walking — Standalone** on Paradox Mods and enable it in your playset. **Do not enable a separate First Person Camera Continued mod or another camera edition alongside it:** the camera is already included.

Tested with *Cities: Skylines II* **1.6.x** on Windows. Some custom roads, buildings and stations lack suitable navigation geometry, so certain paths and exits may be unavailable. The mod uses camera control and game navigation data rather than changing saved city entities; save corruption risk is expected to be low. Back up valuable saves as you would with any code mod.

## How it works

Real Walking reads two kinds of game geometry: pedestrian **Net** paths for road corridors and **Navigation Area** triangles for spaces such as stairs and platforms. It selects a surface near the camera, constrains horizontal movement to the walkable area, and calculates eye height from that surface's world elevation. Bridges and underground levels are handled separately to prevent unwanted height switching.

For public transport, the controller reads vehicle position and motion, uses the embedded camera's follow system, and finds a suitable nearby walking surface when leaving a vehicle. It does not create a simulated player citizen or modify traffic AI.

Read the [technical architecture](docs/ARCHITECTURE.md), [code map](docs/SOURCE_MAP.md), and [development history](docs/DEVELOPMENT_HISTORY.md).

## Source and development

The source is organized in `src/` (Real Walking), `FPCSource/` (adapted embedded camera), `RWLocalization/`, and `UPSTREAM/` (license notices). See [building](BUILDING.md), [contributing](CONTRIBUTING.md), and [localization](LOCALIZATION_GUIDE.md).

**Development transparency:** The project used substantial AI assistance, including C# code generation. The maintainer directed the design, debugging and in-game tests. The [development log](docs/DEVELOPMENT_HISTORY.md) records each iteration.

**License:** [GPL-2.0-only](LICENSE) for the combined distribution. The embedded camera is adapted from [First Person Camera Continued](https://github.com/Cgameworld/FirstPersonCameraContinued). See [credits and license details](CREDITS_AND_LICENSE.md). Community project; not affiliated with Colossal Order or Paradox Interactive.
