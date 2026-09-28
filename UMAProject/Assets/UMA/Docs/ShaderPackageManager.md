# Shader Package Manager

Open **UMA > Shader Package Manager** to find which `.umashaderpack` shaders are used by your project and replace their use on individual UMAMaterials.

The left pane lists one row per package with its file name, imported shader name, and read-only usage checkboxes:

- **In Material** means at least one saved Material uses that shader.
- **In UMAMaterial** means a UMAMaterial references such a Material in its default, second-pass, HDRP, or HDRP second-pass slot.

The scan includes assets and installed packages. It matches shader references, so two shaders with the same display name remain distinct. It does not inspect transient Materials created during Play Mode. A package without an imported shader displays an unavailable message; correct its import before assessing usage.

Select a row to see its shader, UMAMaterial users, and a foldout listing its Material users. **Locate** highlights an asset. The search field filters names and paths. **Refresh** rescans usage; the list also refreshes after project changes and Undo/redo.

## Replace a shader for one UMAMaterial

1. Click **Replace** next to a UMAMaterial.
2. Choose which matching material slots to replace. By default, all its slots using the selected package are included.
3. Select a replacement Shader asset. Shader Graph shaders are supported; another `.umashaderpack` shader is rejected.
4. Review each channel's current property and select a compatible property from the replacement shader. Exact names are retained when possible; common base-map and normal-map names are suggested. Review every suggestion.
5. For a channel the new shader will not consume, select **Not used by shader**. This sets the channel's NonShaderTexture flag; it does not remove the channel or repack its contents. Color channels offer color/vector properties; texture channels offer 2D texture properties.
6. Click **Update** to change the existing Materials and the selected UMAMaterial in place, or choose an existing folder under **Assets** and click **Create copies and replace**.

**Update** preserves Material references and affects all objects that share those Materials. All UMAMaterials sharing the changed Materials have their channel mappings updated. The dialog consolidates matching source properties into one lookup and lists their UMAMaterial users, including channels found only on other UMAMaterials. Those additional channels apply to Update only. Conflicting mappings, incompatible remaining passes, and read-only affected UMAMaterials block Update before any assets change. Update requires writable standalone `.mat` assets under **Assets** and does not use the copies folder. Undo restores the Materials and all affected UMAMaterials together.

Each distinct source Material receives a new, uniquely named copy. Only the selected UMAMaterial points to the copies. Other users retain their original Materials and shaders. When several selected slots use the same source Material, those slots share one new copy within the selected UMAMaterial.

Assigned channel textures, texture scale, and texture offset are transferred to the selected replacement properties. Compatible existing material settings are retained and the replacement shader's material validation is applied.

Channel mappings belong to the entire UMAMaterial, so they also affect material slots that are not being replaced. The dialog blocks mappings that would become incompatible with those remaining passes. Property mapping cannot convert texture packing, shader features, or rendering behavior; the replacement shader must support the intended texture contents and surface appearance. Inspect the resulting copied Materials and rebuild affected avatars to review their appearance.

Read-only UMAMaterials must be copied into **Assets** or checked out before replacement. The dialog saves the changed UMAMaterial and new Material assets. **Undo** restores the original UMAMaterial references and channel mappings; the new Material files remain available for reuse or deletion, including after Undo.

## Archive a shader package

Click **Archive** on a package row. The confirmation shows current Material and UMAMaterial usage; archiving a shader still in use will leave missing shader references. **Cancel** keeps the files unchanged.

After confirmation, the package and its `.meta` file are added to `Assets/UMA/ShaderPackages/Archive/OldShaderPackages.zip` (relative to the installed UMA folder). The ZIP is updated through a temporary file and both new entries are verified before Unity deletes the source asset and metadata. Existing ZIP contents are preserved. Repeated filenames are stored in a unique `Snapshots` folder inside the ZIP. A failed archive write or verification leaves the source intact.

Archiving does not support Undo. To restore a shader, extract both the package and its matching `.meta` file into the project, preferably at the original location. The metadata preserves its GUID.

The toolbar **Refresh** button refreshes the Asset Database and rescans package usage. The list also refreshes automatically after archiving.
