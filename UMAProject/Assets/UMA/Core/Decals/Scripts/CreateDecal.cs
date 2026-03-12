using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMA;
using UMA.CharacterSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Runtime helper to orbit a camera around an UMA avatar and place decal slots on left click.
/// Adds triangle debug selection and removal for last decal (slot or RT).
/// </summary>
namespace UMA.Decals
{

    public class CreateDecal : MonoBehaviour
    {
		public static int MatrixLevel = 0;
		public enum rebuildMethod
		{
			FullRebuild,
			ForceTextures,
			ForceTexturesAndDNA,
			ForceTexturesAndMesh,
			ForceAll
		};
		public enum DecalMethod
        {
            SlotDecal, RenderTexture
        };
        [Header("References")]
        [Tooltip("Camera used for orbiting & raycasting.")]
        public Camera OrbitCamera;
        [Tooltip("UMA avatar to target.")]
        public DynamicCharacterAvatar Avatar;
        [Tooltip("OverlayDataAsset used for the decal (must reference the correct UMAMaterial).")]
        public OverlayDataAsset MeshDecalOverlay;
        [Tooltip("OverlayDataAsset used for texture-based decals (must reference the correct UMAMaterial).")]
        public OverlayDataAsset TextureDecalOverlay;
        [Tooltip("DecalRTStampSlot used to store generated DecalRTStampAssets when using RenderTexture decals.")]
        public DecalRTStampSlot StampField; // Added field per request

		[Header("Stamps")]
		[Tooltip("Currently selected stamp (set by clicking in the Stamps list).")]
		public DecalRTStampAsset CurrentStamp;

		[Header("Slot Generation")]
		[Tooltip("Slot name used by the 'Generate and Save a Slot' tool.")]
		public string GeneratedSlotName;

        [Header("Decal Settings")]
        [Tooltip("Method used to create decals. SlotDecal uses DecalSlotBuilder, RenderTexture uses UMA's built-in render texture decal system.")]
        public DecalMethod decalMethod = DecalMethod.SlotDecal;

        [Tooltip("World-space radius for decal selection.")]
        public float DecalRadius = 0.05f;
        [Tooltip("Fudge factor added to radius to ensure we capture edge cases.")]
        public float fudgeRadius = 0.01f; // Small extra radius to ensure we capture edge cases
        [Tooltip("Rotation around surface normal (degrees, clockwise looking along normal).")]
        public float DecalRotationDegrees = 0f;

        public bool useHitNormalForProjection = true;

        [Tooltip("If true, randomize decal rotation instead of using DecalRotationDegrees.")]
        public bool randomizeRotation = false;

        [Tooltip("Offset applied to decal slot along normal (fixed point 1/100 of a mm , to avoid z-fighting).")]
        public int slotOffset = 3000;
        [Tooltip("Dilation factor for decal render texture method (in pixels, to avoid edge artifacts).")]
        public int decalRTDilation = 8;
        [Tooltip("Expand stamped triangles in UV space (pixels) to reduce seams in RT decals.")]
        public float DecalRTUVExpandPixels = 0.75f;

		[Header("Debug Selection")]
        [Tooltip("Enable triangle debug mode for the last created decal.")]
        public bool EnableTriangleDebug = false;

        [Header("Animation")]
        [Tooltip("Pause the Animator(s) on the selected avatar while working.")]
        public bool PauseAvatarAnimation = false;

		

		[Header("Debug Visualization")]
		[Tooltip("Prefab used to visualize decal placement points.")]
		public GameObject debugSpherePrefab;
        public Color TattooColor;

		[Header("Edit Mode Colors")]
		public Color EditFillKeepColor = new Color(0f, 1f, 0f, 0.5f);
		public Color EditFillRemoveColor = new Color(1f, 0f, 0f, 0.5f);
		public Color EditFillAddColor = new Color(0f, 1f, 0f, 0.5f);
		public Color EditOutlineKeepColor = new Color(0f, 1f, 0f, 1f);
		public Color EditOutlineRemoveColor = new Color(1f, 0f, 0f, 1f);
		public Color EditOutlineUnusedColor = new Color(0f, 1f, 1f, 0.9f);
		public Color EditOutlineAddColor = new Color(0f, 1f, 0f, 1f);

        [Header("Decal Overlay Handling")]
        [Tooltip("If true, automatically add affected overlays to a rt decal slot when using RenderTexture decals.")]
        public bool AutoAddOverlays = true; // If true, automatically add the overlay used for decal creation to the decal slot
        [Tooltip("If true, call Draw on the decal RTs immediately after stamping (otherwise they are drawn during UMAData.Update")]
        public bool DrawRenderTexturesImmediately = false; // If true, call Draw on the decal RTs immediately after stamping (otherwise they are drawn during UMAData.Update)

		public rebuildMethod RebuildMethod = rebuildMethod.ForceTextures;

		// Internal debug state
		private SkinnedMeshRenderer _dbgSmr;
        private int[] _dbgSmrTriangles;                 // Combined SMR triangles (tri indices)
        private Dictionary<int, int> _dbgTriToOrdinal;   // Combined triIndex -> ordinal in last decal
        private int _dbgSequence;                       // Sequence to detect new decal
        private readonly HashSet<int> _selectedOrdinals = new HashSet<int>(); // in-decal -> remove (red)
        private readonly HashSet<int> _selectedAddCombinedTris = new HashSet<int>(); // out-of-decal -> add (green)
        private Mesh _dbgBaked;

        // Paint mode state
        private bool _paintActive;
        private bool _paintForRemoval;     // true = paint removal on in-decal triangles; false = paint addition on non-decal triangles
        private bool _paintTargetSelected; // the target selection state to apply while painting
        private readonly HashSet<int> _paintVisited = new HashSet<int>();

		// UV adjust mode state (Ctrl + LMB)
		private bool _uvAdjustActive;
		private Vector2 _uvAdjustLastMouse;
		private HashSet<int> _uvAdjustVerts;

        // Undo/redo stacks
        private readonly Stack<HashSet<int>> _undo = new Stack<HashSet<int>>();
        private readonly Stack<HashSet<int>> _redo = new Stack<HashSet<int>>();

        // Animator speed cache for pause/resume
        private readonly Dictionary<Animator, float> _animatorSpeedCache = new Dictionary<Animator, float>();

        // GL line material
        private static Material _lineMat;

        // UI state for improved interface
        private Vector2 _scrollPosition;
		private Vector2 _stampsScrollPosition;
        private bool _showDebugSettings = false;
		public bool debugShowSpheres = false;
		public float DecalScale = 1.0f;

		[Header("Orbit Settings")]
        [Tooltip("Offset from avatar root used as orbit pivot.")]
        public Vector3 OrbitOffset = new Vector3(0f, 1f, 0f);
        [Tooltip("Horizontal orbit sensitivity (degrees per normalized screen movement).")]
        public float OrbitSensitivityX = 180f;
        [Tooltip("Vertical orbit sensitivity (degrees per normalized screen movement).")]
        public float OrbitSensitivityY = 120f;
        [Tooltip("Clamp for vertical orbit (min pitch).")]
        public float MinPitch = -80f;
        [Tooltip("Clamp for vertical orbit (max pitch).")]
        public float MaxPitch = 80f;
        [Tooltip("Scroll wheel zoom speed.")]
        public float ZoomSensitivity = 2f;
        [Tooltip("Minimum orbit distance.")]
        public float MinDistance = 2f;
        [Tooltip("Maximum orbit distance.")]
        public float MaxDistance = 10f;
        [Tooltip("Vertical pan speed when holding Shift while orbiting (world units per second per pixel).")]
        public float PanSensitivityY = 1.5f;

        [Header("Input")]
        [Tooltip("Hold this mouse button (1 = right) to orbit.")]
        public int OrbitMouseButton = 1;
        [Tooltip("Mouse button (0 = left) used to place decals.")]
        public int PlaceMouseButton = 0;

        // Internal orbit state
        private float _yaw;
        private float _pitch;
        private float _distance = 5f;
        private Vector3 _targetPos;
        private Rect ScreenArea = new Rect(20f, 20f, 420, 1024);

        private bool _initialized;

        void Start()
        {
#if UNITY_EDITOR
            EnsureTag("debugSphere");
#endif

            if (StampField != null && Avatar != null)
            {
                StampField.OnCharacterBegun(Avatar.umaData);
            }
#if UMA_ADDRESSABLES
			// ensure the overlays are loaded from Addressables
			// by requesting them via the indexers LoadLabelList function.
			// use the last label assigned to the overlay asset
			//
			List<string> addresses = new List<string>();
			if(MeshDecalOverlay != null) {
				AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<OverlayDataAsset>(MeshDecalOverlay.overlayName);
				if(ai != null) {
					addresses.Add(ai.AddressableAddress);
				}
			}
			if(TextureDecalOverlay != null) {
				AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<OverlayDataAsset>(TextureDecalOverlay.overlayName);
				if(ai != null) {
					addresses.Add(ai.AddressableAddress);
				}
			}
			if (addresses.Count == 0) {
				InitializeOrbit();
				return;
			}
			var op = UMAAssetIndexer.Instance.LoadLabelList(addresses,false);
			op.Completed += (handle) =>
			{
				Debug.Log($"CreateDecal: Addressables load complete for overlays.");
				InitializeOrbit();
			};
#else
			InitializeOrbit();
#endif
		}

		private void OnDisable()
        {
            // Ensure we restore animators if this component is disabled while paused
            if (PauseAvatarAnimation)
            {
                PauseAvatarAnimation = false;
                ApplyAnimationPauseState();
            }
        }

