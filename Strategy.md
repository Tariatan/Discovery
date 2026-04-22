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
