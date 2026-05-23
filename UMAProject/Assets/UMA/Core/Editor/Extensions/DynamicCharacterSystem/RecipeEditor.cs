#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Collections;

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

		EditorWindow inspectorWindow;
		public bool Initialized = false;
		private bool baseEditorEnabled;
		private bool pluginsInitialized;
		private bool isDisposed;

		//for showing a warning if any of the compatible races are missing or not assigned to bundles or the index
		protected Texture warningIcon;
		protected GUIStyle warningStyle;
		private List<IUMARecipePlugin> plugins;
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
		}

		public virtual void OnSceneDrag(SceneView view, int index)
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
			avatar.serializedRecipe = recipe;
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

		private void QueueInitializeEditor()
		{
			EditorApplication.delayCall -= InitializeEditor;
			EditorApplication.delayCall += InitializeEditor;
		}

		private void UnqueueInitializeEditor()
		{
			EditorApplication.delayCall -= InitializeEditor;
		}

		private void DestroyPlugins()
		{
			if (!pluginsInitialized || plugins == null)
			{
				plugins = null;
				pluginsInitialized = false;
				return;
			}

			foreach (IUMARecipePlugin plugin in plugins)
			{
				if (plugin == null) continue;
				plugin.OnDestroy();
			}

			plugins = null;
			pluginsInitialized = false;
		}

        public override void OnEnable()
        {
			isDisposed = false;
			Initialized = false;
			QueueInitializeEditor();
        }

		public override void OnDisable()
		{
			isDisposed = true;
			UnqueueInitializeEditor();
			DestroyPlugins();
			baseEditorEnabled = false;
			Initialized = false;
			base.OnDisable();
		}

        private void InitializeEditor()
        {
			UnqueueInitializeEditor();

			if (isDisposed || target == null)
			{
				return;
			}

			if (Initialized)
            {
                return;
            }
			if (EditorApplication.isCompiling || EditorApplication.isUpdating)
			{
				QueueInitializeEditor();
                return;
            }
            if (plugins == null)
            {
                AddPlugins();
            }

			if (!baseEditorEnabled)
			{
				base.OnEnable();
				baseEditorEnabled = true;
			}

			if (!pluginsInitialized)
			{
				foreach (IUMARecipePlugin plugin in plugins)
				{
					plugin.OnEnable();
				}
				pluginsInitialized = true;
			}

            if (!NeedsReenable())
			{
				Initialized = true;
                return;
			}

            _errorMessage = null;
            _recipe = new UMAData.UMARecipe();
            showBaseEditor = false;

            try
            {
                var umaRecipeBase = target as UMARecipeBase;
                if (umaRecipeBase != null)
                {
                    umaRecipeBase.Load(_recipe);
                    _description = umaRecipeBase.GetInfo();
                }
            }
            catch (UMAResourceNotFoundException e)
            {
                _errorMessage = e.Message;
            }

            dnaEditor = new DNAMasterEditor(_recipe);
            slotEditor = new SlotMasterEditor(_recipe, target);

            _rebuildOnLayout = true;
            Initialized = true;
        }




        public void OnDestroy()
		{
			isDisposed = true;
			UnqueueInitializeEditor();
			if (warningStyle != null)
			{
				warningStyle = null;
			}
			DestroyPlugins();

		}

        public override void OnInspectorGUI()
        {
			if (EditorApplication.isCompiling || EditorApplication.isUpdating)
			{
				EditorGUILayout.LabelField("Unity is compiling/updating. Please wait...");
				return;
			}
			if (!Initialized)
			{
                EditorGUILayout.HelpBox("Recipe Editor is not initialized. Please wait until the editor is ready.", MessageType.Info);
                return;
            }
            if (warningIcon == null)
			{
				warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
				warningStyle = new GUIStyle(EditorStyles.label);
                warningStyle.fixedHeight = warningIcon.height + 4f;
				warningStyle.contentOffset = new Vector2(0, -2f);
			}
			if (_recipe == null) return;

			if (plugins != null)
			{
				foreach (IUMARecipePlugin plugin in plugins)
				{
					string label = plugin.GetSectionLabel();
					plugin.foldOut = GUIHelper.FoldoutBar(plugin.foldOut, label);
					if (plugin.foldOut)
					{
						GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
						plugin.OnInspectorGUI(serializedObject);
						GUIHelper.EndVerticalPadded(10);
					}
				}
			}

            base.OnInspectorGUI();
		}

        protected override void DoUpdate()
        {
            _needsUpdate = false;
            var recipeBase = (UMARecipeBase)target;
            recipeBase.Save(_recipe);
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
        }


		/// <summary>
		/// Checks if the given RaceData is in the globalLibrary or an assetBundle
		/// </summary>
		/// <param name="_raceData"></param>
		/// <returns></returns>
		protected bool RaceInIndex(RaceData _raceData)
		{
			return UMAAssetIndexer.Instance.HasRace(_raceData.raceName);
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
