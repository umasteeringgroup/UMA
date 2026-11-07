# Dismemberment plugin

Dismemberment is an optional UMA plugin for runtime mesh cuts, closed cut caps,
detached pieces and physics, surface wounds, bleeding and fluid decals. UMA avatar
generation and its normal editor tools work without this package.

Install **Dismemberment** from **Welcome to UMA > Plugins**. **Examples** and **Tests**
are separate optional packages directly below it. Install the parent first; remove
the companions before removing the parent. The package builders export all three.

On a scene avatar, use **Editors > Plugins > Dismemberment** to add or select its
configuration. Configure supported bones and a cap material, then call `TrySlice`
after the avatar has generated and `IsReady` is true. The Examples package contains
the U3-GoreExample scene, callback/UI scripts, cap materials and surface-fluid profiles. It requires UMA3 Content and SRP Support for
the character assets. Existing project scenes using dismemberment components require
the plugin to remain installed.

See [Artist setup and production guide](ArtistGuide.md) and [Runtime setup and API](../README.md) for configuration, geometry constraints,
completion events, undo, resource ownership and physics behavior.

Uninstall removes unchanged package-owned files and documentation registration.
Modified and unowned project files are preserved. Before removing the plugin from
a project that uses it, remove its components and direct references from your own
gameplay scripts; the UMA core has no dependency on its assemblies.
