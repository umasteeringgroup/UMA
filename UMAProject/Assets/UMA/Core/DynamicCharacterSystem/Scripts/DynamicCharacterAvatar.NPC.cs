using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.CharacterSystem
{
    public partial class DynamicCharacterAvatar
    {
        [Tooltip("Opt in to completed-build templates in NPC-aware spawners. BuildCharacter remains the normal editable build path.")]
        public bool useNPCBuilds;
        [NonSerialized] public string NPCBuildStatus;
        [NonSerialized] public int NPCShortcutHits;
        private bool npcStartupHandled, npcNormalBuild;
        private UMANPCBuildHandle npcOwnedTemplate, npcRequest;
        private GeneratorQualityConfiguration npcGeneratorConfiguration;
        [NonSerialized] public double NPCShortcutMilliseconds;

        /// <summary>Prepare one immutable NPC appearance, or instantiate an explicitly
        /// supplied appearance. The returned owner handle is borrowed: Retain it for a
        /// longer-lived pool. BuildCharacter remains available for ordinary edits.</summary>
        public UMANPCBuildHandle BuildNPC(UMANPCBuildHandle template = null)
        {
            CancelNPCBuild();
            if (!Application.isPlaying) { NPCBuildStatus = "Normal build: NPC templates are runtime-only."; BuildCharacter(); return null; }
            bool enabledBefore = _buildCharacterEnabled;
            _buildCharacterEnabled = false;
            try { InitialStartup(); }
            finally { _buildCharacterEnabled = enabledBefore; }
            npcStartupHandled = true;
            if (template != null && RendererCount == 0 && !dirty)
            {
                if (template.Value == null) throw new ObjectDisposedException(nameof(template));
                if (template.Value.ApplyInputs == null) throw new InvalidOperationException("This NPC template belongs to an ended session.");
            }
            string reason = NPCUnsupportedCallbacks();
            if (reason != null || RendererCount != 0 || dirty)
            {
                NPCBuildStatus = "Normal build: " + (reason ?? "Existing or already queued character.");
                if (template != null && RendererCount == 0 && !dirty) template.Value.ApplyInputs(this);
                BuildCharacter(); return null;
            }
            if (template != null && (template.IsPending || template.IsReady))
            {
                npcRequest = template.Retain();
                NPCBuildStatus = template.IsReady ? "NPC template queued" : "Waiting for the same NPC build";
                UMANPCBuildQueue.Enqueue(this);
                return template;
            }
            if (template != null)
            {
                NPCBuildStatus = "Normal build: " + (template.FailureReason ?? "Template invalidated.");
                template.Value.ApplyInputs(this);
                BuildCharacter(); return null;
            }
            npcOwnedTemplate = new UMANPCBuildHandle(this);
            npcGeneratorConfiguration = GeneratorConfiguration();
            NPCBuildStatus = "Preparing NPC template";
            npcNormalBuild = true;
            try { BuildCharacter(); }
            catch { CancelNPCBuild(); throw; }
            finally { npcNormalBuild = false; }
            return npcOwnedTemplate;
        }

        private GeneratorQualityConfiguration GeneratorConfiguration() => GeneratorQualityConfiguration.Capture(umaGenerator);

        private string NPCUnsupportedCallbacks()
        {
#if UMA_ADDRESSABLES
            return "Addressable content requires normal generation to retain its load handles.";
#else
            if (!reuseGeneratedMeshes || !reuseGeneratedTextures) return "Enable both mesh and atlas reuse for NPC templates.";
            if (keepAvatar || KeepAvatar) return "Externally retained Animator avatars require normal generation.";
            if (umaOverrideRecipe != null) return "A transient recipe/LOD override requires normal generation.";
            if (activeRace?.racedata != null && activeRace.racedata.UsesFbxRoute) return "FBX-route rigs require normal generation.";
            if (GetType() != typeof(DynamicCharacterAvatar)) return "Custom avatar subclass.";
            if (HasRuntimeDNAProviders || HasExternalSkeletonRoot) return "Custom DNA provider or external rig.";
            if (TextureOverrides.Count != 0 || VertexOverrides.Count != 0 || UVOverrides.Count != 0) return "Direct source overrides.";
            if (UMATextureEvent.HasAnyListeners(BuildCharacterBegun) || UMATextureEvent.HasAnyListeners(RecipeUpdated) ||
                UMATextureEvent.HasAnyListeners(SlotsHidden) || UMATextureEvent.HasAnyListeners(WardrobeSuppressed) ||
                UMATextureEvent.HasAnyListeners(CharacterBeforeDnaUpdated) || UMATextureEvent.HasAnyListeners(PreUpdateUMABody) ||
                UMATextureEvent.HasAnyListeners(AtlasUpdated) || UMATextureEvent.HasAnyListeners(AnimatorStateSaved) ||
                UMATextureEvent.HasAnyListeners(AnimatorStateRestored) ||
                UMATextureEvent.ListenerCount(CharacterBegun) > 1 || UMATextureEvent.ListenerCount(CharacterDnaUpdated) > 1)
                return "Custom build-stage callbacks.";
            return null;
#endif
        }

        internal void CaptureNPCBuild()
        {
            var entry = npcOwnedTemplate?.Value;
            if (entry == null || !entry.Pending) return;
            entry.Pending = false; entry.Builder = null;
            string reason = NPCUnsupportedCallbacks() ?? UMANPCSnapshot.Validate(this);
            if (reason == null && entry.Epoch != UMANPCBuildQueue.Epoch) reason = "Inputs changed during generation.";
            if (reason != null) { entry.Failure = reason; NPCBuildStatus = "Normal build: " + reason; return; }
            try
            {
                entry.Snapshot = new UMANPCSnapshot(this);
                entry.Configuration = npcGeneratorConfiguration;
                NPCBuildStatus = "Prepared NPC template";
            }
            catch (Exception exception)
            {
                entry.Failure = exception.Message;
                NPCBuildStatus = "Normal build: template capture unsupported: " + exception.Message;
                Debug.LogWarning(NPCBuildStatus, this);
            }
        }

        internal bool AdvanceNPCBuild()
        {
            if (npcRequest == null) return true;
            if (!isActiveAndEnabled) { CancelNPCBuild(); return true; }
            var entry = npcRequest.Value;
            if (entry.Pending && entry.Builder != null && entry.Builder.isActiveAndEnabled && entry.Builder.dirty &&
                entry.Epoch == UMANPCBuildQueue.Epoch) return false;
            if (entry.Pending)
            {
                entry.Pending = false;
                entry.Failure = "Source build cancelled, invalidated or did not publish a template.";
                entry.Builder = null;
            }
            var hold = npcRequest; npcRequest = null;
            try
            {
                string reason = NPCUnsupportedCallbacks();
                if (reason == null && entry.Epoch == UMANPCBuildQueue.Epoch && entry.Snapshot != null && entry.Snapshot.IsAlive &&
                    entry.Configuration.SameOutput(GeneratorConfiguration()))
                {
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    try { entry.Snapshot.Apply(this); }
                    catch (Exception exception)
                    {
                        HideAndCleanup();
                        NPCBuildStatus = "Normal build after NPC setup failure: " + exception.Message;
                        BuildCharacter();
                        return true;
                    }
                    NPCShortcutHits++;
                    NPCBuildStatus = "Instantiated completed NPC template";
                    resourceReuseStatus = NPCBuildStatus;
                    // Completion callbacks are deliberately outside the fallback transaction:
                    // a user callback must never cause a second CharacterCreated event.
                    try { CompleteNPCBuild(); }
                    finally { NPCShortcutMilliseconds += UMATime.StopwatchTicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - start); }
                }
                else
                {
                    NPCBuildStatus = "Normal build: " + (reason ?? entry.Failure ?? "Template invalidated or build settings changed.");
                    entry.ApplyInputs?.Invoke(this);
                    BuildCharacter();
                }
            }
            finally { hold.Dispose(); }
            return true;
        }

        internal void CompleteNPCBuild()
        {
            umaRace = umaRecipe.raceData;
            _isFirstSettingsBuild = false;
            InitializeExpressionPlayer();
            Show();
            FireUpdatedEvent(false);
            FireCharacterCompletedEvents();
        }

        public void CancelNPCBuild()
        {
            npcRequest?.Dispose(); npcRequest = null;
            var entry = npcOwnedTemplate?.Value;
            if (entry != null && entry.Pending) { entry.Pending = false; entry.Builder = null; entry.Failure = "Builder cancelled or destroyed."; }
            npcOwnedTemplate?.Dispose(); npcOwnedTemplate = null;
        }

        internal Action<DynamicCharacterAvatar> CaptureNPCInputs()
        {
            string race = RacePreset;
            var wardrobe = new Dictionary<string, UMATextRecipe>(WardrobeRecipes);
            var additive = new Dictionary<string, List<UMATextRecipe>>();
            foreach (var pair in AdditiveRecipes) additive.Add(pair.Key, new List<UMATextRecipe>(pair.Value));
            var collections = new Dictionary<string, UMAWardrobeCollection>(WardrobeCollections);
            var colors = new ColorValueList();
            foreach (var color in characterColors.Colors) colors.Colors.Add(color == null ? null : new ColorValue(color));
            var shapes = CopyNPCBlendShapes(blendShapeSettings);
            var included = new List<string>(forceKeepBlendshapes);
            var dna = predefinedDNA?.Clone() ?? new UMAPredefinedDNA();
            if (dna.Count == 0)
                foreach (var pair in GetDNA()) dna.AddDNA(pair.Key, pair.Value.Value);
            var extra = umaAdditionalRecipes == null ? null : (UMARecipeBase[])umaAdditionalRecipes.Clone();
            var removedTags = new HashSet<string>(forceRemovedTags);
            var removedSlots = new HashSet<string>(forceRemovedBaseSlots);
            var suppressed = new List<string>(forceSuppressedWardrobeSlots);
            var containing = new List<string>(forceSuppressSlotsContaining);
            bool ignoreHides = ignoreMeshHideAssets, slotMaterials = forceSlotMaterials;
            bool readable = markNotReadable, dynamicMesh = markDynamic, wideIndices = force32bit;
            float atlasScale = AtlasResolutionScale;
            var tpose = OverrideTpose;
            var defaultRenderer = defaultRendererAsset;
            bool blendshapes = loadBlendShapes, usedOnly = loadOnlyUsedBlendshapes, normals = loadBlendshapeNormals,
                tangents = loadBlendshapeTangents, frames = loadAllFrames;
            return target =>
            {
                bool enabledBefore = target._buildCharacterEnabled; target._buildCharacterEnabled = false;
                try { target.ChangeRaceData(race); }
                finally { target._buildCharacterEnabled = enabledBefore; }
                target.WardrobeRecipes.Clear(); foreach (var pair in wardrobe) target.WardrobeRecipes.Add(pair.Key, pair.Value);
                target.AdditiveRecipes.Clear(); foreach (var pair in additive) target.AdditiveRecipes.Add(pair.Key, new List<UMATextRecipe>(pair.Value));
                target.WardrobeCollections.Clear(); foreach (var pair in collections) target.WardrobeCollections.Add(pair.Key, pair.Value);
                target.characterColors = new ColorValueList();
                foreach (var color in colors.Colors) target.characterColors.Colors.Add(color == null ? null : new ColorValue(color));
                target.blendShapeSettings = CopyNPCBlendShapes(shapes);
                target.forceKeepBlendshapes = new List<string>(included);
                target.loadBlendShapes = blendshapes; target.loadOnlyUsedBlendshapes = usedOnly;
                target.loadBlendshapeNormals = normals; target.loadBlendshapeTangents = tangents; target.loadAllFrames = frames;
                target.predefinedDNA = dna.Clone();
                target.umaAdditionalRecipes = extra == null ? null : (UMARecipeBase[])extra.Clone();
                target.forceRemovedTags = new HashSet<string>(removedTags);
                target.forceRemovedBaseSlots = new HashSet<string>(removedSlots);
                target.forceSuppressedWardrobeSlots = new List<string>(suppressed);
                target.forceSuppressSlotsContaining = new List<string>(containing);
                target.ignoreMeshHideAssets = ignoreHides; target.forceSlotMaterials = slotMaterials;
                target.markNotReadable = readable; target.markDynamic = dynamicMesh; target.force32bit = wideIndices;
                target.AtlasResolutionScale = atlasScale; target.atlasResolutionScale = atlasScale; target.OverrideTpose = tpose;
                target.defaultRendererAsset = defaultRenderer;
            };
        }

        private static BlendShapeSettings CopyNPCBlendShapes(BlendShapeSettings source)
        {
            var copy = new BlendShapeSettings { ignoreBlendShapes = source.ignoreBlendShapes, loadAllFrames = source.loadAllFrames,
                loadNormals = source.loadNormals, loadTangents = source.loadTangents, forceBakedBlendShapeValue = source.forceBakedBlendShapeValue,
                forcedBakedBlendShapeValue = source.forcedBakedBlendShapeValue, filteredBlendshapes = new HashSet<string>(source.filteredBlendshapes) };
            foreach (var pair in source.blendShapes) copy.blendShapes.Add(pair.Key, new BlendShapeData { value = pair.Value.value, isBaked = pair.Value.isBaked });
            return copy;
        }
    }
}
