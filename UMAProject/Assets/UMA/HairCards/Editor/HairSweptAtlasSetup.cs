using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static class HairSweptAtlasSetup
    {
        internal const string DiffusePath = "Assets/UMA/SRP/Textures/Hair/HairAtlasDiffuse_New.png";
        // The two long strip rectangles used by the PointySwept example and UMA atlas.
        internal static readonly Rect[] Strips = { new Rect(.46608f,.03607f,.15093f,.94754f), new Rect(.63557f,.03959f,.14935f,.94601f) };
        internal static void ConfigureNew(HairAtlasProfileAsset atlas)
        {
            atlas.albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffusePath);
            atlas.normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UMA/SRP/Textures/Hair/HairAtlasNormal_New.png");
            atlas.mask = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UMA/SRP/Textures/Hair/HairAtlasAO_New.png");
            atlas.regions.Clear();
            AddStrips(atlas); EditorUtility.SetDirty(atlas);
        }
        internal static void AddStrips(HairAtlasProfileAsset atlas)
        {
            for (int i = 0; i < Strips.Length; i++)
                if (!atlas.regions.Any(r => r.uvRect == Strips[i])) atlas.CreateRegion("Swept long strip " + (i+1), Strips[i]).flipV = true;
        }
        internal static void DrawTreeAction(HairCardStage stage, HairGroomNode node)
        {
            if (node.Kind != HairGroomNodeKind.Atlas || node.Group?.atlas == null) return;
            using (new EditorGUI.DisabledScope(node.Locked))
            if (GUILayout.Button("Use UMA Swept Strips…"))
            {
                if (!EditorUtility.DisplayDialog("Select Swept Atlas Strips?", "Add the two long UV strips used by the PointySwept example and select them for this group? Existing UV sets and textures are preserved. The layout is shared by HairAtlasDiffuse_New and its variants.", "Use Swept Strips", "Cancel")) return;
                var atlas = node.Group.atlas; Undo.RecordObject(atlas,"Add Swept UV Strips"); Undo.RecordObject(stage.Groom,"Select Swept UV Strips");
                AddStrips(atlas); node.Group.atlasRegionSelection=HairAtlasRegionSelectionMode.Selected;
                node.Group.atlasRegionIds=atlas.regions.Where(r=>Strips.Contains(r.uvRect)).Select(r=>r.Id).ToList();
                EditorUtility.SetDirty(atlas); stage.TrackResourceEdit(atlas); HairGroomCommands.Commit(stage.Groom,HairPreviewChange.Geometry);
            }
        }
    }
}
