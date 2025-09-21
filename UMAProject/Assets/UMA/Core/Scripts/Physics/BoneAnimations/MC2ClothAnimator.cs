using UnityEngine;
using UMA;
using System.Collections.Generic;
#if MAGICACLOTH2
using MagicaCloth2;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
/* 
 * This script requires Magica Cloth 2 script to be installed in the project.
 * Magica Cloth 2 is a third-party asset available on the Unity Asset Store.
 * To use this script, ensure you have the Magica Cloth 2 package imported into your Unity project.
 * and add the MAGICACLOTH2 define symbol in the Player Settings.
 */
namespace UMA
{
    public class MC2ClothAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/MC2ClothAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<MC2ClothAnimator>();
        }
#endif
        
        [Header("General Settings")]
        [SerializeField]
        [Tooltip("Provide the MC2 paint-map texture. Fixed(Red) Move(Green) Ignore(Black)")]
        private Texture2D MC_PaintMap;

        [SerializeField]
        [Tooltip("Add Magica CLoth2 preset file. If not set, the default settings will be used.")]
        private TextAsset presetFile;

        private GameObject MC_MeshGO;
        private string RendererName;
#if MAGICACLOTH2
        private MagicaCloth c_cloth;
#endif
        public override void Initialize(UMAData umaData, SlotData sd)
        {
            if (MC_PaintMap == null)
            {
                Debug.LogError("No paint-map set. Please set it in the inspector.");
                return;
            }

            if (sd.rendererAsset.RendererName != null) RendererName = sd.rendererAsset.RendererName;
            else
            {
                Debug.LogError("No Renderer found in the SlotData. Please assign a Renderer to the Slot");
                return;
            }

            base.Initialize(umaData, sd);

            AddMCCloth(umaData);

            initialized = true;
        }

        public void AddMCCloth(UMAData umaData)
        {
#if MAGICACLOTH2
        MC_MeshGO = umaData.gameObject.transform.Find(RendererName)?.gameObject;
        if (MC_MeshGO == null)
        {
            Debug.LogError($"Renderer '{RendererName}' not found in UMAData.");
            return;
        }

        // Check if the MagicaCloth component already exists
        c_cloth = MC_MeshGO.GetComponent<MagicaCloth>();
        if (c_cloth != null)
        {
            //We need to destroy the existing component and add a new one for properly initializing it.
            DestroyImmediate(c_cloth);
        }

        // Add MagicaCloth component to the GameObject
        Renderer skinnedRenderer = MC_MeshGO.GetComponent<Renderer>();
        c_cloth = MC_MeshGO.AddComponent<MagicaCloth>();
        var sdata = c_cloth.SerializeData;
        sdata.clothType = ClothProcess.ClothType.MeshCloth;
        sdata.sourceRenderers.Add(skinnedRenderer);

        // reduction settings
        sdata.reductionSetting.simpleDistance = 0.05f;
        sdata.reductionSetting.shapeDistance = 0.05f;

        // Set the paint map texture
        // *** Paintmap must have Read/Write attributes enabled! ***
        sdata.paintMode = ClothSerializeData.PaintMode.Texture_Fixed_Move;
        sdata.paintMaps.Add(MC_PaintMap);


        if (presetFile != null)
        {
            // If a preset file is provided, import the settings from it
            sdata.ImportJson(presetFile.text);
        }

        // Build MagicaCloth2
        c_cloth.BuildAndRun();
#endif
        }
    }
}