# Card/scalp clearance validation

## Reproduction and correction

ShortHairPart Classic contained evaluated strands and finished ribbon geometry
inside the authoring head. The initial diagnostic measured 1,738 of 24,730 LOD0
vertices more than 1 mm inside, with penetration reaching approximately 4.2 mm
behind the ear. Classic and Relaxed used a 1 mm root inset. Close Cut's Lift
modifier helped its centerlines but was not a final ribbon/LOD collision check.

The new per-group **Prevent scalp penetration** option runs after card sampling,
width, roll and embedding. It translates cross-sections without changing their
width, samples edges and face interiors, and updates moved geometry's normals
and tangents. It does not edit guides, root anchors, maps, UVs or indices. Root
Embed intentionally allows a fading inset; the corrected sample uses zero inset
and 0.5 mm clearance in both groups. All three variants retain their materials,
growth textures, guide layout and existing modifier stacks.

A second bug was fixed in closest-surface normals: `Vector3.normalized` could
zero valid millimeter-sized triangle cross products because those vectors encode
area rather than distance. Collision normals now normalize using their squared
area, matching the raycaster's existing small-triangle treatment.

## Checks

- Final release gate: **577/577 tests passed**, no ignored/skipped cases.
  Results: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/ffe4f8a34fbc46bb8b021bceffab0227/editmode.xml`.
- Actual Unity 6000.3.18f1 source import and compilation in
  `tmp/HairCardsSourceValidation`; no precompiled Hair Cards assembly substitution.
- All three ShortHairPart variants, all three LODs: triangle vertices, edge samples
  and a barycentric face grid must remain above -0.1 mm signed scalp distance.
  Tests also compare newly generated meshes with the saved native mesh assets.
- Convex-surface regression with two-point low-LOD cards whose endpoints clear
  the scalp but whose connecting faces originally cross it: ribbon, cambered
  multi-span ribbon with explicit backfaces, and tube.
- Intentional root embedding, unchanged evaluated guide positions, unchanged
  topology/UVs/cross-section widths, finite normalized frames, repeat-build stability.
- In-place source edits, toggling off, profile/shape/roll/clearance edits, and
  cached-versus-fresh positions, normals, tangents and indices.
- Rotated/translated avatar authoring pose: preview carries the matching posed
  collision mesh, while the original evaluation retains the source mesh.
- Last-used settings, Reset to defaults and Undo; warm preview allocation check
  with clearance both enabled and disabled.
- Actual URP side, opposite-side, front, rear and three-quarter renders of each
  variant. Review images are in `tmp/ShortHairPartReview`.

## Mesh budget and performance

| Variant | LOD0 | LOD1 | LOD2 |
| --- | ---: | ---: | ---: |
| Classic | 19,784 | 9,606 | 3,424 |
| Relaxed | 19,768 | 9,720 | 3,544 |
| Close Cut | 19,792 | 9,666 | 3,416 |

Unique geometry counts are unchanged; Hybrid still submits a second material pass.

Mesh-build measurements from the isolated Editor (not total grooming time):
Classic LOD0 fresh clearance solve 1,629 ms; warm cached build 42.5 ms; one-card
position edit 42.8 ms; clearance disabled 34.8 ms. Relaxed/Close Cut warm builds
were approximately 42 ms. These are individual diagnostic measurements, not a
cross-hardware frame-time guarantee. A fully changed groom, a source-mesh edit,
or changing clearance still needs a complete solve. Evaluation is additional.

The workspace caches exact input/output geometry per stable curve ID, including
normalized UVs, frames, topology and clearance settings. In-place source changes
invalidate the cache; removed/disabled cards are pruned. Surface patches use a
distance-bound proof before accepting a local nearest point, otherwise falling
back to the full BVH. The check has no per-frame runtime simulation cost after bake.

## Limits and deployment

This is a bounded authoring correction against an outward-facing source surface,
not runtime collision or a proof covering every possible continuous intersection.
Test extreme edits, sparse/coarse cards and animated skinning on the final avatar.
Existing grooms remain opt-in. For an open ShortHairPart groom, enable clearance
and set Root Embed to zero on both groups, then rebuild/rebake.

Corrected native sample outputs were installed after the user closed Unity.
`tmp/InstallShortHairPartClearance.ps1` copied the 3 grooms, 9 LOD meshes and
3 preview images after original-file/GUID checks, then byte-verified every file.
Originals are retained in `tmp/CardClearanceBefore/ShortHairPart`.