        static void EnsureLineMaterial()
        {
            if (_lineMat == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                _lineMat = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _lineMat.SetInt("_ZWrite", 0);
                _lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _lineMat.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _lineMat.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
        }

        void InitializeOrbit()
        {
            if (Avatar == null || OrbitCamera == null)
            {
                return;
            }
			_initialized = true;

			_targetPos = Avatar.transform.position + OrbitOffset;
            Vector3 camPos = OrbitCamera.transform.position;
            Vector3 dir = camPos - _targetPos;
            _distance = Mathf.Clamp(dir.magnitude, MinDistance, MaxDistance);
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector3.back;
            }

            // Derive yaw/pitch
            _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Asin(dir.normalized.y) * Mathf.Rad2Deg;
            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);

            UpdateCameraTransform();
        }

        private void OnGUI() {
			GUILayout.BeginArea(ScreenArea, GUI.skin.window);

#if UNITY_EDITOR
			if (StampField != null)
			{
				if (EditorUtility.IsDirty(StampField))
				{
					GUILayout.Label("Stamp Slot Modified (Unsaved Changes)");
					if (GUILayout.Button("Save Moified Stamps"))
					{
						AssetDatabase.SaveAssetIfDirty(StampField);
                    }
                }
			}

			GUILayout.Space(6);
			GUILayout.Label("Generate Utility Slot");
			GUILayout.BeginHorizontal();
			GUILayout.Label("Slot Name:", GUILayout.Width(100));
			GeneratedSlotName = GUILayout.TextField(GeneratedSlotName ?? string.Empty, GUILayout.Width(200));
			GUILayout.EndHorizontal();
			if (GUILayout.Button("Generate and Save a Slot"))
			{
				GenerateAndSaveSlot();
			}
#endif

            // Use scroll view for expandable content
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
			if(EnableTriangleDebug) {
				DrawEditorPanel();
			}
			else {
				DrawLeftPanel();
			}

			GUILayout.EndScrollView();
			GUILayout.EndArea();

			DrawRightPanel();

			// Apply pause state from the toggle each GUI frame
			ApplyAnimationPauseState();
		}

#if UNITY_EDITOR
		private void GenerateAndSaveSlot()
		{
			try
			{
				if (StampField == null)
				{
					EditorUtility.DisplayDialog("UMA", "No StampField assigned.", "OK");
					return;
				}

				string slotName = (GeneratedSlotName ?? string.Empty).Trim();
				if (string.IsNullOrEmpty(slotName))
				{
					EditorUtility.DisplayDialog("UMA", "Please enter a Slot Name.", "OK");
					return;
				}

				var stampGO = StampField.gameObject;
				if (stampGO == null)
				{
					EditorUtility.DisplayDialog("UMA", "StampField has no GameObject.", "OK");
					return;
				}

				string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
				string absFolder = EditorUtility.OpenFolderPanel("Select folder to save slot assets", Application.dataPath, "");
				if (string.IsNullOrEmpty(absFolder))
				{
					return;
				}

				absFolder = absFolder.Replace('\\', '/');
				string absAssetsRoot = (Application.dataPath).Replace('\\', '/');
				if (!absFolder.StartsWith(absAssetsRoot, System.StringComparison.OrdinalIgnoreCase))
				{
					EditorUtility.DisplayDialog("UMA", "Folder must be inside this project's Assets folder.", "OK");
					return;
				}

				string relFolder = "Assets" + absFolder.Substring(absAssetsRoot.Length);
				relFolder = relFolder.TrimEnd('/');
				if (!AssetDatabase.IsValidFolder(relFolder))
				{
					EditorUtility.DisplayDialog("UMA", "Selected folder is not a valid Unity Assets folder.", "OK");
					return;
				}

				// 1) Create prefab clone containing the DecalRTStampSlot
				string stampsPrefabName = slotName + "_Stamps";
				string prefabPath = AssetDatabase.GenerateUniqueAssetPath(relFolder + "/" + stampsPrefabName + ".prefab");
				var temp = Instantiate(stampGO);
				temp.name = stampsPrefabName;
				GameObject prefabAsset;
				try
				{
					prefabAsset = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
				}
				finally
				{
					DestroyImmediate(temp);
				}

				if (prefabAsset == null)
				{
					EditorUtility.DisplayDialog("UMA", "Failed to create stamp prefab.", "OK");
					return;
				}

				var prefabStampSlot = prefabAsset.GetComponent<DecalRTStampSlot>();
				if (prefabStampSlot == null)
				{
					EditorUtility.DisplayDialog("UMA", "Created prefab does not contain DecalRTStampSlot.", "OK");
					return;
				}

				// 2) Create utility SlotDataAsset
				string assetPath = AssetDatabase.GenerateUniqueAssetPath(relFolder + "/" + slotName + ".asset");
				var newSlot = UMA.CustomAssetUtility.CreateAsset<SlotDataAsset>(assetPath, false, slotName);
				if (newSlot == null)
				{
					EditorUtility.DisplayDialog("UMA", "Failed to create SlotDataAsset.", "OK");
					return;
				}

				newSlot.name = slotName;
				newSlot.slotName = slotName;
				newSlot.SlotObject = prefabAsset;
				EditorUtility.SetDirty(newSlot);

				// 3) Hook CharacterBegun -> prefabStampSlot.OnCharacterBegun
				if (newSlot.CharacterBegun == null)
				{
					newSlot.CharacterBegun = new UMADataEvent();
				}
				UnityEditor.Events.UnityEventTools.AddPersistentListener(newSlot.CharacterBegun, prefabStampSlot.OnCharacterBegun);
				EditorUtility.SetDirty(newSlot);
				AssetDatabase.SaveAssetIfDirty(newSlot);

				EditorUtility.DisplayDialog("UMA", $"Created utility slot '{slotName}'.\nPrefab: {prefabPath}\nSlot: {AssetDatabase.GetAssetPath(newSlot)}", "OK");
			}
			catch (System.Exception ex)
			{
				EditorUtility.DisplayDialog("UMA", "GenerateAndSaveSlot failed: " + ex.Message, "OK");
			}
		}
#endif

		private void DrawRightPanel() {

			// Stamps panel (right side)
			float stampsWidth = Mathf.Clamp(Screen.width * 0.2f, 200f, Screen.width);
			var stampsArea = new Rect(Screen.width - stampsWidth - 20f, 20f, stampsWidth, Screen.height - 40f);
			GUILayout.BeginArea(stampsArea, "Stamps", GUI.skin.window);
			try {
				if(StampField == null || StampField.overlayStamps == null) {
					GUILayout.Label("No Stamp Slot");
				} else {
					var allStamps = new List<DecalRTStampAsset>();
					for(int si = 0; si < StampField.overlayStamps.Count; si++) {
						var set = StampField.overlayStamps[si];
						if(set == null || set.stamps == null || set.stamps.Length == 0) {
							continue;
						}
						allStamps.AddRange(set.stamps);
					}

					_stampsScrollPosition = GUILayout.BeginScrollView(_stampsScrollPosition);
					try {
						for(int i = allStamps.Count - 1; i >= 0; i--) {
							var stamp = allStamps[i];
							if(stamp == null)
								continue;

							GUILayout.BeginHorizontal();
							try {
								GUILayout.Label(ReferenceEquals(stamp, CurrentStamp) ? "*" : string.Empty, GUILayout.Width(24), GUILayout.Height(24));
								if(GUILayout.Button(stamp.name, GUILayout.Height(24))) {
									// Switching stamps must reset debug state first, otherwise cached mappings
									// can briefly show triangles from the previously selected stamp/slot.
									ClearCurrent();
									CurrentStamp = stamp;
									if(decalMethod == DecalMethod.RenderTexture) {
										RefreshLastDecalDebug();
									}
									ToTriangleDebugMode();
								}
								if(GUILayout.Button("X", GUILayout.Width(24), GUILayout.Height(24))) {
									if(ReferenceEquals(CurrentStamp, stamp)) {
										CurrentStamp = null;
										ClearCurrent();
										RebuildAvatar();
									}
									StampField.RemoveStamp(stamp);

								}
							} finally {
								GUILayout.EndHorizontal();
							}
						}
					} finally {
						GUILayout.EndScrollView();
					}
				}
			} finally {
				GUILayout.EndArea();
			}
		}

