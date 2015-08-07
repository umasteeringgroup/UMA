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
	/// <summary>
	/// Recipe editor.
	/// Class is marked partial so developers can add their own functionality to edit new properties added to 
	/// UMATextRecipe without changing code delivered with UMA.
	/// </summary>
	[CanEditMultipleObjects]
    [CustomEditor(typeof(UMARecipeBase), true)]
    public partial class RecipeEditor : CharacterBaseEditor
    {
		List<GameObject> draggedObjs;
		public virtual void OnSceneDrag(SceneView view)
		{
			if (Event.current.type == EventType.DragUpdated)
			{
				if (Event.current.mousePosition.x < 0 || Event.current.mousePosition.x >= view.position.width ||
					Event.current.mousePosition.y < 0 || Event.current.mousePosition.y >= view.position.height) return;
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy; // show a drag-add icon on the mouse cursor
				Event.current.Use();
				return;
			}
			if (Event.current.type == EventType.DragPerform)
			{
				if (Event.current.mousePosition.x < 0 || Event.current.mousePosition.x >= view.position.width ||
					Event.current.mousePosition.y < 0 || Event.current.mousePosition.y >= view.position.height) return;

				Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
				RaycastHit hit;
				Vector3 position = Vector3.zero;
				if (Physics.Raycast(ray, out hit))
				{
					position = hit.point;
				}

				var newSelection = new List<Object>(DragAndDrop.objectReferences.Length);
				foreach (var reference in DragAndDrop.objectReferences)
				{
				    if (reference is UMARecipeBase)
				    {
						var avatarGO = CreateAvatar(reference as UMARecipeBase);
						avatarGO.GetComponent<Transform>().position = position;
						position.x = position.x + 1;
						newSelection.Add(avatarGO);
				    }
				}
				Selection.objects = newSelection.ToArray();
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy; // show a drag-add icon on the mouse cursor
				Event.current.Use();
			}
		}

		public virtual GameObject CreateAvatar(UMARecipeBase recipe)
		{
			var GO = new GameObject(recipe.name);
			var avatar = GO.AddComponent<UMADynamicAvatar>();
			avatar.umaRecipe = recipe;
			avatar.loadOnStart = true;
			return GO;
		}

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