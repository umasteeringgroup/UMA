# UMA - Unity Multipurpose Avatar

UMA is an open-source runtime character generation, customization, authoring, and texture-painting system for Unity 6.3 and newer.

## Installation

Install UMA Core through Package Manager using the published package or staged Core folder. Then use **UMA > Welcome to UMA > Install / Update UMA Packages** to install the matching UMA URP or HDRP support, import the matching `UMA3Content` archive, and optionally import UMA2 legacy content.

Core supports both source-tree (`Assets/UMA`) and UPM (`Packages/com.umasteeringgroup.uma`) installations. A UPM Core is read-only, while character content is deliberately project-owned and editable at `Assets/UMA/UMA3` and `Assets/UMA/UMA2`. Project-specific settings, the Global Library index, generated assets, tasks, and Overlay Painter recovery/export data are stored under `Assets/UMAProjectData`.

Do not install Core simultaneously as an imported `Assets/UMA` tree and as a local UPM package. The editable UMA3/UMA2 content folders are expected alongside a UPM Core.

See `Docs/PackageInstallation.md`, `Docs/!GettingStarted.md`, and the in-editor **UMA > Documentation Browser** for setup and authoring guides.

For a traditional `Assets/UMA` import, review package prerequisites in `Docs/PackageInstallation.md`. Once UMA compiles, **UMA > Package Dependencies** reports required and optional Unity packages. Optional integrations such as 2D Sprite, Test Framework, Addressables, and FBX are isolated so their absence does not disable UMA's core functionality.
