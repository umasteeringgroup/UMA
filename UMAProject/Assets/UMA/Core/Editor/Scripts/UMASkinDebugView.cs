using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.Editors
{
    /// <summary>
    /// What the "UMA/URP/Realistic Skin SSS Debug" shader shows. The values are the SKIN_DEBUG_* numbers in
    /// UMA3SkinSSSInput.hlsl (the name in upper case with underscores), and the shader's Show property uses this enum.
    /// </summary>
    public enum UMASkinDebugMap
    {
        Lit = 0,
        AlbedoMap = 1,
        NormalMap = 2,
        MaskScattering = 3,
        MaskOcclusion = 4,
        MaskDetailCoverage = 5,
        MaskSmoothness = 6,
        TintProtection = 7,
        DetailAlbedo = 8,
        DetailNormal = 9,
        DetailSmoothness = 10,
        ControlMap = 11,
        ControlPores = 12,
        FinalAlbedo = 13,
        FinalSmoothness = 14,
        ShadingNormal = 15,
        VertexNormal = 16,
        VertexTangent = 17,
        UVChecker = 18,
        DetailUVChecker = 19,
        VertexColor = 20,
        LitWithoutNormalMap = 21,
        LitWithoutDetail = 22,
        LitWithoutOcclusion = 23,
        LitWithoutSmoothnessMap = 24,
        DiffuseLight = 25,
        Reflection = 26,
        Transmission = 27,
        Curvature = 28,
        Thickness = 29,
        DiffuseNormal = 30
    }

    /// <summary>
    /// Switches the skin in the open scenes between normal rendering and the debug shader's views.
    /// While a view other than <see cref="UMASkinDebugMap.Lit"/> is active, every material instance in the scenes
    /// that uses "UMA/URP/Realistic Skin SSS" is put on the debug shader, including the ones characters get when
    /// they are rebuilt. Choosing Lit puts them back. Material assets are never changed, the production shader is
    /// not involved, and nothing here exists in a player.
    /// </summary>
    [InitializeOnLoad]
    public static class UMASkinDebugView
    {
        public const string DebugShaderName = "UMA/URP/Realistic Skin SSS Debug";
        public const string ViewProperty = "_SkinDebugMap";
        private const string SessionKey = "UMA.SkinDebugView.Current";
        private const double RefreshSeconds = 0.5;

        private static readonly List<Material> materials = new List<Material>();
        private static double nextRefresh;
        private static bool restoreAfterSave;

        /// <summary>Skin materials on the debug shader after the last <see cref="Apply"/>.</summary>
        public static int ActiveMaterialCount { get; private set; }

        /// <summary>Material assets that use the production shader directly in a scene. They are left alone.</summary>
        public static int SkippedAssetCount { get; private set; }

        static UMASkinDebugView()
        {
            EditorApplication.update += Refresh;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        /// <summary>The active view. It lasts for the editor session and is not saved with anything.</summary>
        public static UMASkinDebugMap Current
        {
            get { return (UMASkinDebugMap)SessionState.GetInt(SessionKey, 0); }
            set
            {
                SessionState.SetInt(SessionKey, (int)value);
                Apply(value);
                SceneView.RepaintAll();
            }
        }

        /// <summary>Menu path of a view, grouped the way the toolbar shows it.</summary>
        public static string GetMenuPath(UMASkinDebugMap view)
        {
            switch (view)
            {
                case UMASkinDebugMap.Lit: return "Lit (normal rendering)";
                case UMASkinDebugMap.AlbedoMap: return "Maps/Albedo";
                case UMASkinDebugMap.NormalMap: return "Maps/Normal";
                case UMASkinDebugMap.MaskScattering: return "Maps/Mask R - Scattering";
                case UMASkinDebugMap.MaskOcclusion: return "Maps/Mask G - Occlusion";
                case UMASkinDebugMap.MaskDetailCoverage: return "Maps/Mask B - Detail Coverage";
                case UMASkinDebugMap.MaskSmoothness: return "Maps/Mask A - Smoothness";
                case UMASkinDebugMap.TintProtection: return "Maps/Tint Protection";
                case UMASkinDebugMap.DetailAlbedo: return "Maps/Detail R - Albedo";
                case UMASkinDebugMap.DetailNormal: return "Maps/Detail AG - Normal";
                case UMASkinDebugMap.DetailSmoothness: return "Maps/Detail B - Smoothness";
                case UMASkinDebugMap.ControlMap: return "Maps/Control RGB - Diffusion, Oil, Transmission";
                case UMASkinDebugMap.ControlPores: return "Maps/Control A - Pores";
                case UMASkinDebugMap.FinalAlbedo: return "Result/Albedo After Tint and Detail";
                case UMASkinDebugMap.FinalSmoothness: return "Result/Smoothness";
                case UMASkinDebugMap.ShadingNormal: return "Result/Shading Normal";
                case UMASkinDebugMap.VertexNormal: return "Mesh/Vertex Normal";
                case UMASkinDebugMap.VertexTangent: return "Mesh/Vertex Tangent";
                case UMASkinDebugMap.UVChecker: return "Mesh/UV Checker";
                case UMASkinDebugMap.DetailUVChecker: return "Mesh/Detail UV Checker (one square per detail tile)";
                case UMASkinDebugMap.VertexColor: return "Mesh/Vertex Color";
                case UMASkinDebugMap.LitWithoutNormalMap: return "Lit Without/Normal Map";
                case UMASkinDebugMap.LitWithoutDetail: return "Lit Without/Detail Map";
                case UMASkinDebugMap.LitWithoutOcclusion: return "Lit Without/Occlusion";
                case UMASkinDebugMap.LitWithoutSmoothnessMap: return "Lit Without/Smoothness Map";
                case UMASkinDebugMap.DiffuseLight: return "Lighting/Diffuse Light";
                case UMASkinDebugMap.Reflection: return "Lighting/Reflection";
                case UMASkinDebugMap.Transmission: return "Lighting/Transmission";
                case UMASkinDebugMap.Curvature: return "Lighting/Curvature";
                case UMASkinDebugMap.Thickness: return "Lighting/Thickness";
                case UMASkinDebugMap.DiffuseNormal: return "Lighting/Diffuse Normal (red)";
                default: return view.ToString();
            }
        }

        /// <summary>Short name for the toolbar button.</summary>
        public static string GetShortName(UMASkinDebugMap view)
        {
            string path = GetMenuPath(view);
            int slash = path.LastIndexOf('/');
            string name = slash >= 0 ? path.Substring(slash + 1) : path;
            int bracket = name.IndexOf(" (", System.StringComparison.Ordinal);
            return bracket > 0 ? name.Substring(0, bracket) : name;
        }

        /// <summary>
        /// Puts the scenes' skin material instances on the debug shader with the given view, or back on the
        /// production shader for <see cref="UMASkinDebugMap.Lit"/>. Returns how many are on the debug shader.
        /// Does not change <see cref="Current"/>.
        /// </summary>
        public static int Apply(UMASkinDebugMap view)
        {
            ActiveMaterialCount = 0;
            SkippedAssetCount = 0;
            Shader production = Shader.Find(UMARealisticSkinUtility.SSSShaderName);
            Shader debug = Shader.Find(DebugShaderName);
            if (production == null || debug == null)
            {
                return 0;
            }

            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                renderer.GetSharedMaterials(materials);
                foreach (Material material in materials)
                {
                    if (material == null)
                    {
                        continue;
                    }
                    // An asset would be saved with the debug shader, and a build would then include it.
                    bool asset = EditorUtility.IsPersistent(material);
                    if (view != UMASkinDebugMap.Lit && material.shader == production)
                    {
                        if (asset)
                        {
                            SkippedAssetCount++;
                            continue;
                        }
                        material.shader = debug;
                    }
                    if (material.shader != debug || asset)
                    {
                        continue;
                    }
                    if (view == UMASkinDebugMap.Lit)
                    {
                        material.shader = production;
                    }
                    else
                    {
                        material.SetFloat(ViewProperty, (float)view);
                        ActiveMaterialCount++;
                    }
                }
            }
            materials.Clear();
            return ActiveMaterialCount;
        }

        // Rebuilt characters get new materials on the production shader; pick them up while a view is active.
        private static void Refresh()
        {
            if (EditorApplication.timeSinceStartup < nextRefresh)
            {
                return;
            }
            nextRefresh = EditorApplication.timeSinceStartup + RefreshSeconds;
            UMASkinDebugMap view = Current;
            if (view != UMASkinDebugMap.Lit)
            {
                Apply(view);
            }
        }

        // A scene can hold generated materials. It must never be written with the debug shader on them.
        private static void OnSceneSaving(Scene scene, string path)
        {
            if (Current == UMASkinDebugMap.Lit)
            {
                return;
            }
            Apply(UMASkinDebugMap.Lit);
            restoreAfterSave = true;
        }

        private static void OnSceneSaved(Scene scene)
        {
            if (!restoreAfterSave)
            {
                return;
            }
            restoreAfterSave = false;
            Apply(Current);
        }
    }
}
