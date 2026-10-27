# Changelog

## 3.1f5 (package version 3.1.5)

- Fixed plugin installation throwing `ArgumentException: Invalid path` when no package location has been saved. Empty or invalid remembered paths no longer prevent normal local package discovery.
- The Plans browser now uses the top selector to filter by subfolder, including nested folders. **All folders** restores the complete list; switching to documentation restores the plugin selector.
- Consolidated UMA3 eyebrows into `Wearables/eyebrows`, keeping the authoritative `eyebrows_31` assets and GUIDs. Updated overlay textures, character presets, sample scenes, export scene sources, and Global Library entries to remove duplicate eyebrow recipes.
- Release recipe validation now includes saved Global Library entries hidden by duplicate lookup names. It reports these as recipe name collisions, showing the selected asset, instead of incorrectly claiming they are missing from the index.
- Release Asset Validation now includes recipe load failures and unavailable slots/overlays in its JSON report and overall verdict. It recursively discovers recipes under Content, UMA3, and UMA2 in addition to the Global Library, flags unindexed recipes, and verifies that indexed dependencies can load.
- Added **Assets > UMA > Texture Utilities > Resize Selected** with standard maximum sizes and previews of importer-limit changes. It only lowers Maximum Size in default settings and enabled platform overrides, then reimports; source images, smaller limits, and other import settings are preserved. Moved **Convert selected textures to PNG** into the same submenu.
- Renamed the Welcome window's configuration page to **Configure UMA** and its customization preset to **Maximum Fidelity**. Navigation now highlights the displayed page with a selected button, bold text, and an accent bar, including Render Pipeline Support.
- Maximum Speed uses only compatible opaque Alpha Clip hair passes, backing up default/HDRP material assignments and their second passes. Reapplying Speed preserves the originals; Maximum Fidelity restores them. Both presets now request **Rebuild all editor UMA** after successful setup. Atlas resolutions and quality overrides remain unchanged. See [UMA Generator Setup](Docs/UMAGeneratorSetup.md).
- Accelerated Best Fit Square for single regions, pairs, and equal tiles, and prepared current texture/cropping dimensions once per fit without caching them across builds. Fixed Multiple Heuristics to retain the winning layout and avoid a redundant packing attempt. Consistent, bounded edge rounding prevents atlas overflow and keeps shared regions aligned.
- Added `UMA.Character.*`, `UMA.Recipe.*`, and `UMA.Assets.*` CPU Profiler markers to expose recipe preparation, merging, DNA setup, callbacks, and asset-loading overhead separately from generator work. The generator guide covers initial creation and repeated-edit workloads.
- Added shared-color **Copy** in the avatar editor, including every color channel and material parameter without the color name. Shared Color Tables support **Paste Values...** and **Paste as New**, plus named **Copy All Checked** / **Paste/Update** in Advanced View. Fixed selecting color checkboxes in the standard view. See [Dynamic Character Avatar](Docs/DynamicCharacterAvatar.md).
- Fixed UMAMaterial inspection repeatedly opening Inspector windows; material Inspect buttons are now associated with their assigned material fields. Unresolved `AssetItem._Type` errors include the current AssetItem as JSON for diagnosis.

## 3.1f3 (package version 3.1.3)

- Added **Original Overlay (base)** visibility in the painter layer stack, allowing transparent replacement artwork without deleting the imported textures. Includes all-channel composition, undo/redo and document/recovery persistence.

- Added per-target **Show** controls in Overlay Painter: other slots default to hidden and can be displayed as non-paintable references. Visibility persists separately from the active target, including shared-mesh preview filtering.

- UV-authored paths now show their curve and all editable points in Scene view immediately on layer selection. Eyebrow shell examples support direct surface positioning, curve and width handles, and head/tail labels without first selecting a point in 2D.