		private void DrawEditorPanel() {
			if(EnableTriangleDebug) {
				if(_dbgTriToOrdinal == null) {
					RefreshLastDecalDebug();
				}

				GUILayout.Label("Edit Mode Active");

				GUILayout.Label("Debug Selection (Shift + Left Click to paint)");

				GUILayout.BeginHorizontal();
				GUI.enabled = _dbgTriToOrdinal != null;

				if(GUILayout.Button("Clear Selection", GUILayout.Width(100))) {
					_selectedOrdinals.Clear();
					_selectedAddCombinedTris.Clear();
					_undo.Clear();
					_redo.Clear();
				}
				GUILayout.EndHorizontal();

				GUILayout.BeginHorizontal();
				if(GUILayout.Button("Clear", GUILayout.Width(70))) {
					PushUndo();
					_selectedOrdinals.Clear();
					_selectedAddCombinedTris.Clear();
				}
				if(GUILayout.Button("Select All", GUILayout.Width(90))) {
					PushUndo();
					SelectAll();
				}
				if(GUILayout.Button("Invert", GUILayout.Width(70))) {
					PushUndo();
					InvertSelection();
				}
				GUI.enabled = true;
				GUILayout.EndHorizontal();

				float lastDecalRotationDegrees = DecalRotationDegrees;
				float lastDecalScale = DecalScale;
		

				GUILayout.BeginHorizontal();				
				GUILayout.Label($"Rotation: {DecalRotationDegrees:F1}°", GUILayout.Width(100));
				DecalRotationDegrees = GUILayout.HorizontalSlider(DecalRotationDegrees, -180f, 180f);
				GUILayout.EndHorizontal();

				if (DecalRotationDegrees != lastDecalRotationDegrees)
				{
					float delta = DecalRotationDegrees - lastDecalRotationDegrees;
					RotateCurrentStampUVs(delta);
				}

				GUILayout.BeginHorizontal();
				GUILayout.Label($"Scale: {DecalScale:F2}x", GUILayout.Width(100));
				DecalScale = GUILayout.HorizontalSlider(DecalScale, 0.25f, 4.0f);
				GUILayout.EndHorizontal();

				if (!Mathf.Approximately(DecalScale, lastDecalScale))
				{
					float deltaScale = DecalScale / Mathf.Max(0.0001f, lastDecalScale);
					ScaleCurrentStampUVs(deltaScale);
				}

				GUILayout.BeginHorizontal();
				GUI.enabled = _undo.Count > 0;
				if(GUILayout.Button("Undo", GUILayout.Width(70))) {
					PopUndo();
				}
				GUI.enabled = _redo.Count > 0;
				if(GUILayout.Button("Redo", GUILayout.Width(70))) {
					PopRedo();
				}
				GUI.enabled = true;
				GUILayout.EndHorizontal();

				GUILayout.Label($"Remove (red): {_selectedOrdinals.Count}  Add (green): {_selectedAddCombinedTris.Count}");
				GUILayout.BeginHorizontal();
				if(GUILayout.Button("Apply Changes")) {
					ApplySelectedChanges();
				}
				if(GUILayout.Button("Exit Edit Mode")) {
					EnableTriangleDebug = false;
				}
				GUILayout.EndHorizontal();

				GUILayout.Space(6);
				GUILayout.Label("Edit Mode Controls:");
				GUILayout.Label("- Left Click: toggle triangle selection (red = remove from decal, green = add to decal)");
				GUILayout.Label("  (Note: green addition only works for SlotDecals)");
				GUILayout.Label("- Shift + Left Click/Drag: paint selected status");
				GUILayout.Label("- Ctrl + Left Click/Drag: move decal texture (adjusts overlay UVs of selected triangles)");
				GUILayout.Label("- Use Select All / Invert / Clear for bulk selection changes");
				GUILayout.Label("Navigation:");
				GUILayout.Label("- Right Click + Drag: orbit camera");
				GUILayout.Label("- Shift + Right Click + Drag: pan vertically");
				GUILayout.Label("- Mouse Wheel: zoom");

			}
		}
		private void DrawLeftPanel() {



			// Decal Method Toggle
			GUILayout.BeginHorizontal();
			GUILayout.Label("Decal Method:", GUILayout.Width(100));
			if(GUILayout.Button($"{decalMethod}", GUILayout.Width(150))) {
				decalMethod = decalMethod == DecalMethod.SlotDecal ? DecalMethod.RenderTexture : DecalMethod.SlotDecal;
			}
			GUILayout.EndHorizontal();

			GUILayout.Space(5);

			// Active Overlay Selection
			GUILayout.Label("Active Overlay:");
			if(decalMethod == DecalMethod.SlotDecal) {
				GUILayout.BeginHorizontal();
				GUILayout.Label("Mesh Decal:", GUILayout.Width(100));
#if UNITY_EDITOR
                MeshDecalOverlay = (OverlayDataAsset)EditorGUI.ObjectField(GUILayoutUtility.GetRect(200, 18), MeshDecalOverlay, typeof(OverlayDataAsset), false);
#else
				GUILayout.Label(MeshDecalOverlay != null ? MeshDecalOverlay.name : "None", GUILayout.Width(200));
#endif
				GUILayout.EndHorizontal();
			} else {
				GUILayout.BeginHorizontal();
				GUILayout.Label("Texture Decal:", GUILayout.Width(100));
#if UNITY_EDITOR
                TextureDecalOverlay = (OverlayDataAsset)EditorGUI.ObjectField(GUILayoutUtility.GetRect(200, 18), TextureDecalOverlay, typeof(OverlayDataAsset), false);
#else
				GUILayout.Label(TextureDecalOverlay != null ? TextureDecalOverlay.name : "None", GUILayout.Width(200));
#endif
				GUILayout.EndHorizontal();

				GUILayout.BeginHorizontal();
				GUILayout.Label("Stamp Field:", GUILayout.Width(100));
#if UNITY_EDITOR
                StampField = (DecalRTStampSlot)EditorGUI.ObjectField(GUILayoutUtility.GetRect(200, 18), StampField, typeof(DecalRTStampSlot), true);
#else
				GUILayout.Label(StampField != null ? StampField.name : "None", GUILayout.Width(200));
#endif
				GUILayout.EndHorizontal();
			}

			GUILayout.Space(5);

			// Radius/Rotation Controls
			GUILayout.Label("Decal Settings:");

			GUILayout.BeginHorizontal();
			GUILayout.Label($"Radius: {DecalRadius:F3}", GUILayout.Width(100));
			float newRadius = GUILayout.HorizontalSlider(DecalRadius, 0.01f, 0.5f, GUILayout.Width(150));
			if(System.Math.Abs(newRadius - DecalRadius) > 0.001f) {
				DecalRadius = newRadius;
				UpdateLastDecalIfExists();
			}
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			GUILayout.Label("Fudge Radius:", GUILayout.Width(100));
			string fudgeStr = GUILayout.TextField(fudgeRadius.ToString("F4"), GUILayout.Width(80));
			if(float.TryParse(fudgeStr, out float newFudge)) {
				fudgeRadius = newFudge;
			}
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			GUILayout.Label($"Rotation: {DecalRotationDegrees:F1}°", GUILayout.Width(100));
			float newRotation = GUILayout.HorizontalSlider(DecalRotationDegrees, 0f, 360f, GUILayout.Width(150));
			if(System.Math.Abs(newRotation - DecalRotationDegrees) > 0.1f) {
				DecalRotationDegrees = newRotation;
				UpdateLastDecalIfExists();
			}
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			randomizeRotation = GUILayout.Toggle(randomizeRotation, "Randomize Rotation", GUILayout.Width(150));
			useHitNormalForProjection = GUILayout.Toggle(useHitNormalForProjection, "Use Hit Normal", GUILayout.Width(150));
			GUILayout.EndHorizontal();

			if(GUILayout.Button("Clear All Decals")) {
				ClearAllDecals();
			}

			// Method-specific settings
			if(decalMethod == DecalMethod.SlotDecal) {
				GUILayout.BeginHorizontal();
				GUILayout.Label("Slot Offset:", GUILayout.Width(100));
				string offsetStr = GUILayout.TextField(slotOffset.ToString(), GUILayout.Width(80));
				if(int.TryParse(offsetStr, out int newOffset)) {
					slotOffset = newOffset;
				}
				GUILayout.EndHorizontal();
			} else {
				GUILayout.BeginHorizontal();
				GUILayout.Label("RT Dilation:", GUILayout.Width(100));
				string dilationStr = GUILayout.TextField(decalRTDilation.ToString(), GUILayout.Width(80));
				if(int.TryParse(dilationStr, out int newDilation)) {
					decalRTDilation = newDilation;
				}
				GUILayout.EndHorizontal();

				GUILayout.BeginHorizontal();
				GUILayout.Label("RT UV Expand (px):", GUILayout.Width(120));
				string expandStr = GUILayout.TextField(DecalRTUVExpandPixels.ToString("F2"), GUILayout.Width(80));
				if(float.TryParse(expandStr, out float newExpand)) {
					DecalRTUVExpandPixels = Mathf.Clamp(newExpand, 0f, 8f);
				}
				GUILayout.EndHorizontal();

				GUILayout.BeginHorizontal();
				AutoAddOverlays = GUILayout.Toggle(AutoAddOverlays, "Auto Add Overlays", GUILayout.Width(140));
				DrawRenderTexturesImmediately = GUILayout.Toggle(DrawRenderTexturesImmediately, "Draw RT Immediately", GUILayout.Width(140));
				GUILayout.EndHorizontal();
			}


			GUILayout.Space(5);

			// Place Decal Functionality
			GUILayout.Label("Controls:");
			GUILayout.Label("Left Click: Place Decal (disabled in Debug)");
			GUILayout.Label("Right Click + Drag: Orbit Camera");
			GUILayout.Label("Shift + Right Click + Drag: Pan Vertically");
			GUILayout.Label("Mouse Wheel: Zoom");

			GUILayout.BeginHorizontal();
			if(GUILayout.Button("Restart Avatar", GUILayout.Width(120))) {
				RebuildAvatar();
			}
			PauseAvatarAnimation = GUILayout.Toggle(PauseAvatarAnimation, "Pause Animation", GUILayout.Width(140));
			GUILayout.EndHorizontal();
		}


		private void ToTriangleDebugMode() {
			EnableTriangleDebug = true; // ensure debug mode is set
			PauseAvatarAnimation = true;
			DecalRotationDegrees = 0.0f;
			ApplyAnimationPauseState();
		}

