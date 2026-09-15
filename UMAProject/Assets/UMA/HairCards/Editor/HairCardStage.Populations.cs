using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairCardStage
    {
        private string populationPreviewId, maskPreviewId;
        private bool populationInspection;
        private HairPreviewMode EffectivePreviewMode => populationInspection ? HairPreviewMode.GuidesAndChildren : previewMode;
        private bool EffectiveChildren => populationInspection || showChildren;
        private bool EffectiveChildSplines => populationInspection || showChildSplines;
        private bool EffectiveGuideSplines => populationInspection ? string.IsNullOrEmpty(populationPreviewId) : showGuideSplines;
        private bool EffectiveGuideRoots => !populationInspection && showGuideRoots;
        private readonly System.Collections.Generic.Dictionary<string, HairSurfaceFields> scalpFields = new System.Collections.Generic.Dictionary<string, HairSurfaceFields>();
        private readonly System.Collections.Generic.List<Color32> scalpColors = new System.Collections.Generic.List<Color32>();
        private Color32[] scalpBaseline;
        private Mesh scalpColorMesh;
        private bool scalpPreviewApplied;
        internal SkinnedMeshRenderer SourceRendererForScalp => ResolveSourceRenderer();
        internal void ApplyScalpPreview()
        {
            ApplyBoundScalpPreview();
            if (groom?.SourceMesh == null || !groom.SourceMesh.isReadable || scalpFilter == null) return;
            bool enabled = groom.Groups.Exists(g => g?.enabled == true && g.generation?.scalp?.enabled == true);
            if (!enabled && !scalpPreviewApplied) return;
            scalpPreviewApplied = enabled;
            if (scalpBaseline == null || scalpBaseline.Length != groom.SourceVertexCount)
            {
                scalpBaseline = groom.SourceMesh.colors32;
                if (scalpBaseline.Length != groom.SourceVertexCount)
                {
                    scalpBaseline = new Color32[groom.SourceVertexCount];
                    System.Array.Fill(scalpBaseline, new Color32(255,255,255,255));
                }
            }
            HairScalpShadingUtility.Evaluate(groom, scalpBaseline, scalpColors, scalpFields);
            var current = scalpFilter.sharedMesh;
            if (current != null && current.vertexCount == scalpColors.Count)
            {
                if (current != scalpColorMesh)
                {
                    if (scalpColorMesh != null) DestroyImmediate(scalpColorMesh);
                    scalpColorMesh = Instantiate(current); scalpColorMesh.name = "Hair Scalp Vertex Color Preview";
                    scalpColorMesh.hideFlags = HideFlags.HideAndDontSave; scalpFilter.sharedMesh = scalpColorMesh;
                }
                scalpColorMesh.SetColors(scalpColors);
            }
            var renderers = sourceAvatar?.umaData?.GetRenderers();
            int index = renderers != null ? System.Array.IndexOf(renderers, ResolveSourceRenderer()) : -1;
            if (index >= 0) avatarPreview?.ApplyVertexColors(index, scalpColors);
        }
        private void ReleasePopulationPreview()
        {
            if (populationInspection) InspectPopulation(null);
            if (scalpColorMesh != null) DestroyImmediate(scalpColorMesh);
            scalpColorMesh = null; scalpBaseline = null; scalpColors.Clear(); scalpFields.Clear(); scalpPreviewApplied = false;
        }
        internal string InspectedPopulationId => populationPreviewId;
        internal string InspectedMaskId => maskPreviewId;

        internal void InspectPopulation(string id, string maskId = null)
        {
            bool inspect = !string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(maskId);
            // Never alter persisted visibility/preferences for a temporary diagnostic preview.
            populationInspection = inspect; populationPreviewId = id; maskPreviewId = maskId;
            QueuePreviewChange(HairPreviewChange.Evaluation); RepaintAll();
        }

        private Color PopulationColor(HairEvaluatedCurve curve)
        {
            if (!string.IsNullOrEmpty(maskPreviewId)) return Color.Lerp(new Color(0.12f, 0.12f, 0.15f), Color.white, curve.maskValue);
            var random = new HairDeterministicRandomForEditor(curve.clumpSeed);
            return Color.HSVToRGB(random.Value, 0.65f, 1f);
        }
        // Do not use string.GetHashCode or transient Unity IDs for visible clump identity.
        private readonly struct HairDeterministicRandomForEditor
        {
            internal readonly float Value;
            internal HairDeterministicRandomForEditor(int seed)
            { uint x = unchecked((uint)seed * 747796405u + 2891336453u); x ^= x >> 16; Value = (x & 0xffffu) / 65535f; }
        }
    }
}
