#if UNITY_EDITOR
using System;
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
		private static List<IUMARecipePlugin> plugins;
		public static List<Type> GetRecipeEditorPlugins() {
			List<Type> theTypes = new List<Type>();

			var Assemblies = AppDomain.CurrentDomain.GetAssemblies();

			foreach(var asm in Assemblies) {

				try {
					var Types = asm.GetTypes();
					foreach(var t in Types) {
						if(typeof(IUMARecipePlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract) {
							theTypes.Add(t);
						}
					}
				} catch(Exception) {
					// This apparently blows up on some assemblies. 
				}
			}

			return theTypes;
			/*			return AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
							 .Where(x => typeof(IUMAAddressablePlugin).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract)
							 .Select(x => x).ToList();*/
		}
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

		void AddPlugins() {
			List<Type> PluginTypes = GetRecipeEditorPlugins();

			plugins = new List<IUMARecipePlugin>();
			foreach(Type t in PluginTypes) {
				plugins.Add((IUMARecipePlugin)Activator.CreateInstance(t));
			}
		}

        public override void OnEnable()
        {
			if(plugins == null) {
				AddPlugins();
			}

            base.OnEnable();

			foreach(IUMARecipePlugin plugin in plugins) {
				plugin.OnEnable();
			}

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
				//	if (context == null || context.gameObject.name == "UMAEditorContext")
				//	{
				//		context = umaRecipeBase.CreateEditorContext();//will create or update an UMAEditorContext to the latest version
				//		generatedContext = context.gameObject.transform.parent.gameObject;//The UMAContextBase in a UMAEditorContext is that gameobject's child
				//	}
					//legacy checks for context
					if (context != null)
					{
						umaRecipeBase.Load(_recipe, context);
						_description = umaRecipeBase.GetInfo();
					}
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
			foreach(IUMARecipePlugin plugin in plugins) {
				plugin.OnDestroy();
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

			foreach(IUMARecipePlugin plugin in plugins) 
			{
				string label = plugin.GetSectionLabel();
				plugin.foldOut = GUIHelper.FoldoutBar(plugin.foldOut, label);
				if(plugin.foldOut) {
					GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
					plugin.OnInspectorGUI(serializedObject);
					GUIHelper.EndVerticalPadded(10);
				}
			}

			if (UMAContext.Instance == null)
            {
				EditorGUILayout.HelpBox("A valid context was not found. This is required to be able to view and edit UMA recipes. You can add a Temporary context, and it will disappear when the scene or appdomain is reloaded, or you can add a permanent UMA_GLIB to the scene.", MessageType.Warning);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Add Permanent Context"))
                {
					var glib = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UMA/Getting Started/UMA_GLIB.prefab");
					if (glib != null)
					{
						glib.name = "UMA_GLIB";
						var g = (GameObject)PrefabUtility.InstantiatePrefab(glib);
					}
					else
					{
						EditorUtility.DisplayDialog("error", "Unable to find UMA_GLIB. Please add context manually.", "OK");
					}
				}
				if (GUILayout.Button("Add Temp Context"))
				{
					var glib = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UMA/Getting Started/UMA_GLIB.prefab");
					if (glib != null)
					{
						glib.name = "Temp Context (does not save)";
						var g = (GameObject)PrefabUtility.InstantiatePrefab(glib);
						g.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
						UMAContext.Instance = g.GetComponent<UMAGlobalContext>();
					}
					else
                    {
						EditorUtility.DisplayDialog("error", "Unable to find UMA_GLIB. Please add context manually.", "OK");
                    }
				}
				EditorGUILayout.EndHorizontal();
				return;
            }
            PowerToolsGUI();
            base.OnInspectorGUI();
		}

        protected override void DoUpdate()
        {
            _needsUpdate = false;
            var recipeBase = (UMARecipeBase)target;
            recipeBase.Save(_recipe, UMAContextBase.Instance);
            EditorUtility.SetDirty(recipeBase);
            AssetDatabase.SaveAssetIfDirty(recipeBase);
			_rebuildOnLayout = true;

            if (target is UMATextRecipe)
            {
                UMAUpdateProcessor.UpdateRecipe(target as UMATextRecipe);
            }
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
