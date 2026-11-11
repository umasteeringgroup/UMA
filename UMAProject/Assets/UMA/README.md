# UMA - Unity Multipurpose Avatar

UMA 3 is an open-source runtime character generation, customization, authoring, and texture-painting system for Unity 6.3 and newer.

## Current release: UMA 3.1f5 / 3.1.5

Updating from **3.1f3 / 3.1.3**? Read [What's New](Docs/!WhatsNewInUMA3.md#uma-31f5--changes-since-31f3) for the workflow overview and the [complete changelog](CHANGELOG.md#31f5-package-version-315) for features, fixes, and optional plugin changes. The documentation covers recipe and mesh authoring, pose tools, generation/animation, ragdoll rest, installation, shading, and sample content in one release record.

## Installation

Install UMA Core through Package Manager using the published package or staged Core folder. Then open **UMA > Welcome to UMA > Install SRP Package** (shown as **Render Pipeline Support** once a pipeline is installed) to install the matching UMA URP or HDRP support and click **Install UMA 3 Content...** to import the matching `UMA3Content` archive. Legacy UMA 2 content is available from **Welcome > Plugins > UMA2Compatibility**.

Core supports both source-tree (`Assets/UMA`) and UPM (`Packages/com.umasteeringgroup.uma`) installations. A UPM Core is read-only, while character content is deliberately project-owned and editable at `Assets/UMA/UMA3` and, for UMA2Compatibility, `Assets/UMA2`. Project-specific settings, the Global Library index, generated assets, tasks, and Overlay Painter recovery/export data are stored under `Assets/UMAProjectData`.

Do not install Core simultaneously as an imported `Assets/UMA` tree and as a local UPM package. The editable UMA3/UMA2 content folders are expected alongside a UPM Core.

See `Docs/PackageInstallation.md`, `Docs/!GettingStarted.md`, and the in-editor **UMA > Documentation Browser** for setup and authoring guides.

Open **UMA > Welcome to UMA > Configure UMA** to review skin and hair materials and choose Maximum Speed or Maximum Fidelity. The active page's button stays highlighted. Both presets request editor-avatar rebuilds; they preserve separate runtime/editor atlas resolutions and quality overrides. See [Configure UMA and Generator Setup](Docs/UMAGeneratorSetup.md) for details and atlas-fitting guidance.

For a traditional `Assets/UMA` import, review package prerequisites in `Docs/PackageInstallation.md`. Once UMA compiles, **UMA > Package Dependencies** reports required and optional Unity packages. Optional integrations such as 2D Sprite, Test Framework, Addressables, and FBX are isolated so their absence does not disable UMA's core functionality.