		private void RotateCurrentStampUVs(float deltaDegrees)
		{
			if (Mathf.Abs(deltaDegrees) < 0.0001f)
			{
				return;
			}
			if (decalMethod != DecalMethod.RenderTexture || CurrentStamp == null || CurrentStamp.slots == null)
			{
				return;
			}

			float rad = deltaDegrees * Mathf.Deg2Rad;
			float c = Mathf.Cos(rad);
			float s = Mathf.Sin(rad);

			for (int i = 0; i < CurrentStamp.slots.Count; i++)
			{
				var ss = CurrentStamp.slots[i];
				if (ss == null || ss.debugDontUse) continue;
				// Rotate only decal-space UVs (overlay sampling). Rotating base UVs would move the stamped area around the model.
				RotateUVArray01InPlace(ss.overlayUV, c, s);
			}

			#if UNITY_EDITOR
			EditorUtility.SetDirty(CurrentStamp);
			// AssetDatabase.SaveAssetIfDirty(CurrentStamp);
			#endif
			// Re-stamp so the rotated UVs are applied to the avatar render textures.
			RebuildAvatar();
			RefreshLastDecalDebug();
		}

		private void ScaleCurrentStampUVs(float deltaScale)
		{
			if (Mathf.Abs(deltaScale - 1f) < 0.0001f)
			{
				return;
			}
			if (decalMethod != DecalMethod.RenderTexture || CurrentStamp == null || CurrentStamp.slots == null)
			{
				return;
			}

			for (int i = 0; i < CurrentStamp.slots.Count; i++)
			{
				var ss = CurrentStamp.slots[i];
				if (ss == null || ss.debugDontUse) continue;
				ScaleUVArray01InPlace(ss.overlayUV, deltaScale);
			}

			#if UNITY_EDITOR
			EditorUtility.SetDirty(CurrentStamp);
			//AssetDatabase.SaveAssetIfDirty(CurrentStamp);
			#endif
			RebuildAvatar();
			RefreshLastDecalDebug();
		}

		private static void RotateUVArray01InPlace(Vector2[] uvs, float c, float s)
		{
			if (uvs == null || uvs.Length == 0) return;
			for (int i = 0; i < uvs.Length; i++)
			{
				Vector2 uv = uvs[i];
				uv.x -= 0.5f;
				uv.y -= 0.5f;
				float x = uv.x;
				float y = uv.y;
				uv.x = x * c - y * s;
				uv.y = x * s + y * c;
				uv.x += 0.5f;
				uv.y += 0.5f;
				uvs[i] = uv;
			}
		}

		private static void ScaleUVArray01InPlace(Vector2[] uvs, float deltaScale)
		{
			if (uvs == null || uvs.Length == 0) return;
			// overlayUV maps from destination (mesh UVs) into decal overlay space.
			// To make the decal appear larger, we must sample a smaller portion of the overlay texture -> scale UVs toward the center.
			float inv = 1f / Mathf.Max(0.0001f, deltaScale);
			for (int i = 0; i < uvs.Length; i++)
			{
				Vector2 uv = uvs[i];
				uv.x -= 0.5f;
				uv.y -= 0.5f;
				uv *= inv;
				uv.x += 0.5f;
				uv.y += 0.5f;
				uvs[i] = uv;
			}
		}

		private void ClearAllDecals()
        {
            if (StampField != null)
            {
                StampField.ClearAllStamps();
				RebuildAvatar();
            }
        }


        void Update()
        {
            if (!_initialized)
            {
				return;
            }

            // Auto-pause when debug is enabled at runtime (safety net)
            if (EnableTriangleDebug && !PauseAvatarAnimation)
            {
                PauseAvatarAnimation = true;
                ApplyAnimationPauseState();
            }

            HandleOrbitInput();
            HandleZoom();
            UpdateCameraTransform();



            if (!EnableTriangleDebug)
            {
                PauseAvatarAnimation = true; // always pause when placing decals
                HandlePlacement();
            }
            // Keep pause state applied across avatar rebuilds
            if (PauseAvatarAnimation)
            {
                ApplyAnimationPauseState();
            }
            if (EnableTriangleDebug)
            {
                EnsureDebugBake();
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
				bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
				if(shift) {
					HandlePaintMode();
				} else if(ctrl) {
					HandleUVAdjustMode();
				} else if(Input.GetMouseButtonDown(0)) {
					HandleTriangleToggleClick();
				}
            }
        }


		private void HandleUVAdjustMode() {
			if (decalMethod != DecalMethod.RenderTexture || CurrentStamp == null || CurrentStamp.slots == null)
			{
				_uvAdjustActive = false;
				_uvAdjustVerts = null;
				return;
			}

			// Begin drag
			if (Input.GetMouseButtonDown(0))
			{
				_uvAdjustActive = true;
				_uvAdjustLastMouse = Input.mousePosition;

				// Build a vertex set for all currently selected triangles (remove-set + add-set)
				_uvAdjustVerts = new HashSet<int>();
				for (int si = 0; si < CurrentStamp.slots.Count; si++)
				{
					var ss = CurrentStamp.slots[si];
					if (ss == null || ss.debugDontUse) continue;
					if (ss.triangles == null || ss.triangles.Length == 0) continue;
					if (ss.overlayUV == null || ss.overlayUV.Length == 0) continue;

					int triCount = ss.triangles.Length / 3;
#if UNITY_EDITOR
					var ords = ss.triOrdinals;
					if (ords == null || ords.Length != triCount) continue;

					for (int t = 0; t < triCount; t++)
					{
						int ord = ords[t];
						bool selected = _selectedOrdinals.Contains(ord);
						// Also treat triangles selected-to-add as selected for UV adjust.
						if (!selected && _dbgTriToOrdinal != null)
						{
							foreach (var kv in _dbgTriToOrdinal)
							{
								if (kv.Value == ord)
								{
									if (_selectedAddCombinedTris.Contains(kv.Key))
										selected = true;
									break;
								}
							}
						}
						if (!selected) continue;

						int i0 = ss.triangles[t * 3 + 0];
						int i1 = ss.triangles[t * 3 + 1];
						int i2 = ss.triangles[t * 3 + 2];
						if ((uint)i0 < ss.overlayUV.Length) _uvAdjustVerts.Add(i0);
						if ((uint)i1 < ss.overlayUV.Length) _uvAdjustVerts.Add(i1);
						if ((uint)i2 < ss.overlayUV.Length) _uvAdjustVerts.Add(i2);
					}
#endif
				}
				return;
			}

			// End drag
			if (Input.GetMouseButtonUp(0))
			{
				_uvAdjustActive = false;
				_uvAdjustVerts = null;
				return;
			}

			// Drag
			if (!_uvAdjustActive || !Input.GetMouseButton(0) || _uvAdjustVerts == null || _uvAdjustVerts.Count == 0)
			{
				return;
			}

			Vector2 mouse = Input.mousePosition;
			Vector2 mouseDelta = mouse - _uvAdjustLastMouse;
			_uvAdjustLastMouse = mouse;
			if (mouseDelta.sqrMagnitude < 0.01f)
			{
				return;
			}

			// Convert screen delta to overlayUV delta.
			// Horizontal mouse delta maps directly to +U.
			// Vertical movement in screen space is inverted relative to UV space, so subtract Y.
			// Scale factor: use the selected stamp's overlayUV spread as a proxy for UV island scale.
			float uvPerPixel = 1f / Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
			for (int si = 0; si < CurrentStamp.slots.Count; si++)
			{
				var ss = CurrentStamp.slots[si];
				if (ss == null || ss.debugDontUse) continue;
				if (ss.overlayUV == null || ss.overlayUV.Length == 0) continue;

				// Compute a scale from the current overlay UV bounding box.
				// If triangles cover a small overlay region, we want a smaller UV delta per pixel.
				float minU = float.PositiveInfinity, minV = float.PositiveInfinity;
				float maxU = float.NegativeInfinity, maxV = float.NegativeInfinity;
				for (int i = 0; i < ss.overlayUV.Length; i++)
				{
					var uv = ss.overlayUV[i];
					if (uv.x < minU) minU = uv.x;
					if (uv.y < minV) minV = uv.y;
					if (uv.x > maxU) maxU = uv.x;
					if (uv.y > maxV) maxV = uv.y;
				}
				float span = Mathf.Max(1e-5f, Mathf.Max(maxU - minU, maxV - minV));
				float scaledUvPerPixel = uvPerPixel * span;

				Vector2 duv = new Vector2(mouseDelta.x * scaledUvPerPixel, -mouseDelta.y * scaledUvPerPixel);

				foreach (int vi in _uvAdjustVerts)
				{
					if ((uint)vi >= ss.overlayUV.Length) continue;
					ss.overlayUV[vi] += duv;
				}
			}

			#if UNITY_EDITOR
			EditorUtility.SetDirty(CurrentStamp);
			#endif
			RebuildAvatar();
			RefreshLastDecalDebug();
		}

        private void ApplyAnimationPauseState()
        {
            if (Avatar == null)
            {
                return;
            }

            var animators = Avatar.GetComponentsInChildren<Animator>(true);
            if (PauseAvatarAnimation)
            {
                // Cache current speeds and set to 0
                for (int i = 0; i < animators.Length; i++)
                {
                    var a = animators[i];
                    if (a == null)
                    {
                        continue;
                    }

                    if (!_animatorSpeedCache.ContainsKey(a))
                    {
                        // store pre-pause speed (default 1 if zero at creation)
                        float speed = a.speed;
                        if (Mathf.Approximately(speed, 0f))
                        {
                            speed = 1f;
                        }

                        _animatorSpeedCache[a] = speed;
                    }
                    a.speed = 0f;
                }
            }
            else
            {
                // Restore any cached animator speeds
                if (_animatorSpeedCache.Count > 0)
                {
                    foreach (var kv in _animatorSpeedCache)
                    {
                        if (kv.Key != null)
                        {
                            kv.Key.speed = kv.Value;
                        }
                    }
                    _animatorSpeedCache.Clear();
                }
            }
        }

