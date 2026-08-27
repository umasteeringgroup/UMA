# Changelog

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
