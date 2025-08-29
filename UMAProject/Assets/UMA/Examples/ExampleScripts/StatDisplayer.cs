using JetBrains.Annotations;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;

public class StatDisplayer : MonoBehaviour
{
    private UMAGenerator umaGenerator = null;

    // Styles for bold text and shadow
    private GUIStyle _boldStyle;
    private GUIStyle _shadowStyle;
    private int _lastBaseSize = -1;

    private void EnsureStyles()
    {
        // Derive from current skin and enlarge by 20%
        int baseSize = (GUI.skin != null && GUI.skin.label != null && GUI.skin.label.fontSize > 0)
            ? GUI.skin.label.fontSize
            : 12;

        if (_boldStyle == null || _lastBaseSize != baseSize)
        {
            int targetSize = Mathf.RoundToInt(baseSize * 1.5f);
            _lastBaseSize = baseSize;

            _boldStyle = new GUIStyle(GUI.skin.label)
            {                
                fontStyle = FontStyle.Bold,
                fontSize = targetSize
            };
            _boldStyle.normal.textColor = Color.white;

            _shadowStyle = new GUIStyle(_boldStyle);
            _shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 1f);
            _shadowStyle.contentOffset = Vector2.zero; // offset handled via rect
        }
    }

    private void ShadowLabel(string text)
    {
        // Draw shadow using layout to allocate a line
        GUILayout.Label(text, _shadowStyle);
        Rect r = GUILayoutUtility.GetLastRect();

        // Draw the main text 2px to the right and 2px up
        r.x += 2f;
        r.y -= 2f;
        GUI.Label(r, text, _boldStyle);
    }

    private void OnGUI()
    {
        EnsureStyles();

        if (umaGenerator == null)
        {
            umaGenerator = (UMAAssetIndexer.Instance != null) ? UMAAssetIndexer.Instance.Generator : null;
        }

        if (umaGenerator != null)
        {
            ShadowLabel("  Generation Metrics");
            long elapsedMs = umaGenerator.ElapsedTicks / 10000; // ticks -> ms
            ShadowLabel($"  Elapsed Time: {elapsedMs} ms");
            ShadowLabel($"  Pending UMAs: {umaGenerator.pendingUmas}");
            ShadowLabel($"  Shape Dirty: {umaGenerator.DnaChanged}");
            ShadowLabel($"  Texture Dirty: {umaGenerator.TextureChanged}");
            ShadowLabel($"  Mesh Dirty: {umaGenerator.SlotsChanged}");

            ShadowLabel($"  Validation Ticks : {umaGenerator.validationTicks/10000}");
            ShadowLabel($"  Mesh pre process Ticks: {umaGenerator.meshpreprocessTicks / 10000}");
            ShadowLabel($"  Begun Events Ticks: {umaGenerator.BegunEventsTicks / 10000}");
            ShadowLabel($"  Pre Apply Ticks: {umaGenerator.preapplyTicks / 10000}");
            ShadowLabel($"  Texture Process Ticks: {umaGenerator.textureprocessingTicks / 10000}");
            ShadowLabel($"  Mesh Update Ticks: {umaGenerator.meshUpdatesTicks / 10000}");
            ShadowLabel($"  Skeleton Update Ticks: {umaGenerator.skeletonUpdatesTicks / 10000}");
            ShadowLabel($"  Race Blendshapes Ticks: {umaGenerator.raceblendshapesTicks / 10000}");
            ShadowLabel($"  End Events Ticks: {umaGenerator.endEventsTicks / 10000}");

            ShadowLabel($"  Average Texture: {umaGenerator.averageTextureProcessingTime}");
            ShadowLabel($"  Average Mesh: {umaGenerator.averageMeshUpdatesTime}");
            ShadowLabel($"  Average DNA: {umaGenerator.averageSkeletonUpdatesTime}");
            // --- UMA Mesh Combiner Timings ---
            GUILayout.Space(8);
            ShadowLabel("  Mesh Combiner Timings (ms):");

            double freq = (double)System.Diagnostics.Stopwatch.Frequency / 1000.0; // ticks to ms

            void ShowMeshCombinerTiming(string label, long ticks)
            {
                ShadowLabel($"    {label,-28} : {ticks / freq:F2}");
            }

            //public static long Ticks_BuildCombineInstances;
            //public static long Ticks_PerRendererTotal;
            //public static long Ticks_PerRendererMaterials;
            //public static long Ticks_LegacyUV;
            //Ticks_SkeletonEnsure = 0;
            //Ticks_ClearDNA = 0;
            //Ticks_EnsureUMADataSetup = 0;
            //Ticks_BuildActiveModifiers = 0;
            GUILayout.Space(4);
            ShowMeshCombinerTiming("BuildCharacter", DynamicCharacterAvatar.Ticks_BuildCharacter);
            ShowMeshCombinerTiming("LoadCharacter", DynamicCharacterAvatar.Ticks_LoadCharacter);
            
            GUILayout.Space(4);
                  /*  public static long Ticks_LoadCharacter = 0;
    public static long Ticks_BuildCharacter = 0;
    public static long Ticks_InitializeBuild = 0;
    public static long Ticks_Phase1 = 0;
    public static long Ticks_Phase2 = 0;
    public static long Ticks_Phase3 = 0;
    public static long Ticks_Phase4 = 0;
    public static long Ticks_LoadPhase1 = 0;
    public static long Ticks_LoadPhase2 = 0;
    public static long Ticks_LoadPhase3 = 0;
    public static long Ticks_LoadPhase4 = 0; */
            ShowMeshCombinerTiming("InitializeBuild", DynamicCharacterAvatar.Ticks_InitializeBuild);
            ShowMeshCombinerTiming("Build phase 1", DynamicCharacterAvatar.Ticks_Phase1);
            ShowMeshCombinerTiming("Build phase 2", DynamicCharacterAvatar.Ticks_Phase2);
            ShowMeshCombinerTiming("Build phase 3", DynamicCharacterAvatar.Ticks_Phase3);
            ShowMeshCombinerTiming("Build phase 4", DynamicCharacterAvatar.Ticks_Phase4);
            ShowMeshCombinerTiming("Load phase 1", DynamicCharacterAvatar.Ticks_LoadPhase1);
            ShowMeshCombinerTiming("Load phase 2", DynamicCharacterAvatar.Ticks_LoadPhase2);
            ShowMeshCombinerTiming("Load phase 3", DynamicCharacterAvatar.Ticks_LoadPhase3);
            ShowMeshCombinerTiming("Load phase 4", DynamicCharacterAvatar.Ticks_LoadPhase4);
            ShowMeshCombinerTiming("Recipe Load", UMAPackedRecipeBase.Ticks_Load);




            GUILayout.Space(4);
            ShowMeshCombinerTiming("BuildCombineInstances", UMADefaultMeshCombiner.Ticks_BuildCombineInstances);
            ShowMeshCombinerTiming("PerRendererTotal", UMADefaultMeshCombiner.Ticks_PerRendererTotal);
            ShowMeshCombinerTiming("PerRendererMaterials", UMADefaultMeshCombiner.Ticks_PerRendererMaterials);
            ShowMeshCombinerTiming("LegacyUV", UMADefaultMeshCombiner.Ticks_LegacyUV);
            ShowMeshCombinerTiming("SkeletonEnsure", UMADefaultMeshCombiner.Ticks_SkeletonEnsure);
            ShowMeshCombinerTiming("ClearDNA", UMADefaultMeshCombiner.Ticks_ClearDNA);
            ShowMeshCombinerTiming("EnsureUMADataSetup", UMADefaultMeshCombiner.Ticks_EnsureUMADataSetup);
            ShowMeshCombinerTiming("BuildActiveModifiers", UMADefaultMeshCombiner.Ticks_BuildActiveModifiers);


#if SHOW_SKINNEDMESHCOMBER
            ShowMeshCombinerTiming("CombineInternalTotal", UMA.SkinnedMeshCombinerMeshAPI.Ticks_CombineInternalTotal);
            ShowMeshCombinerTiming("AnalyzeSources", UMA.SkinnedMeshCombinerMeshAPI.Ticks_AnalyzeSources);
            ShowMeshCombinerTiming("AnalyzeBlendshapes", UMA.SkinnedMeshCombinerMeshAPI.Ticks_AnalyzeBlendshapes);
            ShowMeshCombinerTiming("AllocateMeshData", UMA.SkinnedMeshCombinerMeshAPI.Ticks_AllocateMeshData);
            ShowMeshCombinerTiming("MergeTransforms", UMA.SkinnedMeshCombinerMeshAPI.Ticks_MergeTransforms);
            ShowMeshCombinerTiming("EnsureSkeleton", UMA.SkinnedMeshCombinerMeshAPI.Ticks_EnsureSkeleton);
            ShowMeshCombinerTiming("BuildBoneWeights", UMA.SkinnedMeshCombinerMeshAPI.Ticks_BuildBoneWeights);
            ShowMeshCombinerTiming("CopyPositionsAndBounds", UMA.SkinnedMeshCombinerMeshAPI.Ticks_CopyPositionsAndBounds);
            ShowMeshCombinerTiming("PackNormalsTangents", UMA.SkinnedMeshCombinerMeshAPI.Ticks_PackNormalsTangents);
            ShowMeshCombinerTiming("PackColUV01", UMA.SkinnedMeshCombinerMeshAPI.Ticks_PackColUV01);
            ShowMeshCombinerTiming("PackUV23", UMA.SkinnedMeshCombinerMeshAPI.Ticks_PackUV23);
            ShowMeshCombinerTiming("IndexJobsSchedule", UMA.SkinnedMeshCombinerMeshAPI.Ticks_IndexJobsSchedule);
            ShowMeshCombinerTiming("IndexJobsComplete", UMA.SkinnedMeshCombinerMeshAPI.Ticks_IndexJobsComplete);
            ShowMeshCombinerTiming("UVRemap", UMA.SkinnedMeshCombinerMeshAPI.Ticks_UVRemap);
            ShowMeshCombinerTiming("SetSubmeshes", UMA.SkinnedMeshCombinerMeshAPI.Ticks_SetSubmeshes);
            ShowMeshCombinerTiming("ApplyMeshData", UMA.SkinnedMeshCombinerMeshAPI.Ticks_ApplyMeshData);
            ShowMeshCombinerTiming("SetBindposesAndWeights", UMA.SkinnedMeshCombinerMeshAPI.Ticks_SetBindposesAndWeights);
            ShowMeshCombinerTiming("AssignBones", UMA.SkinnedMeshCombinerMeshAPI.Ticks_AssignBones);
            ShowMeshCombinerTiming("BuildCloth", UMA.SkinnedMeshCombinerMeshAPI.Ticks_BuildCloth);
#endif
            if (umaGenerator.convertRenderTexture)
            {
                GUILayout.Space(8);
                ShadowLabel("Texture Metrics");
                ShadowLabel($"Textures Processed: {umaGenerator.TexturesProcessed}");
                ShadowLabel($"Copies Enqueued: {RenderTexToCPU.copiesEnqueued}");
                ShadowLabel($"Copies Dequeued: {RenderTexToCPU.copiesDequeued}");
                ShadowLabel($"Unable to Queue: {RenderTexToCPU.unableToQueue}");
                ShadowLabel($"Missed Uploads: {RenderTexToCPU.misseduploads}");
                ShadowLabel($"Error Uploads: {RenderTexToCPU.errorUploads}");
                ShadowLabel($"Textures Uploaded: {RenderTexToCPU.texturesUploaded}");

                GUILayout.Space(8);
                ShadowLabel("RenderTextures Cleaned");
                ShadowLabel($"UMAData Cleanup: {RenderTexToCPU.renderTexturesCleanedUMAData}");
                ShadowLabel($"Applied Cleanup: {RenderTexToCPU.renderTexturesCleanedApplied}");
                ShadowLabel($"Not Applied Cleanup: {RenderTexToCPU.renderTexturesCleanedMissed}");
                int totalCleanup = RenderTexToCPU.renderTexturesCleanedUMAData
                                 + RenderTexToCPU.renderTexturesCleanedApplied
                                 + RenderTexToCPU.renderTexturesCleanedMissed;
                ShadowLabel($"Total Cleanup: {totalCleanup}");
            }
        }
        else
        {
            ShadowLabel("  UMA Generator not found.");
        }
    }
}
