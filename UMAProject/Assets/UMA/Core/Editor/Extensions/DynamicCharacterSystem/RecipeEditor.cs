#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.Integrations;

namespace UMA.Editors
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

		GameObject generatedContext;

		EditorWindow inspectorWindow;

		//for showing a warning if any of the compatible races are missing or not assigned to bundles or the index
		protected Texture warningIcon;
		protected GUIStyle warningStyle;

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

				var newSelection = new List<UnityEngine.Object>(DragAndDrop.objectReferences.Length);
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

        public override void OnEnable()
        {
            base.OnEnable();

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
					var context = UMAContextBase.Instance;
					//create a virtual UMAContextBase if we dont have one and we have DCS
					if (context == null || context.gameObject.name == "UMAEditorContext")
					{
						context = umaRecipeBase.CreateEditorContext();//will create or update an UMAEditorContext to the latest version
						generatedContext = context.gameObject.transform.parent.gameObject;//The UMAContextBase in a UMAEditorContext is that gameobject's child
					}
					//legacy checks for context
					if (context == null)
					{
						_errorMessage = "Editing a recipe requires a loaded scene with a valid UMAContextBase.";
                        Debug.LogWarning(_errorMessage);
						//_recipe = null;
						//return;
					}
                    umaRecipeBase.Load(_recipe, context);
                    _description = umaRecipeBase.GetInfo();
                }
            }
			catch (UMAResourceNotFoundException e)
            {
                _errorMessage = e.Message;
            }

            dnaEditor = new DNAMasterEditor(_recipe);
            slotEditor = new SlotMasterEditor(_recipe);

            _rebuildOnLayout = true;
        }
		
		public void OnDestroy()
		{
			if (generatedContext != null)
			{
				//Ensure UMAContextBase.Instance is set to null
				UMAContextBase.Instance = null;
				DestroyImmediate(generatedContext);
			}
		}

        public override void OnInspectorGUI()
        {
			if (warningIcon == null)
			{
				warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
				warningStyle = new GUIStyle(EditorStyles.label);
                warningStyle.fixedHeight = warningIcon.height + 4f;
				warningStyle.contentOffset = new Vector2(0, -2f);
			}
			if (_recipe == null) return;
            PowerToolsGUI();
            base.OnInspectorGUI();
		}

        protected override void DoUpdate()
        {
            var recipeBase = (UMARecipeBase)target;
            recipeBase.Save(_recipe, UMAContextBase.Instance);
            EditorUtility.SetDirty(recipeBase);
            AssetDatabase.SaveAssets();
            // AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(recipeBase));
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

		/// <summary>
		/// Checks if the given RaceData is in the globalLibrary or an assetBundle
		/// </summary>
		/// <param name="_raceData"></param>
		/// <returns></returns>
		protected bool RaceInIndex(RaceData _raceData)
		{
			if (UMAContextBase.Instance != null)
			{
				if (UMAContextBase.Instance.HasRace(_raceData.raceName) != null)
					return true;
			}

			AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<RaceData>(_raceData.raceName);
			if (ai != null)
			{
				return true;
			}

			return false;
		}
	}
	/*public class ShowGatheringNotification : EditorWindow
	{

		string notification  = "UMA is gathering Data";

		void OnGUI() {
			this.ShowNotification(new GUIContent(notification));
		}
	}*/
}
#endif
