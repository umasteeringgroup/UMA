using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    [CustomEditor(typeof(HairCardProfileAsset)), CanEditMultipleObjects]
    internal sealed class HairCardProfileEditor : UnityEditor.Editor
    {
        private bool identifiers;

        internal static void DrawWidthCurve(SerializedProperty curve, float rootWidth, float tipWidth, int samples)
        {
            EditorGUILayout.PropertyField(curve, new GUIContent("Width along card",
                "Horizontal: root (0) to tip (1). Vertical: 1 uses Root Width; 0 uses Tip Width. Values between blend those widths; this is not a width multiplier."));
            EditorGUILayout.LabelField("Left = root, right = tip. Value 1 = Root Width; value 0 = Tip Width. Equal widths make a constant-width ribbon.", EditorStyles.wordWrappedMiniLabel);
            if (curve.hasMultipleDifferentValues) return;
            var shape = curve.animationCurveValue;
            if (shape == null) return;
            float start = Mathf.Lerp(tipWidth, rootWidth, Mathf.Clamp01(shape.Evaluate(0)));
            float end = Mathf.Lerp(tipWidth, rootWidth, Mathf.Clamp01(shape.Evaluate(1)));
            EditorGUILayout.LabelField($"Profile width at root: {start * 1000f:0.##} mm   ·   at tip: {end * 1000f:0.##} mm", EditorStyles.wordWrappedMiniLabel);
            if (Mathf.Abs(start - rootWidth) > 1e-6f)
                EditorGUILayout.HelpBox("The curve changes the starting width. Set its first key to Time 0, Value 1 for a full-width root. Root transparency is controlled in Materials & UVs, not by this curve.", MessageType.Info);
            var keys = shape.keys;
            for (int i = 1; i < keys.Length; i++)
                if (keys[i].time > 0 && keys[i - 1].time < 1 && keys[i].time > keys[i - 1].time &&
                    keys[i].time - keys[i - 1].time < 1f / Mathf.Max(1, samples - 1) && Mathf.Abs(keys[i].value - keys[i - 1].value) > .1f)
                {
                    EditorGUILayout.HelpBox("A width transition is shorter than one mesh segment at this sampling level. Spread its keys farther apart or increase points per card for a smoother silhouette.", MessageType.Warning);
                    break;
                }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Shared card geometry settings. Changes affect every hair group using this profile. Use Make profile unique in Hair Properties for independent edits.", MessageType.None);
            void Field(string name, string label, string tooltip = null) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label, tooltip));
            EditorGUILayout.LabelField("Card shape & width", EditorStyles.boldLabel);
            Field("shape", "Card shape");
            Field("defaultWidth", "Root width (m)", "Full width, not radius. The width curve selects/blends the root and tip width settings.");
            Field("tipWidth", "Tip width (m)");
            DrawWidthCurve(serializedObject.FindProperty("widthAlongCard"), serializedObject.FindProperty("defaultWidth").floatValue,
                serializedObject.FindProperty("tipWidth").floatValue, serializedObject.FindProperty("samplesPerCard").intValue);
            bool ribbon = serializedObject.FindProperty("shape").enumValueIndex == (int)HairCardShape.Ribbon;
            if (ribbon) { Field("ribbonSpans", "Segments across width"); Field("ribbonCamber", "Ribbon roundness", "0 is flat; larger values arch the ribbon across its width."); }
            else Field("tubeSides", "Sides around tube");
            Field("doubleSided", "Generate backface geometry", "Duplicates reverse-facing triangles. Unnecessary for a two-sided shader.");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Mesh detail", EditorStyles.boldLabel);
            Field("samplesPerCard", "Points along card", "13 points produce 12 lengthwise segments. LOD settings may override this budget.");
            Field("adaptiveSampling", "Reduce segments on straight cards");
            using (new EditorGUI.DisabledScope(!serializedObject.FindProperty("adaptiveSampling").boolValue))
            { Field("maximumSegmentLength", "Target segment length (m)"); Field("maximumSegmentAngle", "Target bend per segment (°)"); }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Vertex color gradient", EditorStyles.boldLabel);
            Field("useVertexColorGradient", "Write root-to-tip RGBA");
            using (new EditorGUI.DisabledScope(!serializedObject.FindProperty("useVertexColorGradient").boolValue))
            {
                Field("rootVertexColor", "Root vertex color (RGBA)"); Field("tipVertexColor", "Tip vertex color (RGBA)");
                Field("rootColorSegments", "Solid root segments", "Number of complete segments retaining the root color before blending to the tip.");
            }
            EditorGUILayout.LabelField("Vertex colors can drive animation. They affect transparency only when the material enables Use Vertex Alpha as Opacity. Material root/tip opacity works independently.", EditorStyles.wordWrappedMiniLabel);
            identifiers = EditorGUILayout.Foldout(identifiers, "Internal identifier (read only)", true);
            if (identifiers) using (new EditorGUI.DisabledScope(true)) Field("profileId", "Profile ID");
            if (serializedObject.ApplyModifiedProperties())
            {
                var stage = HairCardStage.ActiveStage;
                if (stage != null)
                {
                    foreach (var resource in targets) stage.TrackResourceEdit(resource);
                    stage.QueuePreviewChange(HairPreviewChange.Evaluation | HairPreviewChange.Geometry);
                }
            }
        }
    }
}
