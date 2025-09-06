# UMA (Unity Multipurpose Avatar System) Development Instructions

UMA is a Unity-based character creation and modification system for humanoid characters and other models. This is a Unity 2021.3.45f1 project with extensive example scenes, editor tools, and character creation workflows.

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

**CRITICAL**: These instructions assume Unity Editor can be installed and run. In sandboxed or limited environments where Unity cannot be installed, focus on code analysis and structure understanding rather than running validation scenarios.

## Working Effectively

### Unity Installation and Setup
- Download Unity Hub from https://unity.com/download (requires internet access to Unity domains)
- Alternative download methods if direct access fails:
  - Linux: `wget https://public-cdn.cloud.unity3d.com/hub/prod/UnityHub.AppImage`
  - Windows: Download UnityHubSetup.exe from Unity website
  - macOS: Download Unity Hub .dmg from Unity website
- Install Unity 6000.2.0f1 through Unity Hub (EXACT version required)
- NEVER CANCEL: Unity installation takes 10-15 minutes. Set timeout to 30+ minutes.
- Open Unity Hub and add the project by navigating to the `UMAProject/` folder (not the repository root)
- NEVER CANCEL: First project load takes 5-10 minutes while Unity imports assets and compiles. Set timeout to 20+ minutes.

### Project Structure Validation
- Verify project opens without errors in Unity 6000.2.0f1
- Check that all assembly definitions compile correctly:
  - `UMA_Core.asmdef` - Core UMA functionality
  - `UMA_Examples.asmdef` - Example scenes and scripts 
  - `UMA_Core_Editor.asmdef` - Unity Editor extensions
- Confirm Unity package dependencies are resolved automatically

### Build and Validation Process
- DO NOT attempt to "build" this project like a standalone application
- This is a Unity asset/package, not a deployable application
- Validation consists of running example scenes and testing character creation workflows
- NEVER CANCEL: Scene loading and character generation can take 2-5 minutes on first load

## Validation Scenarios

### Primary Validation - Character Creation Workflow
Always test the complete character creation pipeline after making changes:

1. **Scene Loader Validation**:
   - Open scene: `Assets/UMA/Examples/SceneLoader/SceneLoader.unity`
   - Play the scene - should show a button menu for selecting different UMA examples
   - NEVER CANCEL: Scene compilation takes 1-3 minutes on first play

2. **DCS Demo Validation** (Primary validation scenario):
   - From Scene Loader, click "UMA DCS Demo - Simple Setup"
   - OR directly open: `Assets/UMA/Examples/DynamicCharacterSystem Examples/UMA DCS Demo - Simple Setup.unity`
   - Play the scene
   - Test character creation:
     - Change race (Male/Female)
     - Modify DNA sliders (body proportions)
     - Change wardrobe items (clothing, hair)
     - Verify character updates in real-time
   - EXPECTED: Character should rebuild and appear correctly within 2-10 seconds per change
   - NEVER CANCEL: Character generation takes 2-10 seconds per modification

3. **Additional Critical Validation Scenes**:
   - `UMA DCS Demo - Random Characters.unity` - Test random character generation
   - `UMA Core Demo - Crowd.unity` - Test multiple character performance
   - `Blendshape Example.unity` - Test facial expressions and blendshapes
   - `AddressablesScene.unity` - Test addressable asset loading
   - `UMA Timeline Example.unity` - Test timeline integration

### Available Example Scenes (28 total)
**Core Functionality**: SceneLoader, UMA DCS Demo (Simple Setup, Random Characters, DNA Delegates)
**Advanced Features**: Addressables, Timeline, Blendshapes, Crowd Generation
**Tools**: Photo Booth, Expression Clip Editor, Slot Mesh Verification
**Physics**: Cloth Example, Ragdoll Example
**Integration**: Asset Bundles, UMA Car, Mounting Objects

## Critical Development Workflows

### When Making Code Changes
1. ALWAYS test the primary validation scenario (DCS Demo) after any changes
2. Check Unity Console for errors - UMA systems log detailed information  
3. Verify character generation completes without errors or pink textures
4. Test both male and female race variants (HumanMale/HumanFemale content)
5. Test at least 3 different wardrobe combinations
6. Validate DNA modifications work across all DNA types:
   - Skeleton modifications (bone positioning)
   - Blendshape expressions (facial features)  
   - Color variations (skin, hair, clothing)
   - Overall scaling (character size)

