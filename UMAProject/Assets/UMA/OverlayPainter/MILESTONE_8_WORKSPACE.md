# Overlay Painter Workspace

Milestone 8 turns Overlay Painter into a persistent multi-region Unity editor workspace. Dock it beside the Scene view to keep the reconstructed 3D character and interactive 2D UV canvas visible together.

## Regions

- **Global toolbar** — undo/redo, save, export, UV and shelf visibility, channel solo, source-before comparison, slot isolation, wireframe, sampler, and layout controls.
- **Tool rail** — paint, erase, blur, smear, clone, dodge, burn, normal touchup, plugin brush, and surface-path authoring.
- **Target** — searchable multi-slot selection and a thumbnail Texture Set view. Texture sets are labeled by owning slots, not derived material names.
- **UV canvas** — the selected logical channel, source-before preview, UV wireframe, visible paths, pan/zoom, painting, clone-source selection, and surface-hugging path point editing.
- **Layer / Path** — non-destructive paint, fill, group, plugin, and path layers with thumbnails, visibility, drag reorder, rename, duplicate, merge, and delete.
- **Properties** — explicit Destination, Source, Channel, active-layer, brush, path, stroke/projection, mask/plugin, document, and performance controls.
- **Asset Shelf** — all `BrushPreset` assets with thumbnails, folders, tags, search, favorites, recents, custom ordering, drag-and-drop, rename, and duplicate.

Every region and splitter is persisted in `TexturePaintStageState` schema v10. **Layout > Reset Workspace** returns to the default arrangement.

## 2D and 3D synchronization

The UV canvas and Scene view share the active target, slots, destination layer, channel, brush, masks, undo history, and stroke engine. A 2D stroke is converted to a surface anchor and then dispatched through the same world-space footprint projection used by 3D painting, so it retains cross-slot behavior instead of becoming a separate UV-only paint implementation.

Visible path layers are drawn in both views. In UV mode, enable Path on the tool rail, click empty surface UVs to add points, or drag an existing point. Each edit is projected back to the reconstructed mesh and updates its barycentric surface anchor and controls.

## Shortcuts

| Action | Shortcut |
|---|---|
| Paint / Erase / Blur / Smear / Clone | `B` / `E` / `U` / `S` / `C` |
| Dodge / Burn / Normal / Plugin | `D` / `Shift+D` / `N` / `P` |
| Sample color and surface | `I`, then click |
| Mirror / Wireframe / Asset Shelf | `M` / `W` / `Tab` |
| Logical channels | `1` through `7` |
| Brush size | `[` and `]` |
| Brush hardness | `Shift+[` and `Shift+]` |
| Brush size and hardness drag | `Shift+Right Drag` |
| Undo / Redo / Save | `Ctrl/Cmd+Z` / `Ctrl/Cmd+Shift+Z` / `Ctrl/Cmd+S` |
| Duplicate / Rename / Delete layer | `Ctrl/Cmd+D` / `F2` / `Delete` |

## Plugin integrity

The workspace does not hand plugins UI-owned or engine-owned mutable objects. Brush plugins receive the v2 brush sample contract. Filters, generators, importers, bakers, and exporters remain routed through `PluginHost`, immutable snapshots, bounded artifacts, and validated transactions. Masks, channel declarations, color/data rules, normal normalization, dirty tiles, document persistence, cancellation, diagnostics, and undo/redo remain host-enforced.
