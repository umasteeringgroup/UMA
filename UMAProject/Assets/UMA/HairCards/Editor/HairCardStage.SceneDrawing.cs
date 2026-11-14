using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.HairCards.Editor
{
    /// <summary>
    /// Batched Scene view drawing. A groom can show thousands of child curves; drawing each
    /// segment with its own Handles call costs tens of thousands of draw calls per repaint.
    /// The segments are built once per change, already posed, grouped by color, and drawn
    /// with one call per color.
    /// </summary>
    public sealed partial class HairCardStage
    {
        // Bumped whenever the displayed curves or the authoring pose change.
        private int sceneCurveVersion;

        private void InvalidateSceneCurveCache() => sceneCurveVersion++;

        private sealed class LineBatch
        {
            internal readonly Dictionary<Color, List<Vector3>> Segments = new Dictionary<Color, List<Vector3>>();
            internal readonly List<Color> Colors = new List<Color>();
            internal readonly List<Vector3[]> Arrays = new List<Vector3[]>();
            internal int Version = -1;
            internal object Source;
            internal int Key;

            internal void Begin()
            {
                foreach (List<Vector3> list in Segments.Values) list.Clear();
                Colors.Clear();
                Arrays.Clear();
            }

            internal void Add(Color color, Vector3 a, Vector3 b)
            {
                if (!Segments.TryGetValue(color, out List<Vector3> list))
                {
                    list = new List<Vector3>(256);
                    Segments.Add(color, list);
                }
                list.Add(a);
                list.Add(b);
            }

            internal void End()
            {
                foreach (KeyValuePair<Color, List<Vector3>> pair in Segments)
                {
                    if (pair.Value.Count < 2) continue;
                    Colors.Add(pair.Key);
                    Arrays.Add(pair.Value.ToArray());
                }
            }

            internal void Draw(bool dotted, float dashSize)
            {
                for (int i = 0; i < Arrays.Count; i++)
                {
                    Handles.color = Colors[i];
                    if (dotted) Handles.DrawDottedLines(Arrays[i], dashSize);
                    else Handles.DrawLines(Arrays[i]);
                }
            }
        }

        private readonly LineBatch childLines = new LineBatch();
        private readonly LineBatch candidateLines = new LineBatch();

        private int GroupColorKey()
        {
            unchecked
            {
                int hash = 17;
                if (groom?.Groups == null) return hash;
                for (int i = 0; i < groom.Groups.Count; i++)
                    if (groom.Groups[i] != null) hash = hash * 31 + groom.Groups[i].color.GetHashCode();
                return hash;
            }
        }

        private void DrawEvaluatedChildren()
        {
            if (sculptPreviewPending) return;
            if (strokeActive || !EffectiveChildren || !EffectiveChildSplines || EffectivePreviewMode != HairPreviewMode.GuidesAndChildren ||
                evaluation?.curves == null)
                return;
            // Handles only draw on Repaint; skip the work for Layout and input events.
            if (Event.current.type != EventType.Repaint) return;

            int key;
            unchecked
            {
                key = GroupColorKey();
                key = key * 31 + evaluation.curves.Count;
                key = key * 31 + evaluation.childCurveCount;
                key = key * 31 + (populationInspection ? 1 : 0);
                key = key * 31 + (maskPreviewId?.GetHashCode() ?? 0);
                key = key * 31 + (populationPreviewId?.GetHashCode() ?? 0);
            }
            if (childLines.Version != sceneCurveVersion || !ReferenceEquals(childLines.Source, evaluation) || childLines.Key != key)
            {
                childLines.Version = sceneCurveVersion;
                childLines.Source = evaluation;
                childLines.Key = key;
                childLines.Begin();
                int childCount = evaluation.childCurveCount;
                int stride = Mathf.Max(1, childCount / 2500);
                int encountered = 0;
                for (int curveIndex = 0; curveIndex < evaluation.curves.Count; curveIndex++)
                {
                    HairEvaluatedCurve curve = evaluation.curves[curveIndex];
                    if (curve == null || !curve.isChild || curve.points.Count < 2) continue;
                    if (encountered++ % stride != 0) continue;
                    Matrix4x4 pose = authoringPose?.MatrixForCurve(groom, curve) ?? Matrix4x4.identity;
                    Color color = populationInspection ? PopulationColor(curve) : curve.groupColor;
                    color.a = populationInspection ? 0.95f : 0.38f;
                    for (int pointIndex = 1; pointIndex < curve.points.Count; pointIndex++)
                        childLines.Add(color, pose.MultiplyPoint3x4(curve.points[pointIndex - 1].position),
                            pose.MultiplyPoint3x4(curve.points[pointIndex].position));
                }
                childLines.End();
            }
            childLines.Draw(!populationInspection, 3f);
        }

        private void DrawGenerationPreview()
        {
            if (generationPreview?.guides == null) return;
            if (Event.current.type != EventType.Repaint) return;
            int key = generationPreview.guides.Count;
            if (candidateLines.Version != sceneCurveVersion || !ReferenceEquals(candidateLines.Source, generationPreview) ||
                candidateLines.Key != key)
            {
                candidateLines.Version = sceneCurveVersion;
                candidateLines.Source = generationPreview;
                candidateLines.Key = key;
                candidateLines.Begin();
                Color color = new Color(0.1f, 1f, 0.8f, 0.9f);
                for (int guideIndex = 0; guideIndex < generationPreview.guides.Count; guideIndex++)
                {
                    HairGuide guide = generationPreview.guides[guideIndex];
                    if (guide?.points == null) continue;
                    Matrix4x4 pose = authoringPose != null && authoringPose.TryGetMatrix(guide.root, out Matrix4x4 guidePose)
                        ? guidePose : Matrix4x4.identity;
                    for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                        candidateLines.Add(color, pose.MultiplyPoint3x4(guide.points[pointIndex - 1].position),
                            pose.MultiplyPoint3x4(guide.points[pointIndex].position));
                }
                candidateLines.End();
            }
            candidateLines.Draw(true, 4f);
        }

        // ---------------------------------------------------------------
        // Guide roots: Handles.Button still does the picking (so clicking a root works as
        // before), but its per-root draw is replaced by one batched quad pass.
        // ---------------------------------------------------------------

        private readonly struct RootDot
        {
            internal readonly Vector3 Position;
            internal readonly float Size;
            internal readonly Color Color;

            internal RootDot(Vector3 position, float size, Color color)
            {
                Position = position;
                Size = size;
                Color = color;
            }
        }

        private readonly List<RootDot> rootDots = new List<RootDot>(1024);

        /// <summary>A Handles cap that picks like a dot but leaves the drawing to the batch.</summary>
        private void RootDotCap(int controlId, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            if (eventType == EventType.Repaint)
            {
                // Handles.Button has already applied the hover/pressed color. The size is in
                // the current Handles space; convert it to world units like the position.
                float scale = Handles.matrix.MultiplyVector(Vector3.right).magnitude;
                rootDots.Add(new RootDot(Handles.matrix.MultiplyPoint3x4(position), size * scale, Handles.color));
                return;
            }
            Handles.DotHandleCap(controlId, position, rotation, size, eventType);
        }

        private void BeginRootDots() => rootDots.Clear();

        /// <summary>Frees the batched drawing caches and the dot material when the stage closes.</summary>
        private void ReleaseSceneDrawingResources()
        {
            rootDots.Clear();
            childLines.Begin();
            childLines.Segments.Clear();
            childLines.Source = null;
            candidateLines.Begin();
            candidateLines.Segments.Clear();
            candidateLines.Source = null;
            if (rootDotMaterial != null)
            {
                Object.DestroyImmediate(rootDotMaterial);
                rootDotMaterial = null;
            }
        }

        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static Material rootDotMaterial;

        /// <summary>Unlit vertex-colored material for the batched root dots (like Handles' own).</summary>
        private static Material RootDotMaterial
        {
            get
            {
                if (rootDotMaterial != null) return rootDotMaterial;
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return null;
                rootDotMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                rootDotMaterial.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
                rootDotMaterial.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                rootDotMaterial.SetFloat("_Cull", (int)CullMode.Off);
                rootDotMaterial.SetFloat("_ZWrite", 0);
                return rootDotMaterial;
            }
        }

        private void FlushRootDots()
        {
            if (rootDots.Count == 0 || Event.current.type != EventType.Repaint) return;
            Camera camera = Camera.current;
            if (camera == null) { rootDots.Clear(); return; }
            Vector3 right = camera.transform.right;
            Vector3 up = camera.transform.up;
            Material material = RootDotMaterial;
            if (material == null) { rootDots.Clear(); return; }
            CompareFunction zTest = Handles.zTest == CompareFunction.Disabled ? CompareFunction.Always : Handles.zTest;
            material.SetFloat(ZTestId, (int)zTest);
            material.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.QUADS);
            for (int i = 0; i < rootDots.Count; i++)
            {
                RootDot dot = rootDots[i];
                // Same footprint as Handles.DotHandleCap.
                Vector3 r = right * dot.Size;
                Vector3 u = up * dot.Size;
                GL.Color(dot.Color);
                GL.Vertex(dot.Position - r - u);
                GL.Vertex(dot.Position - r + u);
                GL.Vertex(dot.Position + r + u);
                GL.Vertex(dot.Position + r - u);
            }
            GL.End();
            GL.PopMatrix();
            rootDots.Clear();
        }
    }
}
