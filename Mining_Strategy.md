# Mining automation strategy

## Terminology

Mining automation: the EVE mining workflow run by `Automaton.exe -miner`.
Mining cycle: one full loop from docked station state through undock, warp, mining, return, unload, and repeat.
Docked: station screen is visible and the `Undock` button can be detected.
Mining Hold: ship cargo hold used for ore. Empty hold means the pilot can undock; non-empty hold means unload first.
Item Hangar: station inventory destination for unloading ore.
Undocking: transitional state after clicking `Undock`.
Location change timer: the stable upper-left UI marker that confirms undock completion.
Overview: the EVE overview panel used to select belts, asteroids, and future navigation targets.
BELT tab: overview tab containing asteroid belt entries.
MINE tab: overview tab containing mineable asteroids after landing in a belt.
Asteroid belt row: one selectable asteroid belt entry in the BELT tab.
Asteroid row: one selectable asteroid entry in the MINE tab.
Recovery: a safe non-progress state used when expected screen evidence is missing.

## Main rules

- Keep mining automation separate from Project Discovery automation. Shared infrastructure belongs in small common services; workflow logic stays in mining states.
- Treat mining as a state machine. Each state should own one screen assumption and one narrow transition.
- Put all mining states and state contracts under `Automaton/MiningStates`.
- Capture before acting. State transitions should carry the capture path and detector result when possible so logs explain why the state moved.
- Prefer bounded, stable evidence over whole-screen guesses. Every detector should start from the smallest reliable ROI.
- Prefer keyboard shortcuts over visual button detection when the shortcut is known and safer. The `S` key replaced `warp_to_button` for belt warp.
- Wait 300 ms before clicking any mining UI element. Use `MiningAutomationContext.ClickUiElement` for mouse clicks so the delay stays centralized.
- On missing or ambiguous evidence, transition to `Recovery` instead of blind clicking.
- Keep state execution synchronous and cancellation-aware. Long waits must use `IAutomationInputController.Delay` so cancellation can interrupt them.
- Keep pending states explicit. A `PendingMiningAutomationState` is better than pretending an unfinished workflow step is implemented.
- Keep tests generated-fixture based. Real screenshots are useful for local smoke checks, but do not make permanent tests depend on `bin`, local captures, or user-specific folders.
- Follow `AGENTS.md`: Arrange/Act/Assert comments, behavior-style test names, simple code, no empty blank line at EOF.

## Current Implementation

- `ApplicationStartupOptions` selects Mining mode with `-miner` or `--miner`; default mode remains Project Discovery.
- `MainWindow` starts `MiningAutomationService` in Mining mode, changes the title to `Automaton - Miner`, and disables Project Discovery-only pilot/sample controls.
- `MiningAutomationService` owns the loop, startup delay, step delay, state factory, and state transition logging.
- `DockedState` captures `.mining-docked`, uses `DockedScreenDetector`, focuses Mining Hold if needed, sends `Undock` when the hold is empty, and transitions to `UnloadCargo` when ore is present.
- `UndockingState` waits 15 seconds, then polls once per second for 15 attempts for the resource-backed `location_change_timer` template in the fixed upper-left ROI.
- `EmptyOnUndockState` locates the Overview BELT tab with the resource-backed `overview_belt` template, chooses a random detected belt row, clicks it, then presses `S` to warp.
- `WarpingToAsteroidField`, `UnloadCargo`, and `Recovery` are currently pending states.
- `ScreenCaptureService` normalizes mining and discovery detectors to the left `2560x2160` game viewport at `(0,0)`. This is an intentional current constraint for the target setup.

## What Worked Well

- Extracting `AutomationInputController`, `IAutomationInputController`, `IAutomationClock`, and `SystemAutomationClock` before Mining reduced coupling and kept Project Discovery behavior intact.
- Renaming the app to `Automaton` while preserving `ProjectDiscovery...` domain names clarified the broader purpose without erasing the mini-game concept.
- A dedicated `MiningAutomationService` and `MiningStates` folder made the implementation easy to track and extend.
- Small, stable templates work well as resources when searched inside tight ROIs. This is true for `location_change_timer` and `overview_belt`.
- Color/shape detection is better for broad UI regions like the docked inventory and `Undock` button, where a template would be unnecessarily brittle.
- Generated mining fixtures are fast and durable enough for detector and state contracts.
- Real screenshot smoke checks are useful during calibration, then should be removed once synthetic characterization exists.
- Centralizing the 300 ms pre-click delay made later states safer without repeating timing code.

