using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    /// <summary>The same material controls work in the Inspector and docked Hair Properties.</summary>
    public sealed class HairSweptShaderGUI : ShaderGUI
    {
        internal const string ShaderName = "UMA/Hair Cards/Swept Hair URP";
        internal const string SoftShaderName = "UMA/Hair Cards/Soft Hair URP";
        internal static bool IsHair(Material material) => material != null && material.shader != null &&
            (material.shader.name == ShaderName || material.shader.name == SoftShaderName);
        internal static bool IsHybrid(HairAtlasProfileAsset atlas) => atlas?.material != null && atlas.material.shader.name == ShaderName &&
            atlas.secondPassMaterial != null && atlas.secondPassMaterial.shader.name == SoftShaderName;
        public enum RenderingMode { Cutout, AlphaBlended, Hybrid }
        private bool lighting, advanced;
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            bool blended = ((Material)editor.target).shader.name == SoftShaderName;
            EditorGUILayout.HelpBox(blended ? "Lit alpha-blended hair. No hard color cutoff or depth writing. Overlapping transparent cards can sort imperfectly; use Hybrid in Hair Properties for depth-writing cores plus a soft fringe." :
                "Two-sided cutout hair. Lower cutoff or increase Alpha Density to retain faint fibers. Hybrid in Hair Properties adds a lit alpha-blended fringe. UV1/UV2 carry strand data; vertex RGBA remains available for animation.", MessageType.None);
            void Field(string name) { var p = FindProperty(name, properties, false); if (p != null) editor.ShaderProperty(p, p.displayName); }
            EditorGUILayout.LabelField("Atlas & color", EditorStyles.boldLabel);
            foreach (string name in new[] { "_BaseMap", "_BaseColor", "_RootColor", "_TipColor", "_RootFade", "_ColorPower", "_StrandVariation", "_ClumpVariation", "_AlphaDensity" }) Field(name);
            if (!blended) { Field("_Cutoff"); Field("_DitheredOpacity"); Field("_AlphaToCoverage"); }
            Field("_Coverage"); Field("_RootOpacityFade");
            EditorGUILayout.HelpBox("Root and tip color alpha control opacity. Their transition follows Root Color Reach and Root to Tip Curve. Root Opacity Fade adds a separate fade from zero at the scalp. Cutout clips; Alpha Blended and Hybrid retain soft fades.", MessageType.None);
            if (!blended) { Field("_HybridCore"); EditorGUILayout.LabelField("Enable Hybrid Core only with a Soft Hair second material pass. Hair Properties configures the pair automatically.", EditorStyles.wordWrappedMiniLabel); }
            lighting = EditorGUILayout.Foldout(lighting, "Highlights & lighting", true);
            if (lighting) foreach (string name in new[] { "_SpecularColor", "_SpecularStrength", "_Smoothness", "_SpecularShift", "_SecondaryColor", "_SecondaryStrength", "_SecondarySmoothness", "_SecondaryShift", "_Transmission", "_TransmissionColor", "_DiffuseWrap", "_AmbientStrength" }) Field(name);
            advanced = EditorGUILayout.Foldout(advanced, "Texture channels & diagnostics", true);
            if (advanced)
            {
                foreach (string name in new[] { "_TextureColor", "_DepthInfluence", "_DepthMap", "_UseDepthMap", "_OcclusionMap", "_OcclusionStrength", "_BumpMap", "_BumpScale", "_ShadowCutoff", "_VertexColorInfluence", "_VertexAlphaInfluence", "_DebugView" }) Field(name);
                EditorGUILayout.HelpBox("Depth shading reads atlas red, or the optional separate depth map using the same UVs. Set depth influence to 0 for ordinary color-only shading. Both shaders shade both faces; duplicate backface geometry is unnecessary. Hybrid deliberately adds a transparent second material pass.", MessageType.Info);
                editor.EnableInstancingField(); editor.RenderQueueField();
            }
        }

        internal enum Finish { Matte, Natural, Glossy }
        internal static void ApplyFinish(Material material, Finish finish)
        {
            if (!IsHair(material)) return;
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

        internal static void ApplyRendering(HairGroomAsset groom, HairAtlasProfileAsset atlas, RenderingMode mode, string resourceFolder = null)
        {
            if (groom == null || atlas == null || !IsHair(atlas.material)) return;
            var solidShader = Shader.Find(ShaderName); var softShader = Shader.Find(SoftShaderName);
            if (solidShader == null || softShader == null) return;
            Undo.RecordObject(atlas, "Change Hair Rendering");
            var first = new Material(mode == RenderingMode.AlphaBlended ? softShader : solidShader);
            first.CopyPropertiesFromMaterial(atlas.material);
            first.name = groom.name + (mode == RenderingMode.AlphaBlended ? " Blended Hair" : " Cutout Hair");
            first.renderQueue = mode == RenderingMode.AlphaBlended ? 3000 : 2450;
            first.SetFloat("_DitheredOpacity", 0); first.SetShaderPassEnabled("ShadowCaster", true);
            // Hybrid uses a deterministic hard core; MSAA should not remove that core again.
            first.SetFloat("_AlphaToCoverage", mode == RenderingMode.Cutout ? 1 : 0);
            first.SetFloat("_HybridCore", mode == RenderingMode.Hybrid ? 1 : 0);
            HairGenerationEditor.SaveResourceNear(groom, first, resourceFolder);
            atlas.material = first; atlas.secondPassMaterial = null;
            if (mode == RenderingMode.Hybrid)
            {
                var fringe = new Material(softShader) { name = groom.name + " Soft Fringe" };
                fringe.CopyPropertiesFromMaterial(first); fringe.renderQueue = 3000;
                fringe.SetFloat("_AlphaToCoverage", 0); fringe.SetFloat("_DitheredOpacity", 0);
                fringe.SetFloat("_HybridCore", 0);
                fringe.SetShaderPassEnabled("ShadowCaster", false); // primary already casts the silhouette
                HairGenerationEditor.SaveResourceNear(groom, fringe, resourceFolder); atlas.secondPassMaterial = fringe;
            }
            EditorUtility.SetDirty(atlas);
            HairGroomCommands.Commit(groom, HairPreviewChange.Geometry | HairPreviewChange.Materials);
        }

        internal static void SynchronizeHybrid(HairAtlasProfileAsset atlas)
        {
            if (!IsHybrid(atlas)) return;
            Undo.RecordObject(atlas.material, "Enable Soft Hybrid Roots");
            atlas.material.SetFloat("_HybridCore", 1);
            atlas.material.SetFloat("_AlphaToCoverage", 0);
            EditorUtility.SetDirty(atlas.material);
            Undo.RecordObject(atlas.secondPassMaterial, "Sync Hair Fringe");
            atlas.secondPassMaterial.CopyPropertiesFromMaterial(atlas.material);
            atlas.secondPassMaterial.renderQueue = 3000;
            atlas.secondPassMaterial.SetFloat("_AlphaToCoverage", 0); atlas.secondPassMaterial.SetFloat("_DitheredOpacity", 0);
            atlas.secondPassMaterial.SetFloat("_HybridCore", 0);
            atlas.secondPassMaterial.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(atlas.secondPassMaterial);
        }

        internal static void DrawInline(HairCardStage stage, HairGroup group)
        {
            var atlas = group.atlas; if (atlas == null) return;
            if (!IsHair(atlas.material))
            {
                using (new EditorGUI.DisabledScope(Shader.Find(ShaderName) == null))
                if (GUILayout.Button("Create Swept Hair URP Material", GUILayout.Height(26)))
                {
                    if (atlas.material == null || EditorUtility.DisplayDialog("Assign New Hair Material?", "Create and assign a new two-sided URP hair material? The previous material asset is preserved. Undo restores the assignment.", "Create & Assign", "Cancel"))
                    { CreateMaterial(stage.Groom, atlas); stage.TrackResourceEdit(atlas); stage.QueuePreviewChange(HairPreviewChange.Materials); }
                }
                return;
            }
            EditorGUILayout.LabelField("Strand rendering", EditorStyles.boldLabel);
            var mode = IsHybrid(atlas) ? RenderingMode.Hybrid : atlas.material.shader.name == SoftShaderName ? RenderingMode.AlphaBlended : RenderingMode.Cutout;
            EditorGUILayout.LabelField("Current: " + ObjectNames.NicifyVariableName(mode.ToString()), EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
                foreach (RenderingMode choice in System.Enum.GetValues(typeof(RenderingMode)))
                    using (new EditorGUI.DisabledScope(choice == mode))
                        if (GUILayout.Button(choice == RenderingMode.AlphaBlended ? "Alpha Blended" : choice.ToString()) &&
                            EditorUtility.DisplayDialog("Change Hair Rendering?", "Create and assign a " + choice + " material setup, preserving the current colors and textures? Existing materials are kept. Hybrid uses a depth-writing core plus a transparent fringe and costs an extra draw. Undo restores the assignment.", "Apply", "Cancel"))
                        { ApplyRendering(stage.Groom, atlas, choice); stage.TrackResourceEdit(atlas); }
            if (mode == RenderingMode.Hybrid && GUILayout.Button("Sync fringe from first pass"))
            { SynchronizeHybrid(atlas); stage.TrackResourceEdit(atlas.material); stage.TrackResourceEdit(atlas.secondPassMaterial); stage.QueuePreviewChange(HairPreviewChange.Materials); }
            if (mode == RenderingMode.Hybrid && atlas.material.GetFloat("_HybridCore") < .5f)
                EditorGUILayout.HelpBox("Preview already uses soft Hybrid roots. Use Sync fringe from first pass to save this setup into the materials for baked/exported hair too. Your colors and textures are retained.", MessageType.Info);
            EditorGUILayout.HelpBox("Cutout is fastest. Alpha Blended retains soft strands but can show sorting artifacts. Hybrid reduces those artifacts with depth-writing cores; it does not require MSAA. Inline color/finish edits sync the fringe; after editing a material directly, use Sync fringe.", MessageType.None);
            var material = atlas.material;
            int beforeEdit = EditorUtility.GetDirtyCount(material);
            EditorGUILayout.LabelField("Hair color", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Color root = EditorGUILayout.ColorField(new GUIContent("Roots (RGBA)", "RGB = root color; A = root opacity. 0 is transparent, 1 is opaque."), material.GetColor("_RootColor"), true, true, false);
            Color tip = EditorGUILayout.ColorField(new GUIContent("Tips (RGBA)", "RGB = tip color; A = tip opacity. Blends from the root using Root color reach."), material.GetColor("_TipColor"), true, true, false);
            float reach = EditorGUILayout.Slider("Root color reach", material.GetFloat("_RootFade"), 0.01f, 1f);
            EditorGUILayout.LabelField("Root color reach blends both color and alpha. Use Root opacity fade below for a separate scalp fade.", EditorStyles.wordWrappedMiniLabel);
            float variation = EditorGUILayout.Slider("Strand variation", material.GetFloat("_StrandVariation"), 0f, 1f);
            float density = EditorGUILayout.Slider(new GUIContent("Alpha density", "Boost faint atlas strands without filling fully transparent pixels. 1 leaves the atlas alpha unchanged."), material.GetFloat("_AlphaDensity"), .25f, 4f);
            float rootOpacityFade = EditorGUILayout.Slider(new GUIContent("Root opacity fade", "Fade the first fraction of each card into the scalp (0 = disabled). Try 0.03–0.06 with Hybrid or Alpha Blended hair. Independent of atlas UV direction and vertex alpha used for animation."), material.GetFloat("_RootOpacityFade"), 0f, .2f);
            float cutoff = material.GetFloat("_Cutoff");
            if (material.shader.name != SoftShaderName) cutoff = EditorGUILayout.Slider(mode == RenderingMode.Hybrid ? "Opaque core cutoff" : "Alpha cutoff", cutoff, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(material, "Edit Hair Color"); material.SetColor("_RootColor", root); material.SetColor("_TipColor", tip);
                material.SetFloat("_RootFade", reach); material.SetFloat("_StrandVariation", variation); material.SetFloat("_Cutoff", cutoff);
                material.SetFloat("_AlphaDensity", density);
                material.SetFloat("_RootOpacityFade", rootOpacityFade);
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
            bool blended = material.shader.name == SoftShaderName;
            bool softCoverage = !blended && EditorGUILayout.Toggle("Soft coverage (dithered)", material.GetFloat("_DitheredOpacity") > .5f);
            float coverage = material.GetFloat("_Coverage");
            using (new EditorGUI.DisabledScope(!softCoverage && !blended && mode != RenderingMode.Hybrid)) coverage = EditorGUILayout.Slider("Strand opacity", coverage, 0f, 1f);
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
            if (EditorUtility.GetDirtyCount(material) != beforeEdit && IsHybrid(atlas))
            { SynchronizeHybrid(atlas); stage.TrackResourceEdit(atlas.secondPassMaterial); }
            if (group.profile?.DoubleSided == true || (atlas.secondPassMaterial != null && !IsHybrid(atlas)))
                EditorGUILayout.HelpBox("This shader already shades both sides in one pass. Duplicate backface geometry / a second material pass increases cost; keep them only for a deliberate extra effect.", MessageType.Warning);
        }
    }
}
