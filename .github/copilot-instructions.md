# UMA (Unity Multipurpose Avatar) – Concise Dev & Test Guide

Purpose
- This is a Unity 6000.2.4f1 project (asset/package), not a standalone app.
- Goal: keep edits minimal and validate via example scenes (Play Mode).

Prerequisites
- Unity Hub + Unity 6000.2.4f1 (exact).
- 10GB+ disk, 8GB+ RAM (16GB recommended), GPU with SRP support.
- Internet access only for Unity install/licensing.

Open Project
- Add the project folder in Unity Hub and open with unity 6000.2.4f1
- First import/compile can take several minutes; allow time.

Build/Run Model
- Do not produce standalone builds; validate by entering Play Mode in example scenes.

Primary Validation Flow
1) Scene Loader
   - Open: Assets/UMA/Examples/SceneLoader/SceneLoader.unity
   - Play; verify the example menu appears.

2) DCS Demo (main check)
   - From Scene Loader choose “UMA DCS Demo - Simple Setup”
     or open directly:
     Assets/UMA/Examples/DynamicCharacterSystem Examples/UMA DCS Demo - Simple Setup.unity
   - In Play Mode verify:
     - Race switch (Male/Female) works.
     - DNA sliders update body.
     - Wardrobe changes apply.
     - Each action updates within a few seconds; no errors/pink materials.

Additional Quick Checks (optional but helpful)
- Random Characters: UMA DCS Demo - Random Characters.unity
- Crowd: UMA Core Demo - Crowd.unity
- Blendshapes: Blendshape Example.unity
- Addressables: AddressablesScene.unity
- Timeline: UMA Timeline Example.unity

Expected Timings (approx, first-time may be slower)
- First import: minutes.
- First Play in a scene: 1–3 minutes.
- Character updates: typically under ~10s.

Minimal Pre-Commit Checklist
- [ ] Opens in Unity 6000.2.4f1 without console errors.
- [ ] Scene Loader menu works.
- [ ] DCS Demo:
  - [ ] Race switch OK
  - [ ] DNA sliders OK
  - [ ] Wardrobe OK
  - [ ] No pink/missing textures
  - [ ] No UMA errors/warnings
- [ ] Optional: Addressables/Timeline scenes (if related changes).

Troubleshooting (fast path)
- Pink/missing textures: check shaders/RP, review console errors.
- Generation failures: ensure overlays/slots exist; try UMA > Race Updater.
- Assembly errors: confirm asmdefs and manifest dependencies.

Key Folders
- Core: Assets/UMA/Core/
- Examples: Assets/UMA/Examples/
- Content: Assets/UMA/Content/
- Editor tools: Assets/UMA/Core/Editor/

When Unity is unavailable
- Focus on code structure, asmdef references, script compilation, and UMA API usage (no Play Mode).