- Added **Path > Generators > Hair > Eyebrows** with deterministic tapered hairs, curve-driven shape and growth, clumping, independent hair populations, layered color variation, mirrored variation, coordinated material coverage, presets and diagnostics. Separate Examples and Tests companions include editable HDEyebrows shell projects and focused validation.
- Corrected path-generator shader color binding so linear data values such as metallic are not decoded as sRGB a second time.
- Painter documents now retain raster artwork when a slot's equipped mesh has the same UV triangle layout with reordered vertices; this enables the eyebrow-shell example projects to load onto equipped characters.

- Added Welcome-page configuration presets for Atlas/No Atlas skin materials, reviewable skin-material selection, Jobified generator configuration, bounded generation settings, and mode-specific UV cropping/bone cleanup. See [Configure UMA](Docs/UMAGeneratorSetup.md) for the current workflow.

- Fixed projection symmetry switches not rendering mirrors. Projection layers now update mirrored and radial copies automatically across all channels, including Normal Control, with live placement edits and Undo/Redo.

- Added a dockable **Overlay Painter Material** window with interactive shader preview, editable shader parameters, live character updates, Undo, and Reset to Source. Preview overrides save with painter documents/recovery without modifying shared material assets or generated texture maps. Open it from the Layer Preview button, View menu, or Window > UMA; saved workspace layouts include the new dock.

- Fixed Sprite Set Fill channels drifting apart when switching Flat/Triplanar mapping. Linked channels now share projection and triplanar blending as well as tiling, offset, and rotation; Fill Type edits the shared mapping from any selected channel. Existing saved fills regenerate with corrected mapping while retaining independent material-channel settings.

- Fixed Install All failing before its first package when Unity restored an empty status array. Batch status arrays now initialize before saving and repair safely after reload; error reporting detaches the update callback before processing malformed state, preventing repeated exceptions.

- Install All Plugins now uses one confirmation for the complete batch, including adoption and backed-up replacement while preserving extra files. Approval survives script reloads and downloads; completion or failure shows a per-package results dialog and saves an installation report.

## 3.1f2 (package version 3.1.2)

- Plugin package builds now finish with a per-package Built/Failed results dialog and continue building other packages after a failure. Cancellation lists unbuilt packages separately; failure details retain filenames and recovery paths.

- Hardened plugin package rebuilding against file-replacement failures: retries brief locks, retains the previous archive until publishing succeeds, restores it on failure, and preserves completed new archives for recovery. Error dialogs identify the full destination and recovery paths; cleanup failures report their filenames without hiding the original error.

- Replaced the mask-effect creation choice **Painted Mask** with **White Painted Mask** and **Black Painted Mask**. Both initialize the shared paint raster and replace prior Painted Mask entries with one enabled Multiply effect at opacity 1, with full Undo/Redo. Zero-opacity effects are explicitly labeled in their headers.

- Added per-layer, per-channel **Blur (px)** to Texture/Sprite sources, defaulting to zero. Softens RGBA edges across Fill, Projection, Paint, and Path sources, including ribbon end tiles, while preserving sprite atlas isolation, normal vectors, saved settings, and Undo/Redo.

- Fixed Projection Normal Control textures/sprites losing their own alpha to Albedo coverage. Transparent height pixels now preserve underlying surface detail, including inverted height, and Albedo opacity no longer weakens the independent height source.

- Fixed stale Projection layer references after adding/editing channels. Normal Control's **Height Strength** (0–64), **Invert Height**, and **Sample Radius** are now specific to the selected layer channel, with saved values and Undo/Redo. Sampling includes that layer's masks/effects and preserves neighboring layers; legacy target-wide conversion settings remain internal for compatibility.

- Added per-channel **Multiplier** and **Additive** colors beneath Texture/Sprite sources in Overlay Painter. Both affect RGBA, with neutral defaults, shared silhouette support, saved settings, and Undo/Redo across Paint, Fill, Path, and Projection sources.