## Rejected Or Avoided

- Do not extend `ProjectDiscoveryAutomationService` with mining behavior. Its domain is sample analysis and polygon submission.
- Do not keep `warp_to_button` detection. Pressing `S` after selecting a belt is simpler and removes a fragile template dependency.
- Do not scan the full virtual desktop for mining UI. The current design assumes the game viewport is captured at `(0,0,2560,2160)`.
- Do not add broad multi-monitor support until the fixed viewport assumption stops being true in production.
- Do not use resource templates for every UI element by default. Use templates for tiny stable glyphs; use detector logic or shortcuts for dynamic panels.
- Do not write permanent tests against local runtime screenshots, downloaded images, or files under `bin`.
- Do not implement future states as a large monolithic mining method. The workflow will become too complex to review or recover safely.

## Known Pain Points

- Recovery is only a placeholder. The next real recovery design should distinguish retryable screen drift, bad overview tab state, lost focus, and hard-stop conditions.
- `UnloadCargo` is pending even though `DockedState` can identify ore in the Mining Hold.
- Pilot login and pilot selection for Mining are not wired yet. `PilotAvatarLocator` can be reused, but the Mining startup/login flow still needs its own state design.
- Warping completion is not implemented. The dead Mining thread stopped just as `LandedOnAsteroidBelt` was being explored.
- The landing signal should not depend on broad OCR. The promising first approach is a lower-center template or visual detector for the `ASTEROID BELT` label.
- The MINE overview state is not implemented. After landing, the automation needs to locate the MINE overview to the right of the belt label, select the first asteroid, and press `A`.
- Full cargo detection, mining laser activation, return-to-station, docking completion, and unloading are still open design areas.
- The fixed capture viewport is practical now but is a known environmental assumption.

## Recommended Next Curve

1. Add `WarpingToAsteroidFieldState` or `LandedOnAsteroidBeltState` as the next narrow slice.
2. Detect the lower-center `ASTEROID BELT` label in a bounded ROI. Start with a resource-backed template only if the cropped label is stable across captures.
3. Once the label is found, locate the MINE overview relative to the label/right-side panel, select the first asteroid row, and press `A`.
4. Add generated fixtures for the landed belt screen and MINE overview, plus one local real-screenshot smoke pass during calibration.
5. Promote the next enum/action values only when the state has tests and a real transition.
6. Design `Recovery` before adding long unattended loops. Recovery should be conservative, logged, and easy to stop.

## Detector Guidance

- Template resources are appropriate for tiny, stable, high-contrast UI glyphs in known locations.
- Relative or absolute ROIs should be documented in the detector and represented in synthetic fixtures.
- Use current resource dimensions in tests instead of hardcoding old template sizes.
- Prefer multi-scale template checks around `1.0`, `0.95`, and `1.05` when the UI element may shift slightly with capture or scaling.
- Keep row detection based on stable icon/row structure where possible, not text recognition.
- Return analysis records with nullable bounds and lists so states can decide whether to act or recover.

## State Transition Target

Docked:
focus Mining Hold, unload if full, otherwise undock.

Undocking:
wait fixed undock delay, then poll for location change timer.

EmptyOnUndock:
open/select BELT overview, choose random asteroid belt, press `S`.

WarpingToAsteroidField:
poll for lower-center asteroid belt arrival signal.

LandedOnAsteroidBelt:
locate MINE overview, select first asteroid, press `A`.

Mining:
lock or activate mining once in range, monitor cargo fullness.

ReturningToStation:
warp or dock back to station using the safest available shortcut or overview entry.

UnloadCargo:
move ore from Mining Hold to Item Hangar, then return to Docked.

Recovery:
stop or perform a bounded, logged retry depending on the failure type.
