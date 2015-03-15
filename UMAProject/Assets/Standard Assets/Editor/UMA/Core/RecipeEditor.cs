#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;
using UMA;
using UMA.Integrations;

namespace UMAEditor
{
    [CustomEditor(typeof(UMARecipeBase), true)]
    public class RecipeEditor : CharacterBaseEditor
    {
        public void OnEnable()
        {
            if (!NeedsReenable())
                return;

            _errorMessage = null;
            _recipe = new UMAData.UMARecipe();
            showBaseEditor = false;

            try
            {
                var umaRecipeBase = target as UMARecipeBase;
                if (umaRecipeBase != null)
                {
					var context = UMAContext.FindInstance() ;
					if (context == null)
					{
						_recipe = null;
						return;
					}

                    umaRecipeBase.Load(_recipe, context);
                    _description = umaRecipeBase.GetInfo();
                }
            } catch (UMAResourceNotFoundException e)
            {
                _errorMessage = e.Message;
            }

            dnaEditor = new DNAMasterEditor(_recipe);
            slotEditor = new SlotMasterEditor(_recipe);

            _rebuildOnLayout = true;
        }

        public override void OnInspectorGUI()
        {
			if (_recipe == null) return;
            PowerToolsGUI();
            base.OnInspectorGUI();
        }

        protected override void DoUpdate()
        {
            var recipeBase = (UMARecipeBase)target;
            recipeBase.Save(_recipe, UMAContext.FindInstance());
            EditorUtility.SetDirty(recipeBase);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(recipeBase));
            _rebuildOnLayout = true;

            _needsUpdate = false;
            if (PowerToolsIntegration.HasPreview(recipeBase))
            {
                PowerToolsIntegration.Refresh(recipeBase);
            }
            //else
            //{
            //    PowerToolsIntegration.Show(recipeBase);
            //}
        }

        protected override void Rebuild()
        {
            base.Rebuild();
            var recipeBase = target as UMARecipeBase;
            if (PowerToolsIntegration.HasPowerTools() && PowerToolsIntegration.HasPreview(recipeBase))
            {
                _needsUpdate = true;
            }
        }

        private void PowerToolsGUI()
        {
            if (PowerToolsIntegration.HasPowerTools())
            {
                GUILayout.BeginHorizontal();
                var recipeBase = target as UMARecipeBase;
                if (PowerToolsIntegration.HasPreview(recipeBase))
                {
                    if (GUILayout.Button("Hide"))
                    {
                        PowerToolsIntegration.Hide(recipeBase);
                    }
                    if (GUILayout.Button("Create Prefab"))
                    {
                        //PowerToolsIntegration.CreatePrefab(recipeBase);
                    }
                    if (GUILayout.Button("Hide All"))
                    {
                        PowerToolsIntegration.HideAll();
                    }
                } else
                {
                    if (GUILayout.Button("Show"))
                    {
                        PowerToolsIntegration.Show(recipeBase);
                    }
                    if (GUILayout.Button("Create Prefab"))
                    {
                        //PowerToolsIntegration.CreatePrefab(recipeBase);
                    }
                    if (GUILayout.Button("Hide All"))
                    {
                        PowerToolsIntegration.HideAll();
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
#endif