- Reviewed Properties across all layer types: hide painting-only and inherited instance controls, solid-fill mapping fields, and unused projection garment settings. **Layer Channels & Settings** is now a persistent foldout, with New Channel, its selector, and Add Channel together at the top; newly added channels expand automatically.
- Made path Properties contextual: hides painting selection tools, fixed destination controls, and inapplicable ribbon/generator/UV controls. Retains an active-selection notice with Clear, and releases hidden selection tools so they cannot intercept path editing. Mask painting retains selection tools.
- Ordinary paths now hide Garment Generator settings. Added explicit **Convert to Garment** actions in Path Properties and the path toolbar, preserving path shape, width, and sources with Undo/Redo.
- Fixed ribbon path tiling to account for texture and cropped-sprite aspect ratios. Fits whole repeats with complete Beginning/End images using a shared stretch scale; material channels stay aligned and local Point Width shaping is preserved.
- Added a general **Path Width** control at the top of Path Properties for ordinary paths, ribbons, and generators. Local Point Width percentages are preserved; changes support Undo/Redo and Auto Update.
- Added **Black Expansion** and **Contrast** to Voronoi mask effects, allowing darker, more defined cells without changing their scale or placement. Defaults preserve existing masks.
- Fixed Edge Wear missing thin bevels because small triangle normals were zeroed during normalization. Also bridges shallow sections of continuous convex rims only between detected edges; concave folds and flat triangulation remain excluded.
- Added **Assets > UMA > Remove invalid races from selected recipes**, immediately after Add Race(s). Checks installed RaceData assets (including unindexed races), removes missing race references and their thumbnails from selected wardrobe recipes, and supports Undo.
- Added configurable Edge Wear fade level, falloff curve, and distance-based breakup curve. Exposed-edge wear defaults to a linear fade; breakup no longer clips fading rims into gaps or creates offset wear bands.
- Edge Wear now defaults to physical rims and convex creases, with world-space width and restrained normal-detail wear. UV/shading splits do not create false edges; existing layers retain the original broad treatment as **Overall Wear**.
- Fixed editor-wide stalls while Welcome's Plugins page was open: package status now refreshes after changes instead of validating every second. Navigation caches render-pipeline status and no longer validates UMA3 for an unused button on every GUI event.
- Added **Replace everything** to installation conflicts, with an optional clean replacement that removes extra files inside the selected package folder. Backups and installed companions are preserved in both modes.
- Added **Continue - Remove All** to plugin removal confirmations, allowing modified package-owned files to be removed across the selected parent and companions. The choice persists through interrupted-session recovery; unowned files remain protected. Empty folders are removed from children to parents, including folders omitted from manifests and the package root when empty.
- Added local-first plugin installation with a matching GitHub release download fallback, download cancellation, release-page links, and local-file recovery options.
- Added immediate install/reinstall progress for archive validation, installed-file comparison, backup preparation, and final verification; reused the selected archive validation result.
- Fixed false reinstall conflicts from unowned empty folders and files already identical to the incoming archive. Empty folders and metadata are preserved; divergent local edits remain protected. Builds generate new archive manifests while retaining the installed comparison baseline.
- Added a Plugins page with separate Examples/Tests rows for every official plugin, moved package builds to **UMA > Build > Build Plugin Packages**, and derived release/package versions from installed UMASettings.
- Separated Overlay Painter, Hair Card Editor, and Dismemberment from Core, and registered plugin documentation in separate Documentation Browser selections.
- Added Overlay Painter path generators for scars, tattoo designs, text, stitching, seams, trim, zippers, and distress; added text flips, generator material-channel handling, and related layer-effect fixes.
- Added Wetness & Sweat, Leather & Coated Fabric, Print Aging, clothing-finish presets, automatic seam paths, and PBR texture-set import. Expanded cloth/quilt mapping, rotation, sprite-set selection, and normal combination.
- Added dockable Overlay Painter Properties, saved-layout restoration, and Layer Preview progress and stale-layer regeneration controls.
- Added the Recipe Prepared slot event, UMADnaOverrideSlot and its editor/example, and blendshape optimization documentation.

