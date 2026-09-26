using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
	internal class UmaExamineMaterialsWindow : EditorWindow
	{
		private const string ErrorShaderName = "Hidden/InternalErrorShader";

		private readonly List<UMA.UMAMaterial> _materials = new List<UMA.UMAMaterial>();
		private Vector2 _scroll;

		public static void Open(List<UMA.UMAMaterial> materials)
		{
			var window = GetWindow<UmaExamineMaterialsWindow>(false, "Examine UMAMaterials", true);
			window.minSize = new Vector2(860f, 320f);
			window._materials.Clear();
			if (materials != null)
			{
				window._materials.AddRange(materials);
			}
			window.SortMaterials();
			window.Show();
			window.Focus();
		}

		private void RefreshFromSelection()
		{
			var selected = Selection.GetFiltered(typeof(UMA.UMAMaterial), SelectionMode.Assets);
			_materials.Clear();
			for (int i = 0; i < selected.Length; i++)
			{
				var material = selected[i] as UMA.UMAMaterial;
				if (material != null)
				{
					_materials.Add(material);
				}
			}
			SortMaterials();
			_scroll = Vector2.zero;
			Repaint();
		}

		private void SortMaterials()
		{
			_materials.Sort((a, b) => string.Compare(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty, System.StringComparison.OrdinalIgnoreCase));
		}

		private void OnGUI()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Label("Examine UMAMaterials", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
			{
				RefreshFromSelection();
			}
			EditorGUILayout.EndHorizontal();

			if (_materials.Count == 0)
			{
				EditorGUILayout.HelpBox("Select one or more UMAMaterial assets in the Project window.", MessageType.Info);
				return;
			}

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("UMAMaterial", EditorStyles.boldLabel, GUILayout.Width(180f));
			GUILayout.Space(154f);
			EditorGUILayout.LabelField("Material", EditorStyles.boldLabel, GUILayout.Width(180f));
			GUILayout.Space(134f);
			EditorGUILayout.LabelField("Shader", EditorStyles.boldLabel);
			EditorGUILayout.EndHorizontal();

			_scroll = EditorGUILayout.BeginScrollView(_scroll);
			for (int i = 0; i < _materials.Count; i++)
			{
				var umaMaterial = _materials[i];
				if (umaMaterial == null)
				{
					continue;
				}

				DrawMaterialRow(umaMaterial);
			}
			EditorGUILayout.EndScrollView();
		}

		private void DrawMaterialRow(UMA.UMAMaterial umaMaterial)
		{
			Material material = umaMaterial.material;
			string materialName = material != null ? material.name : "(None)";
			string shaderDisplay = GetShaderDisplay(material);
			bool shaderInvalid = material == null || IsShaderInvalid(material);

			EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
			EditorGUILayout.LabelField(umaMaterial.name, GUILayout.Width(180f));
			if (GUILayout.Button("Examine UMAMaterial", GUILayout.Width(150f)))
			{
				QueuePopupInspector(umaMaterial);
			}
			EditorGUILayout.LabelField(materialName, GUILayout.Width(180f));
			using (new EditorGUI.DisabledScope(material == null))
			{
				if (GUILayout.Button("Examine Material", GUILayout.Width(130f)))
				{
					QueuePopupInspector(material);
				}
			}
			if (shaderInvalid)
			{
				Color prevColor = GUI.color;
				GUI.color = new Color(1f, 0.6f, 0.6f);
				EditorGUILayout.LabelField(shaderDisplay);
				GUI.color = prevColor;
			}
			else
			{
				EditorGUILayout.LabelField(shaderDisplay);
			}
			EditorGUILayout.EndHorizontal();
		}

		private static string GetShaderDisplay(Material material)
		{
			if (material == null)
			{
				return "Null Shader";
			}

			Shader shader = material.shader;
			if (shader == null)
			{
				return "Null Shader";
			}

			if (shader.name == ErrorShaderName)
			{
				return "Error Shader";
			}

			return shader.name;
		}

		private static bool IsShaderInvalid(Material material)
		{
			if (material == null)
			{
				return true;
			}

			Shader shader = material.shader;
			if (shader == null)
			{
				return true;
			}

			return shader.name == ErrorShaderName;
		}

		private static void QueuePopupInspector(Object target)
		{
			// Opening an InspectorWindow while processing an IMGUI event can leave
			// the current view with an invalid GUILayout state. Defer it until the
			// current event is complete.
			EditorApplication.delayCall += () =>
			{
				if (target != null)
				{
					InspectorUtlity.InspectTarget(target);
				}
			};
		}
	}
}