### When Modifying DNA or Character Systems
1. Open UMA menu: `UMA > Welcome to UMA` for diagnostic tools
2. Use `UMA > Race Updater` if race data needs refreshing  
3. Test DNA modifications in DCS Demo scene
4. Verify bone pose and expression systems still function
5. Check that all 5 DNA plugin types work correctly:
   - BlendshapeDNAConverterPlugin
   - BonePoseDNAConverterPlugin  
   - ColorDNAConverterPlugin
   - OverallScaleDNAConverterPlugin
   - SkeletonDNAConverterPlugin

### When Working with Content Assets
1. Verify both Core content (HumanMale/HumanFemale/HumanShared) and Example content
2. Test Hair, Hats, and other content categories load correctly
3. Check that shader graphs and skin shaders compile properly
4. Validate photobooth functionality if modifying image generation

### When Working with Addressables
1. Use UMA's addressable build tools: `UMA > Sample Addressables Build`
2. NEVER CANCEL: Addressable builds take 10-20 minutes. Set timeout to 30+ minutes.
3. Test addressable loading in Addressables example scene

## Project Navigation

### Key Directories
- `Assets/UMA/Core/` - Core UMA systems and scripts
- `Assets/UMA/Examples/` - All example scenes and demonstration code  
- `Assets/UMA/Content/` - UMA assets, races, clothing, materials
- `Assets/UMA/Core/Editor/` - Unity Editor extensions and tools

### Core System Locations
- **DNA System**: `Assets/UMA/Core/Scripts/DNA/` and `Assets/UMA/Core/Scripts/DNAPlugins/`
  - BlendshapeDNAConverterPlugin.cs - Facial expression DNA
  - BonePoseDNAConverterPlugin.cs - Bone positioning DNA  
  - ColorDNAConverterPlugin.cs - Color variation DNA
  - SkeletonDNAConverterPlugin.cs - Skeleton modification DNA
- **Character System**: `Assets/UMA/Examples/DynamicCharacterSystem Examples/`
- **Addressable Support**: `Assets/UMA/Core/Scripts/AddressableUtility.cs`
- **Asset Management**: `Assets/UMA/Core/Scripts/UMAAssetIndexer.cs`

### Important Files
- `Assets/UMA/Core/UMA_Core.asmdef` - Core assembly definition
- `Assets/UMA/Examples/SceneLoader/SceneLoader.unity` - Main entry point scene
- `ProjectSettings/EditorBuildSettings.asset` - Contains build scene configuration
- `Assets/UMA/Core/Scripts/UMASettings.cs` - Project-wide UMA settings

### Key Scripts Locations
- Character building: `Assets/UMA/Core/Scripts/`
- DNA systems: `Assets/UMA/Core/Scripts/DNAPlugins/`
- Editor tools: `Assets/UMA/Core/Editor/Scripts/`
- Example implementations: `Assets/UMA/Examples/ExampleScripts/`

## UMA Menu System
Access UMA tools through Unity's top menu bar (64+ menu items available):

### Essential Tools
- `UMA > Welcome to UMA` - Main diagnostic and tool dashboard
- `UMA > Race Updater` - Updates race data when needed
- `UMA > Extract T-Pose` - Extract T-pose from animated characters

### Development Tools
- `UMA > Texture Channel Combiner` - Texture processing tools  
- `UMA > Sample Addressables Build` - Addressable asset building
- `UMA > Pose Tools/Bone Pose Mixer` - Bone pose and animation tools

### Runtime Save/Load (GameObject Context Menu)
- `GameObject > UMA > Save as UMA Preset` - Save character configurations
- `GameObject > UMA > Save Atlas Textures` - Export generated textures
- `GameObject > UMA > Save as Character Text file` - Export character data
- `GameObject > UMA > Show Mesh Info` - Display mesh statistics

### Asset Creation (Assets Context Menu)  
- `Assets > Create > UMA > Core >` - Create UMA asset types
- Multiple asset creation options for slots, overlays, races, recipes

### Editor Shortcuts
- Many tools accessible via right-click context menus in Inspector
- DynamicCharacterAvatar components have extensive context menu options