- Moved legacy UMA2 wearable samples and utility slots into the separate UMA2Compatibility Examples package, preserving GUIDs and script identities. Required base-character resources stay in the parent; base races build without Examples.
- Added shared anatomical and garment-region masks to Overlay Painter's **Region Placement**
  properties: knees, elbows, armpits, wrist/ankle cuffs, collar, lips, shoulders, waist and seat.
  Bone-relative envelopes share reusable profiles and affect every material channel. Knee and
  elbow masks cover an outward 120-degree sector, with pose-aware bend direction and rounded
  envelopes. Scene outlines, custom bone names, standalone slot alignment and mirrored UV
  ownership are supported; region examples and regressions remain in companion packages.

- Parent plugin rows now offer **Remove All**, removing installed Examples and Tests before
  their parent with one confirmation. Modified and unowned files are preserved, and the
  complete removal sequence resumes after an interrupted Editor session.
- Packaged the complete legacy `Assets/UMA2` content tree as the official **UMA2Compatibility**
  plugin, preserving asset GUIDs and its assembly name. Welcome's Plugins page now provides
  installation, reinstallation and removal, with separate Examples and Tests companions.
  Plugin builds use the installed UMASettings version, and the Documentation Browser has a
  separate UMA2Compatibility selection. Earlier nested `Assets/UMA/UMA2` trees retain a
  GUID-preserving migration path.
- Added a positioned-Prefab conversion utility under **Assets > UMA** and **UMA > Asset
  Management**. It retains the original path and GUID on a new identity-root wrapper, keeps the old
  hierarchy as an unpacked `_positioned` child at its authored transform, creates a separate
  `_positioned.prefab` recovery copy, and validates prefab-owned local object ids before committing.
  Prefab Variants are now materialized automatically while their positioned recovery copies retain
  the original Variant relationship.
- Added **Assets > Find Usage in Material** for a selected Texture2D. The results dialog finds
  materials and material sub-assets using the texture, lists each match with its asset path, and
  provides per-material **Ping** and popup **Inspect** actions.
- Fixed Icon Creator camera capture allocating a depthless temporary output, which Unity 6.3 render
  graph rejected and rendered with error-colored pixels. Supersampled camera targets now always have
  a depth/stencil attachment, and the capture test is isolated from unrelated scene geometry.
- Fixed the Markdown viewer rendering every inline link twice. Links now remain inline, underline
  and highlight on hover, provide a short squash/pop click animation, and use Unity 6.3 rich-text
  link events directly in headings, paragraphs, lists, and table cells.
- Fixed the U3-GoreExample Shift-drag cut helper line being omitted by HDRP. Its always-on-top
  preview shader now supplies explicit HDRP, URP, and Built-in unlit passes in a supported
  transparent queue, with deterministic late renderer sorting.
- Fixed the UMA Welcome window reopening every two seconds after being closed while required render
  pipeline setup remained incomplete. Dismissal now lasts for the current setup state and editor
  session; a meaningful pipeline/content state change can show the next required step, and the
  **UMA > Welcome to UMA** command always opens it explicitly.
- Fixed Overlay Painter's 3D, Path, and painting Scene overlays appearing in a newly installed
  project before Overlay Painter had opened. They now register hidden by default and continuously
  reconcile Unity's restored layout visibility with the active Overlay Painter stage.
- Fixed an explicitly opened Overlay Painter document being replaced by an older compatible recovery
  snapshot. The requested document is now the default choice, with **Recover Instead** available
  explicitly; choosing the document discards the superseded recovery.
- Fixed Overlay Painter document and Material Preset object pickers consuming stale completion
  command names during Layout/Repaint. Picker completion now handles only Unity's ExecuteCommand
  event, eliminating `Event.Use() should not be called for events of type Layout` errors on close.
- Fixed Stubble Maker and other owned-tile CPU generators exceeding the Plugin command-memory budget
  on multi-channel UDIM targets. Compressible `Color32` commands now remain compressed while the
  atomic transaction is queued, expand one tile at commit time, and release immediately afterward;
  the existing decoded-tile and uncompressible-payload safety limits remain enforced.
