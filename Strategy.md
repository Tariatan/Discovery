# Polygons identification best practices

## Terminology

Sample: one Project Discovery plot to analyze.
Playfield: the plot area only; exclude surrounding UI.
Dot: one plotted cell.
Cluster: a visible population of related dots.
Core: the densest part of a cluster.
Smear: a lower-density extension attached to a core.
Comet tail: a smear that should often be separated from the dense body.
Thinnest separation: the narrowest gap between two populations.
Polygon: the closed boundary around one cluster.
Gold-standard sample: a scored training/check sample depicting the expected clusters detection with brown regions.

## Main rules

- Optimal polygons per submission: as few as possible, usually 1-3 on easy samples; at most 8 polygons.
- Polygons must be closed logically.
- Polygons must not overlap.
- Optimal points per polygon: usually 4, sometimes 5-6 on awkward/tail shapes; avoid maxing to 10 unless truly needed.
- Focus on isolating dense, colored cell clusters (often resembling comets) from scattered noise.
- Use consistent, tight bounding boxes and keep your methodology simple to ensure high-accuracy submissions.
- Focus on Density: Identify high-density cell clusters and draw polygons tightly around them.
- The "Comet" Method: Treat the clusters like comets, separating the dense "head" from the scattered "tail".
- Handle Redundant Data: If a cluster seems obvious but the expected example says otherwise, tighten your box or focus on separating the dense core.
- Avoid Overlap: Ensure your drawn areas for identifying multiple clusters do not overlap.
- Draw Roughly: A simple box covering the clusters is often enough to maintain 99% accuracy.
- Prefer separation over over-grouping: if two dense populations are clearly distinct, they should usually stay as two polygons rather than one broad combined balloon.
- Be careful with broad polygon growth: aggressive dilation, balloon expansion, or eager sibling merging can easily collapse separate clusters into one oversized polygon.

## Handling Smears and Clusters

- Smears represent clusters where cells are maturing slowly, resulting in a continuous, often "smeared" population rather than distinct, tight clusters of mature or immature cells.
- Identify the Trend: Smears often appear as elongated, diagonal, or curved lines of dots. They indicate a transition state.
- "Comet" Shapes: When a "comet" shape appears (dense head, long tail), it is crucial to include both the head and the "tail" (the smear) within your polygon.
- Focus on Separation: Tight bounds are less important than ensuring distinct cell populations (clusters and smears) are separated from each other.
- Don't Over-complicate: If there is a massive cluster and a "smear" descending from it, draw one bounding box around the main group and another around the trailing cells if necessary.
- The "Two-Box" Method: Many samples follow a formula of one major cluster at the top and a smeared or scattered group below. Drawing two distinct boxes-one for the top and one for the bottom-often results in 99% accuracy.

## Implementation Observations

- Color is the primary useful signal for cluster discovery in this project. The current HSV-style candidate mask is more reliable than grayscale-first detection because the plotted cells are vivid while much of the surrounding UI is dark or low-saturation.
- Full-frame grayscale conversion is not useful as a primary cluster-identification method here. In practice it degraded the gold-standard sample set by either over-splitting some cases or merging others incorrectly.
- Grayscale may still be acceptable as a narrow diagnostic or secondary validation signal, but not as a driver of the main cluster mask.
- A split polygon should not be expanded twice. If a split path already builds an expanded polygon from candidate points, another balloonization pass tends to make polygons too broad.
- Sibling-polygon merging must stay conservative. Nearby polygons of similar size are often genuinely separate clusters, not artifacts that should be recombined.
- Some real samples contain a sparse detached lower lobe that does not survive the normal dense-cluster mask, even though the gold-standard treats it as a real second cluster.
- When that happens, a narrow recovery pass below the primary cluster can be useful, but it should only activate when the main pipeline found a single dominant cluster and there is a clearly detached lower population.
- Sparse-cluster recovery should stay density-aware and local. It is a fallback for a specific miss pattern, not a second general-purpose cluster detector.
- Keep coordinate spaces consistent during any recovery or refinement pass. A valid recovered contour can be lost entirely if playfield-local masks are compared against already translated full-image polygons.

## Known-Sample Matching

- Known-sample matching is an intentional shortcut, not a temporary hack. When a live playfield closely matches a known example, using the matching expected polygons is often more reliable than re-detecting the same shape from noisy dots.
- Match only the playfield crop, not the whole screenshot. Whole-screen matching is too sensitive to window position, DPI, popup state, and surrounding UI.
- Keep example assets in relative runtime folders. Do not reintroduce project-root walking or absolute paths.
- Use `*.expected.png` as the human-readable gold standard and `*.expected.masked.png` as an optional extraction aid. Do not overwrite the original expected image with a thresholded or filtered variant.
- Extracting polygons from semi-transparent brown overlays is inherently unstable because the apparent color depends on the dots underneath. Prefer masked sidecars for difficult examples.
- Overlay extraction should use conservative morphology. Aggressive closing can merge two nearby expected regions into one contour before polygon simplification even starts.
- The known-sample template cache assumes the `expected` folder is stable for the process lifetime. That leaves a bounded unmanaged `Mat` retention concern, but it is acceptable for the current fixed-runtime model.