        private void OnRenderObject()
        {
            if (!EnableTriangleDebug)
            {
                return;
            }

            if (_dbgSmr == null || _dbgSmrTriangles == null || _dbgBaked == null)
            {
                return;
            }

            if (_dbgSmrTriangles.Length == 0)
            {
                return;
            }

            EnsureLineMaterial();
            _lineMat.SetPass(0);

            var v = _dbgBaked.vertices;
            var t = _dbgSmrTriangles;
            var tr = _dbgSmr.transform;
            Vector3 camFwd = OrbitCamera != null ? OrbitCamera.transform.forward : Vector3.forward;

            int triCount = t.Length / 3;

            GL.PushMatrix();
			try {
				MatrixLevel++;
				GL.MultMatrix(Matrix4x4.identity);

				// Pass 1: Fill existing decal triangles (green), ones selected to remove are red
				GL.Begin(GL.TRIANGLES);
				for(int i = 0; i < triCount; i++) {
					if(_dbgTriToOrdinal == null || !_dbgTriToOrdinal.ContainsKey(i)) {
						continue; // only decal triangles
					}

					int i0 = t[i * 3 + 0];
					int i1 = t[i * 3 + 1];
					int i2 = t[i * 3 + 2];
					if((uint)i0 >= v.Length || (uint)i1 >= v.Length || (uint)i2 >= v.Length) {
						continue;
					}

					Vector3 w0 = tr.TransformPoint(v[i0]);
					Vector3 w1 = tr.TransformPoint(v[i1]);
					Vector3 w2 = tr.TransformPoint(v[i2]);

					Vector3 n = Vector3.Cross(w1 - w0, w2 - w0);
					if(n.sqrMagnitude < 1e-12f) {
						continue;
					}

					n.Normalize();
					// Only draw those facing camera
					if(Vector3.Dot(n, camFwd) >= 0f) {
						continue;
					}

					int ord = _dbgTriToOrdinal[i];
					bool removing = _selectedOrdinals.Contains(ord);
					Color fillCol = removing ? EditFillRemoveColor : EditFillKeepColor;
					GL.Color(fillCol);
					GL.Vertex(w0);
					GL.Vertex(w1);
					GL.Vertex(w2);
				}
				GL.End();

				// Pass 2: Fill triangles selected to add (currently not in decal) with green
				GL.Begin(GL.TRIANGLES);
				for(int i = 0; i < triCount; i++) {
					if(_dbgTriToOrdinal != null && _dbgTriToOrdinal.ContainsKey(i)) {
						continue; // skip decal triangles here
					}

					if(!_selectedAddCombinedTris.Contains(i)) {
						continue; // only selected additions
					}

					int i0 = t[i * 3 + 0];
					int i1 = t[i * 3 + 1];
					int i2 = t[i * 3 + 2];
					if((uint)i0 >= v.Length || (uint)i1 >= v.Length || (uint)i2 >= v.Length) {
						continue;
					}

					Vector3 w0 = tr.TransformPoint(v[i0]);
					Vector3 w1 = tr.TransformPoint(v[i1]);
					Vector3 w2 = tr.TransformPoint(v[i2]);

					Vector3 n = Vector3.Cross(w1 - w0, w2 - w0);
					if(n.sqrMagnitude < 1e-12f) {
						continue;
					}

					n.Normalize();
					if(Vector3.Dot(n, camFwd) >= 0f) {
						continue; // Only facing camera
					}

					GL.Color(EditFillAddColor);
					GL.Vertex(w0);
					GL.Vertex(w1);
					GL.Vertex(w2);
				}
				GL.End();

				// Pass 3: Outlines for existing decal triangles (green or red)
				GL.Begin(GL.LINES);
				for(int i = 0; i < triCount; i++) {
					if(_dbgTriToOrdinal == null || !_dbgTriToOrdinal.ContainsKey(i)) {
						continue;
					}

					int i0 = t[i * 3 + 0];
					int i1 = t[i * 3 + 1];
					int i2 = t[i * 3 + 2];
					if((uint)i0 >= v.Length || (uint)i1 >= v.Length || (uint)i2 >= v.Length) {
						continue;
					}

					Vector3 w0 = tr.TransformPoint(v[i0]);
					Vector3 w1 = tr.TransformPoint(v[i1]);
					Vector3 w2 = tr.TransformPoint(v[i2]);

					Vector3 n = Vector3.Cross(w1 - w0, w2 - w0);
					if(n.sqrMagnitude < 1e-12f) {
						continue;
					}

					n.Normalize();
					if(Vector3.Dot(n, camFwd) >= 0f) {
						continue; // facing away -> no line
					}

					int ord = _dbgTriToOrdinal[i];
					bool removing = _selectedOrdinals.Contains(ord);
					Color edgeCol = removing ? EditOutlineRemoveColor : EditOutlineKeepColor;
					GL.Color(edgeCol);
					GL.Vertex(w0);
					GL.Vertex(w1);
					GL.Vertex(w1);
					GL.Vertex(w2);
					GL.Vertex(w2);
					GL.Vertex(w0);
				}
				GL.End();

				// Pass 4: Outlines for unused triangles facing camera (cyan). If selected to add, draw green.
				GL.Begin(GL.LINES);
				for(int i = 0; i < triCount; i++) {
					if(_dbgTriToOrdinal != null && _dbgTriToOrdinal.ContainsKey(i)) {
						continue; // only triangles not in decal
					}

					int i0 = t[i * 3 + 0];
					int i1 = t[i * 3 + 1];
					int i2 = t[i * 3 + 2];
					if((uint)i0 >= v.Length || (uint)i1 >= v.Length || (uint)i2 >= v.Length) {
						continue;
					}

					Vector3 w0 = tr.TransformPoint(v[i0]);
					Vector3 w1 = tr.TransformPoint(v[i1]);
					Vector3 w2 = tr.TransformPoint(v[i2]);

					Vector3 n = Vector3.Cross(w1 - w0, w2 - w0);
					if(n.sqrMagnitude < 1e-12f) {
						continue;
					}

					n.Normalize();
					if(Vector3.Dot(n, camFwd) >= 0f) {
						continue; // only show facing camera
					}

					bool addSel = _selectedAddCombinedTris.Contains(i);
					Color edgeCol = addSel ? EditOutlineAddColor : EditOutlineUnusedColor;
					GL.Color(edgeCol);
					GL.Vertex(w0);
					GL.Vertex(w1);
					GL.Vertex(w1);
					GL.Vertex(w2);
					GL.Vertex(w2);
					GL.Vertex(w0);
				}
				GL.End();

			} finally {
				GL.PopMatrix();
				MatrixLevel--;
			}
		}

        private void HandleOrbitInput()
        {
            if (!Input.GetMouseButton(OrbitMouseButton))
            {
                return;
            }

            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Normalize by screen size for consistent feel
            float normX = dx / Mathf.Max(1f, Screen.width);
            float normY = dy / Mathf.Max(1f, Screen.height);

            if (shift)
            {
                // Shift + RMB: vertical pan of the orbit pivot (focus)
                float panY = -normY * PanSensitivityY * Screen.height * Time.deltaTime; // dy * PanSensitivityY * dt
                OrbitOffset.y += panY;
                return;
            }

            _yaw += normX * OrbitSensitivityX * Time.deltaTime * Screen.width;   // scale back to keep same overall sensitivity
            _pitch -= normY * OrbitSensitivityY * Time.deltaTime * Screen.height;

            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
        }

