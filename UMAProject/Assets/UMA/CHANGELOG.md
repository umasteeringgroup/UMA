# Changelog

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
