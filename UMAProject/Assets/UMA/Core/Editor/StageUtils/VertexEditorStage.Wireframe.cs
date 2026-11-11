using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    /// <summary>
    /// The wireframe of the editing stage, drawn on the editable slots only.
    /// </summary>
    /// <remarks>
    /// The Scene view's Shaded Wireframe mode draws every triangle of everything in view, read-only
    /// slots included. The stage draws its own instead: a mesh of lines over the edges of the editable
    /// slots, on a hidden child renderer so the render pipeline depth-tests it against the character.
    /// The lines use the baked mesh's vertex order and are kept on its positions as it is sculpted.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private const string WireframePrefsKey = "UMA.VertexEditor.Wireframe";
        private static readonly Color EditableWireframeColor = new Color(0f, 0f, 0f, 0.45f);

        private GameObject wireOverlayObject;
        private MeshFilter wireOverlayFilter;
        private MeshRenderer wireOverlayRenderer;
        private Mesh wireMesh;
        private Material wireMaterial;
        private Mesh wireSourceMesh;
        private int wireSlotSignature;
        private bool wireHasLines;
        private bool wirePrefsLoaded;

        /// <summary>Whether the wireframe is shown on the editable slots.</summary>
        public bool ShowEditableWireframe
        {
            get
            {
                LoadWireframePrefs();
                return showVertexWireframe;
            }
            set
            {
                LoadWireframePrefs();
                showVertexWireframe = value;
                EditorPrefs.SetBool(WireframePrefsKey, value);
                UpdateEditableWireframe();
                SceneView.RepaintAll();
            }
        }

        /// <summary>Number of edges the wireframe currently draws; 0 when it is hidden or nothing is editable.</summary>
        internal int EditableWireframeEdgeCount =>
            wireMesh != null && wireHasLines && wireOverlayRenderer != null && wireOverlayRenderer.enabled
                ? (int)(wireMesh.GetIndexCount(0) / 2)
                : 0;

        // Kept in EditorPrefs: a new stage object is made every time the editor opens.
        private void LoadWireframePrefs()
        {
            if (wirePrefsLoaded)
            {
                return;
            }
            wirePrefsLoaded = true;
            showVertexWireframe = EditorPrefs.GetBool(WireframePrefsKey, true);
        }

        private void DrawWireframeOption()
        {
            bool show = EditorGUILayout.Toggle(
                new GUIContent("Wireframe", "Draw the wireframe on the editable slots. Read-only slots are drawn without it."),
                ShowEditableWireframe);
            if (show != showVertexWireframe)
            {
                ShowEditableWireframe = show;
            }
        }

        private Material GetWireframeMaterial()
        {
            if (wireMaterial != null)
            {
                return wireMaterial;
            }
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return null;
            }
            wireMaterial = new Material(shader) { name = "Editable Wireframe", hideFlags = HideFlags.HideAndDontSave };
            wireMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            wireMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            wireMaterial.SetInt("_Cull", (int)CullMode.Off);
            wireMaterial.SetInt("_ZWrite", 0);
            wireMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            // Towards the camera, so a line wins over the triangle edge it lies on.
            wireMaterial.SetFloat("_ZBias", -2f);
            wireMaterial.SetColor("_Color", EditableWireframeColor);
            // After the character and the mask wash.
            wireMaterial.renderQueue = (int)RenderQueue.Transparent + 110;
            return wireMaterial;
        }

        /// <summary>True when the stage can draw its own wireframe; otherwise the Scene view's mode is used.</summary>
        private bool CanDrawEditableWireframe => GetWireframeMaterial() != null;

        /// <summary>
        /// Brings the wireframe in line with the option, the baked mesh and the editable slots. Cheap
        /// when nothing changed; called from the Scene GUI.
        /// </summary>
        private void UpdateEditableWireframe()
        {
            LoadWireframePrefs();
            if (closing || !showVertexWireframe || VertexObject == null || BakedMesh == null || thisDCA == null || thisDCA.umaData == null ||
                thisDCA.umaData.umaRecipe == null || GetWireframeMaterial() == null)
            {
                SetEditableWireframeVisible(false);
                return;
            }

            int signature = BuildEditableSlotSignature(out List<KeyValuePair<int, int>> ranges);
            if (wireMesh == null || !ReferenceEquals(wireSourceMesh, BakedMesh) || wireMesh.vertexCount != BakedMesh.vertexCount ||
                signature != wireSlotSignature || wireOverlayObject == null)
            {
                RebuildEditableWireframe(ranges);
                wireSlotSignature = signature;
            }
            SetEditableWireframeVisible(wireHasLines);
        }

        /// <summary>Where each editable slot's vertices are in the baked mesh (start, count), and a hash of that.</summary>
        private int BuildEditableSlotSignature(out List<KeyValuePair<int, int>> ranges)
        {
            ranges = new List<KeyValuePair<int, int>>();
            int hash = 17;
            int vertexCount = BakedMesh.vertexCount;
            foreach (SlotData slot in RecipeSlots)
            {
                if (slot == null || slot.asset == null || slot.asset.meshData == null || !IsSlotEditable(slot))
                {
                    continue;
                }
                int start = GetVisibleBakedVertexIndex(slot, 0);
                int count = slot.asset.meshData.vertexCount;
                if (start < 0 || count <= 0 || start + count > vertexCount)
                {
                    continue;
                }
                ranges.Add(new KeyValuePair<int, int>(start, count));
                unchecked
                {
                    hash = hash * 31 + start;
                    hash = hash * 31 + count;
                }
            }
            return hash;
        }

        private void RebuildEditableWireframe(List<KeyValuePair<int, int>> ranges)
        {
            EnsureEditableWireframeObject();
            int vertexCount = BakedMesh.vertexCount;
            var editable = new bool[vertexCount];
            foreach (KeyValuePair<int, int> range in ranges)
            {
                for (int i = 0; i < range.Value; i++)
                {
                    editable[range.Key + i] = true;
                }
            }

            int[] triangles = GetBakedTriangles();
            var seen = new HashSet<ulong>();
            var lines = new List<int>(triangles.Length);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                // A slot's triangles use only its own vertices.
                if (a < 0 || a >= vertexCount || !editable[a])
                {
                    continue;
                }
                AddWireframeEdge(a, b, seen, lines);
                AddWireframeEdge(b, c, seen, lines);
                AddWireframeEdge(c, a, seen, lines);
            }

            if (wireMesh == null)
            {
                wireMesh = new Mesh { name = "Editable Wireframe", hideFlags = HideFlags.HideAndDontSave };
                wireMesh.MarkDynamic();
            }
            wireMesh.Clear();
            wireMesh.indexFormat = IndexFormat.UInt32;
            wireMesh.vertices = BakedMesh.vertices;
            wireMesh.SetIndices(lines, MeshTopology.Lines, 0, false);
            wireMesh.bounds = WireframeBounds();
            wireSourceMesh = BakedMesh;
            wireHasLines = lines.Count > 0;
            wireOverlayFilter.sharedMesh = wireMesh;
        }

        private static void AddWireframeEdge(int a, int b, HashSet<ulong> seen, List<int> lines)
        {
            ulong key = a < b ? ((ulong)(uint)a << 32) | (uint)b : ((ulong)(uint)b << 32) | (uint)a;
            if (seen.Add(key))
            {
                lines.Add(a);
                lines.Add(b);
            }
        }

        // Generous, so a stroke that moves the surface never gets the lines culled.
        private Bounds WireframeBounds()
        {
            Bounds bounds = BakedMesh.bounds;
            bounds.Expand(1f);
            return bounds;
        }

        private void EnsureEditableWireframeObject()
        {
            if (wireOverlayObject != null && wireOverlayObject.transform.parent == VertexObject.transform)
            {
                return;
            }
            if (wireOverlayObject != null)
            {
                DestroyImmediate(wireOverlayObject);
            }
            wireOverlayObject = new GameObject("Editable Wireframe") { hideFlags = HideFlags.HideAndDontSave };
            wireOverlayObject.transform.SetParent(VertexObject.transform, false);
            wireOverlayFilter = wireOverlayObject.AddComponent<MeshFilter>();
            wireOverlayRenderer = wireOverlayObject.AddComponent<MeshRenderer>();
            wireOverlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            wireOverlayRenderer.receiveShadows = false;
            wireOverlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            wireOverlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            wireOverlayRenderer.allowOcclusionWhenDynamic = false;
            wireOverlayRenderer.sharedMaterial = GetWireframeMaterial();
            SceneVisibilityManager.instance.DisablePicking(wireOverlayObject, false);
        }

        private void SetEditableWireframeVisible(bool visible)
        {
            if (wireOverlayRenderer != null && wireOverlayRenderer.enabled != visible)
            {
                wireOverlayRenderer.enabled = visible;
            }
        }

        /// <summary>The baked mesh's vertices moved: keeps the lines on them.</summary>
        private void SyncEditableWireframeVertices()
        {
            if (wireMesh == null || !ReferenceEquals(wireSourceMesh, BakedMesh) || bakedVertices == null ||
                bakedVertices.Length != wireMesh.vertexCount)
            {
                return;
            }
            wireMesh.vertices = bakedVertices;
            wireMesh.bounds = WireframeBounds();
        }

        private void DestroyEditableWireframe()
        {
            if (wireOverlayObject != null)
            {
                DestroyImmediate(wireOverlayObject);
            }
            if (wireMesh != null)
            {
                DestroyImmediate(wireMesh);
            }
            if (wireMaterial != null)
            {
                DestroyImmediate(wireMaterial);
            }
            wireOverlayObject = null;
            wireOverlayFilter = null;
            wireOverlayRenderer = null;
            wireMesh = null;
            wireMaterial = null;
            wireSourceMesh = null;
            wireHasLines = false;
        }
    }
}
