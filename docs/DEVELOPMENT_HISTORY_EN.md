# Real Walking — Development and Debugging History

## 1. Walking System: Early Research to G1.9

| Version / Stage | Development and Debugging Notes |
| --- | --- |
| **v0.2.0–v0.2.5** | Investigated the game's Net, Lane, and ECS APIs. Early builds encountered assembly-reference and API naming issues. By v0.2.5, the mod could read actual `PedestrianLane` and `Curve` data in-game. |
| **v0.2.6–v0.2.7** | Added scans for nearby pedestrian paths, curve slopes, and sample positions, then switched to manually triggered scans. Pedestrian curves were detected, but the target ramp could not be located during testing. |
| **G0.1–G0.2** | Experimented with compiling FPC code separately and fixed `GetValueOrDefault` compatibility errors. G0.2 received a successful-build report. |
| **G1–G1.1** | Read the first-person camera position from FPC and searched for nearby pedestrian lanes. Searching only nearby lanes proved unreliable for identifying the sidewalk under the camera. |
| **G1.3 (v0.3.4–v0.3.7)** | Added `SubLane` lookup through road Net owners and read `Navigation Area` triangles. Improved elevation-layer checks and scan logging. The final test recorded 39 completed scans. |
| **G1.4** | Visualized pedestrian Net paths and Navigation Area triangles in first person, making it easier to inspect overpasses, subway stairs, and navigation surfaces at different elevations. In-game visualization was confirmed after fixing Unity ECS compilation errors. |
| **G1.5** | First implemented camera movement that automatically follows walking-surface height through FPC's `HeightOffset`. Introduced Navigation, Terrain, and Free modes. Crossing an overpass was confirmed, while path width, quay elevation, and performance still needed work. |
| **G1.5 Fix1** | Separated debug visualization limits from the actual navigation candidate cache so drawing limits would not restrict walking candidates. Hid the small debug radar by default. |
| **G1.6** | Added walking-boundary constraints and safe-position recovery to prevent the camera from automatically switching to free flight when leaving a valid path. Tests showed that the constraints were too strict and diagonal movement along an edge could get stuck. |
| **G1.7** | Added sliding along path boundaries, introduced a minimum width rule for ordinary sidewalks, widened selected pedestrian paths, and cached nearby path data. Stuttering improved, but the bottom of some subway stairs still did not connect correctly. |
| **G1.8** | Increased the width multiplier for wide pedestrian roads to 2.25 and tightened platform boundaries near railway tracks. Tests revealed that some bottom-end Navigation Area triangles were suspended above the ground; added height smoothing also caused camera lag when descending stairs quickly. |
| **G1.9 (v0.9.0)** | Added a constrained virtual ramp-foot connection at the base of suspended stair surfaces; removed final height smoothing and vertical speed limits; improved quay-height compensation. **In-game tests passed for three key cases: the suspended stair seam, rapid stair traversal, and quay walking.** |
| **G1.9.1 Performance Probe / Early RC3** | Added frame-interval and ECS wait-time measurements to investigate stuttering in Navigation mode at 3× simulation speed. One valid recording measured an average navigation-controller time of approximately 0.165 ms and a peak ECS wait of approximately 0.008 ms. Long frame intervals were observed, but their cause was not determined. The working walking algorithm was left unchanged. |

## 2. Traffic Interaction Experiments and Architecture Shift

| Stage | Development and Debugging Notes |
| --- | --- |
| **G2.0** | Investigated ECS structures including `HumanCurrentLane`, `LaneObject`, and `LaneOverlap`, and confirmed that the game's native crosswalk flags could be identified. |
| **Early RC4–RC4.1** | Built a read-only traffic sampling tool covering regular sidewalks, signalized and unsignalized crosswalks, and roads at different elevations. Fixed an ECS `Time` API compilation error; collected 80 snapshots and 413 creature-entity observations. |
| **G2.1 / Early RC5** | Created a temporary invisible pedestrian proxy and attempted to register it on crosswalks to trigger vehicle yielding. Logs recorded six proxy creations and six cleanups. Vehicles appeared to slow down, but still passed through the player; reliable stopping was not achieved. |
| **G2 Branch Discontinued** | Stopped work on invisible pedestrian proxies and vehicle yielding. The experimental ECS proxy was excluded from later standalone builds, and development shifted toward first-person public transport riding. |
| **Early RC6–RC6.1** | Added initial **E-key boarding/disembarking** interactions using external FPC vehicle following. Buses and ships were rideable in tests, while multi-car trains and subways still had vehicle-selection and camera-position problems. |
| **Early RC7–RC7.1** | Continued work on train-front identification, cab camera positioning, and safe disembarking. These issues were not fully solved, leading to the decision to embed FPC in a standalone architecture. |
| **Official Project Migration** | Moved the walking system into the game's official C# mod project. Fixed duplicate declarations generated by Unity ECS for system classes missing `partial`. Both compilation and in-game operation subsequently received successful reports. |

## 3. Standalone Integration: S1 Series

