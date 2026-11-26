using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>The Body Region tool: paint a region on the body anywhere, then make pattern pieces from it.</summary>
    public sealed partial class DesignerSession
    {
        /// <summary>The painted region: indices of the body's triangles (the body collider's).</summary>
        public readonly HashSet<int> BodyRegion = new HashSet<int>();
        /// <summary>Paint the other side of the body too.</summary>
        public bool RegionMirror = true;
        /// <summary>How much bigger than the body the pieces are cut (a fraction; negative for stretch fabrics that grip).</summary>
        public float RegionEase;
        public string RegionFabric = "Jersey";
        public float RegionRadius = 0.04f;
        /// <summary>What Make Pieces did, or why it could not.</summary>
        public string RegionMessage;

        private BodyRegionBrush regionBrush;
        private Vector3 regionHover;
        private bool regionHoverValid;
        private static Material regionMaterial;

        /// <summary>The brush for the current body (made the first time it is needed: welding the body takes a moment).</summary>
        public BodyRegionBrush RegionBrush
        {
            get
            {
                if (Body == null) return null;
                if (regionBrush == null || regionBrush.Body != Body)
                {
                    // A new measurement of the same body keeps its triangles; another body does not.
                    if (regionBrush != null && regionBrush.TriangleCount != Body.Collider.TriangleCount) BodyRegion.Clear();
                    regionBrush = new BodyRegionBrush(Body);
                }
                return regionBrush;
            }
        }

        public void ClearRegion()
        {
            BodyRegion.Clear();
            RegionMessage = null;
            SceneView.RepaintAll();
            NotifyChanged();
        }

        private void BodyRegionSceneGUI(SceneView view, bool pointerOverPanel)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.Repaint)
            {
                DrawRegion();
                if (regionHoverValid)
                {
                    Handles.color = e.shift ? new Color(1f, 0.4f, 0.35f) : new Color(0.25f, 0.8f, 1f);
                    Handles.DrawWireDisc(regionHover, (view.camera.transform.position - regionHover).normalized, RegionRadius);
                }
            }
            if (e.alt || pointerOverPanel) { regionHoverValid = false; return; }
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseMove:
                    regionHoverValid = RaycastBody(HandleUtility.GUIPointToWorldRay(e.mousePosition), out regionHover);
                    view.Repaint();
                    break;
                case EventType.MouseDown:
                case EventType.MouseDrag:
                    if (e.button != 0) break;
                    if (e.type == EventType.MouseDrag && GUIUtility.hotControl != id) break;
                    if (RaycastBody(HandleUtility.GUIPointToWorldRay(e.mousePosition), out Vector3 hit))
                    {
                        BodyRegionBrush brush = RegionBrush;
                        if (brush != null)
                        {
                            brush.Paint(hit, RegionRadius, BodyRegion, e.shift);
                            if (RegionMirror) brush.Paint(brush.Mirror(hit), RegionRadius, BodyRegion, e.shift);
                            RegionMessage = null;
                            NotifyChanged();
                        }
                        regionHover = hit;
                        regionHoverValid = true;
                    }
                    if (e.type == EventType.MouseDown) GUIUtility.hotControl = id;
                    view.Repaint();
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
        }

        /// <summary>The painted region, drawn on the body as a see-through tint.</summary>
        private void DrawRegion()
        {
            if (BodyRegion.Count == 0 || Body == null) return;
            if (regionMaterial == null)
            {
                regionMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                regionMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                regionMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                regionMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                regionMaterial.SetInt("_ZWrite", 0);
                regionMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            }
            BodyCollider c = Body.Collider;
            regionMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Handles.matrix);
            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(0.2f, 0.75f, 1f, 0.45f));
            foreach (int t in BodyRegion)
            {
                if (t < 0 || t >= c.TriangleCount) continue;
                Vector3 lift = c.N[t] * 0.0015f;
                GL.Vertex(c.A[t] + lift); GL.Vertex(c.B[t] + lift); GL.Vertex(c.C[t] + lift);
            }
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// Makes pattern pieces from the painted region and adds them to the pattern (a new pattern if none is open):
        /// split by part, cut open, unrolled, split into gores where needed, sewn where they meet, laid where painted.
        /// </summary>
        public void MakeRegionPieces()
        {
            RegionMessage = null;
            if (Body == null) { RegionMessage = "The body has not been measured: " + BodyProblem; return; }
            if (BodyRegion.Count == 0) { RegionMessage = "Paint a region on the body first."; return; }
            BodyRegionBrush brush = RegionBrush;
            // Cut at the body plus the clearance the cloth keeps from it: Ease 0 is a snug fit with that much room.
            float outward = FitData != null ? FitData.clearanceCm * 0.01f : 0.006f;
            List<RegionPatch> patches = brush.Patches(BodyRegion, outward);
            var flats = new List<FlatPiece>();
            var notes = new List<string>();
            foreach (RegionPatch patch in patches)
            {
                List<FlatPiece> pieces = RegionUnfold.Unfold(patch, out string why);
                if (why != null) notes.Add(patch.Name + ": " + why);
                if (pieces != null) flats.AddRange(pieces);
            }
            if (flats.Count == 0) { RegionMessage = notes.Count > 0 ? string.Join(" ", notes) : "No pieces could be made from the region."; return; }
            if (Pattern == null)
            {
                var fresh = ScriptableObject.CreateInstance<ClothesPattern>();
                fresh.name = "Body Region";
                NewPattern(fresh);
            }
            Undo.RecordObject(Pattern, "Make Pieces from Body Region");
            string race = Stage != null && Stage.CharacterRace != null ? Stage.CharacterRace.raceName : null;
            List<PatternPiece> added = RegionPieces.AddToPattern(Pattern, flats, Body.Landmarks, RegionEase, RegionFabric, race);
            Pattern.Migrate();
            // The new pieces need their room on the texture.
            UvLayout.Repack(Pattern);
            EditorUtility.SetDirty(Pattern);
            float worst = 0f;
            foreach (FlatPiece f in flats) worst = Mathf.Max(worst, f.Distortion);
            RegionMessage = "Made " + added.Count + (added.Count == 1 ? " piece" : " pieces") + " from the region; flattening stretches the cloth up to " + (worst * 100f).ToString("0") + "%."
                + (brush.LeftOut > 0 ? " " + brush.LeftOut + " painted triangles in small scraps on their own were left out." : "")
                + (notes.Count > 0 ? " " + string.Join(" ", notes) : "");
            BodyRegion.Clear();
            RebuildCloth(false);
            NotifyChanged();
        }
    }
}
