using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    /// <summary>The same material controls work in the Inspector and docked Hair Properties.</summary>
    public sealed class HairSweptShaderGUI : ShaderGUI
    {
        internal const string ShaderName = "UMA/Hair Cards/Swept Hair URP";
        private bool lighting, advanced;
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUILayout.HelpBox("Two-sided, alpha-clipped strand shading. UV1 carries root-to-tip / strand ID; UV2 carries clump ID / mask. Vertex RGBA is left available for animation by default.", MessageType.None);
            void Field(string name) { var p = FindProperty(name, properties, false); if (p != null) editor.ShaderProperty(p, p.displayName); }
            EditorGUILayout.LabelField("Atlas & color", EditorStyles.boldLabel);
            foreach (string name in new[] { "_BaseMap", "_BaseColor", "_RootColor", "_TipColor", "_RootFade", "_ColorPower", "_StrandVariation", "_ClumpVariation", "_Cutoff", "_DitheredOpacity", "_Coverage" }) Field(name);
            lighting = EditorGUILayout.Foldout(lighting, "Highlights & lighting", true);
            if (lighting) foreach (string name in new[] { "_SpecularColor", "_SpecularStrength", "_Smoothness", "_SpecularShift", "_SecondaryColor", "_SecondaryStrength", "_SecondarySmoothness", "_SecondaryShift", "_Transmission", "_TransmissionColor", "_DiffuseWrap", "_AmbientStrength" }) Field(name);
            advanced = EditorGUILayout.Foldout(advanced, "Texture channels & diagnostics", true);
            if (advanced)
            {
                foreach (string name in new[] { "_TextureColor", "_DepthInfluence", "_DepthMap", "_UseDepthMap", "_OcclusionMap", "_OcclusionStrength", "_BumpMap", "_BumpScale", "_ShadowCutoff", "_AlphaToCoverage", "_VertexColorInfluence", "_VertexAlphaInfluence", "_DebugView" }) Field(name);
                EditorGUILayout.HelpBox("Depth shading reads atlas red, or the optional separate depth map using the same UVs. A nearly white diffuse atlas needs a separate depth map for fiber contrast. Set depth influence to 0 for ordinary color-only shading. Alpha to coverage smooths edges with MSAA. No duplicate backfaces or second pass are needed.", MessageType.Info);
                editor.EnableInstancingField(); editor.RenderQueueField();
            }
        }

        internal enum Finish { Matte, Natural, Glossy }
        internal static void ApplyFinish(Material material, Finish finish)
        {
            if (material == null || material.shader.name != ShaderName) return;
            Undo.RecordObject(material, "Change Hair Finish");
            material.SetFloat("_SpecularStrength", finish == Finish.Matte ? .10f : finish == Finish.Natural ? .28f : .6f);
            material.SetFloat("_SecondaryStrength", finish == Finish.Matte ? .035f : finish == Finish.Natural ? .12f : .3f);
            material.SetFloat("_Smoothness", finish == Finish.Matte ? .4f : finish == Finish.Natural ? .6f : .8f);
            material.SetFloat("_SecondarySmoothness", finish == Finish.Matte ? .2f : finish == Finish.Natural ? .35f : .55f);
            EditorUtility.SetDirty(material);
        }

        internal static Material CreateMaterial(HairGroomAsset groom, HairAtlasProfileAsset atlas, string resourceFolder = null)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) return null;
            var material = new Material(shader) { name = groom.name + " Swept Hair", renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest };
            if (atlas?.albedo != null) material.SetTexture("_BaseMap", atlas.albedo);
            if (atlas?.normal != null) material.SetTexture("_BumpMap", atlas.normal);
            if (atlas?.mask != null) material.SetTexture("_OcclusionMap", atlas.mask);
            HairGenerationEditor.SaveResourceNear(groom, material, resourceFolder);
            if (atlas != null) { Undo.RecordObject(atlas, "Assign Swept Hair Material"); atlas.material = material; EditorUtility.SetDirty(atlas); }
            return material;
        }

        internal static void DrawInline(HairCardStage stage, HairGroup group)
        {
            var atlas = group.atlas; if (atlas == null) return;
            if (atlas.material == null || atlas.material.shader.name != ShaderName)
            {
                using (new EditorGUI.DisabledScope(Shader.Find(ShaderName) == null))
                if (GUILayout.Button("Create Swept Hair URP Material", GUILayout.Height(26)))
                {
                    if (atlas.material == null || EditorUtility.DisplayDialog("Assign New Hair Material?", "Create and assign a new two-sided URP hair material? The previous material asset is preserved. Undo restores the assignment.", "Create & Assign", "Cancel"))
                    { CreateMaterial(stage.Groom, atlas); stage.TrackResourceEdit(atlas); stage.QueuePreviewChange(HairPreviewChange.Materials); }
                }
                return;
            }
            var material = atlas.material;
            EditorGUILayout.LabelField("Hair color", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Color root = EditorGUILayout.ColorField("Roots", material.GetColor("_RootColor"));
            Color tip = EditorGUILayout.ColorField("Tips", material.GetColor("_TipColor"));
            float reach = EditorGUILayout.Slider("Root color reach", material.GetFloat("_RootFade"), 0.01f, 1f);
            float variation = EditorGUILayout.Slider("Strand variation", material.GetFloat("_StrandVariation"), 0f, 1f);
            float cutoff = EditorGUILayout.Slider("Alpha cutoff", material.GetFloat("_Cutoff"), 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(material, "Edit Hair Color"); material.SetColor("_RootColor", root); material.SetColor("_TipColor", tip);
                material.SetFloat("_RootFade", reach); material.SetFloat("_StrandVariation", variation); material.SetFloat("_Cutoff", cutoff);
                EditorUtility.SetDirty(material); stage.TrackResourceEdit(material); stage.QueuePreviewChange(HairPreviewChange.Materials);
            }
            EditorGUILayout.LabelField("Surface finish", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Edits affect every use of this shared material. Finish presets change only highlights, not your colors or textures.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
                foreach (Finish finish in System.Enum.GetValues(typeof(Finish)))
                    if (GUILayout.Button(finish.ToString()))
                    { ApplyFinish(material, finish); stage.TrackResourceEdit(material); stage.QueuePreviewChange(HairPreviewChange.Materials); }
            EditorGUI.BeginChangeCheck();
            float primary = EditorGUILayout.Slider("Primary shine", material.GetFloat("_SpecularStrength"), 0f, 2f);
            float roughness = EditorGUILayout.Slider("Primary roughness", 1f - material.GetFloat("_Smoothness"), 0f, 1f);
            float secondary = EditorGUILayout.Slider("Secondary shine", material.GetFloat("_SecondaryStrength"), 0f, 2f);
            float secondaryRoughness = EditorGUILayout.Slider("Secondary roughness", 1f - material.GetFloat("_SecondarySmoothness"), 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(material, "Edit Hair Finish");
                material.SetFloat("_SpecularStrength", primary); material.SetFloat("_Smoothness", 1f - roughness);
                material.SetFloat("_SecondaryStrength", secondary); material.SetFloat("_SecondarySmoothness", 1f - secondaryRoughness);
                EditorUtility.SetDirty(material); stage.TrackResourceEdit(material); stage.QueuePreviewChange(HairPreviewChange.Materials);
            }
            EditorGUILayout.LabelField("Higher roughness broadens highlights. Reduce both shine strengths for a dry, matte finish; zero removes both highlights. Full lighting controls remain in the material Inspector.", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            bool softCoverage = EditorGUILayout.Toggle("Soft coverage (dithered)", material.GetFloat("_DitheredOpacity") > .5f);
            float coverage = material.GetFloat("_Coverage");
            using (new EditorGUI.DisabledScope(!softCoverage)) coverage = EditorGUILayout.Slider("Strand opacity", coverage, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(material, "Edit Hair Coverage"); material.SetFloat("_DitheredOpacity", softCoverage ? 1 : 0); material.SetFloat("_Coverage", coverage);
                EditorUtility.SetDirty(material); stage.TrackResourceEdit(material); stage.QueuePreviewChange(HairPreviewChange.Materials);
            }
            if (softCoverage) EditorGUILayout.HelpBox("Soft coverage keeps depth writing and breaks up solid-looking cards. It uses fine screen-door stippling, not sorted transparency. Review motion and distance with your project's antialiasing; disable for clean MSAA cutouts if stippling is visible.", MessageType.Info);
            if (atlas.albedo != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(atlas.albedo)) is TextureImporter importer && !importer.mipmapEnabled)
            {
                EditorGUILayout.HelpBox("This atlas has no mipmaps. Fine strands can sparkle at a distance. Alpha-preserving mipmaps improve stability.", MessageType.Warning);
                if (GUILayout.Button("Enable Alpha-Preserving Mipmaps…") && EditorUtility.DisplayDialog("Update Shared Atlas Import?",
                    "Enable mipmaps, preserve alpha coverage at this cutoff, and use 8× anisotropic filtering? This changes the texture import wherever the atlas is used; its image pixels are not edited.", "Update Import", "Cancel"))
                {
                    Undo.RecordObject(importer,"Optimize Hair Atlas Import"); importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;
                    importer.alphaTestReferenceValue=material.GetFloat("_Cutoff"); importer.anisoLevel=8;importer.SaveAndReimport();
                    stage.QueuePreviewChange(HairPreviewChange.Materials);
                }
            }
            if (group.profile?.DoubleSided == true || atlas.secondPassMaterial != null)
                EditorGUILayout.HelpBox("This shader already shades both sides in one pass. Duplicate backface geometry / a second material pass increases cost; keep them only for a deliberate extra effect.", MessageType.Warning);
        }
    }
}