## Common Issues and Solutions

### Project Opening Issues
- **Error**: "Version mismatch" when opening project
  - SOLUTION: Ensure exactly Unity 2021.3.45f1 is installed, no other versions
  - CRITICAL: UMA is version-sensitive due to shader and API dependencies

### Pink/Missing Textures
- Usually indicates missing shader or render pipeline mismatch
- Use `UMA > Welcome to UMA` diagnostic tools
- Check Unity console for shader compilation errors
- Verify render pipeline settings match project requirements

### Character Generation Failures
- Check Unity console for detailed error messages
- Common causes: Missing texture atlases, insufficient memory, corrupted assets
- Verify all required assets are present in UMA library
- Use `UMA > Race Updater` to refresh race data
- Ensure sufficient memory for texture atlas generation

### Performance Issues
- EXPECTED: Character generation is computationally intensive
- First-time generation slower due to shader compilation
- Normal generation time: 0-1 seconds per character modification
- Heavy scenes may take longer - this is normal for UMA
- Crowd scenes may require 30+ seconds for initial generation

### Assembly Compilation Errors
- Check that all assembly references are correctly resolved
- Verify Unity packages in manifest.json are compatible
- Missing addressables or burst packages are common issues
- Rebuild library if assembly compilation fails persistently

## Timing Expectations

### Scene Operations
- Scene loading: 1-3 minutes (first time), 10-30 seconds (subsequent)
- Character generation: 0-1 seconds per modification
- Addressable builds: 10-20 minutes (NEVER CANCEL)
- Shader compilation: 10-25 minutes (first time in new project)

### Project Operations  
- Initial project import: 5-10 minutes (NEVER CANCEL)
- Unity Editor startup: 2-5 minutes with UMA project
- Library rebuild: 5-15 minutes if required (NEVER CANCEL)

## Validation Checklist

Before committing changes, ALWAYS verify:
- [ ] Unity project opens without errors in Unity 2021.3.45f1
- [ ] All 5 assembly definitions compile successfully (Core, Examples, Editor, Content, Addressable_Editor)
- [ ] Scene Loader menu appears and functions correctly
- [ ] DCS Demo character creation works completely:
  - [ ] Male and female character switching
  - [ ] DNA sliders modify appearance correctly
  - [ ] Wardrobe items apply without errors  
  - [ ] Character rebuilds within 2-10 seconds
- [ ] No pink textures or missing materials visible
- [ ] Unity Console shows no UMA-related errors or warnings
- [ ] Both male and female races generate correctly
- [ ] All 5 DNA plugin types function properly
- [ ] Character generation completes within expected time ranges
- [ ] Addressable assets load correctly (if using addressables)
- [ ] Timeline integration works (if using timeline features)

## Development Environment Requirements

- Unity 6000.2.0f1 (exact version required - critical for compatibility)
- Unity Hub for project management
- Internet access to Unity domains for installation and licensing
- Minimum 8GB RAM (16GB recommended for complex scenes)
- GPU supporting Unity's render pipeline
- 10GB+ free disk space (1.6GB repository + Unity Editor + cache)
- The repository contains 28 Unity scenes and 445 C# scripts

## Branching and Version Control

- Follow git-flow model: develop → feature branches → develop → master
- Never commit directly to master or develop
- Use feature-* branch naming convention
- Master branch matches current Asset Store version
- Test thoroughly in Unity before creating pull requests

## Critical Notes for AI Development Assistants

### Environment Limitations
- **Unity Installation Required**: These instructions assume Unity Editor can be installed and run
- **Network Dependencies**: Unity Hub and Unity Editor require internet access to Unity domains
- **Sandboxed Environments**: In environments where Unity cannot be installed, focus on:
  - Code structure analysis and understanding
  - Assembly definition validation  
  - Script compilation checking
  - Documentation and code review
  - Understanding UMA architecture without running scenes

### Alternative Validation When Unity Unavailable
- Examine assembly definition references for consistency
- Review C# scripts for compilation errors using IDE analysis
- Validate project structure and file organization
- Check for missing dependencies in manifest.json
- Analyze code patterns and architecture decisions
- Focus on code quality, style, and UMA API usage patterns

Always ensure Unity project can be opened and primary validation scenarios pass before pushing changes.