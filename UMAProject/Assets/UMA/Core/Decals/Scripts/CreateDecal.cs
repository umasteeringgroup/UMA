using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEditor;
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

        public Color TattooColor;

        [Header("Decal Overlay Handling")]
        [Tooltip("If true, automatically add affected overlays to a rt decal slot when using RenderTexture decals.")]
        public bool AutoAddOverlays = true; // If true, automatically add the overlay used for decal creation to the decal slot
        [Tooltip("If true, call Draw on the decal RTs immediately after stamping (otherwise they are drawn during UMAData.Update")]
        public bool DrawRenderTexturesImmediately = true; // If true, call Draw on the decal RTs immediately after stamping (otherwise they are drawn during UMAData.Update)

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

        // Undo/redo stacks
        private readonly Stack<HashSet<int>> _undo = new Stack<HashSet<int>>();
        private readonly Stack<HashSet<int>> _redo = new Stack<HashSet<int>>();

        // Animator speed cache for pause/resume
        private readonly Dictionary<Animator, float> _animatorSpeedCache = new Dictionary<Animator, float>();

        // GL line material
        private static Material _lineMat;

        // UI state for improved interface
        private Vector2 _scrollPosition;
        private bool _showDebugSettings = false;

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
            InitializeOrbit();
            if (StampField != null && Avatar != null)
            {
                StampField.OnCharacterBegun(Avatar.umaData);
            }
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

            _initialized = true;
            UpdateCameraTransform();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(ScreenArea, GUI.skin.window);
            
            // Use scroll view for expandable content
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
            
            // Decal Method Toggle
            GUILayout.BeginHorizontal();
            GUILayout.Label("Decal Method:", GUILayout.Width(100));
            if (GUILayout.Button($"{decalMethod}", GUILayout.Width(150)))
            {
                decalMethod = decalMethod == DecalMethod.SlotDecal ? DecalMethod.RenderTexture : DecalMethod.SlotDecal;
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            
            // Active Overlay Selection
            GUILayout.Label("Active Overlay:");
            if (decalMethod == DecalMethod.SlotDecal)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Mesh Decal:", GUILayout.Width(100));
#if UNITY_EDITOR
                MeshDecalOverlay = (OverlayDataAsset)EditorGUI.ObjectField(GUILayoutUtility.GetRect(200, 18), MeshDecalOverlay, typeof(OverlayDataAsset), false);
#else
                GUILayout.Label(MeshDecalOverlay != null ? MeshDecalOverlay.name : "None", GUILayout.Width(200));
#endif
                GUILayout.EndHorizontal();
            }
            else
            {
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
            if (System.Math.Abs(newRadius - DecalRadius) > 0.001f)
            {
                DecalRadius = newRadius;
                UpdateLastDecalIfExists();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Fudge Radius:", GUILayout.Width(100));
            string fudgeStr = GUILayout.TextField(fudgeRadius.ToString("F4"), GUILayout.Width(80));
            if (float.TryParse(fudgeStr, out float newFudge))
            {
                fudgeRadius = newFudge;
            }
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Rotation: {DecalRotationDegrees:F1}°", GUILayout.Width(100));
            float newRotation = GUILayout.HorizontalSlider(DecalRotationDegrees, 0f, 360f, GUILayout.Width(150));
            if (System.Math.Abs(newRotation - DecalRotationDegrees) > 0.1f)
            {
                DecalRotationDegrees = newRotation;
                UpdateLastDecalIfExists();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            randomizeRotation = GUILayout.Toggle(randomizeRotation, "Randomize Rotation", GUILayout.Width(150));
            useHitNormalForProjection = GUILayout.Toggle(useHitNormalForProjection, "Use Hit Normal", GUILayout.Width(150));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Clear All Decals"))
            {
                ClearAllDecals();
            }

            // Method-specific settings
            if (decalMethod == DecalMethod.SlotDecal)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Slot Offset:", GUILayout.Width(100));
                string offsetStr = GUILayout.TextField(slotOffset.ToString(), GUILayout.Width(80));
                if (int.TryParse(offsetStr, out int newOffset))
                {
                    slotOffset = newOffset;
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("RT Dilation:", GUILayout.Width(100));
                string dilationStr = GUILayout.TextField(decalRTDilation.ToString(), GUILayout.Width(80));
                if (int.TryParse(dilationStr, out int newDilation))
                {
                    decalRTDilation = newDilation;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("RT UV Expand (px):", GUILayout.Width(120));
                string expandStr = GUILayout.TextField(DecalRTUVExpandPixels.ToString("F2"), GUILayout.Width(80));
                if (float.TryParse(expandStr, out float newExpand))
                {
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
            if (GUILayout.Button("Restart Avatar", GUILayout.Width(120)))
            {
                Avatar.BuildCharacter();
            }
            PauseAvatarAnimation = GUILayout.Toggle(PauseAvatarAnimation, "Pause Animation", GUILayout.Width(140));
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5);


            // Debug Settings (Collapsible)
#if UNITY_EDITOR
            TattooColor = EditorGUILayout.ColorField("Tattoo Color", TattooColor);
            if (GUILayout.Button("Update Tattoo Color"))
            {
                OverlayColorData ocd = new OverlayColorData(1);
                ocd.SetColor(0, false, TattooColor);
                Avatar.SetRawColor("Tattoo", ocd);
            }
            _showDebugSettings = EditorGUI.Foldout(GUILayoutUtility.GetRect(300, 18), _showDebugSettings, "Debug Settings", true);
#else
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_showDebugSettings ? "▼ Debug Settings" : "▶ Debug Settings", GUILayout.Width(150)))
            {
                _showDebugSettings = !_showDebugSettings;
            }
            GUILayout.EndHorizontal();
#endif
            if (_showDebugSettings)
            {
                // Toggle with auto-pause when enabling debug
                bool prevDebug = EnableTriangleDebug;
                EnableTriangleDebug = GUILayout.Toggle(EnableTriangleDebug, "Enable Triangle Debug");
                if (!prevDebug && EnableTriangleDebug)
                {
                    PauseAvatarAnimation = true;
                    ApplyAnimationPauseState();
                }
                
                if (EnableTriangleDebug)
                {
                    if (_dbgTriToOrdinal == null)
                    {
                        RefreshLastDecalDebug();
                    }
                    
                    GUILayout.Label("Debug Selection (Shift + Left Click to paint)");
                    
                    GUILayout.BeginHorizontal();
                    GUI.enabled = _dbgTriToOrdinal != null;
                    if (GUILayout.Button("Apply Changes", GUILayout.Width(110)))
                    {
                        ApplySelectedChanges();
                    }
                    if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                    {
                        _selectedOrdinals.Clear();
                        _selectedAddCombinedTris.Clear();
                        _undo.Clear();
                        _redo.Clear();
                    }
                    GUILayout.EndHorizontal();
                    
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Clear", GUILayout.Width(70)))
                    {
                        PushUndo();
                        _selectedOrdinals.Clear();
                        _selectedAddCombinedTris.Clear();
                    }
                    if (GUILayout.Button("Select All", GUILayout.Width(90)))
                    {
                        PushUndo();
                        SelectAll();
                    }
                    if (GUILayout.Button("Invert", GUILayout.Width(70)))
                    {
                        PushUndo();
                        InvertSelection();
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    
                    GUILayout.BeginHorizontal();
                    GUI.enabled = _undo.Count > 0;
                    if (GUILayout.Button("Undo", GUILayout.Width(70)))
                    {
                        PopUndo();
                    }
                    GUI.enabled = _redo.Count > 0;
                    if (GUILayout.Button("Redo", GUILayout.Width(70)))
                    {
                        PopRedo();
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    
                    GUILayout.Label($"Remove (red): {_selectedOrdinals.Count}  Add (green): {_selectedAddCombinedTris.Count}");
                }
            }
            
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Apply pause state from the toggle each GUI frame
            ApplyAnimationPauseState();
        }

        private void ClearAllDecals()
        {
            if (StampField != null)
            {
                StampField.ClearAllStamps();
                Avatar.BuildCharacter();
            }
        }

        void Update()
        {
            if (!_initialized)
            {
                InitializeOrbit();
            }

            if (Avatar == null || OrbitCamera == null)
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
                if (shift)
                {
                    HandlePaintMode();
                }
                else if (Input.GetMouseButtonDown(0))
                {
                    HandleTriangleToggleClick();
                }
            }
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
            GL.MultMatrix(Matrix4x4.identity);

            // Pass 1: Fill existing decal triangles (green), ones selected to remove are red
            GL.Begin(GL.TRIANGLES);
            for (int i = 0; i < triCount; i++)
            {
                if (_dbgTriToOrdinal == null || !_dbgTriToOrdinal.ContainsKey(i))
                {
                    continue; // only decal triangles
                }

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
                // Only draw those facing camera
                if (Vector3.Dot(n, camFwd) >= 0f)
                {
                    continue;
                }

                int ord = _dbgTriToOrdinal[i];
                bool removing = _selectedOrdinals.Contains(ord);
                Color fillCol = removing ? new Color(1f, 0f, 0f, 0.5f) : new Color(0f, 1f, 0f, 0.5f);
                GL.Color(fillCol);
                GL.Vertex(w0); GL.Vertex(w1); GL.Vertex(w2);
            }
            GL.End();

            // Pass 2: Fill triangles selected to add (currently not in decal) with green
            GL.Begin(GL.TRIANGLES);
            for (int i = 0; i < triCount; i++)
            {
                if (_dbgTriToOrdinal != null && _dbgTriToOrdinal.ContainsKey(i))
                {
                    continue; // skip decal triangles here
                }

                if (!_selectedAddCombinedTris.Contains(i))
                {
                    continue; // only selected additions
                }

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
                    continue; // Only facing camera
                }

                GL.Color(new Color(0f, 1f, 0f, 0.5f));
                GL.Vertex(w0); GL.Vertex(w1); GL.Vertex(w2);
            }
            GL.End();

            // Pass 3: Outlines for existing decal triangles (green or red)
            GL.Begin(GL.LINES);
            for (int i = 0; i < triCount; i++)
            {
                if (_dbgTriToOrdinal == null || !_dbgTriToOrdinal.ContainsKey(i))
                {
                    continue;
                }

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
                    continue; // facing away -> no line
                }

                int ord = _dbgTriToOrdinal[i];
                bool removing = _selectedOrdinals.Contains(ord);
                Color edgeCol = removing ? new Color(1f, 0f, 0f, 1f) : new Color(0f, 1f, 0f, 1f);
                GL.Color(edgeCol);
                GL.Vertex(w0); GL.Vertex(w1);
                GL.Vertex(w1); GL.Vertex(w2);
                GL.Vertex(w2); GL.Vertex(w0);
            }
            GL.End();

            // Pass 4: Outlines for unused triangles facing camera (cyan). If selected to add, draw green.
            GL.Begin(GL.LINES);
            for (int i = 0; i < triCount; i++)
            {
                if (_dbgTriToOrdinal != null && _dbgTriToOrdinal.ContainsKey(i))
                {
                    continue; // only triangles not in decal
                }

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
                    continue; // only show facing camera
                }

                bool addSel = _selectedAddCombinedTris.Contains(i);
                Color edgeCol = addSel ? new Color(0f, 1f, 0f, 1f) : new Color(0f, 1f, 1f, 0.9f);
                GL.Color(edgeCol);
                GL.Vertex(w0); GL.Vertex(w1);
                GL.Vertex(w1); GL.Vertex(w2);
                GL.Vertex(w2); GL.Vertex(w0);
            }
            GL.End();

            GL.PopMatrix();
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

            if (decalMethod == DecalMethod.SlotDecal)
            {
                // DecalSlotBuilder.enableDebug = true;
                // Build decal slot
                var slotAsset = DecalSlotBuilder.CreateDecalSlot(
                    Avatar,
                    ray,
                    DecalRadius,
                    fudgeRadius,
                    DecalRotationDegrees,
                    MeshDecalOverlay.material,  // Using UMAMaterial from overlay
                    MeshDecalOverlay,
                    new DecalSlotBuilder.DecalBuildOptions
                    {
                        useHitNormalForProjection = this.useHitNormalForProjection,
                        backOffset = 0.04f, // Slight offset back to ensure we capture edges
                                            //multithread = false,              // requirement: allocate per click, no async
                                            // copyBlendshapes = true,
                        facingThreshold = 0.2f,
                        enableDebug = true
                    });

                if (slotAsset == null)
                {
                    Debug.Log("Decal creation produced no geometry (nothing within radius or facing threshold).");
                    return;
                }
                UMAAssetIndexer.Instance.ProcessNewItem(slotAsset, false, false); // Ensure new asset is indexed

                // Wrap into SlotData and add overlay
                SlotData slotData = new SlotData(slotAsset);
                if (MeshDecalOverlay != null)
                {
                    var overlayInstance = new OverlayData(MeshDecalOverlay);
                    DecalSlotBuilder.SetLastDecalOverlay(overlayInstance);
                    slotData.AddOverlay(overlayInstance);
                }
                slotData.expandAlongNormal = slotOffset; // Slight expansion to avoid z-fighting

                // Add (accumulate) into existing UMA recipe
                Avatar.umaData.umaRecipe.MergeSlot(slotData, true);
                Avatar.ForceUpdate(true, true, true);
            }
            else
            {
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

                // Additional behavior for RenderTexture mode: create a DecalRTStampAsset and store in StampField
                if (StampField != null && TextureDecalOverlay != null)
                {
                    var last = DecalRenderTexture.LastStamp;
                    if (last != null)
                    {
                        // Clone the last stamp (runtime instance) so we can add to the stamp slot set
                        var clone = ScriptableObject.CreateInstance<DecalRTStampAsset>();
                        clone.overlayName = last.overlayName;
                        clone.bleedPixels = last.bleedPixels;
                        clone.forceLinearSampling = last.forceLinearSampling;
                        clone.slots = new List<DecalRTStampAsset.SlotStamp>(last.slots.Count);
                        for (int i = 0; i < last.slots.Count; i++)
                        {
                            var s = last.slots[i];
                            if (s == null)
                            {
                                continue;
                            }

                            var ns = new DecalRTStampAsset.SlotStamp
                            {
                                slotName = s.slotName,
                                slotHash = UMAUtils.StringToHash(s.slotName),
                                umaMaterialName = s.umaMaterialName,
                                normBaseUV = (s.normBaseUV != null) ? (Vector2[])s.normBaseUV.Clone() : new Vector2[0],
                                overlayUV = (s.overlayUV != null) ? (Vector2[])s.overlayUV.Clone() : new Vector2[0],
                                triangles = (s.triangles != null) ? (int[])s.triangles.Clone() : new int[0],
                                triOrdinals = s.triOrdinals != null ? (int[])s.triOrdinals.Clone() : null
                            };
                            clone.slots.Add(ns);
                        }

#if UNITY_EDITOR
                        // Persist as an asset so Inspector does not show 'Type Mismatch' for transient SOs
                        try
                        {
                            clone.name = $"Stamp_{(string.IsNullOrEmpty(clone.overlayName) ? "Overlay" : clone.overlayName)}";
                            var folder = "Assets/UMA/GeneratedDecalStamps";
                            if (!System.IO.Directory.Exists(folder))
                            {
                                System.IO.Directory.CreateDirectory(folder);
                            }
                            var safeName = clone.name.Replace('/', '_').Replace('\\', '_');
                            var path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{folder}/{safeName}.asset");
                            UnityEditor.AssetDatabase.CreateAsset(clone, path);
                            EditorUtility.SetDirty(clone);
                            UnityEditor.AssetDatabase.SaveAssetIfDirty(clone);
                        }
                        catch { /* ignore editor persistence errors */ }
#endif
                        bool addedNew = false;
                        // Find or create an overlay stamp set that will be triggered by base overlays on affected slots (not the decal overlay itself)
                        DecalRTStampSlot.OverlayStampSet targetSet = null;
                        if (StampField.overlayStamps != null)
                        {
                            // Prefer a dedicated auto set if present
                            for (int i = 0; i < StampField.overlayStamps.Count; i++)
                            {
                                var set = StampField.overlayStamps[i];
                                if (set != null && string.Equals(set.name, "AutoRTDecals", System.StringComparison.Ordinal))
                                {
                                    targetSet = set; break;
                                }
                            }
                        }
                        if (targetSet == null)
                        {
                            targetSet = new DecalRTStampSlot.OverlayStampSet
                            {
                                name = "AutoRTDecals",
                                overlays = new List<OverlayDataAsset>(),
                                overlayNames = new List<string>()
                            };
                            StampField.overlayStamps.Add(targetSet);
                            addedNew = true;
                        }

                        // Ensure trigger overlay names include the overlays currently used on the affected slots
                        var triggerNames = new HashSet<string>(System.StringComparer.Ordinal);
                        for (int si = 0; si < clone.slots.Count; si++)
                        {
                            var ss = clone.slots[si];
                            if (ss == null) continue;
                            var runtimeSlot = Avatar.umaData?.umaRecipe?.GetSlot(ss.slotName);
                            if (runtimeSlot == null) continue;
                            var overlays = runtimeSlot.GetOverlayList();
                            if (overlays == null) continue;
                            for (int oi = 0; oi < overlays.Count; oi++)
                            {
                                var od = overlays[oi];
                                if (od == null) continue;
                                var oname = od.overlayName;
                                if (!string.IsNullOrEmpty(oname)) triggerNames.Add(oname);
                            }
                        }
                        bool empty = targetSet.overlays == null || targetSet.overlays.Count == 0;
                        // Merge into set.overlayNames
                        if (targetSet.overlayNames == null) 
                            targetSet.overlayNames = new List<string>();
                        foreach (var n in triggerNames)
                        {
                            bool exists = false;
                            for (int j = 0; j < targetSet.overlayNames.Count; j++)
                            {
                                if (string.Equals(targetSet.overlayNames[j], n, System.StringComparison.Ordinal))
                                {
                                    exists = true; break;
                                }
                            }
                            if (empty || AutoAddOverlays)
                            {
                                if (!exists) targetSet.overlayNames.Add(n);
                            }
                        }

                        // Append the new stamp to the set
                        var list = new List<DecalRTStampAsset>();
                        if (targetSet.stamps != null && targetSet.stamps.Length > 0)
                        {
                            list.AddRange(targetSet.stamps);
                        }

                        list.Add(clone);
                        targetSet.stamps = list.ToArray();

                        // Ensure the slot is subscribed to atlas updates (use its public entrypoint)
                        if (Avatar.umaData != null)
                        {
                            StampField.OnCharacterBegun(Avatar.umaData);
                        }

                        // Trigger textures-only generation so atlas changes (if any) propagate and slot can re-stamp via OnAtlasUpdated
                        try
                        {
                            if (UMAAssetIndexer.Instance != null && UMAAssetIndexer.Instance.generator != null && !DrawRenderTexturesImmediately)
                            {
                                Avatar.ForceUpdate(false, true, false);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogException(ex);
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
                if (DecalRenderTexture.TryGetLastDebug(out var rSmr, out var rTris, out var rMap, out var rSeq))
                {
                    _dbgSmr = rSmr; _dbgSmrTriangles = rTris; _dbgTriToOrdinal = rMap; _dbgSequence = rSeq;
                }
            }
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
                // RT mode currently only supports removal in builder API
                if (_selectedOrdinals.Count > 0)
                {
                    changed = DecalRenderTexture.RemoveTrianglesFromLastStamp(_selectedOrdinals, Avatar, Avatar.umaData);
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

                _selectedOrdinals.Clear();
                _selectedAddCombinedTris.Clear();
                RefreshLastDecalDebug();
            }
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

#if (UNITY_IOS || UNITY_ANDROID)
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