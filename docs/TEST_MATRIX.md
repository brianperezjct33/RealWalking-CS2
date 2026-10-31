# In-game test scenarios

| Scenario | Check |
| --- | --- |
| Alt+F above street, bridge, stairs and quay | Nearest walkable entry; no wrong-height or water landing |
| Walking across a road edge | Boundary stop and sideways slide |
| Walking up/down stairs | Surface height follows correctly |
| Vanilla Pedestrian Street | Walking-width adjustment remains correct |
| Navigation / Free over hills and water | Free altitude stays constant until R/F input |
| Bus/tram | Boarding when stopped; exit only at a safe stationary position |
| Metro and passenger train | Vehicle following and platform exit |
| Ferry / passenger ship | Stable prompt and suitable dock landing |
| Alt+F / Esc while riding | No unexpected old vehicle follow |
| Stop display and other UI locales | Names display correctly |

Windows build instructions: [BUILDING.md](../BUILDING.md).
