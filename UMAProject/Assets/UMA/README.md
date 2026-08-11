# UMA - Unity Multipurpose Avatar

UMA is an open-source runtime character generation, customization, authoring, and texture-painting system for Unity 6.3 and newer.

## Installation

Install the package through Package Manager using the published package, a local package folder, or the repository subfolder containing this `package.json`.

UMA supports both source-tree (`Assets/UMA`) and UPM (`Packages/com.umasteeringgroup.uma`) installations. Package content is treated as read-only. Project-specific settings, the Global Library index, generated assets, tasks, and Overlay Painter recovery/export data are stored under `Assets/UMAProjectData`.

Do not install the same checkout simultaneously as an imported `Assets/UMA` tree and as a local UPM package.

See `Docs/PackageInstallation.md`, `Docs/!GettingStarted.md`, and the in-editor **UMA > Documentation Browser** for setup and authoring guides.

For a traditional `Assets/UMA` import, review package prerequisites in `Docs/PackageInstallation.md`. Once UMA compiles, **UMA > Package Dependencies** reports required and optional Unity packages. Optional integrations such as 2D Sprite, Test Framework, Addressables, and FBX are isolated so their absence does not disable UMA's core functionality.
