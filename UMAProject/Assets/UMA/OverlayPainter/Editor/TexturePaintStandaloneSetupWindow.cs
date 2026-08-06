using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public static class TexturePaintUdimResolver
    {
        public static bool TryResolve(SlotDataAsset selected, out List<SlotDataAsset> members, out string error)
        {
            members = new List<SlotDataAsset>();
            error = null;
            if (selected == null) { error = "Select a SlotDataAsset."; return false; }
            if (UMAMeshData.IsNullOrEmptyMeshData(selected.meshData))
            { error = $"Slot '{selected.slotName}' has no mesh data."; return false; }
            if (!selected.IsUdimMember)
            {
                members.Add(selected);
                return true;
            }
            string groupId = selected.udimGroupId?.Trim();
            string groupName = selected.udimGroupName?.Trim();
            if (string.IsNullOrEmpty(groupId)) { error = "The selected UDIM slot has an empty group ID."; return false; }

            string[] guids = AssetDatabase.FindAssets("t:SlotDataAsset");
            Dictionary<int, SlotDataAsset> byTile = new Dictionary<int, SlotDataAsset>();
            int sharedSourceSubmesh = selected.udimSourceSubmeshIndex;
            if (sharedSourceSubmesh < 0)
            { error = $"UDIM member '{selected.name}' has no source-submesh identity."; return false; }
            for (int i = 0; i < guids.Length; i++)
            {
                SlotDataAsset candidate = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (candidate == null || !string.Equals(candidate.udimGroupId?.Trim(), groupId, StringComparison.Ordinal)) continue;
                if (!candidate.IsUdimMember || candidate.udimTileNumber < 1001)
                { error = $"UDIM group '{groupId}' contains member '{candidate.name}' with incompatible metadata."; return false; }
                if (!string.Equals(candidate.udimGroupName?.Trim() ?? string.Empty, groupName ?? string.Empty,
                    StringComparison.Ordinal))
                { error = $"UDIM member '{candidate.name}' has a group name that conflicts with the selected slot."; return false; }
                if (UMAMeshData.IsNullOrEmptyMeshData(candidate.meshData))
                { error = $"UDIM member '{candidate.name}' has no mesh data."; return false; }
                if (byTile.TryGetValue(candidate.udimTileNumber, out SlotDataAsset duplicate))
                { error = $"UDIM tile {candidate.udimTileNumber} is assigned to both '{duplicate.name}' and '{candidate.name}'."; return false; }
                byTile.Add(candidate.udimTileNumber, candidate);
                // Every tile produced by one UDIM split comes from the same original submesh.
                // Sharing this value is required; a different value indicates that unrelated
                // source geometry was accidentally assigned the same UDIM group ID.
                if (candidate.udimSourceSubmeshIndex != sharedSourceSubmesh)
                { error = $"UDIM member '{candidate.name}' references source submesh {candidate.udimSourceSubmeshIndex}, " +
                    $"but this group uses source submesh {sharedSourceSubmesh}."; return false; }
            }
            if (!byTile.ContainsValue(selected))
            { error = $"The selected slot was not found in UDIM group '{groupId}'."; return false; }
            List<int> tiles = new List<int>(byTile.Keys);
            tiles.Sort();
            for (int i = 0; i < tiles.Count; i++) members.Add(byTile[tiles[i]]);
            return members.Count > 0;
        }
    }

    public sealed class TexturePaintStandaloneSetupWindow : EditorWindow
    {
        [Serializable]
        private sealed class PersistedSetupState
        {
            public int version = 2;
            public int sourceMode;
            public string umaMaterialGuid;
            public string selectedOverlayGuid;
            public int resolution = 2048;
            public bool fixupRotations;
            public Vector3 slotRotationEuler = Vector3.zero;
            public List<PersistedMemberOverlay> memberOverlays = new List<PersistedMemberOverlay>();
        }

        [Serializable]
        private sealed class PersistedMemberOverlay
        {
            public string slotGuid;
            public string overlayGuid;
        }

        [SerializeField] private SlotDataAsset selectedSlot;
        [SerializeField] private TexturePaintStandaloneSourceMode sourceMode;
        [SerializeField] private UMAMaterial umaMaterial;
        [SerializeField] private UMAMaterial selectedUmaMaterial;
        [SerializeField] private OverlayDataAsset selectedOverlay;
        [SerializeField] private int resolution = 2048;
        [SerializeField] private bool fixupRotations;
        [SerializeField] private Vector3 slotRotationEuler = Vector3.zero;
        [SerializeField] private List<OverlayDataAsset> memberOverlays = new List<OverlayDataAsset>();
        private readonly List<SlotDataAsset> members = new List<SlotDataAsset>();
        private string resolverError;
        private string validationError;
        private string loadedSetupPrefsKey;
        private Vector2 scroll;

        public static void ShowForSlot(SlotDataAsset slot)
        {
            if (slot == null) return;
            TexturePaintStandaloneSetupWindow window = CreateInstance<TexturePaintStandaloneSetupWindow>();
            window.titleContent = new GUIContent("Overlay Painter Setup");
            window.minSize = new Vector2(470f, 390f);
            window.selectedSlot = slot;
            window.ResolveMembers();
            window.LoadSetupPreferences();
            window.ShowUtility();
        }

        private static string SetupPrefsPrefix => "UMA.TexturePaint.StandaloneSetup." +
            Hash128.Compute(Application.dataPath) + ".";

        private void OnEnable()
        {
            ResolveMembers();
            LoadSetupPreferences();
        }

        private void OnDisable() => SaveSetupPreferences();

        private void ResolveMembers()
        {
            members.Clear();
            resolverError = null;
            if (selectedSlot == null) return;
            if (!TexturePaintUdimResolver.TryResolve(selectedSlot, out List<SlotDataAsset> resolved, out resolverError)) return;
            members.AddRange(resolved);
            while (memberOverlays.Count < members.Count) memberOverlays.Add(null);
            if (memberOverlays.Count > members.Count) memberOverlays.RemoveRange(members.Count, memberOverlays.Count - members.Count);
            int selectedIndex = members.IndexOf(selectedSlot);
            if (selectedIndex >= 0 && selectedOverlay != null) memberOverlays[selectedIndex] = selectedOverlay;
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Standalone Slot Session", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This opens the slot directly from UMAMeshData. It does not find, generate, or modify an avatar.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Slot", selectedSlot, typeof(SlotDataAsset), false);
            if (!string.IsNullOrEmpty(resolverError)) EditorGUILayout.HelpBox(resolverError, MessageType.Error);
            else if (selectedSlot != null && selectedSlot.IsUdimMember)
                EditorGUILayout.HelpBox($"UDIM group '{selectedSlot.udimGroupName}' will open as one paint target ({members.Count} tiles).", MessageType.Info);

            EditorGUILayout.Space();
            bool setupChanged = false;
            TexturePaintStandaloneSourceMode previousSourceMode = sourceMode;
            sourceMode = (TexturePaintStandaloneSourceMode)EditorGUILayout.EnumPopup("Source", sourceMode);
            setupChanged |= sourceMode != previousSourceMode;
            if (sourceMode == TexturePaintStandaloneSourceMode.UMAMaterial)
            {
                UMAMaterial previousMaterial = selectedUmaMaterial;
                selectedUmaMaterial = (UMAMaterial)EditorGUILayout.ObjectField("UMA Material", selectedUmaMaterial,
                    typeof(UMAMaterial), false);
                setupChanged |= selectedUmaMaterial != previousMaterial;
                umaMaterial = selectedUmaMaterial;
                EditorGUILayout.HelpBox("A removable Default White flat Fill layer will be created. Other material channels start at semantic-neutral values.", MessageType.None);
            }
            else
            {
                OverlayDataAsset previousSelectedOverlay = selectedOverlay;
                selectedOverlay = (OverlayDataAsset)EditorGUILayout.ObjectField("Selected Tile Overlay", selectedOverlay,
                    typeof(OverlayDataAsset), false);
                if (selectedOverlay != previousSelectedOverlay)
                {
                    setupChanged = true;
                    umaMaterial = selectedOverlay != null ? selectedOverlay.material : null;
                    int selectedIndex = members.IndexOf(selectedSlot);
                    if (selectedIndex >= 0) memberOverlays[selectedIndex] = selectedOverlay;
                }
                if (members.Count > 1)
                {
                    EditorGUILayout.LabelField("UDIM Member Sources", EditorStyles.boldLabel);
                    for (int i = 0; i < members.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField($"{members[i].udimTileNumber}  {members[i].slotName}", GUILayout.Width(190f));
                            int selectedIndex = members.IndexOf(selectedSlot);
                            using (new EditorGUI.DisabledScope(i == selectedIndex))
                            {
                                OverlayDataAsset previousMemberOverlay = memberOverlays[i];
                                memberOverlays[i] = (OverlayDataAsset)EditorGUILayout.ObjectField(memberOverlays[i],
                                    typeof(OverlayDataAsset), false);
                                setupChanged |= memberOverlays[i] != previousMemberOverlay;
                            }
                            if (memberOverlays[i] == null) GUILayout.Label("Neutral base", GUILayout.Width(80f));
                        }
                    }
                }
            }

            int[] resolutions = { 512, 1024, 2048, 4096 };
            string[] labels = { "512", "1024", "2048", "4096" };
            int resolutionIndex = Array.IndexOf(resolutions, resolution);
            int previousResolution = resolution;
            resolution = resolutions[EditorGUILayout.Popup("Working Resolution", Mathf.Max(0, resolutionIndex), labels)];
            setupChanged |= resolution != previousResolution;
            bool previousFixupRotations = fixupRotations;
            Vector3 previousRotationEuler = slotRotationEuler;
            fixupRotations = EditorGUILayout.Toggle(new GUIContent("Additional Rotation",
                "Apply an optional rotation after SlotToMesh reconstructs canonical character space from the slot's " +
                "bones and bind poses. This does not modify the SlotDataAsset."), fixupRotations);
            using (new EditorGUI.DisabledScope(!fixupRotations))
                slotRotationEuler = EditorGUILayout.Vector3Field(new GUIContent("Adjustment (Degrees)",
                    "Euler angles applied after canonical SlotToMesh conversion."),
                    slotRotationEuler);
            setupChanged |= fixupRotations != previousFixupRotations || slotRotationEuler != previousRotationEuler;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset Rotation", GUILayout.Width(110f)))
                {
                    slotRotationEuler = MeshReconstructor.DefaultStandaloneSlotRotationEuler;
                    setupChanged = true;
                }
            }
            if (setupChanged) SaveSetupPreferences();
            DrawCapabilitySummary();
            if (!string.IsNullOrEmpty(validationError)) EditorGUILayout.HelpBox(validationError, MessageType.Error);
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(90f))) Close();
                using (new EditorGUI.DisabledScope(!ValidateSetup(out _)))
                    if (GUILayout.Button("Open", GUILayout.Width(110f))) OpenStage();
            }
        }

        private void DrawCapabilitySummary()
        {
            validationError = null;
            if (!ValidateInputs(out validationError)) return;
            Texture[] sources = SourceTextures(sourceMode == TexturePaintStandaloneSourceMode.OverlayDataAsset ? selectedOverlay : null);
            TexturePaintMaterialCapabilityDescriptor descriptor = TexturePaintMaterialCapabilityService.Compile(
                umaMaterial, umaMaterial.material, sources, sourceMode == TexturePaintStandaloneSourceMode.UMAMaterial);
            MessageType type = descriptor.IsSupported ? MessageType.Info : MessageType.Error;
            string pipeline = descriptor.pipeline == TexturePaintMaterialPipeline.HighDefinition ? "HDRP" : "URP";
            string summary = $"{pipeline} | {umaMaterial.material.shader.name} | {descriptor.Channels.Count} physical channels";
            if (!descriptor.IsSupported) summary += "\n" + descriptor.FailureSummary();
            EditorGUILayout.HelpBox(summary, type);
            for (int i = 0; i < descriptor.Channels.Count; i++)
            {
                TexturePaintMaterialChannelCapability channel = descriptor.Channels[i];
                string source = channel.sourceTexture != null
                    ? $"{channel.sourceTexture.name} ({channel.sourceTexture.width}x{channel.sourceTexture.height})"
                    : "Semantic neutral base";
                EditorGUILayout.LabelField($"{i}: {channel.materialProperty}", source, EditorStyles.miniLabel);
            }
            if (descriptor.Channels.Count > 0 &&
                !ContainsChannel(descriptor.Channels[0].LogicalChannels, TexturePaintChannel.Albedo))
                EditorGUILayout.HelpBox("The first physical channel is not an albedo/color channel. Default White will use its first editable logical component.", MessageType.Warning);
            if (TryGetGroupBounds(out Bounds bounds))
                EditorGUILayout.LabelField("Preview Bounds", $"Center {bounds.center:F3} | Size {bounds.size:F3}", EditorStyles.miniLabel);
            if (!descriptor.IsSupported) validationError = descriptor.FailureSummary();
        }

        private static bool ContainsChannel(IReadOnlyList<TexturePaintChannel> channels,
            TexturePaintChannel expected)
        {
            for (int i = 0; channels != null && i < channels.Count; i++)
                if (channels[i] == expected) return true;
            return false;
        }

        private bool TryGetGroupBounds(out Bounds bounds)
        {
            bounds = default;
            bool initialized = false;
            Matrix4x4 additionalRotation = fixupRotations
                ? Matrix4x4.Rotate(Quaternion.Euler(slotRotationEuler))
                : Matrix4x4.identity;
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                SlotDataAsset member = members[memberIndex];
                Vector3[] vertices = member?.meshData?.vertices;
                SlotDataAsset.TryGetCanonicalMeshFromRootMatrix(member?.meshData,
                    member != null ? member.slotName : "Standalone Slot", out Matrix4x4 canonicalMeshFromRoot);
                Matrix4x4 meshTransform = additionalRotation * canonicalMeshFromRoot;
                for (int vertexIndex = 0; vertices != null && vertexIndex < vertices.Length; vertexIndex++)
                {
                    Vector3 position = meshTransform.MultiplyPoint3x4(vertices[vertexIndex]);
                    if (!initialized) { bounds = new Bounds(position, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(position);
                }
            }
            return initialized;
        }

        private bool ValidateSetup(out string error)
        {
            if (!ValidateInputs(out error)) return false;
            TexturePaintMaterialCapabilityDescriptor descriptor = TexturePaintMaterialCapabilityService.Compile(
                umaMaterial, umaMaterial.material,
                SourceTextures(sourceMode == TexturePaintStandaloneSourceMode.OverlayDataAsset ? selectedOverlay : null),
                sourceMode == TexturePaintStandaloneSourceMode.UMAMaterial);
            if (!descriptor.IsSupported) { error = descriptor.FailureSummary(); return false; }
            return true;
        }

        private bool ValidateInputs(out string error)
        {
            error = resolverError;
            if (!string.IsNullOrEmpty(error)) return false;
            if (selectedSlot == null || members.Count == 0) { error = "No slot group is resolved."; return false; }
            if (sourceMode == TexturePaintStandaloneSourceMode.OverlayDataAsset)
            {
                if (selectedOverlay == null) { error = "Select an OverlayDataAsset for the selected tile."; return false; }
                umaMaterial = selectedOverlay.material;
            }
            if (umaMaterial == null || umaMaterial.material == null)
            { error = "Select a UMAMaterial with an active render-pipeline material."; return false; }
            int channelCount = umaMaterial.channels != null ? umaMaterial.channels.Length : 0;
            if (channelCount == 0) { error = "The UMAMaterial declares no channels."; return false; }
            if (sourceMode == TexturePaintStandaloneSourceMode.OverlayDataAsset)
            {
                for (int i = 0; i < memberOverlays.Count; i++)
                {
                    OverlayDataAsset overlay = memberOverlays[i];
                    if (overlay == null) continue;
                    if (overlay.material != umaMaterial)
                    { error = $"Overlay '{overlay.name}' uses a different UMAMaterial."; return false; }
                    if (overlay.textureList == null || overlay.textureList.Length != channelCount)
                    { error = $"Overlay '{overlay.name}' has {overlay.textureList?.Length ?? 0} textures; {channelCount} are required."; return false; }
                }
            }
            return true;
        }

        private Texture[] SourceTextures(OverlayDataAsset overlay)
        {
            int count = umaMaterial?.channels != null ? umaMaterial.channels.Length : 0;
            Texture[] result = new Texture[count];
            if (overlay?.textureList != null)
                Array.Copy(overlay.textureList, result, Mathf.Min(count, overlay.textureList.Length));
            return result;
        }

        private void OpenStage()
        {
            if (!ValidateSetup(out validationError)) return;
            TexturePaintLaunchContext context = new TexturePaintLaunchContext
            {
                kind = TexturePaintLaunchKind.StandaloneSlot,
                sourceMode = sourceMode,
                selectedSlot = selectedSlot,
                umaMaterial = umaMaterial,
                selectedSlotGuid = GuidFor(selectedSlot),
                umaMaterialGuid = GuidFor(umaMaterial),
                udimGroupId = selectedSlot.IsUdimMember ? selectedSlot.udimGroupId : string.Empty,
                resolution = resolution,
                fixupRotations = fixupRotations,
                slotRotationEuler = slotRotationEuler
            };
            for (int i = 0; i < members.Count; i++)
            {
                OverlayDataAsset overlay = sourceMode == TexturePaintStandaloneSourceMode.OverlayDataAsset ? memberOverlays[i] : null;
                context.members.Add(new TexturePaintStandaloneMemberContext
                {
                    slot = members[i], overlay = overlay, slotGuid = GuidFor(members[i]), overlayGuid = GuidFor(overlay),
                    tileNumber = members[i].IsUdimMember ? members[i].udimTileNumber : 0,
                    sourceFingerprint = Fingerprint(members[i], overlay)
                });
            }
            Close();
            TexturePaintStageWindow.ShowStage(context);
        }

        private string GetSetupPrefsKey()
        {
            if (selectedSlot == null) return string.Empty;
            string identity = selectedSlot.IsUdimMember
                ? "udim|" + (selectedSlot.udimGroupId ?? string.Empty).Trim()
                : "slot|" + GuidFor(selectedSlot);
            return SetupPrefsPrefix + Hash128.Compute(identity);
        }

        private void LoadSetupPreferences()
        {
            string key = GetSetupPrefsKey();
            if (string.IsNullOrEmpty(key)) return;
            loadedSetupPrefsKey = key;
            if (!EditorPrefs.HasKey(key))
            {
                if (selectedUmaMaterial == null && sourceMode == TexturePaintStandaloneSourceMode.UMAMaterial)
                    selectedUmaMaterial = umaMaterial;
                return;
            }

            try
            {
                PersistedSetupState state = JsonUtility.FromJson<PersistedSetupState>(EditorPrefs.GetString(key));
                if (state == null || state.version < 1 || state.version > 2) return;
                bool legacyRawMeshRotation = state.version == 1;
                sourceMode = Enum.IsDefined(typeof(TexturePaintStandaloneSourceMode), state.sourceMode)
                    ? (TexturePaintStandaloneSourceMode)state.sourceMode
                    : TexturePaintStandaloneSourceMode.UMAMaterial;
                selectedUmaMaterial = LoadAssetByGuid<UMAMaterial>(state.umaMaterialGuid);
                resolution = Array.IndexOf(new[] { 512, 1024, 2048, 4096 }, state.resolution) >= 0
                    ? state.resolution
                    : 2048;
                // Version 1 angles compensated for raw vertices and must not be reapplied after
                // canonical bone/bind-pose reconstruction.
                fixupRotations = !legacyRawMeshRotation && state.fixupRotations;
                slotRotationEuler = legacyRawMeshRotation ? Vector3.zero : state.slotRotationEuler;

                Dictionary<string, string> overlayBySlot = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; state.memberOverlays != null && i < state.memberOverlays.Count; i++)
                {
                    PersistedMemberOverlay entry = state.memberOverlays[i];
                    if (entry != null && !string.IsNullOrEmpty(entry.slotGuid))
                        overlayBySlot[entry.slotGuid] = entry.overlayGuid;
                }
                while (memberOverlays.Count < members.Count) memberOverlays.Add(null);
                for (int i = 0; i < members.Count; i++)
                {
                    memberOverlays[i] = overlayBySlot.TryGetValue(GuidFor(members[i]), out string overlayGuid)
                        ? LoadAssetByGuid<OverlayDataAsset>(overlayGuid)
                        : null;
                }
                int selectedIndex = members.IndexOf(selectedSlot);
                selectedOverlay = selectedIndex >= 0 && selectedIndex < memberOverlays.Count
                    ? memberOverlays[selectedIndex]
                    : LoadAssetByGuid<OverlayDataAsset>(state.selectedOverlayGuid);
                umaMaterial = sourceMode == TexturePaintStandaloneSourceMode.OverlayDataAsset
                    ? (selectedOverlay != null ? selectedOverlay.material : null)
                    : selectedUmaMaterial;
                if (legacyRawMeshRotation) SaveSetupPreferences();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Overlay Painter setup preferences for '{selectedSlot.name}' could not be restored: {exception.Message}");
            }
        }

        private void SaveSetupPreferences()
        {
            string key = GetSetupPrefsKey();
            if (string.IsNullOrEmpty(key) || (loadedSetupPrefsKey != null && loadedSetupPrefsKey != key)) return;
            loadedSetupPrefsKey = key;
            PersistedSetupState state = new PersistedSetupState
            {
                sourceMode = (int)sourceMode,
                umaMaterialGuid = GuidFor(selectedUmaMaterial),
                selectedOverlayGuid = GuidFor(selectedOverlay),
                resolution = resolution,
                fixupRotations = fixupRotations,
                slotRotationEuler = slotRotationEuler
            };
            for (int i = 0; i < members.Count; i++)
            {
                state.memberOverlays.Add(new PersistedMemberOverlay
                {
                    slotGuid = GuidFor(members[i]),
                    overlayGuid = i < memberOverlays.Count ? GuidFor(memberOverlays[i]) : string.Empty
                });
            }
            EditorPrefs.SetString(key, JsonUtility.ToJson(state));
        }

        private static T LoadAssetByGuid<T>(string guid) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string GuidFor(UnityEngine.Object asset) => asset == null ? string.Empty :
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        private static string Fingerprint(SlotDataAsset slot, OverlayDataAsset overlay)
        {
            string slotPath = AssetDatabase.GetAssetPath(slot);
            string overlayPath = AssetDatabase.GetAssetPath(overlay);
            return Hash128.Compute(string.Join("|", GuidFor(slot), slot != null ? slot.udimTileNumber.ToString() : "0",
                string.IsNullOrEmpty(slotPath) ? string.Empty : AssetDatabase.GetAssetDependencyHash(slotPath).ToString(),
                GuidFor(overlay), string.IsNullOrEmpty(overlayPath) ? string.Empty : AssetDatabase.GetAssetDependencyHash(overlayPath).ToString())).ToString();
        }
    }
}