## Boundary and Shape Finalization

- Any step that mutates polygons can reintroduce invalid shapes. After randomization, clipping, spacing, or collision handling, run the final cleanup sequence again instead of assuming earlier cleanup still holds.
- Final polygons must stay inside the marker frame: left edge of left markers, right edge of right markers, top edge of top markers, and bottom edge of bottom markers.
- Upper-band polygons have an extra practical ceiling at the upper edge of the top marker row when their centroid is still in the marker band. This prevents top polygons from bleeding into marker/UI territory.
- Randomization is for natural-looking clicks only. It must happen before final restrictions so randomized points are still clipped, normalized, spaced, and collision-checked.
- Normalization should remove inward dents by returning to an outward hull. Expected regions are generally protruding, not concave.
- Merge close neighboring points during normalization so the automation does not produce machine-like clusters of nearly identical clicks. Keep at least three points.
- Point spacing must check vertex-to-vertex and vertex-to-segment distance. A point can be dangerously close to another polygon edge even when no two vertices are close.
- Collision resolution must run after marker clipping too. Clipping can create a fresh overlap even if polygons were non-overlapping before the clip.

## Automation Constraints

- The control button location is intentionally hardcoded to the known safe screen region. Earlier template, contour, and rules-panel detection attempts were less reliable than the fixed safe area in the target setup.
- Startup `PLAY NOW` detection uses template matching against `Properties.Resources.play`. If launcher art changes significantly, update the resource first before retuning thresholds.
- If startup `PLAY NOW` detection fails, leave automation idle and write a `No play button found` debug overlay into the startup capture.
- Scale the control-button cursor target for DPI before calling `SetCursorPos`. WPF logical coordinates and physical screen pixels diverge on scaled displays.
- Current cursor mapping assumes the app runs on the single-monitor setup it was built for. Multi-monitor virtual-screen offsets are a known limitation; supporting them requires translating capture-image points back through `VirtualScreenLeft` and `VirtualScreenTop`.
- Capture full physical virtual-screen dimensions with Win32 `GetSystemMetrics`, not WPF `SystemParameters`. WPF dimensions are logical units and caused cropped screenshots at 150% display scaling.
- The main-window `Debug` checkbox controls screenshot retention. Leave it unchecked for normal automation so raw, focused, startup, pilot-selection, and annotated trace images are deleted once the workflow no longer needs them; check it only when investigating detector behavior.
- Once a mouse-down event is sent, always send mouse-up in a `finally`. Cancellation must stop future work, not leave the physical mouse button logically held down.
- The submit cap is a rolling submit-time rule, not a cycle-count rule. Delay immediately before Submit when five submissions already exist inside the rolling window; the current safety window is 90 seconds.
- The submit limiter must be service-level state and must not reset after pilot switches or manual Stop/Start inside the same app process. Resetting it creates a loophole that can exceed five Submit clicks in a rolling minute.
- Only run maximum-submissions popup detection after a focused screenshot fails playfield detection. Running it while the normal playfield is visible produced false positives on ordinary Project Discovery panels.
- Analyze focused screenshots without writing `*.focused.annotated.png`; they are state probes after Submit focus, not normal detector debug artifacts. Only the max-submissions popup detector should draw into the focused screenshot, and only when the popup is actually detected.
- Popup detection should require the whole popup signature: two-line title evidence, body text bands, information icon shape, and button evidence. Single-feature detection is too prone to false positives.
- Keep the popup information-icon evidence round-ish. A wide contour can be produced by inventory item art and cyan item frames, which caused a focused post-submit screenshot to be misclassified as the max-submissions popup.
- When a max-submissions popup is detected, draw debug text into the focused screenshot. When pilot selection fails, draw `Pilot <index> not found` into the pilot-selection screenshot.
- Pilot switching should scan the relative `pilot` folder and advance through available numeric avatars instead of blindly incrementing forever. Do not wrap after the highest configured pilot; if max submissions is detected on the last pilot, press `Alt+Shift+Q`, wait 2 seconds, press `Enter`, then stop automation.

## Testing and Handoff Notes

- Prefer generated images and temporary directories in tests. Do not make tests depend on local capture, sample, expected, failed, or pilot folders unless explicitly requested.
- Keep test names, class names, and `Arrange - Act - Assert` comments aligned with `AGENTS.md`.
- Regression tests should characterize real workflow behavior when the pipeline is being tuned. Low-value tests for trivial helpers are less useful than sample-like generated cases.
- File locks from a running `Discovery.exe` or Visual Studio can block test/build output. Close the app before treating a build failure as a code failure.
- Long agent threads in this project repeatedly died during remote compaction. Keep durable findings in this file and keep future task scopes narrow enough that handoff does not depend on the chat history.
