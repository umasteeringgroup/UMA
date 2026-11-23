using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>The tool the left mouse button uses on the cloth in the Scene view.</summary>
    public enum ClothTool
    {
        Grab = 0,
        Pin = 1,
        ErasePin = 2,
        /// <summary>Move a piece round the body, up and down, and out from it, before sewing.</summary>
        Place = 3
    }

    /// <summary>
    /// Everything the Clothes Designer keeps for one open Vertex Editor: the body, the pattern and fit
    /// being edited (working copies of their assets), the simulation, the cloth drawn in the stage, the
    /// tools and their Undo, and the try-on character.
    /// </summary>
    public sealed class DesignerSession : IDisposable
    {
        public readonly VertexEditorStage Stage;
        public ClothesDesignerWorkflow.Mode Mode = ClothesDesignerWorkflow.Mode.NewClothes;
        public readonly List<string> FitSlotNames = new List<string>();
        public BodyReference Body;
        public string BodyProblem;
        public readonly FitSession Fit = new FitSession();
        public ClothesPattern Pattern;
        public ClothesFit FitData;
        public ClothesPattern PatternAsset
        {
            get => undoState != null ? undoState.patternAsset : null;
            set { if (undoState != null) undoState.patternAsset = value; }
        }

        public ClothesFit FitAsset
        {
            get => undoState != null ? undoState.fitAsset : null;
            set { if (undoState != null) undoState.fitAsset = value; }
        }
        public bool Running;
        public bool Settled;
        public ClothTool Tool = ClothTool.Place;
        /// <summary>The piece the Place tool has selected, or -1.</summary>
        public int PlacePiece = -1;
        public float BrushRadius = 0.06f;
        public float BrushStrength = 1f;
        public PinKind NewPinKind = PinKind.Body;
        public FitMetrics Metrics;
        public bool HasMetrics;
        public GarmentBaker.Result LastBake;
        public string LastBakeError;
        public readonly SlotRefit Refit = new SlotRefit();
        public event Action Changed;

        private string savedPatternJson;
        private string savedFitJson;
        private GameObject preview;
        private Mesh previewMesh;
        private Material previewMaterial;
        private Texture2D previewTexture;
        private int[] previewTriangles;
        private int[] weldGroup;
        private ClothesDesignerUndoState undoState;
        private int seenUndoVersion;
        private double lastMetricsTime;
        private Vector3[] lastSettleCheck;
        private float lastSettleSimTime;
        /// <summary>Frames a tick on the GPU (queued; the Editor does not wait for them).</summary>
        public const int GpuFramesPerTick = 3;
        private const int CpuFramesPerTickLimit = 4;
        private const double CpuBudgetMs = 10.0;
        private const double MetricsInterval = 1.0;
        /// <summary>Settled when the cloth moves less than this on average a frame (m).</summary>
        public const float SettledMetresPerFrame = 0.00005f;
        private bool dragging;
        private Vector3[] preDrag;
        private float preDragTime;
        private Vector3 dragHit;
        private Plane dragPlane;
        private Dictionary<int, float> dragWeights;
        private Vector3[] dragStart;
        private bool hoverValid;
        private Vector3 hoverPoint, hoverNormal;
        private DynamicCharacterAvatar tryOn;
        private GameObject tryOnView;
        private Mesh tryOnMesh;
        private Quaternion[] tryOnArmRest;
        public bool TryOnArmsRaised;
        /// <summary>Where Arms Raised puts the arms (degrees from hanging at the sides: 90 level, 180 straight up).</summary>
        public float TryOnArmAngle = 150f;
        private string builtShapeKey, builtFabricKey, builtRevision, builtFitPlacementKey;
        private List<int>[] pieceOutline;
        private int hoverPiece = -1;
        private bool placing;
        private PiecePlacement placeStart;
        private Vector3[] prePlacePositions;
        private float placeStartT, placeStartY;
        private Vector2 placeStartMouse;

        public DesignerSession(VertexEditorStage stage)
        {
            Stage = stage;
            undoState = ScriptableObject.CreateInstance<ClothesDesignerUndoState>();
            undoState.hideFlags = HideFlags.HideAndDontSave;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += Tick;
            // GPU buffers and native arrays do not survive a script reload: let them go first.
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        public bool HasPattern => Pattern != null;
        public bool ClothReady => Fit.HasCloth;

        // ---- Body ------------------------------------------------------------------------------

        public void CaptureBody()
        {
            Body = null;
            BodyProblem = null;
            try
            {
                Body = BodyReference.Capture(Stage.Character);
            }
            catch (Exception ex)
            {
                BodyProblem = ex.Message;
                Debug.LogWarning("[Clothes Designer] The body could not be captured: " + ex.Message);
            }
            BodyLandmarks before = FitData != null ? FitData.landmarks : null;
            if (Body != null && FitData != null)
            {
                FitData.landmarks = Body.Landmarks;
                Fit.Body = Body;
                if (Fit.HasCloth) Fit.SetBody(Body);
                // A new body shape changes what the sizes work out to: lay the pieces out round the new body.
                // (The old drape would start partly inside a larger body.) Pins live on the pattern and stay.
                if (Fit.HasCloth && before != null && before.valid && MeasurementsDiffer(before, Body.Landmarks))
                {
                    RebuildCloth(false);
                    Fit.Messages.Add("The body changed shape, so the sizes were worked out again and the pieces laid out round it. Simulate to drape them.");
                }
            }
            if (Mode == ClothesDesignerWorkflow.Mode.FitExistingSlot && Refit.IsBound && Body != null)
            {
                Refit.Conform(Body, 20, 0.004f);
                ShowRefitPreview();
            }
            NotifyChanged();
        }

        private static bool MeasurementsDiffer(BodyLandmarks a, BodyLandmarks b) =>
            Mathf.Abs(a.chestCm - b.chestCm) > 0.5f || Mathf.Abs(a.waistCm - b.waistCm) > 0.5f || Mathf.Abs(a.hipCm - b.hipCm) > 0.5f ||
            Mathf.Abs(a.shoulderTopY - b.shoulderTopY) > 0.005f || Mathf.Abs(a.waistY - b.waistY) > 0.005f;

        /// <summary>Puts the fit's saved body shape (DNA) on the stage's character, then reads the body again.</summary>
        public void ApplyFitDna()
        {
            if (FitData == null || Stage.Character == null) return;
            Dictionary<string, DnaSetter> dna = Stage.Character.GetDNA();
            bool changed = false;
            foreach (KeyValuePair<string, DnaSetter> kv in dna)
            {
                float want = 0.5f;
                foreach (DnaValue v in FitData.dna) if (v.name == kv.Key) want = v.value;
                if (!Mathf.Approximately(kv.Value.Get(), want)) { kv.Value.Set(want); changed = true; }
            }
            if (changed) Stage.RebuildCharacter(true);
        }

        // ---- Fit Existing Slot preview ------------------------------------------------------------

        /// <summary>Draws the refitted slot over the character, in the designer's cloth colours.</summary>
        public void ShowRefitPreview()
        {
            if (!Refit.IsBound || Refit.Current == null) return;
            if (preview == null)
            {
                preview = new GameObject("Clothes Designer Refit") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                preview.AddComponent<MeshFilter>();
                preview.AddComponent<MeshRenderer>();
                if (Stage.scene.IsValid()) SceneManager.MoveGameObjectToScene(preview, Stage.scene);
            }
            if (previewMesh == null) previewMesh = new Mesh { name = "Refit", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            previewMesh.Clear();
            previewMesh.SetVertices(Refit.Current);
            previewMesh.SetTriangles(Refit.SourceTriangles, 0);
            if (Refit.Source.meshData.uv != null && Refit.Source.meshData.uv.Length == Refit.VertexCount) previewMesh.SetUVs(0, Refit.Source.meshData.uv);
            previewMesh.RecalculateBounds();
            var normals = new Vector3[Refit.VertexCount];
            new UMABuddyNormals(Refit.Current, Refit.SourceTriangles).Recalculate(Refit.Current, normals);
            previewMesh.SetNormals(normals);
            if (previewMaterial == null)
            {
                previewMaterial = new Material(UMAUtils.GetDefaultDiffuseMaterial()) { hideFlags = HideFlags.DontSave };
                if (previewMaterial.HasProperty("_Cull")) previewMaterial.SetFloat("_Cull", 0f);
            }
            Color tint = new Color(0.3f, 0.75f, 1f);
            if (previewMaterial.HasProperty("_BaseColor")) previewMaterial.SetColor("_BaseColor", tint);
            if (previewMaterial.HasProperty("_Color")) previewMaterial.SetColor("_Color", tint);
            preview.GetComponent<MeshFilter>().sharedMesh = previewMesh;
            preview.GetComponent<MeshRenderer>().sharedMaterial = previewMaterial;
            SceneView.RepaintAll();
        }

        // ---- Pattern and fit -------------------------------------------------------------------

        /// <summary>Starts a fit for this stage's race, keeping any pattern already open.</summary>
        public void EnsureFit()
        {
            if (FitData != null) return;
            FitData = ScriptableObject.CreateInstance<ClothesFit>();
            FitData.hideFlags = HideFlags.DontSave;
            FitData.name = "New Fit";
            FitData.race = Stage.CharacterRace;
            FitData.publish.material = DefaultMaterial();
            if (Body != null) FitData.landmarks = Body.Landmarks;
            savedFitJson = null;
        }

        public static UMAMaterial DefaultMaterial()
        {
            const string path = "Assets/UMA/SRP/ShaderGraphs/UMAMaterial/UMAMaterial_UMA_SRP_DiffuseNormalSpecular.asset";
            UMAMaterial m = AssetDatabase.LoadAssetAtPath<UMAMaterial>(path);
            if (m != null) return m;
            foreach (string guid in AssetDatabase.FindAssets("t:UMAMaterial"))
            {
                m = AssetDatabase.LoadAssetAtPath<UMAMaterial>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.materialType == UMAMaterial.MaterialType.Atlas) return m;
            }
            return null;
        }

        /// <summary>Opens a pattern for editing. The asset itself is only changed by Save Pattern.</summary>
        public void OpenPattern(ClothesPattern asset)
        {
            EnsureFit();
            PatternAsset = asset;
            Pattern = asset != null ? UnityEngine.Object.Instantiate(asset) : null;
            if (Pattern != null)
            {
                Pattern.name = asset.name;
                Pattern.hideFlags = HideFlags.DontSave;
                List<string> migrated = Pattern.Migrate();
                foreach (string note in migrated) Fit.Messages.Add(note);
            }
            savedPatternJson = Pattern != null ? JsonUtility.ToJson(Pattern) : null;
            FitData.pattern = asset;
            RebuildCloth(false);
        }

        public void NewPattern(ClothesPattern startFrom)
        {
            EnsureFit();
            PatternAsset = null;
            Pattern = startFrom != null ? startFrom : ScriptableObject.CreateInstance<ClothesPattern>();
            Pattern.hideFlags = HideFlags.DontSave;
            if (startFrom == null)
            {
                Pattern.name = "New Pattern";
                Pattern.pieces.Add(PatternEditing.NewRectanglePiece("Piece 1", new Rect(-25f, 0f, 50f, 60f)));
            }
            savedPatternJson = null;
            FitData.pattern = null;
            RebuildCloth(false);
        }

        public void OpenFit(ClothesFit asset)
        {
            if (asset == null) return;
            FitAsset = asset;
            FitData = UnityEngine.Object.Instantiate(asset);
            FitData.name = asset.name;
            FitData.hideFlags = HideFlags.DontSave;
            savedFitJson = JsonUtility.ToJson(FitData);
            if (FitData.schemaVersion < ClothesFit.CurrentSchemaVersion)
            {
                Fit.Messages.Add("This fit was saved before each piece had its own fabric. Its fabric is now set per piece on the pattern (Cotton unless changed); check it under Fabric.");
                FitData.schemaVersion = ClothesFit.CurrentSchemaVersion;
            }
            if (FitData.race != null && Stage.CharacterRace != null && FitData.race != Stage.CharacterRace)
            {
                Fit.Messages.Add("This fit was made for '" + FitData.race.raceName + "'; the body here is '" + Stage.CharacterRace.raceName + "'. Its kept drape will not match.");
            }
            if (FitData.pattern != null)
            {
                PatternAsset = FitData.pattern;
                Pattern = UnityEngine.Object.Instantiate(FitData.pattern);
                Pattern.name = FitData.pattern.name;
                Pattern.hideFlags = HideFlags.DontSave;
                Pattern.Migrate();
                savedPatternJson = JsonUtility.ToJson(Pattern);
            }
            // The body shape the fit was made on.
            ApplyFitDna();
            RebuildCloth(false);
        }

        public bool PatternDirty => Pattern != null && (savedPatternJson == null || JsonUtility.ToJson(Pattern) != savedPatternJson);
        public bool FitDirty => FitData != null && Pattern != null && (savedFitJson == null || JsonUtility.ToJson(FitData) != savedFitJson);

        /// <summary>Saves the pattern; the first time, asks where. Returns false when the artist cancelled or the write failed.</summary>
        public bool SavePattern(bool saveAs, string pathOverride = null)
        {
            if (Pattern == null) return false;
            try
            {
                if (PatternAsset == null || saveAs)
                {
                    string folder = "Assets/UMAProjectData/ClothesDesigner/Patterns";
                    GarmentBaker.EnsureFolder(folder);
                    string path = pathOverride ?? EditorUtility.SaveFilePanelInProject(saveAs ? "Save Pattern As" : "Save Pattern", Pattern.name, "asset", "Where to save the pattern.", folder);
                    if (string.IsNullOrEmpty(path)) return false;
                    ClothesPattern copy = UnityEngine.Object.Instantiate(Pattern);
                    copy.name = Path.GetFileNameWithoutExtension(path);
                    copy.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(copy, path);
                    AssetDatabase.SaveAssetIfDirty(copy);
                    PatternAsset = copy;
                    Pattern.name = copy.name;
                }
                else
                {
                    string keepName = PatternAsset.name;
                    EditorUtility.CopySerialized(Pattern, PatternAsset);
                    PatternAsset.name = keepName;
                    PatternAsset.hideFlags = HideFlags.None;
                    EditorUtility.SetDirty(PatternAsset);
                    AssetDatabase.SaveAssetIfDirty(PatternAsset);
                }
                savedPatternJson = JsonUtility.ToJson(Pattern);
                if (FitData != null) FitData.pattern = PatternAsset;
                NotifyChanged();
                return true;
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Clothes Designer", "The pattern could not be saved. Your work is still open.\n\n" + ex.Message, "OK");
                return false;
            }
        }

        /// <summary>Saves the fit (and the pattern first, when it has never been saved).</summary>
        public bool SaveFit(bool saveAs, string pathOverride = null, string patternPathOverride = null)
        {
            if (FitData == null) return false;
            if (PatternAsset == null && Pattern != null && !SavePattern(false, patternPathOverride)) return false;
            if (PatternDirty && !SavePattern(false, patternPathOverride)) return false;
            try
            {
                FitData.pattern = PatternAsset;
                if (FitAsset == null || saveAs)
                {
                    string folder = "Assets/UMAProjectData/ClothesDesigner/Fits";
                    GarmentBaker.EnsureFolder(folder);
                    string suggested = (PatternAsset != null ? PatternAsset.name : "Clothes") + " on " + (FitData.race != null ? FitData.race.raceName : "body");
                    string path = pathOverride ?? EditorUtility.SaveFilePanelInProject(saveAs ? "Save Fit As" : "Save Fit", suggested, "asset", "Where to save the fit.", folder);
                    if (string.IsNullOrEmpty(path)) return false;
                    ClothesFit copy = UnityEngine.Object.Instantiate(FitData);
                    copy.name = Path.GetFileNameWithoutExtension(path);
                    copy.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(copy, path);
                    AssetDatabase.SaveAssetIfDirty(copy);
                    FitAsset = copy;
                }
                else
                {
                    string keepName = FitAsset.name;
                    EditorUtility.CopySerialized(FitData, FitAsset);
                    FitAsset.name = keepName;
                    FitAsset.hideFlags = HideFlags.None;
                    EditorUtility.SetDirty(FitAsset);
                    AssetDatabase.SaveAssetIfDirty(FitAsset);
                }
                savedFitJson = JsonUtility.ToJson(FitData);
                NotifyChanged();
                return true;
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Clothes Designer", "The fit could not be saved. Your work is still open.\n\n" + ex.Message, "OK");
                return false;
            }
        }

        public void MarkFitChanged() => NotifyChanged();

        // ---- Cloth ------------------------------------------------------------------------------

        /// <summary>
        /// Builds the cloth again from the pattern and fit. With <paramref name="keepShape"/> the current
        /// drape is carried over through the flat pattern (a new mesh level keeps the folds).
        /// </summary>
        public void RebuildCloth(bool keepShape)
        {
            Running = false;
            Settled = false;
            CancelDrag();
            placing = false;
            if (Pattern == null || FitData == null || Body == null)
            {
                Fit.DisposeSolver();
                UpdatePreview();
                RememberBuiltData();
                NotifyChanged();
                return;
            }
            PatternMesh oldMesh = Fit.Mesh;
            Vector3[] oldPositions = keepShape && Fit.HasCloth ? (Vector3[])Fit.SyncPositions().Clone() : null;
            bool wasSewn = Fit.Sewn;
            Fit.Pattern = Pattern;
            Fit.Fit = FitData;
            Fit.Body = Body;
            try
            {
                Fit.Rebuild();
            }
            catch (Exception ex)
            {
                Fit.Messages.Add("The cloth could not be built: " + ex.Message);
                Debug.LogException(ex);
            }
            if (Fit.HasCloth && oldMesh != null && oldPositions != null && wasSewn)
            {
                Vector3[] carried = Resample(oldMesh, oldPositions, Fit.Mesh);
                Fit.Restore(carried, FitSession.SewSeconds + FitSession.GravityRampSeconds);
                Fit.Messages.Add("Kept the drape on the new mesh; simulate to let it settle.");
            }
            weldGroup = Fit.HasCloth ? GarmentBaker.WeldGroups(Fit.Mesh.VertexCount, Fit.Mesh.stitches) : null;
            pieceOutline = Fit.HasCloth ? PieceOutlines(Fit.Mesh) : null;
            if (PlacePiece >= Pattern.pieces.Count) PlacePiece = -1;
            HasMetrics = false;
            RememberBuiltData();
            RebuildPreviewMesh();
            NotifyChanged();
        }

        // ---- Keeping the cloth in step with the pattern and fit (edits, Undo, Redo) ----------------

        [Serializable]
        private sealed class FitShapeKey
        {
            public List<PlacementOverride> placements;
            public List<AllowanceOverride> allowances;
            public bool overrideMeshLevel;
            public MeshLevel meshLevel;
            public float customEdgeLengthCm;
        }

        private static readonly FabricSettings blankFabric = new FabricSettings();

        /// <summary>Everything that shapes the cloth: the pattern without its fabrics, and the fit's sizes, placement and mesh level.</summary>
        private string ShapeKey()
        {
            if (Pattern == null || FitData == null) return string.Empty;
            var kept = new FabricSettings[Pattern.pieces.Count];
            for (int i = 0; i < kept.Length; i++) { kept[i] = Pattern.pieces[i].fabric; Pattern.pieces[i].fabric = blankFabric; }
            string pattern;
            try { pattern = JsonUtility.ToJson(Pattern); }
            finally { for (int i = 0; i < kept.Length; i++) Pattern.pieces[i].fabric = kept[i]; }
            return pattern + "|" + FitPlacementKey() + "|" + FitData.overrideMeshLevel + FitData.meshLevel + FitData.customEdgeLengthCm.ToString("R");
        }

        private string FitPlacementKey() => FitData == null ? string.Empty
            : JsonUtility.ToJson(new FitShapeKey { placements = FitData.placements, allowances = FitData.allowances });

        private string FabricKey()
        {
            if (Pattern == null) return string.Empty;
            var text = new System.Text.StringBuilder();
            foreach (PatternPiece piece in Pattern.pieces) text.Append(JsonUtility.ToJson(piece.fabric)).Append('\n');
            return text.ToString();
        }

        private void RememberBuiltData()
        {
            builtShapeKey = ShapeKey();
            builtFabricKey = FabricKey();
            builtRevision = Pattern != null ? Pattern.ComputeRevision() : null;
            builtFitPlacementKey = FitPlacementKey();
        }

        /// <summary>
        /// Brings the cloth up to date after the pattern or fit changed (an edit, Undo or Redo): a fabric
        /// change is taken up in place, keeping the drape; a change of shape lays the pieces out again. A
        /// mesh level change on the fit carries the drape over.
        /// </summary>
        public void SyncToData()
        {
            if (Pattern == null || FitData == null) return;
            if (ShapeKey() != builtShapeKey)
            {
                bool onlyMeshLevel = Pattern.ComputeRevision() == builtRevision && FitPlacementKey() == builtFitPlacementKey;
                RebuildCloth(onlyMeshLevel);
                return;
            }
            if (FabricKey() != builtFabricKey) ApplyFabric();
        }

        /// <summary>Takes up changed fabrics without losing the drape.</summary>
        public void ApplyFabric()
        {
            if (Fit.HasCloth)
            {
                Running = Running && !dragging;
                Fit.ApplyFabric();
                PullPositions(true);
                Settled = false;
            }
            builtFabricKey = FabricKey();
            NotifyChanged();
        }

        /// <summary>The placement in effect for a piece, and the asset it lives on: the fit when the fit has its own for it, otherwise the pattern.</summary>
        public PiecePlacement EditablePlacement(int pieceIndex, out UnityEngine.Object owner)
        {
            owner = Pattern;
            if (Pattern == null || pieceIndex < 0 || pieceIndex >= Pattern.pieces.Count) return null;
            PatternPiece piece = Pattern.pieces[pieceIndex];
            if (FitData != null)
            {
                foreach (PlacementOverride o in FitData.placements)
                {
                    if (o != null && o.pieceId == piece.id) { owner = FitData; return o.placement; }
                }
            }
            return piece.placement;
        }

        /// <summary>True when the fit has its own placement for the piece, which then wins over the pattern's.</summary>
        public bool HasFitPlacement(int pieceIndex)
        {
            EditablePlacement(pieceIndex, out UnityEngine.Object owner);
            return owner == FitData && FitData != null;
        }

        /// <summary>Drops the fit's own placement for a piece, so the pattern's is used.</summary>
        public void UsePatternPlacement(int pieceIndex)
        {
            if (FitData == null || pieceIndex < 0 || pieceIndex >= Pattern.pieces.Count) return;
            string id = Pattern.pieces[pieceIndex].id;
            Undo.RecordObject(FitData, "Use Pattern's Placement");
            FitData.placements.RemoveAll(o => o != null && o.pieceId == id);
            RebuildCloth(false);
        }

        /// <summary>Sets a piece's placement (on whichever asset holds it) and lays the pieces out again. One Undo step.</summary>
        public void SetPlacement(int pieceIndex, float angle, float topAboveShoulderCm, float distanceCm)
        {
            PiecePlacement placement = EditablePlacement(pieceIndex, out _);
            if (placement == null) return;
            SetPlacement(pieceIndex, angle, topAboveShoulderCm, distanceCm, placement.part, placement.flat);
        }

        /// <summary>Sets a piece's whole placement, its body part and flatness included. One Undo step.</summary>
        public void SetPlacement(int pieceIndex, float angle, float topAboveShoulderCm, float distanceCm, BodyPart part, bool flat)
        {
            PiecePlacement placement = EditablePlacement(pieceIndex, out _);
            if (placement == null) return;
            PiecePlacement next = placement.Clone();
            next.angle = angle; next.topAboveShoulderCm = topAboveShoulderCm; next.distanceCm = distanceCm; next.part = part; next.flat = flat;
            SetPlacement(pieceIndex, next);
        }

        /// <summary>Sets every value of a piece's placement (on whichever asset holds it) and lays the pieces out again. One Undo step.</summary>
        public void SetPlacement(int pieceIndex, PiecePlacement values)
        {
            PiecePlacement placement = EditablePlacement(pieceIndex, out UnityEngine.Object owner);
            if (placement == null || values == null || placement.SameAs(values)) return;
            Undo.RecordObject(owner, "Place Piece");
            placement.angle = values.angle;
            placement.topAboveShoulderCm = values.topAboveShoulderCm;
            placement.distanceCm = values.distanceCm;
            placement.part = values.part;
            placement.flat = values.flat;
            placement.acrossCm = values.acrossCm;
            RebuildCloth(false);
        }

        /// <summary>The boundary edges of each piece's mesh (pairs of vertex indices), for outlining a piece in the Scene view.</summary>
        private static List<int>[] PieceOutlines(PatternMesh mesh)
        {
            var count = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = mesh.triangles[t + k], b = mesh.triangles[t + (k + 1) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }
            var outlines = new List<int>[mesh.pieceCount.Length];
            for (int p = 0; p < outlines.Length; p++) outlines[p] = new List<int>();
            foreach (KeyValuePair<long, int> kv in count)
            {
                if (kv.Value != 1) continue;
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                outlines[mesh.piece[a]].Add(a);
                outlines[mesh.piece[a]].Add(b);
            }
            return outlines;
        }

        /// <summary>Positions for a new mesh from an old one, through the flat pattern.</summary>
        public static Vector3[] Resample(PatternMesh from, Vector3[] fromPositions, PatternMesh to) =>
            new PatternMeshSampler(from).Carry(fromPositions, to);

        public void SetRunning(bool run)
        {
            if (!Fit.HasCloth) return;
            Running = run;
            Settled = false;
            lastSettleCheck = null;
            NotifyChanged();
        }

        public void StepOnce()
        {
            if (!Fit.HasCloth) return;
            Running = false;
            Fit.Step(1);
            PullPositions(true);
            UpdateMetrics(true);
            NotifyChanged();
        }

        public void ResetToArrangement()
        {
            if (!Fit.HasCloth) return;
            RecordUndo("Reset to Arrangement");
            Fit.ResetToArrangement();
            CommitUndo();
            Running = false;
            PullPositions(true);
            UpdateMetrics(true);
            NotifyChanged();
        }

        public void KeepDrape()
        {
            if (!Fit.HasCloth) return;
            Undo.RecordObject(FitData, "Keep Drape");
            Fit.KeepDrape();
            NotifyChanged();
        }

        public void SetPreferCpu(bool cpu)
        {
            Fit.SetPreferCpu(cpu);
            NotifyChanged();
        }

        private void Tick()
        {
            if (Stage == null || !Fit.HasCloth) return;
            if (Running || dragging)
            {
                // Several frames a tick: the GPU queues them without waiting; the CPU runs as many as fit in a
                // short budget.
                // Fine meshes take many substeps, each a few dispatches: queue fewer frames so a tick stays short.
                int substeps = ClothModel.Substeps(Fit.FinalQuality, Fit.Mesh != null ? Fit.Mesh.edgeLengthCm : 2f);
                int most = Fit.Solver.IsGpu ? Mathf.Clamp(GpuFramesPerTick * 48 / Mathf.Max(1, substeps), 1, GpuFramesPerTick) : CpuFramesPerTickLimit;
                double start = EditorApplication.timeSinceStartup;
                int frames = 0;
                do
                {
                    Fit.Step(1);
                    frames++;
                }
                while (frames < most && (Fit.Solver.IsGpu || (EditorApplication.timeSinceStartup - start) * 1000.0 < CpuBudgetMs));
                PullPositions(false);
                UpdateMetrics(false);
                SceneView.RepaintAll();
            }
        }

        /// <summary>Brings the solver's positions to the preview: by readback on the GPU, directly on the CPU.</summary>
        private void PullPositions(bool wait)
        {
            if (!Fit.HasCloth) return;
            if (!wait && Fit.Solver is ClothSolverGpu gpu)
            {
                gpu.RequestPositions(positions =>
                {
                    if (!Fit.HasCloth || positions.Length != Fit.Model.ParticleCount) return;
                    Fit.Positions = positions;
                    UpdatePreview();
                });
                return;
            }
            Fit.SyncPositions();
            UpdatePreview();
        }

        private void UpdateMetrics(bool force)
        {
            if (!Fit.HasCloth) return;
            double now = EditorApplication.timeSinceStartup;
            // The report reads the whole cloth against the body: once a second while simulating.
            if (!force && now - lastMetricsTime < MetricsInterval) return;
            lastMetricsTime = now;
            Metrics = Fit.Measure();
            HasMetrics = true;
            // Settled: on average nothing moved more than 0.05 mm a frame since the last check, counted in
            // simulated frames, so the test holds however fast the frames come.
            Vector3[] p = Fit.Positions;
            float frames = (Fit.SimTime - lastSettleSimTime) * 60f;
            if (Running && Fit.Sewn && !dragging && lastSettleCheck != null && lastSettleCheck.Length == p.Length && frames >= 5f)
            {
                double moved = 0;
                for (int i = 0; i < p.Length; i++) moved += (p[i] - lastSettleCheck[i]).magnitude;
                if (moved / p.Length / frames < SettledMetresPerFrame) { Running = false; Settled = true; }
            }
            lastSettleCheck = (Vector3[])p.Clone();
            lastSettleSimTime = Fit.SimTime;
            NotifyChanged();
        }

        // ---- Preview ------------------------------------------------------------------------------

        private void RebuildPreviewMesh()
        {
            if (!Fit.HasCloth)
            {
                DestroyPreview();
                return;
            }
            if (preview == null)
            {
                preview = new GameObject("Clothes Designer Cloth") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                preview.AddComponent<MeshFilter>();
                preview.AddComponent<MeshRenderer>();
                if (Stage.scene.IsValid()) SceneManager.MoveGameObjectToScene(preview, Stage.scene);
            }
            if (previewMesh == null) previewMesh = new Mesh { name = "Clothes Designer Cloth", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            previewMesh.Clear();
            previewMesh.SetVertices(Fit.Positions);
            previewTriangles = Fit.Mesh.triangles.ToArray();
            if (Fit.Arranged != null && Fit.Arranged.flipTriangles)
            {
                for (int t = 0; t < previewTriangles.Length; t += 3) { int tmp = previewTriangles[t + 1]; previewTriangles[t + 1] = previewTriangles[t + 2]; previewTriangles[t + 2] = tmp; }
            }
            previewMesh.SetTriangles(previewTriangles, 0);
            previewMesh.SetUVs(0, Fit.Mesh.uv);
            preview.GetComponent<MeshFilter>().sharedMesh = previewMesh;
            if (previewTexture != null) UnityEngine.Object.DestroyImmediate(previewTexture);
            previewTexture = UvCheckTexture.Draw(Pattern, Fit.Mesh, 512);
            previewTexture.hideFlags = HideFlags.DontSave;
            if (previewMaterial == null)
            {
                previewMaterial = new Material(UMAUtils.GetDefaultDiffuseMaterial()) { hideFlags = HideFlags.DontSave };
                if (previewMaterial.HasProperty("_Cull")) previewMaterial.SetFloat("_Cull", 0f);
                if (previewMaterial.HasProperty("_CullMode")) previewMaterial.SetFloat("_CullMode", 0f);
                if (previewMaterial.HasProperty("_BaseColor")) previewMaterial.SetColor("_BaseColor", Color.white);
                if (previewMaterial.HasProperty("_Color")) previewMaterial.SetColor("_Color", Color.white);
            }
            foreach (string property in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
            {
                if (previewMaterial.HasProperty(property)) previewMaterial.SetTexture(property, previewTexture);
            }
            preview.GetComponent<MeshRenderer>().sharedMaterial = previewMaterial;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (previewMesh == null || !Fit.HasCloth || Fit.Positions.Length != previewMesh.vertexCount) return;
            previewMesh.SetVertices(Fit.Positions);
            previewMesh.SetNormals(WeldedNormals(Fit.Positions));
            previewMesh.RecalculateBounds();
        }

        /// <summary>Normals where every sewn group of copies shares one normal (the battle-buddy rule, by the seams).</summary>
        private Vector3[] WeldedNormals(Vector3[] positions)
        {
            var normals = new Vector3[positions.Length];
            int[] tris = previewTriangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 n = Vector3.Cross(positions[tris[t + 1]] - positions[tris[t]], positions[tris[t + 2]] - positions[tris[t]]);
                normals[weldGroup[tris[t]]] += n; normals[weldGroup[tris[t + 1]]] += n; normals[weldGroup[tris[t + 2]]] += n;
            }
            for (int v = 0; v < positions.Length; v++)
            {
                Vector3 n = normals[weldGroup[v]];
                normals[v] = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
            }
            return normals;
        }

        public void SetPreviewVisible(bool visible)
        {
            if (preview != null) preview.SetActive(visible);
        }

        private void DestroyPreview()
        {
            if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
            if (previewMesh != null) UnityEngine.Object.DestroyImmediate(previewMesh);
            if (previewMaterial != null) UnityEngine.Object.DestroyImmediate(previewMaterial);
            if (previewTexture != null) UnityEngine.Object.DestroyImmediate(previewTexture);
            preview = null; previewMesh = null; previewMaterial = null; previewTexture = null;
        }

        // ---- Undo -----------------------------------------------------------------------------

        private void RecordUndo(string label)
        {
            Fit.SyncPositions();
            undoState.positions = (Vector3[])Fit.Positions.Clone();
            undoState.simTime = Fit.SimTime;
            Undo.RecordObject(undoState, label);
        }

        private void CommitUndo()
        {
            Fit.SyncPositions();
            undoState.positions = (Vector3[])Fit.Positions.Clone();
            undoState.simTime = Fit.SimTime;
            undoState.version++;
            seenUndoVersion = undoState.version;
            Undo.IncrementCurrentGroup();
        }

        private void OnUndoRedo()
        {
            if (undoState == null) return;
            if (undoState.version != seenUndoVersion && Fit.HasCloth && undoState.positions.Length == Fit.Model.ParticleCount)
            {
                seenUndoVersion = undoState.version;
                Running = false;
                Fit.Restore(undoState.positions, undoState.simTime);
                PullPositions(true);
            }
            if (Fit.HasCloth) Fit.RefreshPins();
            // A pattern, fabric, placement or size change undone or redone: bring the cloth up to date.
            SyncToData();
            NotifyChanged();
        }

        // ---- Scene view tools -------------------------------------------------------------------

        public void OnSceneGUI(SceneView view, bool pointerOverPanel)
        {
            if (!Fit.HasCloth) return;
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            DrawSeams(view);
            DrawPins();
            if (Tool == ClothTool.Place) DrawPieceOutlines();
            if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(id);
            if (e.alt || pointerOverPanel) { hoverValid = false; hoverPiece = -1; return; }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                hoverValid = Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out hoverPoint, out hoverNormal, out int hovered);
                int piece = hoverValid ? Fit.Mesh.piece[hovered] : -1;
                if (piece != hoverPiece) { hoverPiece = piece; NotifyChanged(); }
                view.Repaint();
            }
            if (Tool == ClothTool.Place)
            {
                PlaceSceneGUI(view, e, id);
                return;
            }
            if (e.type == EventType.Repaint && hoverValid && !dragging)
            {
                Handles.color = Tool == ClothTool.Grab ? new Color(1f, 0.8f, 0.2f) : Tool == ClothTool.Pin ? new Color(0.3f, 0.9f, 1f) : new Color(1f, 0.35f, 0.35f);
                Handles.DrawWireDisc(hoverPoint, hoverNormal, BrushRadius);
            }

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0) break;
                    if (!Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out Vector3 hit, out _, out int particle)) break;
                    if (Tool == ClothTool.Grab) BeginDrag(hit, particle, view);
                    else ApplyPinBrush(particle, Tool == ClothTool.ErasePin);
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    if (dragging) UpdateDrag(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                    else if (Tool != ClothTool.Grab && Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out _, out _, out int p2)) ApplyPinBrush(p2, Tool == ClothTool.ErasePin);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    if (dragging) EndDrag();
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape && dragging)
                    {
                        CancelDrag();
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        // ---- Place tool -----------------------------------------------------------------------------

        /// <summary>The piece under the pointer, or -1.</summary>
        public int HoverPiece => hoverPiece;

        private void PlaceSceneGUI(SceneView view, Event e, int id)
        {
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0) break;
                    if (Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out Vector3 hit, out _, out int particle))
                    {
                        PlacePiece = Fit.Mesh.piece[particle];
                        BeginPlace(hit, e.mousePosition);
                        GUIUtility.hotControl = id;
                    }
                    else PlacePiece = -1;
                    NotifyChanged();
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id || !placing) break;
                    UpdatePlace(HandleUtility.GUIPointToWorldRay(e.mousePosition), e.mousePosition, e.shift, view);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    if (placing) EndPlace();
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape && placing)
                    {
                        CancelPlace();
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>Starts moving the selected piece from a point on it.</summary>
        public void BeginPlace(Vector3 hit, Vector2 mouse)
        {
            PiecePlacement placement = EditablePlacement(PlacePiece, out UnityEngine.Object owner);
            if (placement == null) return;
            Undo.RecordObject(owner, "Move Piece");
            Running = false;
            placing = true;
            placeStart = placement.Clone();
            prePlacePositions = (Vector3[])Fit.SyncPositions().Clone();
            placeStartMouse = mouse;
            AroundBody(hit, PieceAxes(PlacePiece, placeStart.distanceCm), out placeStartT);
            placeStartY = hit.y;
        }

        /// <summary>
        /// Moves the piece so the point that was grabbed follows the pointer round the body and up or
        /// down; with Shift, moving the pointer up or down moves the piece out from or in towards the body.
        /// The pieces are previewed laid out; the cloth is rebuilt when the button is released.
        /// </summary>
        public void UpdatePlace(Ray ray, Vector2 mouse, bool outward, SceneView view)
        {
            if (!placing) return;
            PiecePlacement placement = EditablePlacement(PlacePiece, out _);
            if (outward)
            {
                placement.angle = placeStart.angle;
                placement.topAboveShoulderCm = placeStart.topAboveShoulderCm;
                // Pointer up moves the piece out; 20 pixels a centimetre.
                placement.distanceCm = Mathf.Round((placeStart.distanceCm - (mouse.y - placeStartMouse.y) * 0.05f) * 10f) / 10f;
            }
            else if (placement.part != BodyPart.Torso)
            {
                // Round an arm, a leg or the head: across the screen turns the piece round the part (half a
                // degree a pixel), up and down moves it along (20 pixels a centimetre).
                float turn = (mouse.x - placeStartMouse.x) * 0.5f;
                if (PiecePlacement.IsArm(placement.part) && placement.part == BodyPart.LeftArm) turn = -turn;
                placement.angle = Mathf.Round(Mathf.DeltaAngle(0f, placeStart.angle + turn));
                placement.topAboveShoulderCm = Mathf.Round((placeStart.topAboveShoulderCm - (mouse.y - placeStartMouse.y) * 0.05f) * 2f) / 2f;
                placement.distanceCm = placeStart.distanceCm;
            }
            else
            {
                Vector2 axes = PieceAxes(PlacePiece, placeStart.distanceCm);
                if (!PointerOnLayout(ray, axes, view, out Vector3 point)) return;
                AroundBody(point, axes, out float t);
                placement.angle = Mathf.Round(Mathf.DeltaAngle(0f, placeStart.angle + Mathf.DeltaAngle(placeStartT, t)));
                placement.topAboveShoulderCm = Mathf.Round((placeStart.topAboveShoulderCm + (point.y - placeStartY) * 100f) * 2f) / 2f;
                placement.distanceCm = placeStart.distanceCm;
            }
            PreviewLayout();
        }

        /// <summary>Shows the pieces laid out with the current placements, without rebuilding the cloth.</summary>
        public void PreviewLayout()
        {
            if (!Fit.HasCloth) return;
            Arrangement.Result layout = Arrangement.Arrange(Pattern, FitData, Fit.Mesh, Body, Fit.Arranged.gap);
            Fit.Positions = layout.positions;
            UpdatePreview();
            SceneView.RepaintAll();
            NotifyChanged();
        }

        public void EndPlace()
        {
            if (!placing) return;
            placing = false;
            PiecePlacement placement = EditablePlacement(PlacePiece, out _);
            bool moved = !Mathf.Approximately(placement.angle, placeStart.angle) || !Mathf.Approximately(placement.topAboveShoulderCm, placeStart.topAboveShoulderCm)
                || !Mathf.Approximately(placement.distanceCm, placeStart.distanceCm);
            if (moved)
            {
                int keep = PlacePiece;
                RebuildCloth(false);
                PlacePiece = keep;
                Undo.IncrementCurrentGroup();
            }
            NotifyChanged();
        }

        public void CancelPlace()
        {
            if (!placing) return;
            placing = false;
            PiecePlacement placement = EditablePlacement(PlacePiece, out _);
            placement.angle = placeStart.angle;
            placement.topAboveShoulderCm = placeStart.topAboveShoulderCm;
            placement.distanceCm = placeStart.distanceCm;
            Fit.Positions = prePlacePositions;
            UpdatePreview();
            SceneView.RepaintAll();
            NotifyChanged();
        }

        public bool Placing => placing;

        /// <summary>The ellipse a piece is laid on (half-widths across and front to back, m).</summary>
        private Vector2 PieceAxes(int piece, float distanceCm)
        {
            Vector2 axes = Fit.Arranged != null ? Fit.Arranged.semiAxes : new Vector2(0.2f, 0.15f);
            float extra = distanceCm * 0.01f;
            return new Vector2(Mathf.Max(0.02f, axes.x + extra), Mathf.Max(0.02f, axes.y + extra));
        }

        /// <summary>Where a point is round the body, in degrees: 0 front, 90 the body's left (the arrangement's own angle).</summary>
        private void AroundBody(Vector3 point, Vector2 axes, out float degrees)
        {
            Vector2 c = Body.Landmarks.torsoCenter;
            degrees = Mathf.Atan2(-(point.x - c.x) / axes.x, (point.z - c.y) / axes.y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The point under the pointer on the ellipse the pieces are laid on (the side facing the camera),
        /// or, when the pointer is off it, on a plane through the body's axis facing the camera.
        /// </summary>
        private bool PointerOnLayout(Ray ray, Vector2 axes, SceneView view, out Vector3 point)
        {
            Vector2 c = Body.Landmarks.torsoCenter;
            float ox = (ray.origin.x - c.x) / axes.x, oz = (ray.origin.z - c.y) / axes.y;
            float dx = ray.direction.x / axes.x, dz = ray.direction.z / axes.y;
            float qa = dx * dx + dz * dz, qb = 2f * (ox * dx + oz * dz), qc = ox * ox + oz * oz - 1f;
            float disc = qb * qb - 4f * qa * qc;
            if (qa > 1e-9f && disc >= 0f)
            {
                float s = (-qb - Mathf.Sqrt(disc)) / (2f * qa);
                if (s > 0f) { point = ray.origin + ray.direction * s; return true; }
            }
            Vector3 facing = view != null && view.camera != null ? -view.camera.transform.forward : Vector3.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            var plane = new Plane(facing.normalized, new Vector3(c.x, 0f, c.y));
            if (plane.Raycast(ray, out float enter)) { point = ray.origin + ray.direction * enter; return true; }
            point = Vector3.zero;
            return false;
        }

        private void DrawPieceOutlines()
        {
            if (Event.current.type != EventType.Repaint || pieceOutline == null) return;
            Vector3[] p = Fit.Positions;
            UnityEngine.Rendering.CompareFunction oldZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            for (int piece = 0; piece < pieceOutline.Length; piece++)
            {
                bool selected = piece == PlacePiece, hovered = piece == hoverPiece;
                if (!selected && !hovered) continue;
                Handles.color = selected ? new Color(1f, 0.85f, 0.1f) : new Color(1f, 1f, 1f, 0.8f);
                List<int> edges = pieceOutline[piece];
                for (int k = 0; k + 1 < edges.Count; k += 2)
                {
                    if (edges[k] < p.Length && edges[k + 1] < p.Length) Handles.DrawLine(p[edges[k]], p[edges[k + 1]], selected ? 3f : 2f);
                }
            }
            Handles.zTest = oldZTest;
        }

        /// <summary>The front-most cloth triangle under a ray, the hit point, its normal and the nearest particle.</summary>
        public bool Raycast(Ray ray, out Vector3 point, out Vector3 normal, out int particle)
        {
            point = Vector3.zero; normal = Vector3.up; particle = -1;
            if (!Fit.HasCloth || previewTriangles == null) return false;
            Vector3[] p = Fit.Positions;
            float best = float.MaxValue;
            for (int t = 0; t < previewTriangles.Length; t += 3)
            {
                Vector3 a = p[previewTriangles[t]], b = p[previewTriangles[t + 1]], c = p[previewTriangles[t + 2]];
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 h = Vector3.Cross(ray.direction, e2);
                float det = Vector3.Dot(e1, h);
                if (Mathf.Abs(det) < 1e-12f) continue;
                float inv = 1f / det;
                Vector3 s = ray.origin - a;
                float u = inv * Vector3.Dot(s, h);
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float w = inv * Vector3.Dot(ray.direction, q);
                if (w < 0f || u + w > 1f) continue;
                float dist = inv * Vector3.Dot(e2, q);
                if (dist <= 0f || dist >= best) continue;
                best = dist;
                point = ray.origin + ray.direction * dist;
                normal = Vector3.Cross(e1, e2).normalized;
                if (Vector3.Dot(normal, ray.direction) > 0f) normal = -normal;
                float da = (a - point).sqrMagnitude, db = (b - point).sqrMagnitude, dc = (c - point).sqrMagnitude;
                particle = da <= db && da <= dc ? previewTriangles[t] : (db <= dc ? previewTriangles[t + 1] : previewTriangles[t + 2]);
            }
            return particle >= 0;
        }

        public void BeginDrag(Vector3 hit, int particle, SceneView view)
        {
            Fit.SyncPositions();
            preDrag = (Vector3[])Fit.Positions.Clone();
            preDragTime = Fit.SimTime;
            RecordUndo("Grab Cloth");
            dragging = true;
            dragHit = hit;
            Vector3 facing = view != null && view.camera != null ? -view.camera.transform.forward : Vector3.forward;
            dragPlane = new Plane(facing, hit);
            dragWeights = Fit.ConnectedWithin(particle, BrushRadius);
            dragStart = Fit.Positions;
            UpdateDragTargets(hit);
        }

        public void UpdateDrag(Ray ray)
        {
            if (!dragging) return;
            if (dragPlane.Raycast(ray, out float enter)) UpdateDragTargets(ray.origin + ray.direction * enter);
        }

        /// <summary>Moves the grab targets so the grabbed point follows <paramref name="cursor"/>.</summary>
        public void UpdateDragTargets(Vector3 cursor)
        {
            Vector3 offset = cursor - dragHit;
            Fit.GrabPins.Clear();
            foreach (KeyValuePair<int, float> kv in dragWeights)
            {
                Fit.GrabPins.Add(new SolverPin { particle = kv.Key, target = dragStart[kv.Key] + offset, weight = Mathf.Clamp01(kv.Value * BrushStrength) * 0.5f });
            }
            Fit.RefreshPins();
        }

        public void EndDrag()
        {
            dragging = false;
            Fit.GrabPins.Clear();
            Fit.RefreshPins();
            CommitUndo();
            if (!Running) { Running = true; Settled = false; }
            NotifyChanged();
        }

        public void CancelDrag()
        {
            if (!dragging) return;
            dragging = false;
            Fit.GrabPins.Clear();
            Fit.RefreshPins();
            if (preDrag != null) Fit.Restore(preDrag, preDragTime);
            Undo.RevertAllInCurrentGroup();
            PullPositions(true);
            NotifyChanged();
        }

        /// <summary>Adds pins round a particle (soft at the edge of the brush), or removes the pins there.</summary>
        public void ApplyPinBrush(int particle, bool erase)
        {
            Undo.RecordObject(FitData, erase ? "Erase Pins" : "Pin Cloth");
            Fit.SyncPositions();
            Dictionary<int, float> area = Fit.ConnectedWithin(particle, BrushRadius);
            var keys = new HashSet<int>(area.Keys);
            FitData.pins.RemoveAll(pin => keys.Contains(Fit.ParticleAt(pin.pieceId, pin.flatPosition)));
            if (!erase)
            {
                foreach (KeyValuePair<int, float> kv in area)
                {
                    int v = kv.Key;
                    var pin = new ClothPin
                    {
                        pieceId = Pattern.pieces[Fit.Mesh.piece[v]].id,
                        flatPosition = Fit.Mesh.drawn[v],
                        kind = NewPinKind,
                        weight = Mathf.Clamp01(kv.Value * BrushStrength),
                        target = Fit.Positions[v]
                    };
                    if (NewPinKind == PinKind.Body && Body.Collider.Closest(Fit.Positions[v], out int tri, out Vector3 onBody, out float d))
                    {
                        pin.bodyTriangle = tri;
                        pin.barycentric = UMASlotSkinTransfer.CalculateHitVertexBlend(onBody, Body.Collider.A[tri], Body.Collider.B[tri], Body.Collider.C[tri]);
                        pin.normalOffset = d;
                        pin.bodySlot = tri < Body.Surface.Triangles.Length ? Body.Surface.Triangles[tri].SlotName : null;
                    }
                    else pin.kind = PinKind.Placement;
                    FitData.pins.Add(pin);
                }
            }
            Fit.RefreshPins();
            NotifyChanged();
        }

        public void ClearPins()
        {
            Undo.RecordObject(FitData, "Clear Pins");
            FitData.pins.Clear();
            Fit.RefreshPins();
            NotifyChanged();
        }

        private const string ShowSeamsPref = "UMA.ClothesDesigner.ShowSeams";
        private static bool? showSeams;

        /// <summary>Whether the Scene view draws the seams: both sewn edges and the threads between them.</summary>
        public static bool ShowSeams
        {
            get
            {
                if (!showSeams.HasValue) showSeams = EditorPrefs.GetBool(ShowSeamsPref, true);
                return showSeams.Value;
            }
            set
            {
                if (ShowSeams == value) return;
                showSeams = value;
                EditorPrefs.SetBool(ShowSeamsPref, value);
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// Draws every seam in its Pattern Canvas colour: the two edges being sewn, and a thread from each
        /// point to the point it is sewn to while they are apart. What is behind the body or the cloth is
        /// drawn faintly, so the seams round the back still read.
        /// </summary>
        private void DrawSeams(SceneView view)
        {
            if (!ShowSeams || Event.current.type != EventType.Repaint) return;
            PatternMesh mesh = Fit.Mesh;
            Vector3[] p = Fit.Positions;
            if (mesh == null || p == null || p.Length != mesh.VertexCount || mesh.stitchSeam.Count != mesh.stitches.Count || mesh.stitches.Count == 0) return;
            Camera camera = view != null ? view.camera : Camera.current;
            Vector3 eye = camera != null ? camera.transform.position : Vector3.zero;
            // Lift the lines 2 mm towards the eye so the cloth they lie on does not hide them.
            Vector3 Lift(Vector3 point) => camera != null ? point + (eye - point).normalized * 0.002f : point;

            UnityEngine.Rendering.CompareFunction oldZTest = Handles.zTest;
            var threads = new List<Vector3>();
            for (int pass = 0; pass < 2; pass++)
            {
                Handles.zTest = pass == 0 ? UnityEngine.Rendering.CompareFunction.Greater : UnityEngine.Rendering.CompareFunction.LessEqual;
                float alpha = pass == 0 ? 0.3f : 1f;
                for (int start = 0; start < mesh.stitches.Count;)
                {
                    int seam = mesh.stitchSeam[start];
                    int end = start;
                    while (end < mesh.stitches.Count && mesh.stitchSeam[end] == seam) end++;
                    var sideA = new Vector3[end - start];
                    var sideB = new Vector3[end - start];
                    threads.Clear();
                    for (int k = start; k < end; k++)
                    {
                        Vector3 a = p[mesh.stitches[k].x], b = p[mesh.stitches[k].y];
                        sideA[k - start] = Lift(a);
                        sideB[k - start] = Lift(b);
                        if ((a - b).sqrMagnitude > 0.003f * 0.003f) { threads.Add(sideA[k - start]); threads.Add(sideB[k - start]); }
                    }
                    // The Pattern Canvas hue, stronger: the canvas is dark, the Scene view light grey.
                    Color.RGBToHSV(PatternEditing.SeamColor(seam), out float hue, out _, out _);
                    Color c = Color.HSVToRGB(hue, 0.9f, 0.95f);
                    Handles.color = new Color(c.r, c.g, c.b, alpha);
                    if (sideA.Length > 1)
                    {
                        Handles.DrawAAPolyLine(6f, sideA);
                        Handles.DrawAAPolyLine(6f, sideB);
                    }
                    for (int t = 0; t < threads.Count; t += 2)
                    {
                        Handles.DrawAAPolyLine(2.5f, threads[t], threads[t + 1]);
                    }
                    start = end;
                }
            }
            Handles.zTest = oldZTest;
        }

        private void DrawPins()
        {
            if (Event.current.type != EventType.Repaint) return;
            foreach (SolverPin pin in Fit.ActivePins)
            {
                if (pin.particle < 0 || pin.particle >= Fit.Positions.Length) continue;
                Vector3 p = Fit.Positions[pin.particle];
                Handles.color = Color.Lerp(new Color(0.3f, 0.8f, 1f, 0.5f), new Color(0.1f, 0.3f, 1f, 1f), pin.weight);
                Handles.DotHandleCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * 0.012f, EventType.Repaint);
            }
        }

        // ---- Publish and try on -----------------------------------------------------------------

        public void BakeAndTryOn()
        {
            LastBake = null;
            LastBakeError = null;
            List<string> problems = GarmentBaker.Validate(Fit);
            if (problems.Count > 0) { LastBakeError = string.Join(" ", problems); NotifyChanged(); return; }
            if (FitData.output.slot != null && !EditorUtility.DisplayDialog("Clothes Designer",
                "Bake again over '" + FitData.output.slot.name + "'?\n\nThe slot's mesh is replaced, so edits made to the slot directly are lost. Textures on the overlay and anything added to the recipe (mesh hides, tags) are kept. The slot, overlay and recipe keep their GUIDs, so everything using them keeps working.", "Bake", "Cancel"))
            {
                return;
            }
            try
            {
                EditorUtility.DisplayProgressBar("Clothes Designer", "Baking '" + FitData.publish.slotName + "'...", 0.5f);
                LastBake = GarmentBaker.Bake(Fit);
                ShowTryOn();
            }
            catch (Exception ex)
            {
                LastBakeError = ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            NotifyChanged();
        }

        /// <summary>Shows a second character of the race beside the body, wearing the baked recipe through the normal wardrobe.</summary>
        public void ShowTryOn()
        {
            HideTryOn();
            if (FitData == null || FitData.output.recipe == null) return;
            RaceData race = FitData.race != null ? FitData.race : Stage.CharacterRace;
            var go = new GameObject("Clothes Designer Try-On (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.position = new Vector3(1000f, 0f, 0f);
            tryOn = go.AddComponent<DynamicCharacterAvatar>();
            tryOn.editorTimeGeneration = false;
            tryOn.showPlaceholder = false;
            tryOn.activeRace.name = race.raceName;
            tryOn.activeRace.data = race;
            tryOn.GenerateNow();
            tryOn.SetSlot(FitData.output.recipe);
            tryOn.BuildCharacter(false, true);
            tryOn.GenerateNow();
            tryOn.enabled = false;
            RefreshTryOnView();
            // It stands beside the body, often out of view: show it.
            FrameTryOn();
        }

        public void RefreshTryOnView()
        {
            if (tryOn == null || tryOn.umaData == null) return;
            Animator animator = tryOn.umaData.animator;
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                Transform la = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), ra = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Transform ls = animator.GetBoneTransform(HumanBodyBones.LeftShoulder), rs = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
                Transform root = tryOn.transform;
                if (la != null && ra != null)
                {
                    bool shoulders = ls != null && rs != null;
                    if (tryOnArmRest == null)
                    {
                        tryOnArmRest = new[] { la.localRotation, ra.localRotation, shoulders ? ls.localRotation : Quaternion.identity, shoulders ? rs.localRotation : Quaternion.identity };
                    }
                    la.localRotation = tryOnArmRest[0];
                    ra.localRotation = tryOnArmRest[1];
                    if (shoulders) { ls.localRotation = tryOnArmRest[2]; rs.localRotation = tryOnArmRest[3]; }
                    if (TryOnArmsRaised)
                    {
                        // Each arm from where it starts (the race's pose may hold the arms level, or lower).
                        Transform le = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), re = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                        float leftRest = le != null ? Vector3.Angle(le.position - la.position, -root.up) : 90f;
                        float rightRest = re != null ? Vector3.Angle(re.position - ra.position, -root.up) : 90f;
                        SplitArmRaise(leftRest, TryOnArmAngle, shoulders, out float leftShoulder, out float leftArm);
                        SplitArmRaise(rightRest, TryOnArmAngle, shoulders, out float rightShoulder, out float rightArm);
                        // The collarbones first (the upper arms go with them), then the upper arms themselves.
                        if (shoulders)
                        {
                            ls.rotation = Quaternion.AngleAxis(-leftShoulder, root.forward) * ls.rotation;
                            rs.rotation = Quaternion.AngleAxis(rightShoulder, root.forward) * rs.rotation;
                        }
                        la.rotation = Quaternion.AngleAxis(-leftArm, root.forward) * la.rotation;
                        ra.rotation = Quaternion.AngleAxis(rightArm, root.forward) * ra.rotation;
                    }
                }
            }
            SkinnedMeshRenderer[] renderers = tryOn.umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0) return;
            if (tryOnMesh == null) tryOnMesh = new Mesh { name = "Try-On", hideFlags = HideFlags.DontSave };
            renderers[0].BakeMesh(tryOnMesh, true);
            if (tryOnView == null)
            {
                tryOnView = new GameObject("Clothes Designer Try-On View") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                tryOnView.AddComponent<MeshFilter>();
                tryOnView.AddComponent<MeshRenderer>();
                if (Stage.scene.IsValid()) SceneManager.MoveGameObjectToScene(tryOnView, Stage.scene);
            }
            float side = Body != null ? Body.Bounds.extents.x * 2f + 0.3f : 1.6f;
            tryOnView.transform.position = new Vector3(side, 0f, 0f);
            tryOnView.GetComponent<MeshFilter>().sharedMesh = tryOnMesh;
            tryOnView.GetComponent<MeshRenderer>().sharedMaterials = renderers[0].sharedMaterials;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// How much of an arm's angle from the side the shoulder blade and collarbone carry (degrees), the
        /// scapulohumeral rhythm: none for the first 30 degrees, then one for every two at the shoulder joint
        /// (a third of the rest), 50 with the arm straight up.
        /// </summary>
        public static float ShoulderPart(float armAngle) => Mathf.Clamp((armAngle - 30f) / 3f, 0f, 50f);

        /// <summary>
        /// Splits moving an arm from one angle to another (degrees from hanging at the sides) between the
        /// collarbone and the upper arm, the way a body does it: the shoulder joint alone only takes an
        /// arm to about level; higher, the shoulder blade and collarbone turn too.
        /// </summary>
        public static void SplitArmRaise(float fromAngle, float toAngle, bool hasShoulders, out float shoulder, out float arm)
        {
            toAngle = Mathf.Clamp(toAngle, 0f, 180f);
            shoulder = hasShoulders ? ShoulderPart(toAngle) - ShoulderPart(fromAngle) : 0f;
            arm = (toAngle - fromAngle) - shoulder;
        }

        public bool TryOnVisible => tryOnView != null;

        /// <summary>Points the Scene view at the try-on character.</summary>
        public void FrameTryOn()
        {
            if (tryOnView == null) return;
            Renderer r = tryOnView.GetComponent<Renderer>();
            if (r != null) Frame(r.bounds);
        }

        /// <summary>Points the Scene view at the body the clothes are fitted on, with the cloth.</summary>
        public void FrameFittingBody()
        {
            Renderer body = Stage != null ? Stage.CharacterRenderer : null;
            if (body == null) return;
            Bounds b = body.bounds;
            if (preview != null && preview.activeInHierarchy && preview.GetComponent<Renderer>() is Renderer cloth) b.Encapsulate(cloth.bounds);
            Frame(b);
        }

        private static void Frame(Bounds bounds)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.Frame(bounds, false);
            view.Repaint();
        }

        public void HideTryOn()
        {
            if (tryOnView != null) UnityEngine.Object.DestroyImmediate(tryOnView);
            if (tryOnMesh != null) UnityEngine.Object.DestroyImmediate(tryOnMesh);
            if (tryOn != null) UnityEngine.Object.DestroyImmediate(tryOn.gameObject);
            tryOnView = null; tryOnMesh = null; tryOn = null; tryOnArmRest = null;
        }

        // ---- Unsaved work and closing --------------------------------------------------------------

        public bool HasUnsavedChanges => PatternDirty || FitDirty;

        public string DescribeUnsavedChanges()
        {
            var parts = new List<string>();
            if (PatternDirty) parts.Add("the pattern '" + Pattern.name + "'");
            if (FitDirty) parts.Add("the fit (placement, pins and kept drape)");
            return parts.Count == 0 ? null : "Not saved: " + string.Join(" and ", parts) + ".";
        }

        public bool SaveAll()
        {
            bool ok = true;
            if (PatternDirty) ok &= SavePattern(false);
            if (ok && FitDirty) ok &= SaveFit(false);
            return ok;
        }

        public void DiscardAll()
        {
            savedPatternJson = Pattern != null ? JsonUtility.ToJson(Pattern) : null;
            savedFitJson = FitData != null ? JsonUtility.ToJson(FitData) : null;
        }

        private void NotifyChanged()
        {
            Changed?.Invoke();
            Stage?.RepaintEditors();
        }

        public void Dispose()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Running = false;
            Fit.Dispose();
            Refit.Dispose();
            DestroyPreview();
            HideTryOn();
            if (undoState != null) { Undo.ClearUndo(undoState); UnityEngine.Object.DestroyImmediate(undoState); }
            undoState = null;
        }
    }
}
