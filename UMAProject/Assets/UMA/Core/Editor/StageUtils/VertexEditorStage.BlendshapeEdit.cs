using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Blendshape editing for Sculpt mode: load a blendshape the character's slots already have, sculpt
    /// with the shape fully dialled in, and save the strokes back into that blendshape.
    /// </summary>
    /// <remarks>
    /// The stage sculpts a baked copy of the character's mesh: skinned, in the pose and with the DNA
    /// the character has. Blendshape deltas live before skinning, in each slot's own mesh space, and
    /// for a slot worn through legacy rig conversion in the mesh space of the original slot. The
    /// maps between those spaces are worked out here, per vertex, so what is saved reproduces what
    /// was sculpted.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private sealed class BlendshapeEditSlot
        {
            public SlotData slot;
            /// <summary>The project asset the shape is read from and saved to.</summary>
            public SlotDataAsset source;
            public UMABlendShapeSlotSpace space;   // null when the slot could not be placed on the race's rig
            public int start;
            public int count;
            public bool hadShape;
        }

        [SerializeField] private string editBlendshapeName = string.Empty;
        [SerializeField] private bool showBlendshapeEditSection = true;
        [SerializeField] private string editBlendshapeChoice = string.Empty;
        [SerializeField] private string blendshapeEditStatus = string.Empty;
        [SerializeField] private MessageType blendshapeEditStatusType = MessageType.Info;

        private string pendingBlendshapeEdit;
        private IDictionary<SlotDataAsset, UMABlendShape> pendingUnsavedShapes;
        // The stage built its own character (see ShowStageForSlots) and removes it when it closes.
        private bool ownsTemporaryCharacter;
        // The slots put on the temporary character, with the overlay each is drawn with. The avatar
        // has no wardrobe, so rebuilding its recipe drops them; RestoreTemporarySlots puts them back.
        private readonly List<KeyValuePair<SlotDataAsset, OverlayDataAsset>> temporarySlots = new List<KeyValuePair<SlotDataAsset, OverlayDataAsset>>();
        // Shapes being edited that are not on their slot assets yet: written by the next save.
        private readonly Dictionary<SlotDataAsset, UMABlendShape> unsavedShapes = new Dictionary<SlotDataAsset, UMABlendShape>();
        private Vector3[] editRestVertices;
        private readonly List<BlendshapeEditSlot> editSlots = new List<BlendshapeEditSlot>();
        // Per baked vertex: how a delta in the worn slot's mesh space moves the baked vertex.
        private Matrix4x4[] bakedSkin;
        private Mesh bakedSkinMesh;
        private float bakedSkinError;
        private UMABlendShapeRigContext blendshapeRigContext;
        private RaceData blendshapeRigContextRace;
        private readonly Dictionary<SlotDataAsset, UMABlendShapeSlotSpace> blendshapeSpaces = new Dictionary<SlotDataAsset, UMABlendShapeSlotSpace>();
        private string[] blendshapeNameCache;
        private Mesh blendshapeNameCacheMesh;

        /// <summary>True while a blendshape is loaded into the sculpt.</summary>
        public bool IsEditingBlendshape => !string.IsNullOrEmpty(editBlendshapeName) && editRestVertices != null;

        /// <summary>The blendshape loaded into the sculpt, or an empty string.</summary>
        public string EditedBlendshapeName => IsEditingBlendshape ? editBlendshapeName : string.Empty;

        /// <summary>What the last blendshape edit action reported.</summary>
        public string BlendshapeEditStatus => blendshapeEditStatus;

        // --- spaces ----------------------------------------------------------------------------------

        /// <summary>
        /// The project asset behind a slot as the character wears it. A race with prebaked blendshapes
        /// wears baked copies, and a legacy slot is worn as a converted copy; neither is where a
        /// blendshape belongs.
        /// </summary>
        private static SlotDataAsset ResolveBlendshapeSourceSlot(SlotDataAsset worn)
        {
            if (worn == null)
            {
                return null;
            }
            SlotDataAsset source = worn;
            UMAAssetIndexer index = UMAAssetIndexer.Instance;
            if (worn.isBaked && !string.IsNullOrEmpty(worn.sourceSlot) && worn.sourceSlot != worn.slotName)
            {
                source = index != null ? index.GetAsset<SlotDataAsset>(worn.sourceSlot) : null;
            }
            else if (!EditorUtility.IsPersistent(worn))
            {
                source = index != null ? index.GetAsset<SlotDataAsset>(worn.slotName) : null;
            }
            if (source == null || !EditorUtility.IsPersistent(source) || UMAMeshData.IsNullOrEmptyMeshData(source.meshData) ||
                UMAMeshData.IsNullOrEmptyMeshData(worn.meshData) || source.meshData.vertexCount != worn.meshData.vertexCount)
            {
                return null;
            }
            return source;
        }

        private UMABlendShapeSlotSpace GetBlendshapeSpace(SlotDataAsset source)
        {
            if (source == null)
            {
                return null;
            }
            RaceData race = thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.umaRecipe != null ? thisDCA.umaData.umaRecipe.raceData : null;
            if (race != blendshapeRigContextRace)
            {
                blendshapeRigContextRace = race;
                blendshapeSpaces.Clear();
                if (!UMABlendShapeRigContext.TryCreate(race, out blendshapeRigContext, out _))
                {
                    blendshapeRigContext = null;
                }
            }
            if (blendshapeRigContext == null)
            {
                return null;
            }
            if (!blendshapeSpaces.TryGetValue(source, out UMABlendShapeSlotSpace space))
            {
                if (!UMABlendShapeSlotSpace.TryCreate(source, blendshapeRigContext, out space, out _))
                {
                    space = null;
                }
                blendshapeSpaces[source] = space;
            }
            return space;
        }

        /// <summary>
        /// Works out each baked vertex's skinning matrix from the character's renderer, and checks it by
        /// skinning the renderer's own mesh and comparing with the baked mesh the stage shows.
        /// </summary>
        private bool EnsureBakedSkin(out string error)
        {
            error = null;
            if (bakedSkin != null && ReferenceEquals(bakedSkinMesh, BakedMesh))
            {
                return true;
            }
            bakedSkin = null;
            bakedSkinMesh = null;
            SkinnedMeshRenderer renderer = stageSkinnedMeshRenderer;
            Mesh shared = renderer != null ? renderer.sharedMesh : null;
            if (BakedMesh == null || shared == null || shared.vertexCount != BakedMesh.vertexCount)
            {
                error = "The character's mesh is not available.";
                return false;
            }
            Transform[] bones = renderer.bones;
            Matrix4x4[] bindPoses = shared.bindposes;
            var perBone = new Matrix4x4[bindPoses.Length];
            Matrix4x4 toRenderer = renderer.transform.worldToLocalMatrix;
            for (int i = 0; i < perBone.Length; i++)
            {
                perBone[i] = i < bones.Length && bones[i] != null ? toRenderer * bones[i].localToWorldMatrix * bindPoses[i] : Matrix4x4.identity;
            }

            int count = shared.vertexCount;
            var matrices = new Matrix4x4[count];
            using (Unity.Collections.NativeArray<byte> perVertex = shared.GetBonesPerVertex())
            using (Unity.Collections.NativeArray<BoneWeight1> weights = shared.GetAllBoneWeights())
            {
                if (perVertex.Length != count)
                {
                    error = "The character's mesh has no bone weights.";
                    return false;
                }
                // Unity skins with at most this many influences per vertex (the strongest ones, which the
                // mesh lists first) and scales them to sum to one. The character is drawn, and was baked,
                // that way, so the matrices follow the same rule.
                int limit = SkinInfluenceLimit(renderer);
                int cursor = 0;
                for (int v = 0; v < count; v++)
                {
                    Matrix4x4 blended = default;
                    float total = 0f;
                    int influences = perVertex[v];
                    for (int k = 0; k < influences; k++)
                    {
                        BoneWeight1 weight = weights[cursor++];
                        if (k < limit && weight.weight > 0f && weight.boneIndex >= 0 && weight.boneIndex < perBone.Length)
                        {
                            Matrix4x4 bone = perBone[weight.boneIndex];
                            for (int e = 0; e < 16; e++)
                            {
                                blended[e] += bone[e] * weight.weight;
                            }
                            total += weight.weight;
                        }
                    }
                    if (total > 1e-6f)
                    {
                        float scale = 1f / total;
                        for (int e = 0; e < 16; e++)
                        {
                            blended[e] *= scale;
                        }
                        matrices[v] = blended;
                    }
                    else
                    {
                        matrices[v] = Matrix4x4.identity;
                    }
                }
            }

            // Check against what the renderer baked: the same vertices, with any blendshape weights it has set.
            Vector3[] sourceVertices = shared.vertices;
            var deltas = new Vector3[count];
            Vector3[] frame = null;
            for (int s = 0; s < shared.blendShapeCount; s++)
            {
                float weight = renderer.GetBlendShapeWeight(s);
                if (Mathf.Abs(weight) < 1e-4f)
                {
                    continue;
                }
                frame = frame ?? new Vector3[count];
                int last = shared.GetBlendShapeFrameCount(s) - 1;
                shared.GetBlendShapeFrameVertices(s, last, frame, null, null);
                float scale = weight / Mathf.Max(1e-4f, shared.GetBlendShapeFrameWeight(s, last));
                for (int v = 0; v < count; v++)
                {
                    deltas[v] += frame[v] * scale;
                }
            }
            Vector3[] rest = GetRestBakedVertices();
            float worst = 0f;
            for (int v = 0; v < count; v++)
            {
                worst = Mathf.Max(worst, (matrices[v].MultiplyPoint3x4(sourceVertices[v] + deltas[v]) - rest[v]).sqrMagnitude);
            }
            bakedSkinError = Mathf.Sqrt(worst);
            if (bakedSkinError > 0.002f)
            {
                error = $"The character's mesh could not be matched to its skeleton (off by {bakedSkinError * 1000f:0.0} mm). Rebuild the character and open the editor again.";
                return false;
            }
            for (int v = 0; v < count; v++)
            {
                // Only the linear part matters for deltas.
                matrices[v].SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                matrices[v].SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            }
            bakedSkin = matrices;
            bakedSkinMesh = BakedMesh;
            return true;
        }

        /// <summary>How many bone influences per vertex Unity uses when it skins this renderer.</summary>
        private static int SkinInfluenceLimit(SkinnedMeshRenderer renderer)
        {
            switch (renderer.quality)
            {
                case SkinQuality.Bone1: return 1;
                case SkinQuality.Bone2: return 2;
                case SkinQuality.Bone4: return 4;
            }
            switch (QualitySettings.skinWeights)
            {
                case SkinWeights.OneBone: return 1;
                case SkinWeights.TwoBones: return 2;
                case SkinWeights.FourBones: return 4;
                default: return 255;
            }
        }

        /// <summary>The baked mesh as it was before any stroke, and before a blendshape was loaded.</summary>
        private Vector3[] GetRestBakedVertices()
        {
            if (editRestVertices != null && editRestVertices.Length == BakedMesh.vertexCount)
            {
                return editRestVertices;
            }
            Vector3[] vertices = BakedMesh.vertices;
            foreach (SculptSlotEditState state in sculptSlotStates.Values)
            {
                if (state != null && state.originalVertices != null && state.start >= 0 && state.start + state.count <= vertices.Length)
                {
                    Array.Copy(state.originalVertices, 0, vertices, state.start, state.count);
                }
            }
            return vertices;
        }

        /// <summary>
        /// Turns movement of a slot's baked vertices into deltas in the mesh space of the slot's project
        /// asset: back through the character's skinning, and for a legacy slot back through rig conversion.
        /// </summary>
        private bool TryMapBakedDeltasToSource(SlotData slot, int start, Vector3[] bakedDeltas, out SlotDataAsset source,
            out UMABlendShapeSlotSpace space, out Vector3[] ownDeltas, out string error)
        {
            ownDeltas = null;
            space = null;
            source = slot != null ? ResolveBlendshapeSourceSlot(slot.asset) : null;
            if (source == null)
            {
                error = "Its SlotDataAsset in the project could not be found, or has another vertex count.";
                return false;
            }
            if (!EnsureBakedSkin(out error))
            {
                return false;
            }
            space = GetBlendshapeSpace(source);
            bool converted = !ReferenceEquals(source, slot.asset) && !slot.asset.isBaked;
            if (converted && (space == null || !space.ConvertedFromLegacy))
            {
                error = "The character wears a converted copy of this slot, and the conversion could not be worked out.";
                return false;
            }
            ownDeltas = new Vector3[bakedDeltas.Length];
            for (int i = 0; i < bakedDeltas.Length; i++)
            {
                if (bakedDeltas[i] == Vector3.zero)
                {
                    continue;
                }
                Matrix4x4 skin = bakedSkin[start + i];
                Vector3 worn = Mathf.Abs(skin.determinant) > 1e-12f ? skin.inverse.MultiplyVector(bakedDeltas[i]) : bakedDeltas[i];
                ownDeltas[i] = space != null ? space.WornToOwnSpace(i, worn) : worn;
            }
            return true;
        }

        /// <summary>
        /// The current slot's mesh data with its sculpt applied, for the Save Slot Mesh commands. The
        /// sculpt is taken back through the character's skinning into mesh space first; without that the
        /// saved mesh moves in another direction than was sculpted wherever mesh space and the character
        /// are turned against each other (the UMA 3 races), or by another amount under DNA bone scale.
        /// </summary>
        /// <param name="intoProjectAsset">
        /// True: the result is for the slot's asset in the project (<paramref name="basis"/>), which for a
        /// race-baked copy is its base slot and for a slot worn through legacy rig conversion the
        /// original slot. False: the result is the slot as the character wears it, for a new slot.
        /// </param>
        private bool TryBuildSculptedMeshData(bool intoProjectAsset, out SlotDataAsset basis, out UMAMeshData meshData, out string error)
        {
            basis = null;
            meshData = null;
            if (sculptSlot == null || sculptSlot.asset == null || BakedMesh == null || sculptOriginalVertices == null ||
                sculptSlotStart < 0 || sculptSlotStart + sculptSlotVertexCount > BakedMesh.vertexCount || !HasCurrentSculptChanges())
            {
                error = "Nothing has been sculpted on the current slot, or its topology no longer matches the sculpt preview.";
                return false;
            }
            int count = sculptSlotVertexCount;
            int start = sculptSlotStart;
            Vector3[] current = BakedMesh.vertices;
            var bakedDeltas = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 delta = current[start + i] - sculptOriginalVertices[i];
                bakedDeltas[i] = delta.sqrMagnitude > 1e-12f ? delta : Vector3.zero;
            }

            UMABlendShapeSlotSpace space = null;
            Vector3[] deltas;
            if (intoProjectAsset)
            {
                if (!TryMapBakedDeltasToSource(sculptSlot, start, bakedDeltas, out basis, out space, out deltas, out error))
                {
                    return false;
                }
            }
            else
            {
                if (!EnsureBakedSkin(out error))
                {
                    return false;
                }
                basis = sculptSlot.asset;
                deltas = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    deltas[i] = BakedToWornSpace(start + i, bakedDeltas[i]);
                }
            }
            if (UMAMeshData.IsNullOrEmptyMeshData(basis.meshData))
            {
                error = "The slot's MeshData is unavailable.";
                return false;
            }
            meshData = basis.meshData.DeepCopy();
            if (meshData == null || meshData.vertices == null || meshData.vertices.Length != count)
            {
                meshData = null;
                error = "The slot's topology no longer matches the sculpt preview.";
                return false;
            }

            Vector3[] normals = BakedMesh.normals;
            bool hasNormals = meshData.normals != null && meshData.normals.Length == count && sculptOriginalNormals != null &&
                              sculptOriginalNormals.Length == count && normals != null && start + count <= normals.Length;
            for (int i = 0; i < count; i++)
            {
                meshData.vertices[i] += deltas[i];
                if (!hasNormals)
                {
                    continue;
                }
                Vector3 normalDelta = normals[start + i] - sculptOriginalNormals[i];
                if (normalDelta.sqrMagnitude < 1e-12f)
                {
                    continue;
                }
                Vector3 mapped = BakedToWornSpace(start + i, normalDelta);
                if (intoProjectAsset && space != null)
                {
                    mapped = space.WornToOwnSpace(i, mapped);
                }
                Vector3 adjusted = meshData.normals[i] + mapped;
                if (adjusted.sqrMagnitude > 1e-12f)
                {
                    meshData.normals[i] = adjusted.normalized;
                }
            }
            error = null;
            return true;
        }

        /// <summary>A vector on the baked mesh, taken back through that vertex's skinning into the worn slot's mesh space.</summary>
        private Vector3 BakedToWornSpace(int bakedIndex, Vector3 vector)
        {
            if (vector == Vector3.zero || bakedSkin == null)
            {
                return vector;
            }
            Matrix4x4 skin = bakedSkin[bakedIndex];
            return Mathf.Abs(skin.determinant) > 1e-12f ? skin.inverse.MultiplyVector(vector) : vector;
        }

        /// <summary>Puts sculpted mesh data on a slot asset and saves that asset. One Undo step.</summary>
        private static void WriteSculptedMeshData(SlotDataAsset target, UMAMeshData meshData)
        {
            Undo.RecordObject(target, "Overwrite Slot MeshData From Sculpt");
            target.meshData = meshData;
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            // Converted copies of a legacy slot were made from the mesh as it was.
            UMALegacySlotConverter.ClearCache();
        }

        /// <summary>
        /// Writes the current slot's sculpt into the slot's asset in the project, without asking. This is
        /// what Save slot modifications to base slot does after its warning. Returns the asset written.
        /// </summary>
        public SlotDataAsset SaveSculptIntoSlotAsset(out string message)
        {
            EndSculptStroke(true);
            if (!TryBuildSculptedMeshData(true, out SlotDataAsset target, out UMAMeshData meshData, out message))
            {
                return null;
            }
            WriteSculptedMeshData(target, meshData);
            message = "Saved the sculpt into '" + target.name + "'.";
            return target;
        }

        private static UMABlendShape FindBlendshape(UMAMeshData meshData, string name)
        {
            if (meshData == null || meshData.blendShapes == null)
            {
                return null;
            }
            foreach (UMABlendShape shape in meshData.blendShapes)
            {
                if (shape != null && string.Equals(shape.shapeName, name, StringComparison.Ordinal))
                {
                    return shape;
                }
            }
            return null;
        }

        /// <summary>
        /// The blendshape a sculpt makes on one slot, in the mesh space of the slot's project asset, with
        /// normal and tangent deltas worked out the way Transfer Blend Shapes works them out.
        /// </summary>
        private bool TryBuildMappedSculptBlendshape(SculptSlotEditState state, string blendshapeName, out SlotDataAsset target,
            out UMABlendShape shape, out string error)
        {
            shape = null;
            target = null;
            if (state == null || state.slot == null || state.slot.asset == null || BakedMesh == null ||
                state.originalVertices == null || state.originalVertices.Length != state.count ||
                state.start < 0 || state.start + state.count > BakedMesh.vertexCount ||
                state.slot.asset.meshData.vertexCount != state.count)
            {
                error = "The source topology no longer matches the sculpt preview.";
                return false;
            }
            Vector3[] current = BakedMesh.vertices;
            var bakedDeltas = new Vector3[state.count];
            for (int i = 0; i < state.count; i++)
            {
                Vector3 delta = current[state.start + i] - state.originalVertices[i];
                bakedDeltas[i] = delta.sqrMagnitude > 1e-12f ? delta : Vector3.zero;
            }
            if (!TryMapBakedDeltasToSource(state.slot, state.start, bakedDeltas, out target, out UMABlendShapeSlotSpace space, out Vector3[] ownDeltas, out error))
            {
                return false;
            }
            var frame = new UMABlendFrame { frameWeight = 100f, deltaVertices = ownDeltas, deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] };
            if (space != null)
            {
                UMABlendShapeTransfer.RecalculateNormalDeltas(space, frame);
            }
            else
            {
                // The slot could not be placed on the race's rig: keep the change the preview's normals show.
                Vector3[] normals = BakedMesh.normals;
                if (state.hasOriginalNormals && state.originalNormals != null && state.originalNormals.Length == state.count &&
                    normals != null && state.start + state.count <= normals.Length)
                {
                    frame.deltaNormals = new Vector3[state.count];
                    for (int i = 0; i < state.count; i++)
                    {
                        frame.deltaNormals[i] = normals[state.start + i] - state.originalNormals[i];
                    }
                }
            }
            shape = new UMABlendShape { shapeName = blendshapeName, frames = new[] { frame } };
            return true;
        }

        // --- loading and saving ------------------------------------------------------------------------

        /// <summary>The blendshapes the character's slots have in the project, in name order.</summary>
        public string[] GetEditableBlendshapeNames()
        {
            if (blendshapeNameCache != null && ReferenceEquals(blendshapeNameCacheMesh, BakedMesh))
            {
                return blendshapeNameCache;
            }
            var names = new List<string>();
            if (thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.umaRecipe != null && thisDCA.umaData.umaRecipe.slotDataList != null)
            {
                foreach (SlotData slot in thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (!IsSelectableSlot(slot))
                    {
                        continue;
                    }
                    SlotDataAsset source = ResolveBlendshapeSourceSlot(slot.asset);
                    if (source == null || source.meshData.blendShapes == null)
                    {
                        continue;
                    }
                    foreach (UMABlendShape shape in source.meshData.blendShapes)
                    {
                        if (shape != null && !string.IsNullOrEmpty(shape.shapeName) && !names.Contains(shape.shapeName))
                        {
                            names.Add(shape.shapeName);
                        }
                    }
                }
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            blendshapeNameCache = names.ToArray();
            blendshapeNameCacheMesh = BakedMesh;
            return blendshapeNameCache;
        }

        /// <summary>
        /// Asks the stage to load a blendshape into the sculpt: now when the stage is open, else as soon
        /// as it has opened.
        /// </summary>
        /// <param name="unsaved">
        /// Shapes of that name that are not on the slot assets yet (a transfer that has only been
        /// previewed), by slot asset. They are shown and sculpted like saved ones and written by Save.
        /// </param>
        public void RequestBlendshapeEdit(string blendshapeName, IDictionary<SlotDataAsset, UMABlendShape> unsaved = null)
        {
            if (string.IsNullOrEmpty(blendshapeName))
            {
                return;
            }
            editBlendshapeChoice = blendshapeName;
            if (BakedMesh != null && VertexObject != null)
            {
                BeginBlendshapeEdit(blendshapeName, out _, unsaved);
            }
            else
            {
                pendingBlendshapeEdit = blendshapeName;
                pendingUnsavedShapes = unsaved;
            }
        }

        private void ProcessPendingBlendshapeEdit()
        {
            if (string.IsNullOrEmpty(pendingBlendshapeEdit))
            {
                return;
            }
            string name = pendingBlendshapeEdit;
            IDictionary<SlotDataAsset, UMABlendShape> unsaved = pendingUnsavedShapes;
            pendingBlendshapeEdit = null;
            pendingUnsavedShapes = null;
            BeginBlendshapeEdit(name, out _, unsaved);
        }

        /// <summary>
        /// Opens the editor on a temporary character of a race that wears the given slots, with only
        /// those slots editable. The character belongs to the stage and goes when the stage closes. Use
        /// it to sculpt slots without first putting them on a character in the scene.
        /// </summary>
        public static VertexEditorStage ShowStageForSlots(RaceData race, IList<SlotDataAsset> slots, out string message)
        {
            message = null;
            var generator = UMAAssetIndexer.Instance != null ? UMAAssetIndexer.Instance.Generator : null;
            if (race == null || generator == null)
            {
                message = race == null ? "Choose a race." : "No UMA generator is available to build the character.";
                return null;
            }
            var avatarObject = new GameObject("Blendshape Sculpt Character (temporary)") { hideFlags = HideFlags.DontSave };
            var notes = new List<string>();
            VertexEditorStage stage = null;
            UmaTPose plainTPose = null;
            try
            {
                var avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
                // Only this code builds the character: with edit-time generation on, the avatar would
                // rebuild itself from its own empty wardrobe and drop the slots added below.
                avatar.editorTimeGeneration = false;
                avatar.showPlaceholder = false;
                avatar.activeRace.name = race.raceName;
                avatar.activeRace.data = race;
                if (avatar.predefinedDNA == null)
                {
                    avatar.predefinedDNA = new UMAPredefinedDNA();
                }
                avatar.ignoreMeshHideAssets = true;
                avatar.GenerateNow();
                avatar.enabled = false;
                if (avatar.umaData == null || avatar.umaData.umaRecipe == null)
                {
                    message = $"A character of '{race.raceName}' could not be generated.";
                    return null;
                }

                var names = new HashSet<string>(StringComparer.Ordinal);
                var added = new List<KeyValuePair<SlotDataAsset, OverlayDataAsset>>();
                UMAData.UMARecipe recipe = avatar.umaData.umaRecipe;
                foreach (SlotDataAsset slot in slots)
                {
                    if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || !names.Add(slot.slotName))
                    {
                        continue;
                    }
                    OverlayDataAsset overlay = UMARigProfilePreview.FindOverlayFor(slot);
                    if (overlay == null)
                    {
                        names.Remove(slot.slotName);
                        notes.Add($"'{slot.slotName}' was left off the character: no overlay was found near it to draw it with.");
                        continue;
                    }
                    var slotData = new SlotData(slot);
                    slotData.AddOverlay(new OverlayData(overlay));
                    recipe.SetSlot(recipe.slotDataList.Length, slotData);
                    added.Add(new KeyValuePair<SlotDataAsset, OverlayDataAsset>(slot, overlay));
                }
                if (names.Count == 0)

                {
                    message = "None of the slots could be put on a character. " + string.Join(" ", notes);
                    return null;
                }
                // Slots built for the race's legacy rig are converted by the generator, as for any character.
                // The pose the slots were modelled in, as the transfer preview draws them: generated
                // without the T-pose asset's stored human pose, then turned back from the T-pose.
                plainTPose = CreateTPoseWithoutHumanPose(avatar.umaData.GetTPose());
                if (plainTPose != null)
                {
                    avatar.umaData.OverrideTpose = plainTPose;
                }
                avatar.umaData.Dirty(true, true, true);
                generator.GenerateSingleUMA(avatar.umaData, false);
                ApplyModelledPose(avatar);

                stage = ShowStage(avatar, null);
                if (stage == null)
                {
                    message = "The editor could not be opened.";
                    return null;
                }
                stage.ownsTemporaryCharacter = true;
                stage.temporaryTPose = plainTPose;
                plainTPose = null;
                stage.temporarySlots.AddRange(added);

                stage.EditOnlySlots(names);
                message = notes.Count > 0 ? string.Join(" ", notes) : null;
                return stage;
            }
            finally
            {
                if (stage == null && avatarObject != null)
                {
                    DestroyImmediate(avatarObject);
                }
                if (plainTPose != null)
                {
                    // The stage did not take it over.
                    DestroyImmediate(plainTPose);
                }
            }
        }

        /// <summary>Makes the named slots the only editable ones; every other slot is locked.</summary>
        /// <summary>
        /// The temporary character's recipe was rebuilt from the avatar, which knows nothing of the
        /// slots this stage put on it: adds back the ones that are missing.
        /// </summary>
        private void RestoreTemporarySlots()
        {
            if (!ownsTemporaryCharacter || thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                return;
            }
            UMAData.UMARecipe recipe = thisDCA.umaData.umaRecipe;
            foreach (KeyValuePair<SlotDataAsset, OverlayDataAsset> pair in temporarySlots)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    continue;
                }
                bool worn = false;
                if (recipe.slotDataList != null)
                {
                    foreach (SlotData existing in recipe.slotDataList)
                    {
                        if (existing != null && existing.slotName == pair.Key.slotName)
                        {
                            worn = true;
                            break;
                        }
                    }
                }
                if (worn)
                {
                    continue;
                }
                var slotData = new SlotData(pair.Key);
                slotData.AddOverlay(new OverlayData(pair.Value));
                recipe.SetSlot(recipe.slotDataList != null ? recipe.slotDataList.Length : 0, slotData);
            }
        }

        public void EditOnlySlots(ICollection<string> slotNames)
        {
            if (slotNames == null || slotNames.Count == 0)
            {
                return;
            }
            slotTargets.EditOnly(GetSelectableSlots(), slot => slot != null && slotNames.Contains(slot.slotName));
            RepaintLinkedEditors();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Loads a blendshape into the sculpt: every slot that has it, the body included, is shown with
        /// the shape fully dialled in, and strokes from here on are changes to the shape.
        /// </summary>
        public bool BeginBlendshapeEdit(string blendshapeName, out string message, IDictionary<SlotDataAsset, UMABlendShape> unsaved = null)
        {
            message = null;
            EndSculptStroke(true);
            if (IsEditingBlendshape)
            {
                message = $"'{editBlendshapeName}' is being edited. Save or stop first.";
            }
            else if (BakedMesh == null || thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                message = "The character is not ready.";
            }
            else if (HasSculptChanges())
            {
                message = "Save or discard the current sculpt before loading a blendshape.";
            }
            if (message == null && !EnsureBakedSkin(out string skinError))
            {
                message = skinError;
            }
            if (message != null)
            {
                SetBlendshapeEditStatus(message, MessageType.Warning);
                return false;
            }

            Vector3[] rest = BakedMesh.vertices;
            var loaded = (Vector3[])rest.Clone();
            float already = LoadedRendererWeight(blendshapeName);
            var slots = new List<BlendshapeEditSlot>();
            var skipped = new List<string>();
            var toWrite = new Dictionary<SlotDataAsset, UMABlendShape>();
            int withShape = 0;
            foreach (SlotData slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (!IsSelectableSlot(slot) || slots.Exists(s => ReferenceEquals(s.slot, slot)))
                {
                    continue;
                }
                int count = slot.asset.meshData.vertexCount;
                int start = GetVisibleBakedVertexIndex(slot, 0);
                if (start < 0 || start + count > loaded.Length)
                {
                    continue;
                }
                SlotDataAsset source = ResolveBlendshapeSourceSlot(slot.asset);
                if (source == null)
                {
                    continue;
                }
                UMABlendShapeSlotSpace space = GetBlendshapeSpace(source);
                bool converted = !ReferenceEquals(source, slot.asset) && !slot.asset.isBaked;
                UMABlendShape shape = FindBlendshape(source.meshData, blendshapeName);
                bool notSavedYet = false;
                if (shape == null && unsaved != null && unsaved.TryGetValue(source, out UMABlendShape previewed) && previewed != null)
                {
                    // The slot's own shape wins when it has one: it may carry hand-made changes.
                    shape = previewed;
                    notSavedYet = true;
                }
                var entry = new BlendshapeEditSlot { slot = slot, source = source, space = space, start = start, count = count };
                if (shape != null && shape.frames != null && shape.frames.Length > 0)
                {
                    Vector3[] deltas = shape.frames[shape.frames.Length - 1].deltaVertices;
                    if (deltas == null || deltas.Length != count || (converted && (space == null || !space.ConvertedFromLegacy)))
                    {
                        skipped.Add(slot.slotName);
                    }
                    else
                    {
                        entry.hadShape = true;
                        withShape++;
                        if (notSavedYet)
                        {
                            toWrite[source] = shape;
                        }
                        float share = 1f - already;
                        for (int i = 0; i < count; i++)
                        {
                            if (deltas[i] == Vector3.zero)
                            {
                                continue;
                            }
                            Vector3 worn = space != null ? space.OwnToWornSpace(i, deltas[i]) : deltas[i];
                            loaded[start + i] += bakedSkin[start + i].MultiplyVector(worn) * share;
                        }
                    }
                }
                slots.Add(entry);
            }
            if (withShape == 0)
            {
                message = $"None of the character's slots has a blendshape named '{blendshapeName}'.";
                SetBlendshapeEditStatus(message, MessageType.Warning);
                return false;
            }

            editRestVertices = rest;
            editSlots.Clear();
            editSlots.AddRange(slots);
            unsavedShapes.Clear();
            foreach (KeyValuePair<SlotDataAsset, UMABlendShape> pair in toWrite)
            {
                unsavedShapes[pair.Key] = pair.Value;
            }
            editBlendshapeName = blendshapeName;
            editBlendshapeChoice = blendshapeName;
            ApplyBakedVertices(loaded);
            message = $"Editing '{blendshapeName}' on {withShape} slot(s). The character shows it fully dialled in." +
                      (unsavedShapes.Count > 0 ? $" On {unsavedShapes.Count} slot(s) it comes from a transfer preview and is not saved yet." : string.Empty) +
                      (skipped.Count > 0 ? $" Left out: {string.Join(", ", skipped)} (its shape does not fit the slot as worn)." : string.Empty);
            SetBlendshapeEditStatus(message, skipped.Count > 0 ? MessageType.Warning : MessageType.Info);
            return true;
        }

        /// <summary>How far the character's renderer already has the shape dialled in, 0 to 1.</summary>
        private float LoadedRendererWeight(string blendshapeName)
        {
            SkinnedMeshRenderer renderer = stageSkinnedMeshRenderer;
            Mesh shared = renderer != null ? renderer.sharedMesh : null;
            int index = shared != null ? shared.GetBlendShapeIndex(blendshapeName) : -1;
            if (index < 0)
            {
                return 0f;
            }
            float full = Mathf.Max(1e-4f, shared.GetBlendShapeFrameWeight(index, shared.GetBlendShapeFrameCount(index) - 1));
            return Mathf.Clamp01(renderer.GetBlendShapeWeight(index) / full);
        }

        /// <summary>
        /// Puts new vertices on the baked mesh and makes them the starting point of every slot's sculpt,
        /// so "sculpt changes" means strokes made after this. Earlier strokes can no longer be undone
        /// across this point, which would otherwise mix two baselines.
        /// </summary>
        private void ApplyBakedVertices(Vector3[] vertices)
        {
            Undo.ClearUndo(BakedMesh);
            BakedMesh.vertices = vertices;
            BakedMesh.RecalculateNormals();
            SynchronizeCrossSlotSculptNormals();
            BakedMesh.RecalculateBounds();
            Vector3[] normals = BakedMesh.normals;
            foreach (SculptSlotEditState state in sculptSlotStates.Values)
            {
                if (state == null || state.start < 0 || state.start + state.count > vertices.Length)
                {
                    continue;
                }
                Array.Copy(vertices, state.start, state.originalVertices, 0, state.count);
                if (state.originalNormals != null && normals != null && state.start + state.count <= normals.Length)
                {
                    Array.Copy(normals, state.start, state.originalNormals, 0, state.count);
                }
            }
            sculptChangesUiDirty = true;
            sculptMaskDirty = true;
            RefreshBakedMeshCaches();
            RefreshSculptCollider();
            RepaintLinkedEditors();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// True when there is something Save would write: strokes made since the blendshape was loaded or
        /// last saved, or a previewed shape that is not on its slot yet.
        /// </summary>
        public bool HasBlendshapeEditChanges()
        {
            return IsEditingBlendshape && (unsavedShapes.Count > 0 || HasSculptChanges());
        }

        /// <summary>How many slots show a shape that has not been written to them yet.</summary>
        public int UnsavedBlendshapeSlotCount => IsEditingBlendshape ? unsavedShapes.Count : 0;

        /// <summary>
        /// Saves the strokes made since loading into the blendshape, on every slot they touched. A slot
        /// that did not have the shape gets it. Returns how many slot assets were written.
        /// </summary>
        public int SaveBlendshapeEdit()
        {
            EndSculptStroke(true);
            if (!IsEditingBlendshape)
            {
                return 0;
            }
            List<SculptSlotEditState> changed = GetChangedSculptStates();
            if (changed.Count == 0 && unsavedShapes.Count == 0)
            {
                SetBlendshapeEditStatus("Nothing has been sculpted since the blendshape was loaded.", MessageType.Info);
                return 0;
            }

            var targets = new List<SlotDataAsset>();
            var shapes = new List<UMABlendShape>();
            var errors = new List<string>();
            Vector3[] current = BakedMesh.vertices;
            foreach (SculptSlotEditState state in changed)
            {
                string slotName = state.slot != null ? state.slot.slotName : "Unknown";
                var bakedDeltas = new Vector3[state.count];
                for (int i = 0; i < state.count; i++)
                {
                    Vector3 delta = current[state.start + i] - state.originalVertices[i];
                    bakedDeltas[i] = delta.sqrMagnitude > 1e-12f ? delta : Vector3.zero;
                }
                if (!TryMapBakedDeltasToSource(state.slot, state.start, bakedDeltas, out SlotDataAsset source, out UMABlendShapeSlotSpace space, out Vector3[] strokes, out string error))
                {
                    errors.Add($"Slot '{slotName}': {error}");
                    continue;
                }
                if (targets.Contains(source))
                {
                    errors.Add($"Slot '{slotName}' shares SlotDataAsset '{source.name}' with another sculpted slot. Give them independent slot assets first.");
                    continue;
                }
                targets.Add(source);
                // A previewed shape that is not on the slot yet is what the strokes were made on.
                UMABlendShape existing = unsavedShapes.TryGetValue(source, out UMABlendShape previewed) ? previewed : FindBlendshape(source.meshData, editBlendshapeName);
                shapes.Add(UMABlendShapeTransfer.AddToShape(existing, editBlendshapeName, strokes, space));
            }
            // Previewed shapes on slots that were not sculpted are written as they are.
            foreach (KeyValuePair<SlotDataAsset, UMABlendShape> pair in unsavedShapes)
            {
                if (pair.Key != null && !targets.Contains(pair.Key))
                {
                    targets.Add(pair.Key);
                    shapes.Add(pair.Value);
                }
            }
            if (errors.Count > 0)
            {
                SetBlendshapeEditStatus("Nothing was saved. " + errors[0], MessageType.Error);
                EditorUtility.DisplayDialog("Unable to Save the Blendshape", string.Join("\n\n", errors), "OK");
                return 0;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Save Blendshape Edit");
            var undoTargets = new UnityEngine.Object[targets.Count];
            for (int i = 0; i < targets.Count; i++) undoTargets[i] = targets[i];
            Undo.RegisterCompleteObjectUndo(undoTargets, "Save Blendshape Edit");
            for (int i = 0; i < targets.Count; i++)
            {
                AddOrReplaceBlendshape(targets[i].meshData, shapes[i]);
                targets[i].meshData.InvalidateBuildData();
                EditorUtility.SetDirty(targets[i]);
                AssetDatabase.SaveAssetIfDirty(targets[i]);
            }
            Undo.CollapseUndoOperations(undoGroup);
            // Converted copies of legacy slots were made from the shapes as they were.
            UMALegacySlotConverter.ClearCache();
            blendshapeNameCache = null;
            unsavedShapes.Clear();

            // What is on screen is now the saved shape: strokes from here on are new changes.
            ApplyBakedVertices(current);
            foreach (BlendshapeEditSlot slot in editSlots)
            {
                if (targets.Contains(slot.source))
                {
                    slot.hadShape = true;
                }
            }
            SetBlendshapeEditStatus($"Saved '{editBlendshapeName}' on {targets.Count} slot(s). Regenerate characters to load it.", MessageType.Info);
            return targets.Count;
        }

        /// <summary>Leaves blendshape editing and shows the character at rest again. Unsaved strokes are dropped.</summary>
        public void EndBlendshapeEdit()
        {
            EndSculptStroke(true);
            if (!IsEditingBlendshape)
            {
                return;
            }
            Vector3[] rest = editRestVertices;
            string name = editBlendshapeName;
            editRestVertices = null;
            editBlendshapeName = string.Empty;
            editSlots.Clear();
            unsavedShapes.Clear();
            if (BakedMesh != null && rest.Length == BakedMesh.vertexCount)
            {
                ApplyBakedVertices(rest);
            }
            SetBlendshapeEditStatus($"Stopped editing '{name}'.", MessageType.Info);
        }

        /// <summary>The character was rebuilt: the baked mesh is new and shows the rest shape.</summary>
        private void OnBakedMeshRebuiltForBlendshapeEdit()
        {
            bakedSkin = null;
            bakedSkinMesh = null;
            blendshapeNameCache = null;
            if (!string.IsNullOrEmpty(editBlendshapeName) || editRestVertices != null)
            {
                string name = editBlendshapeName;
                editRestVertices = null;
                editBlendshapeName = string.Empty;
                editSlots.Clear();
                unsavedShapes.Clear();
                SetBlendshapeEditStatus($"The character was rebuilt, which ended the edit of '{name}'. Load it again to continue.", MessageType.Warning);
            }
        }

        private void SetBlendshapeEditStatus(string message, MessageType type)
        {
            blendshapeEditStatus = message ?? string.Empty;
            blendshapeEditStatusType = type;
            RepaintLinkedEditors();
        }

        // --- GUI -------------------------------------------------------------------------------------

        private void DrawBlendshapeEditSection()
        {
            if (!DrawSectionFoldout(ref showBlendshapeEditSection, "Edit Blendshape"))
            {
                return;
            }
            // A stage restored after a script reload has the name but not the mesh state.
            if (!string.IsNullOrEmpty(editBlendshapeName) && editRestVertices == null)
            {
                editBlendshapeName = string.Empty;
            }

            if (IsEditingBlendshape)
            {
                int with = 0, legacy = 0;
                foreach (BlendshapeEditSlot slot in editSlots)
                {
                    if (slot.hadShape) with++;
                    if (slot.space != null && slot.space.ConvertedFromLegacy) legacy++;
                }
                EditorGUILayout.HelpBox(
                    $"Editing '{editBlendshapeName}', shown fully dialled in on {with} slot(s). Strokes change the blendshape; make the slots to change editable in the Slots panel." +
                    (legacy > 0 ? $" {legacy} slot(s) are worn through legacy rig conversion: their edits are written to the original slots." : string.Empty),
                    MessageType.None);
                if (unsavedShapes.Count > 0)
                {
                    EditorGUILayout.HelpBox($"On {unsavedShapes.Count} slot(s) this shape comes from a transfer preview and is not on the slot yet. Save writes it, with your strokes.", MessageType.Info);
                }
                bool changes = HasSculptChangesForUI() || unsavedShapes.Count > 0;
                GUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!changes))
                {
                    if (GUILayout.Button(new GUIContent("Save to '" + editBlendshapeName + "'", "Add the strokes made since loading to this blendshape, on every slot they touched. A slot that did not have the shape gets it.")))
                    {
                        SaveBlendshapeEdit();
                        GUIUtility.ExitGUI();
                    }
                }
                if (GUILayout.Button(new GUIContent("Stop Editing", "Show the character at rest again. Strokes that were not saved are dropped."), GUILayout.Width(96f)))
                {
                    if (!changes || EditorUtility.DisplayDialog("Stop Editing the Blendshape",
                            unsavedShapes.Count > 0
                                ? "The shape has not been saved to the slots yet. It and any strokes will be dropped."
                                : "Strokes made since the last save will be dropped.", "Stop Editing", "Cancel"))
                    {
                        EndBlendshapeEdit();
                    }
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                string[] names = GetEditableBlendshapeNames();
                if (names.Length == 0)
                {
                    EditorGUILayout.HelpBox("None of this character's slots has a blendshape. Clothing gets the body's blendshapes with UMA > Tools > Slot Tools > Transfer Blend Shapes.", MessageType.None);
                }
                else
                {
                    int index = Mathf.Max(0, Array.IndexOf(names, editBlendshapeChoice));
                    index = EditorGUILayout.Popup(new GUIContent("Blendshape", "A blendshape the character's slots have in the project. Slots that lack it can still be sculpted and get it on save."), index, names);
                    editBlendshapeChoice = names[index];
                    bool pending = HasSculptChangesForUI();
                    using (new EditorGUI.DisabledScope(pending))
                    {
                        if (GUILayout.Button(new GUIContent("Edit '" + editBlendshapeChoice + "'", "Show the character with this blendshape fully dialled in and sculpt changes to it.")))
                        {
                            BeginBlendshapeEdit(editBlendshapeChoice, out _);
                            GUIUtility.ExitGUI();
                        }
                    }
                    if (pending)
                    {
                        EditorGUILayout.LabelField("Save or discard the current sculpt before loading a blendshape.", EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            // The message from loading only repeats what the boxes above say while the edit is open.
            bool repeatsTheAbove = IsEditingBlendshape && blendshapeEditStatusType == MessageType.Info && blendshapeEditStatus.StartsWith("Editing '", StringComparison.Ordinal);
            if (!string.IsNullOrEmpty(blendshapeEditStatus) && !repeatsTheAbove)
            {
                EditorGUILayout.HelpBox(blendshapeEditStatus, blendshapeEditStatusType);
            }
        }
    }
}