        private void HandleZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                _distance -= scroll * ZoomSensitivity;
                _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);
            }
        }

        private void UpdateCameraTransform()
        {
            if (Avatar == null)
            {
                return;
            }

            _targetPos = Avatar.transform.position + OrbitOffset;

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 camPos = _targetPos + rot * (Vector3.back * _distance);
            OrbitCamera.transform.position = camPos;
            OrbitCamera.transform.rotation = rot;
        }

        private void HandlePlacement()
        {
            if (!Input.GetMouseButtonDown(PlaceMouseButton))
            {
                return;
            }

            // Do not place decals when clicking over UI (uGUI or this window area)
            if (IsPointerOverUI())
            {
                return;
            }

            // Validate required overlays per method
            if (decalMethod == DecalMethod.SlotDecal)
            {
                if (MeshDecalOverlay == null || MeshDecalOverlay.material == null)
                {
                    Debug.LogWarning("MeshDecalOverlay or its UMAMaterial is missing. Cannot place slot decal.");
                    return;
                }
            }
            else // RenderTexture
            {
                if (TextureDecalOverlay == null)
                {
                    Debug.LogWarning("TextureDecalOverlay is missing. Cannot place RT decal.");
                    return;
                }
                //Debug.Log($"Applying RT decal. {TextureDecalOverlay.name}");
            }

            if (Avatar == null || Avatar.umaData == null)
            {
                Debug.LogWarning("Avatar or UMAData not ready.");
                return;
            }

            Ray ray = OrbitCamera.ScreenPointToRay(Input.mousePosition);

            if (randomizeRotation)
            {
                DecalRotationDegrees = Random.Range(0f, 360f);
            }

            if (decalMethod == DecalMethod.SlotDecal) {
				// DecalSlotBuilder.enableDebug = true;
				// Build decal slot
                Debug.Log($"Creating Mesh decal. {MeshDecalOverlay.name}");
                var slotAsset = DecalSlotBuilder.CreateDecalSlot(
					Avatar,
					ray,
					DecalRadius,
					fudgeRadius,
					DecalRotationDegrees,
					MeshDecalOverlay.material,  // Using UMAMaterial from overlay
					MeshDecalOverlay,
					new DecalSlotBuilder.DecalBuildOptions {
						useHitNormalForProjection = this.useHitNormalForProjection,
						backOffset = 0.04f, // Slight offset back to ensure we capture edges
											//multithread = false,              // requirement: allocate per click, no async
											// copyBlendshapes = true,
						facingThreshold = 0.2f,
						enableDebug = true
					});

				if(slotAsset == null) {
					Debug.Log("Decal creation produced no geometry (nothing within radius or facing threshold).");
					return;
				}
				CreateDebugSphere();
				UMAAssetIndexer.Instance.ProcessNewItem(slotAsset, false, false); // Ensure new asset is indexed

				// Wrap into SlotData and add overlay
				SlotData slotData = new SlotData(slotAsset);
				if(MeshDecalOverlay != null) {
					var overlayInstance = new OverlayData(MeshDecalOverlay);
					DecalSlotBuilder.SetLastDecalOverlay(overlayInstance);
					slotData.AddOverlay(overlayInstance);
				}
				slotData.expandAlongNormal = slotOffset; // Slight expansion to avoid z-fighting

				// Add (accumulate) into existing UMA recipe
				Avatar.umaData.umaRecipe.MergeSlot(slotData, true);
				Avatar.ForceUpdate(true, true, true);
			} else
            {
                //Debug.Log($"Creating RenderTexture decal. {TextureDecalOverlay.name}");

                // Example call
                var options = new DecalRenderTexture.DecalRTOptions
                {
                    layerMask = ~0,
                    facingThreshold = 0.15f,
                    enableDebug = true,
                    forceLinearSampling = false,
                    useHitNormalForProjection = this.useHitNormalForProjection,
                    uvExpandPixels = DecalRTUVExpandPixels,
					bleedPixels = decalRTDilation
                };

                if (Avatar.umaData == null)
                {
                    Debug.LogWarning("UMAData not ready on avatar.");
                    return;
                }

				// 

                var result = DecalRenderTexture.CreateDecalLayer(
                    Avatar,
                    ray,
                    radius: DecalRadius,
                    fudgeRadius: fudgeRadius,
                    angleDegrees: DecalRotationDegrees,
                    umaData: Avatar.umaData,
                    overlay: TextureDecalOverlay,
                    options: options
                );

                if (!(result.HasValue && result.Value.success))
                {
                    return;
                }

				// CreateDebugSphere(result.Value.hitPoint, result.Value.hitNormal);

				// Additional behavior for RenderTexture mode: create a DecalRTStampAsset and store in StampField
				if (StampField != null && TextureDecalOverlay != null)
                {
                    var last = DecalRenderTexture.LastStamp;
                    if (last != null) {
						bool hadAnyStampsBefore = false;
						try
						{
							hadAnyStampsBefore = StampField.overlayStamps != null && StampField.overlayStamps.Any(s => s != null && s.stamps != null && s.stamps.Length > 0);
						}
						catch { }

						// Clone the last stamp (runtime instance) so we can add to the stamp slot set
						var clone = ScriptableObject.CreateInstance<DecalRTStampAsset>();
					clone.overlayGroup = last.overlayGroup;
						clone.bleedPixels = last.bleedPixels;
						clone.forceLinearSampling = last.forceLinearSampling;
                       clone.invertY = last.invertY;
						CurrentStamp = clone;
						clone.slots = new List<DecalRTStampAsset.SlotStamp>(last.slots.Count);
						for(int i = 0; i < last.slots.Count; i++) {
							var s = last.slots[i];
							if(s == null) {
								continue;
							}

							var ns = new DecalRTStampAsset.SlotStamp {
								slotName = s.slotName,
								slotGroup = s.slotGroup,
								slotHash = UMAUtils.StringToHash(s.slotName),
								umaMaterialName = s.umaMaterialName,
								normBaseUV = (s.normBaseUV != null) ? (Vector2[])s.normBaseUV.Clone() : new Vector2[0],
								overlayUV = (s.overlayUV != null) ? (Vector2[])s.overlayUV.Clone() : new Vector2[0],
								triangles = (s.triangles != null) ? (int[])s.triangles.Clone() : new int[0],
#if UNITY_EDITOR
								triOrdinals = s.triOrdinals != null ? (int[])s.triOrdinals.Clone() : null,
								slotRelativeTriangles = s.slotRelativeTriangles != null ? (int[])s.slotRelativeTriangles.Clone() : null,
#endif
                          recordedUVArea = s.recordedUVArea,
							debugDontUse = s.debugDontUse
							};
							clone.slots.Add(ns);
						}

#if UNITY_EDITOR
                        // Persist as an asset so Inspector does not show 'Type Mismatch' for transient SOs
                        try
                        {
                          clone.name = $"Stamp_{(string.IsNullOrEmpty(clone.overlayGroup) ? "OverlayGroup" : clone.overlayGroup)}";
                            var folder = "Assets/UMA/GeneratedDecalStamps";
                            if (!System.IO.Directory.Exists(folder))
                            {
                                System.IO.Directory.CreateDirectory(folder);
                            }
                            var safeName = clone.name.Replace('/', '_').Replace('\\', '_');
                            var path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{folder}/{safeName}.asset");
                            UnityEditor.AssetDatabase.CreateAsset(clone, path);
                            EditorUtility.SetDirty(clone);
                            //UnityEditor.AssetDatabase.SaveAssetIfDirty(clone);
                        }
                        catch { /* ignore editor persistence errors */ }
#endif
						// CreateDebugSphere();

                     StampField.AddStampToSet(clone);

						// Ensure the slot is subscribed to atlas updates (use its public entrypoint)
						if(Avatar.umaData != null) {
							StampField.OnCharacterBegun(Avatar.umaData);
						}
						//StampField.NotifyStampsChanged();
#if UNITY_EDITOR
						EditorUtility.SetDirty(StampField);
						//AssetDatabase.SaveAssetIfDirty(StampField);
#endif


						// Trigger textures-only generation so atlas changes (if any) propagate and slot can re-stamp via OnAtlasUpdated
						RebuildAvatar();

						// If this was the first-ever stamp, there may be no later OnAtlasUpdated event to cause stamping.
						// Do a best-effort immediate apply against the currently generated atlas RTs (safe no-op if not ready).
						if (!hadAnyStampsBefore && !DrawRenderTexturesImmediately)
						{
							// TryApplyStampToCurrentAtlases(clone);
						}
					}
				}
            }

            // If debug is enabled, refresh selection mapping and open selection UI automatically
            if (EnableTriangleDebug)
            {
                RefreshLastDecalDebug();
            }
		}

		private void TryApplyStampToCurrentAtlases(DecalRTStampAsset stamp)
		{
			if (stamp == null)
			{
				return;
			}
			if (Avatar == null || Avatar.umaData == null)
			{
				return;
			}

			// UMA's texture pipeline invokes stamping via OnAtlasUpdated.
			// When adding the first stamp to an empty set, that event may not be triggered again soon,
			// so we force a textures update as a one-time bootstrap.
			try
			{
				Avatar.ForceUpdate(false, true, false, true);
			}
			catch
			{
				// Best-effort only
			}
		}

		private void RebuildAvatar() {
			try 
			{
				//Debug.Log("Rebuilding avatar per decal edit.");
                if (UMAAssetIndexer.Instance != null && UMAAssetIndexer.Instance.generator != null && !DrawRenderTexturesImmediately) {
                   // Debug.Log("Rebuilding method: " + RebuildMethod);

                    switch (RebuildMethod) {
					case rebuildMethod.ForceTextures:

                            Avatar.ForceUpdate(false, true, false, true);
						break;
					case rebuildMethod.ForceAll:
						Avatar.ForceUpdate(true, true, true, true);
						break;
					case rebuildMethod.ForceTexturesAndMesh:
						Avatar.ForceUpdate(false, true, true, true);
						break;
					case rebuildMethod.ForceTexturesAndDNA:
						Avatar.ForceUpdate(true, true, false, true);
						break;
					case rebuildMethod.FullRebuild:
						Avatar.BuildCharacter(true);
						break;
					}
					// Avatar.ForceUpdate(false, true, false);
				}
			} catch(System.Exception ex) {
				Debug.LogException(ex);
			}
		}

		private void ShowSpheres(bool show) {

			GameObject[] debugSpheres = GameObject.FindGameObjectsWithTag("debugSphere");
			foreach(var go in debugSpheres) {
				go.SetActive(show);
			}
		}
#if UNITY_EDITOR
        public void EnsureTag(string tagName) {
			if(!UnityEditorInternal.InternalEditorUtility.tags.Contains(tagName)) {
				var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
				var tagsProp = tagManager.FindProperty("tags");
				bool found = false;
				for(int i = 0; i < tagsProp.arraySize; i++) {
					SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
					if(t.stringValue == tagName) { found = true; break; }
				}
				if(!found) {
					tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
					SerializedProperty newTag = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
					newTag.stringValue = tagName;
					tagManager.ApplyModifiedPropertiesWithoutUndo();
					//Debug.Log($"Added tag: { tagName } - Saving...");
					AssetDatabase.SaveAssets();
				}
			}
		}
#endif


        private void CreateDebugSphere(Vector3 position, Vector3 direction) {
			if(debugSpherePrefab == null)
				return;

			GameObject go = GameObject.Instantiate(debugSpherePrefab);
			go.transform.position = position;
			go.transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 1e-12f ? direction : Vector3.forward);
			go.transform.localScale = 0.03f * Vector3.one;
			// if "debugSphere" tag does not exist, then add it to the tags.
			go.name = "debugSphere";
			go.SetActive(debugShowSpheres);

			GameObject.DontDestroyOnLoad(go);
		}