| Version | Development and Debugging Notes |
| --- | --- |
| **S1** | Began integrating the required FPC camera functionality into Real Walking to eliminate the external FPC dependency. The initial integrated build failed to compile. |
| **S1.1** | Fixed `Dictionary.GetValueOrDefault` compatibility errors when targeting .NET Framework. |
| **S1.2** | Addressed a possible `NullReferenceException` when the camera controller was not yet initialized, adding a safe early exit and deferred recovery. |
| **S1.3** | Slimmed down the embedded camera code by removing unnecessary picture-in-picture, random following, and legacy large UI features, while retaining functionality needed for walking and transport. |
| **S1.4** | Reimplemented a lightweight public transport stop-strip UI. In-game testing confirmed it worked, but stop names and display size still needed improvement. |
| **S1.5–S1.5.1** | Added actual stop-name lookup, train cab positioning, and initial railway-platform disembarking logic. Fixed compilation errors caused by missing namespace imports in `RWStopNames.cs`. |
| **S1.6** | Added front/rear car orientation checks to address occasional reversed train direction; adjusted the route strip and platform validation. |
| **S1.7–S1.7.1** | Fixed discontinuities in the stop-strip drawing and adjusted underground station candidate detection. Also resolved compilation errors involving `Game.Pathfind.Position` and IMGUI event types. |
| **S1.7.2** | For subway stations without valid stop-position markers, searched for same-level walkable surfaces near the stopped train. **In-game feedback confirmed that subway disembarking worked.** |
| **S1.7.3** | Extended the subway geometry-based landing search to railway disembarking. |
| **S1.7.4** | Added dedicated landing searches near docks for ferries and large passenger ships. |
| **S1.7.5** | Testing showed that ship disembarking could select pedestrian paths close to water level. Strengthened checks for sufficient clearance above water and valid walking geometry. |
| **S1.7.6** | Addressed residual building-selection highlights when entering walking mode and removed the stop-strip label “站序示意” (stop-order diagram). Subsequent Windows build problems were handled by adding missing imports and adjusting UI calls. |

## 4. Standalone Release Candidates: RC1–RC27

The RC numbering below restarts with the standalone release-candidate series. It is separate from the earlier traffic-experiment RC versions described in Section 2.

| Version | Development and Debugging Notes |
| --- | --- |
| **RC1** | Checked potential conflicts when standalone Real Walking and external FPC were loaded together; adjusted standalone localization loading. |
| **RC2** | Stopped identifying transport vehicles by Chinese display names; introduced multilingual strings, English fallback, and localization checks. |
| **RC3** | Attempted to fix the camera returning to its original location after exiting first person. The handoff was revisited in RC6. |
| **RC4** | Removed automatic disembarking triggered by WASD and clarified E-key disembarking and free-flight controls. |
| **RC5 / Hotfix** | Required vehicles to be fully stopped before boarding, with a second check when E was pressed; subsequently fixed an `InterpolatedTransform` compilation compatibility issue. |
| **RC6** | Fixed a bug where exiting first person could send the camera far away, handing off from the current viewpoint and clearing vehicle-follow state. |
| **RC7** | Adjusted stop-marker caching for ship disembarking so geometry scans and the actual E-key action used consistent markers. |
| **RC8** | Expanded boarding candidate and dock landing search ranges specifically for large passenger ships. |
| **RC9** | Addressed flickering boarding prompts caused by ships bobbing vertically by using horizontal movement and orientation to decide whether large ships were stationary. |
| **RC10** | Identified conflicting height conditions for disembarking at one elevated passenger terminal and adjusted dock height references and permitted vertical range. |
| **RC11** | Cleared stale camera and vehicle-follow references that could cause the camera to follow a previous ship when re-entering first person after disembarking. |
| **RC12** | Extended RC9's ship stop detection and prompt anti-flicker behavior to ferries. |
| **RC13** | Calculated camera eye height directly from walkable-surface world coordinates, avoiding terrain or sea-level interference on stairs, elevated paths, and docks. |
| **RC14** | Experimented with the pre-transition camera state and raycasts to select a landing point when entering walking with Alt+F. |
| **RC15** | Replaced the inconvenient raycast-based selection with searches near the screen target and strengthened waterfront candidate filtering. |
| **RC16** | Fixed over-widening of some pedestrian paths by limiting the double-width special case to the vanilla `Pedestrian Street`. |
| **RC17** | Adjusted the initial walking-entry scan range to avoid prematurely excluding valid landing candidates. |
| **RC18** | Replaced screen-aimed landing selection with the **nearest safe walkable point in 3D to the camera position before entry**. This approach passed in-game testing. |
| **RC19** | Made high-volume diagnostic logging opt-in, reducing output during normal play without changing walking or transit algorithms. |
| **RC20** | Fixed truncation of long English stop and route names by adjusting stop-strip text layout and display behavior. |
| **RC21** | Simplified modes and shortcuts: J switches between Navigation and Free, V controls debug overlays, and the legacy Terrain mode and redundant hotkeys were removed. |
| **RC22** | Fixed free-flight height anomalies across changes in coastlines and terrain; required buses and trams to be stationary with a safe landing point before disembarking. |
| **RC23** | Split transit scanning, vehicle following, disembark landing, UI, and navigation visualization into separate source files. |
| **RC24** | Further separated walking control, Net/Area geometry queries, railway platform landing, and dock landing logic while preserving core algorithms and thresholds. |
| **RC25 / BuildFix1** | Reorganized the `src/` directory, split initialization logic, and removed unused helper code. This introduced `CS0246` because a deleted file left behind an invalid `using` directive; a build fix then removed the stale import. |
| **RC26** | Removed retired experimental probes, old performance statistics, and logging utilities while retaining useful debug UI and error reporting. |
| **RC27** | Completed cleanup of unused helper methods, old selection/random-follow interfaces, and obsolete localization keys while preserving walking, boarding, and disembarking behavior. |