- Fixed Overlay Painter documents reopening without their saved layers when regenerated UMA material
  names received new random `_Genb_<number>` suffixes. New surface ids exclude that nonce, while
  legacy documents rebind conservatively from saved slot, UV/topology, UMA material, and
  renderer/submesh evidence; unchanged-UV layers and black masks restore exactly.
- Bound Compact View's Overlay Painter 3D, Path, and painting toolbar overlays to its dedicated
  docked Scene tab, and hid those overlays from every other Scene window while that workspace is
  active.
- Added the optional **Overlay Painter Compact View** UMA setting, enabled by default. It opens a
  dedicated floating workspace with Layers/Brush tabs on the left and Scene/2D tabs on the right,
  remembers its geometry, frames the active target, and provides Layout- and Window-menu reset
  commands. Disabling it retains the independent dockable windows; unavailable dynamic-layout
  support falls back safely for the session.
- Moved the complete Overlay Painter painting palette from its dockable editor window into a native
  Scene-view **Overlay Painter Toolbar** overlay. The 13 synchronized icon controls cover every
  brush, geometry-fill, Path, and help action; legacy saved window instances close automatically.
- Made character-launched Overlay Painter sessions give startup priority to a target whose visible
  name contains the standalone word Body, ahead of any restored target selection. Fresh and legacy
  sessions enable Isolate by default; current explicit Isolate choices and standalone launches remain
  unchanged.
- Fixed Overlay Painter stage teardown so compilation, assembly reload, Play Mode, ordinary close,
  and failed launch immediately remove its 3D and Path Scene-view toolbars. Added a full-width
  **Shutdown Overlay Painter** toolbar button that retains the normal Save/Discard/Cancel flow.
- Added Overlay Painter project settings for enabling periodic automatic recovery, configuring the
  post-edit idle delay, and enforcing a minimum interval between background saves. Recommended
  defaults are enabled, 120 seconds idle, and 300 seconds between saves; explicit Save and close
  protection remain available when periodic recovery is disabled.
- Added Overlay Painter's transparent Stubble Maker generator for facial and shaved-head hair,
  including downward tapered strands, placement/randomization controls, beard/scalp shadow, shaving
  redness, rash, pimples, pigment spots, and coordinated alpha-bearing material outputs. Documented
  the existing ribbon-guided Scar/Wound workflow for albedo, roughness, thickness, and normal control.
- Hardened the editable UMA3/UMA2 content release flow: deterministic archives now omit unimportable empty leaf folders, reject missing metadata and malformed/path-escaping tar members, bind atomic transactions to exact archive/manifest hashes, preserve root GUIDs and failed-rollback backups, and validate legacy package GUID ownership. The Welcome package page now presents URP, HDRP, UMA3, and UMA2 installation in one dependency-aware workflow and opens that unified page automatically when required content is missing.

## 3.0.4

- Added Unity 6.3 package metadata and package-location-independent asset resolution.
- Added project-owned writable settings, Global Library index, generated-data, and Overlay Painter locations.
- Added explicit UMA3 sample assemblies and immutable-package safeguards.
- Split editable UMA3 and optional UMA2 legacy content into validated `.unitypackage`
  installers that deploy to `Assets/UMA/UMA3` and `Assets/UMA/UMA2`.
- Added transactional content install/update handling with local-change detection,
  adoption of matching source checkouts, backups, rollback, and reload recovery.
- Hardened content updates to preserve root-folder GUIDs, default to aborting on
  local conflicts, report every classified path, enforce exact dependencies and
  Core compatibility ranges, and retain importer/root metadata in backups.
- Made content archives reproducible and Core staging transactional, hash-verified,
  and recoverable. Added Core-only, URP, HDRP, update, migration, conflict,
  removal, and player-build validation workflows.
- Removed the nested HDRP Races installer by expanding its assets into UMA2 content.
- Isolated optional Addressables and FBX Exporter integrations behind constrained assemblies and dependency-neutral bridges.
- Continued Overlay Painter generator, filter, export, persistence, spline, mask, and release validation work.