private void CreateDebugSphere()
{
	// Legacy slot-based placement (DecalSlotBuilder sets these)
	CreateDebugSphere(DecalSlotBuilder._lastHitPointWorld, DecalSlotBuilder._lastProjectionDirWorld);
}

		private void ClearCurrent() {
			_selectedOrdinals.Clear();
			_selectedAddCombinedTris.Clear();
			_undo.Clear();
			_redo.Clear();
			_dbgSmr = null;
			_dbgSmrTriangles = null;
			_dbgTriToOrdinal = null;
		}

		private void RefreshLastDecalDebug()
        {
            _selectedOrdinals.Clear();
            _selectedAddCombinedTris.Clear();
            _undo.Clear();
            _redo.Clear();
            _dbgSmr = null;
            _dbgSmrTriangles = null;
            _dbgTriToOrdinal = null;

            if (decalMethod == DecalMethod.SlotDecal)
            {
                if (DecalSlotBuilder.TryGetLastDebug(out var sSmr, out var sTris, out var sMap, out var sSeq))
                {
                    _dbgSmr = sSmr; _dbgSmrTriangles = sTris; _dbgTriToOrdinal = sMap; _dbgSequence = sSeq;
                    return;
                }
            }
            else
            {
				if (EnableTriangleDebug && CurrentStamp != null && Avatar != null && Avatar.umaData != null)
				{
					if (TryBuildRTDebugFromStamp(CurrentStamp, Avatar.umaData, out _dbgSmr, out _dbgSmrTriangles, out _dbgTriToOrdinal))
					{
						_dbgSequence++;
						return;
					}
				}
				if (DecalRenderTexture.TryGetLastDebug(out var rSmr, out var rTris, out var rMap, out var rSeq))
				{
					_dbgSmr = rSmr; _dbgSmrTriangles = rTris; _dbgTriToOrdinal = rMap; _dbgSequence = rSeq;
				}
            }
        }

		private bool TryBuildRTDebugFromStamp(DecalRTStampAsset stamp, UMAData umaData, out SkinnedMeshRenderer smr, out int[] combinedTriangles, out Dictionary<int, int> triToOrdinal)
		{
			smr = null;
			combinedTriangles = null;
			triToOrdinal = null;

			if (stamp == null || stamp.slots == null || stamp.slots.Count == 0 || umaData == null)
			{
				return false;
			}

			// Pick the first usable slot stamp (only for choosing an SMR to bake/visualize).
			DecalRTStampAsset.SlotStamp ssChosen = null;
			for (int i = 0; i < stamp.slots.Count; i++)
			{
				var ss = stamp.slots[i];
				if (ss == null || ss.debugDontUse) continue;
				if (ss.triangles == null || ss.triangles.Length == 0) continue;
				ssChosen = ss;
				break;
			}
			if (ssChosen == null)
			{
				return false;
			}

			// Determine which renderer to visualize by using the SlotData's assigned SkinnedMeshRenderer index.
           SlotData chosenSlot = null;
			if (umaData.umaRecipe != null)
			{
				if (!string.IsNullOrEmpty(ssChosen.slotName))
				{
					chosenSlot = umaData.umaRecipe.GetSlot(ssChosen.slotName);
				}
				if (chosenSlot == null && !string.IsNullOrEmpty(ssChosen.slotGroup))
				{
					chosenSlot = umaData.umaRecipe.GetSlotBySlotGroup(ssChosen.slotGroup);
				}
			}
			if (chosenSlot == null)
			{
				return false;
			}
			var renderers = umaData.GetRenderers();
			if (renderers == null || renderers.Length == 0)
			{
				return false;
			}

			int rendererIndex = (chosenSlot.skinnedMeshRenderer >= 0 && chosenSlot.skinnedMeshRenderer < renderers.Length)
				? chosenSlot.skinnedMeshRenderer
				: 0;
			smr = renderers[rendererIndex];
			if (smr == null)
			{
				return false;
			}

			// Use a baked mesh to get the triangle index buffer that matches the baked vertices used for hit-testing.
			var baked = new Mesh();
			try
			{
				smr.BakeMesh(baked);
				combinedTriangles = baked.triangles;
				if (combinedTriangles == null || combinedTriangles.Length == 0)
				{
					return false;
				}
			}
			finally
			{
				UMAUtils.DestroySceneObject(baked);
			}

			triToOrdinal = new Dictionary<int, int>();
			int combinedTriCount = combinedTriangles.Length / 3;
			for (int si = 0; si < stamp.slots.Count; si++)
			{
				var ss = stamp.slots[si];
				if (ss == null || ss.debugDontUse) continue;
				if (string.IsNullOrEmpty(ss.slotName)) continue;
#if UNITY_EDITOR
				if (ss.slotRelativeTriangles == null || ss.slotRelativeTriangles.Length == 0) continue;
				if (ss.triOrdinals == null || ss.triOrdinals.Length != (ss.slotRelativeTriangles.Length / 3)) continue;
#else
				continue;
#endif

				var sd = umaData.umaRecipe != null ? umaData.umaRecipe.GetSlot(ss.slotName) : null;
				if (sd == null) continue;
				if (sd.skinnedMeshRenderer != rendererIndex) continue;

#if UNITY_EDITOR
				// slotRelativeTriangles are vertex indices relative to the slot's mesh.
				// Convert them back to combined/baked mesh vertex indices by adding vertexOffset.
				int baseVert = sd.vertexOffset;
				var rel = ss.slotRelativeTriangles;
				var ords = ss.triOrdinals;
				int relTriCount = rel.Length / 3;
				for (int t = 0; t < relTriCount; t++)
				{
					int a = rel[t * 3 + 0] + baseVert;
					int b = rel[t * 3 + 1] + baseVert;
					int c = rel[t * 3 + 2] + baseVert;
					int ord = ords[t];

					for (int ci = 0; ci < combinedTriCount; ci++)
					{
						int i0 = combinedTriangles[ci * 3 + 0];
						int i1 = combinedTriangles[ci * 3 + 1];
						int i2 = combinedTriangles[ci * 3 + 2];
						if ((i0 == a && i1 == b && i2 == c) || (i0 == a && i1 == c && i2 == b) ||
							(i0 == b && i1 == a && i2 == c) || (i0 == b && i1 == c && i2 == a) ||
							(i0 == c && i1 == a && i2 == b) || (i0 == c && i1 == b && i2 == a))
						{
							if (!triToOrdinal.ContainsKey(ci)) triToOrdinal.Add(ci, ord);
							break;
						}
					}
				}
#endif
			}

			return triToOrdinal.Count > 0;
		}

        private void EnsureDebugBake()
        {
            if (_dbgSmr == null)
            {
                return;
            }

            if (_dbgBaked == null)
            {
                _dbgBaked = new Mesh();
            }

            _dbgSmr.BakeMesh(_dbgBaked);
        }

        private void HandleTriangleToggleClick()
        {
            int bestTri;
            bool inDecal;
            if (!FindBestTriangleUnderMouse(out bestTri, out inDecal))
            {
                return;
            }

            PushUndo();
            if (inDecal)
            {
                int ord = _dbgTriToOrdinal[bestTri];
                if (_selectedOrdinals.Contains(ord))
                {
                    _selectedOrdinals.Remove(ord);
                }
                else
                {
                    _selectedOrdinals.Add(ord);
                }
            }
            else
            {
                if (_selectedAddCombinedTris.Contains(bestTri))
                {
                    _selectedAddCombinedTris.Remove(bestTri);
                }
                else
                {
                    _selectedAddCombinedTris.Add(bestTri);
                }
            }
        }

        private bool FindBestTriangleUnderMouse(out int bestTri, out bool inDecal)
        {
            bestTri = -1; inDecal = false;
            if (_dbgSmr == null || _dbgBaked == null || _dbgSmrTriangles == null)
            {
                return false;
            }

            if (_dbgSmrTriangles.Length == 0)
            {
                return false;
            }

            Ray ray = OrbitCamera.ScreenPointToRay(Input.mousePosition);
            var v = _dbgBaked.vertices;
            var t = _dbgSmrTriangles;
            var tr = _dbgSmr.transform;
            Vector3 camFwd = OrbitCamera != null ? OrbitCamera.transform.forward : Vector3.forward;
            float bestDist = float.MaxValue;
            int triCount = t.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                int i0 = t[i * 3 + 0];
                int i1 = t[i * 3 + 1];
                int i2 = t[i * 3 + 2];
                if ((uint)i0 >= v.Length || (uint)i1 >= v.Length || (uint)i2 >= v.Length)
                {
                    continue;
                }

                Vector3 w0 = tr.TransformPoint(v[i0]);
                Vector3 w1 = tr.TransformPoint(v[i1]);
                Vector3 w2 = tr.TransformPoint(v[i2]);
                Vector3 n = Vector3.Cross(w1 - w0, w2 - w0);
                if (n.sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                n.Normalize();
                if (Vector3.Dot(n, camFwd) >= 0f)
                {
                    continue; // ignore back facing
                }

                if (RayTriangle(ray.origin, ray.direction, w0, w1, w2, out float dist))
                {
                    if (dist > 0f && dist < bestDist)
                    {
                        bestDist = dist;
                        bestTri = i;
                    }
                }
            }
            if (bestTri >= 0)
            {
                inDecal = (_dbgTriToOrdinal != null && _dbgTriToOrdinal.ContainsKey(bestTri));
                return true;
            }
            return false;
        }

        private void HandlePaintMode()
        {
            if (_dbgSmr == null || _dbgBaked == null || _dbgSmrTriangles == null)
            {
                return;
            }

            if (_dbgSmrTriangles.Length == 0)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0) && !_paintActive)
            {
                if (FindBestTriangleUnderMouse(out int startTri, out bool inDecal))
                {
                    PushUndo();
                    _paintActive = true;
                    _paintVisited.Clear();
                    _paintForRemoval = inDecal; // paint removal if starting on in-decal tri, else paint addition

                    // Determine initial triangle's current selection state and flip it for the paint target
                    bool currentSelected;
                    if (_paintForRemoval)
                    {
                        int ord = _dbgTriToOrdinal[startTri];
                        currentSelected = _selectedOrdinals.Contains(ord);
                    }
                    else
                    {
                        currentSelected = _selectedAddCombinedTris.Contains(startTri);
                    }
                    _paintTargetSelected = !currentSelected; // flip state for initial press, and use that for the rest

                    // Apply to initial tri
                    ApplyPaintToTriangle(startTri);
                }
            }
            else if (Input.GetMouseButton(0) && _paintActive)
            {
                if (FindBestTriangleUnderMouse(out int tri, out bool _))
                {
                    ApplyPaintToTriangle(tri);
                }
            }
            else if (Input.GetMouseButtonUp(0) && _paintActive)
            {
                _paintActive = false;
                _paintVisited.Clear();
            }
        }

        private void ApplyPaintToTriangle(int tri)
        {
            if (_paintVisited.Contains(tri))
            {
                return;
            }

            _paintVisited.Add(tri);

            bool inDecal = _dbgTriToOrdinal != null && _dbgTriToOrdinal.ContainsKey(tri);
            if (_paintForRemoval)
            {
                // Only apply to in-decal triangles
                if (!inDecal)
                {
                    return;
                }

                int ord = _dbgTriToOrdinal[tri];
                if (_paintTargetSelected)
                {
                    _selectedOrdinals.Add(ord);
                }
                else
                {
                    _selectedOrdinals.Remove(ord);
                }
            }
            else
            {
                // Only apply to non-decal triangles
                if (inDecal)
                {
                    return;
                }

                if (_paintTargetSelected)
                {
                    _selectedAddCombinedTris.Add(tri);
                }
                else
                {
                    _selectedAddCombinedTris.Remove(tri);
                }
            }
        }

        private static bool RayTriangle(Vector3 ro, Vector3 rd, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
        {
            distance = 0f;
            const float EPS = 1e-7f;
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 p = Vector3.Cross(rd, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -EPS && det < EPS)
            {
                return false;
            }

            float invDet = 1f / det;
            Vector3 tvec = ro - v0;
            float u = Vector3.Dot(tvec, p) * invDet;
            if (u < 0 || u > 1)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(tvec, e1);
            float v = Vector3.Dot(rd, q) * invDet;
            if (v < 0 || (u + v) > 1)
            {
                return false;
            }

            float t = Vector3.Dot(e2, q) * invDet;
            if (t < 0)
            {
                return false;
            }

            distance = t;
            return true;
        }

        private void ApplySelectedChanges()
        {
            bool changed = false;
            if (decalMethod == DecalMethod.SlotDecal)
            {
                // Apply add+remove via DecalSlotBuilder
                changed = DecalSlotBuilder.ApplyAddRemoveToLastDecal(
                    Avatar,
                    _selectedAddCombinedTris.Count > 0 ? new HashSet<int>(_selectedAddCombinedTris) : null,
                    _selectedOrdinals.Count > 0 ? new HashSet<int>(_selectedOrdinals) : null,
                    enableDebug: true);
            }
            else
            {
				// RT mode: remove triangles from the CURRENT stamp (by ordinal), then restamp.
				if (_selectedOrdinals.Count > 0 && CurrentStamp != null)
				{
					changed = RemoveTrianglesFromStamp(CurrentStamp, _selectedOrdinals);
				}
            }

            if (changed)
            {
                // Important: For RT stamping, the stamp is drawn directly into the atlases.
                // Rebuilding textures would wipe that work. Only force a rebuild for SlotDecal.
                if (decalMethod == DecalMethod.SlotDecal)
                {
                    Avatar.ForceUpdate(true, true, true);
                }
				else
				{
					// Restamp pipeline: rebuilding textures will cause DecalRTStampSlot to re-apply stamps.
					if (Avatar != null)
					{
						Avatar.ForceUpdate(false, true, false, true);
					}
				}

                _selectedOrdinals.Clear();
                _selectedAddCombinedTris.Clear();
                RefreshLastDecalDebug();
            }
        }

		private static bool RemoveTrianglesFromStamp(DecalRTStampAsset stamp, HashSet<int> ordinalsToRemove)
		{
			if (stamp == null || ordinalsToRemove == null || ordinalsToRemove.Count == 0)
			{
				return false;
			}
			if (stamp.slots == null || stamp.slots.Count == 0)
			{
				return false;
			}

			bool changed = false;
			for (int si = stamp.slots.Count - 1; si >= 0; si--)
			{
				var s = stamp.slots[si];
				if (s == null || s.triangles == null || s.triangles.Length == 0)
				{
					continue;
				}
				int triCount = s.triangles.Length / 3;
#if UNITY_EDITOR
				var triOrd = s.triOrdinals;
#else
				int[] triOrd = null;
#endif
				if (triOrd == null || triOrd.Length != triCount)
				{
					continue;
				}
				var newTri = new List<int>(s.triangles.Length);
				var newOrd = new List<int>(triOrd.Length);
#if UNITY_EDITOR
				var newSlotRel = (s.slotRelativeTriangles != null && s.slotRelativeTriangles.Length == s.triangles.Length)
					? new List<int>(s.slotRelativeTriangles.Length)
					: null;
#endif
				for (int t = 0; t < triCount; t++)
				{
					int ord = triOrd[t];
					if (ordinalsToRemove.Contains(ord))
					{
						changed = true;
						continue;
					}
					newTri.Add(s.triangles[t * 3 + 0]);
					newTri.Add(s.triangles[t * 3 + 1]);
					newTri.Add(s.triangles[t * 3 + 2]);
					newOrd.Add(ord);
#if UNITY_EDITOR
					if (newSlotRel != null)
					{
						newSlotRel.Add(s.slotRelativeTriangles[t * 3 + 0]);
						newSlotRel.Add(s.slotRelativeTriangles[t * 3 + 1]);
						newSlotRel.Add(s.slotRelativeTriangles[t * 3 + 2]);
					}
#endif
				}
				if (changed)
				{
					s.triangles = newTri.ToArray();
#if UNITY_EDITOR
					s.triOrdinals = newOrd.ToArray();
					if (newSlotRel != null) s.slotRelativeTriangles = newSlotRel.ToArray();
#endif
				}
				if (s.triangles == null || s.triangles.Length == 0)
				{
					stamp.slots.RemoveAt(si);
				}
			}
			return changed;
		}

        private void SelectAll()
        {
            if (_dbgTriToOrdinal == null)
            {
                return;
            }

            _selectedOrdinals.Clear();
            foreach (var kv in _dbgTriToOrdinal)
            {
	_selectedOrdinals.Add(kv.Value);
            }
        }

        private void InvertSelection()
        {
            if (_dbgTriToOrdinal == null)
            {
                return;
            }

            var all = new HashSet<int>(_dbgTriToOrdinal.Values);
            var toKeep = new HashSet<int>();
            foreach (var ord in all)
            {
                if (!_selectedOrdinals.Contains(ord))
                {
                    toKeep.Add(ord);
                }
            }
            _selectedOrdinals.Clear();
            foreach (var ord in toKeep)
            {
                _selectedOrdinals.Add(ord);
            }
       }

        private void PushUndo()
        {
            _undo.Push(new HashSet<int>(_selectedOrdinals));
            _redo.Clear();
        }

        private void PopUndo()
        {
            if (_undo.Count == 0)
            {
                return;
            }

            var state = _undo.Pop();
            _redo.Push(new HashSet<int>(_selectedOrdinals));
            _selectedOrdinals.Clear();
            foreach (var s in state)
            {
                _selectedOrdinals.Add(s);
            }
        }

        private void PopRedo()
        {
            if (_redo.Count == 0)
            {
                return;
            }

            var state = _redo.Pop();
            _undo.Push(new HashSet<int>(_selectedOrdinals));
            _selectedOrdinals.Clear();
            foreach (var s in state)
            {
                _selectedOrdinals.Add(s);
            }
        }

        // Returns true if the current pointer is over any UI element (uGUI) or inside this script's IMGUI panel.
        private bool IsPointerOverUI()
        {
            // Check uGUI (EventSystem)
            if (EventSystem.current != null)
            {
                // Mouse
                if (EventSystem.current.IsPointerOverGameObject())
                {
                    return true;
                }

#if(UNITY_IOS || UNITY_ANDROID)
            // Touches
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    if (EventSystem.current.IsPointerOverGameObject(Input.touches[i].fingerId))
                        return true;
                }
            }
#endif
            }

            // Check IMGUI area used by this component
            Vector2 mp = Input.mousePosition;
            Vector2 guiPos = new Vector2(mp.x, Screen.height - mp.y); // convert to IMGUI coords (top-left origin)
            if (ScreenArea.Contains(guiPos))
            {
                return true;
            }

            // If any IMGUI control currently has the mouse captured
            if (GUIUtility.hotControl != 0)
            {
                return true;
            }

            return false;
        }

        // Method to update last decal when settings change
        private void UpdateLastDecalIfExists()
        {
            if (!EnableTriangleDebug)
                return;
                
            // For now, we'll just refresh the debug data
            // In the future, this could be extended to actually modify the last decal
            RefreshLastDecalDebug();
        }
    }
}