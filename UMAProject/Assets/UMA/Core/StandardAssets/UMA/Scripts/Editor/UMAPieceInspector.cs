using UnityEngine;
using UnityEditor;
using System;

namespace UMA
{
	[CustomEditor(typeof(UMAPiece))]
	public class UMAPieceInspector : Editor
	{
		private bool _showProperties;
		private bool _showBlocks;
		UMAPiece piece { get { return target as UMAPiece; } }

		void OnEnable()
		{
			_showProperties = EditorPrefs.GetBool("UMAPieceInspector_ShowProperties", true);
			_showBlocks = EditorPrefs.GetBool("UMAPieceInspector_ShowBlocks", true);
		}

		private bool ShowProperties
		{
			get
			{
				return _showProperties;
			}
			set
			{
				if (value != _showProperties)
				{
					_showProperties = value;
					EditorPrefs.SetBool("UMAPieceInspector_ShowProperties", value);
				}
			}
		}

		private bool ShowBlocks
		{
			get
			{
				return _showBlocks;
			}
			set
			{
				if (value != _showBlocks)
				{
					_showBlocks = value;
					EditorPrefs.SetBool("UMAPieceInspector_ShowBlocks", value);
				}
			}
		}

		public override void OnInspectorGUI()
		{
			EditorGUI.BeginChangeCheck();
			if (piece.Properties == null)
			{
				piece.Properties = new BasePieceProperty[0];
			}
			if (piece.Blocks == null)
			{
				piece.Blocks = new UMAPieceBlock[0];
			}

			ShowProperties = EditorGUILayout.Foldout(ShowProperties, "Properties");
			if (ShowProperties)
			{
				EditorGUI.indentLevel++;
				for (int i = piece.Properties.Length - 1; i >= 0; i--)
				{
					DrawProperty(piece.Properties[i], i);
				}
				GUILayout.BeginHorizontal(GUILayout.Height(15));
				GUILayout.Label("", GUILayout.Width(EditorGUI.indentLevel*20));
				if (GUILayout.Button("Add Property"))
				{
					var newProperty = BasePieceProperty.CreateProperty(BaseProperty.PropertyTypes[0]);
					newProperty.name = "Added";
					AddScriptableObjectToAsset(piece, newProperty);
					ArrayUtility.Insert(ref piece.Properties, 0, newProperty);
				}
				GUILayout.EndHorizontal();
				EditorGUI.indentLevel--;
				EditorGUILayout.Space();
			}

			ShowBlocks = EditorGUILayout.Foldout(ShowBlocks, "Blocks");
			if (ShowBlocks)
			{
				EditorGUI.indentLevel++;
				for (int i = piece.Blocks.Length - 1; i >= 0; i--)
				{
					GUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Block "+(piece.Blocks.Length-i));
					if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
					{
						DestroyImmediate(piece.Blocks[i].Condition, true);
						ArrayUtility.RemoveAt(ref piece.Blocks, i);
						GUILayout.EndHorizontal();
						continue;
					}
					GUILayout.EndHorizontal();
					EditorGUI.indentLevel++;
					DrawBlock(piece.Blocks[i]);
					EditorGUI.indentLevel--;
				}
				GUILayout.BeginHorizontal();
				GUILayout.Label("", GUILayout.Width(EditorGUI.indentLevel*20));
				if (GUILayout.Button("Add Block"))
				{
					var newBlock = new UMAPieceBlock();
					newBlock.Condition = BaseCondition.CreateCondition(typeof(AlwaysCondition));
					AddScriptableObjectToAsset(piece, newBlock.Condition);
					ArrayUtility.Insert(ref piece.Blocks, 0, newBlock);
				}
				GUILayout.EndHorizontal();
				EditorGUI.indentLevel--;
				EditorGUILayout.Space();
			}

			if (EditorGUI.EndChangeCheck())
			{
				UnityEditor.EditorUtility.SetDirty(piece);
				UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(piece), ImportAssetOptions.ForceSynchronousImport);
			}
		}

		private void DrawProperty(BasePieceProperty property, int propertyIndex)
		{
			GUILayout.BeginHorizontal();
			EditorGUI.BeginChangeCheck();
			var propertyType = UMAEditorGUILayout.PropertyTypeField(property.name, property.GetPropertyType());
			if (EditorGUI.EndChangeCheck())
			{
				var newProperty = BasePieceProperty.CreateProperty(propertyType);
				newProperty.name = property.name;
				AddScriptableObjectToAsset(piece, newProperty);
				DestroyImmediate(property, true);
				piece.Properties[propertyIndex] = newProperty;
				property = newProperty;
			}
			if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
			{
				ArrayUtility.RemoveAt(ref piece.Properties, propertyIndex);
				DestroyImmediate(property, true);
				GUILayout.EndHorizontal();
				return;
			}
			GUILayout.EndHorizontal();

			EditorGUI.indentLevel++;
			property.name = EditorGUILayout.TextField("Name", property.name);

			DrawScriptableObject(property);
			EditorGUI.indentLevel--;
		}

		private void DrawBlock(UMAPieceBlock block)
		{
			EditorGUI.BeginChangeCheck();
			var propertyType = UMAEditorGUILayout.ConditionTypeField("Condition", block.Condition.GetType());
			if (EditorGUI.EndChangeCheck())
			{
				var newCondition = BaseCondition.CreateCondition(propertyType);
				AddScriptableObjectToAsset(piece, newCondition);
				DestroyImmediate(block.Condition, true);
				block.Condition = newCondition;
			}
			EditorGUI.indentLevel++;
			DrawScriptableObject(block.Condition);
			EditorGUI.indentLevel--;

			EditorGUILayout.LabelField("Slots");
			EditorGUI.indentLevel++;
			for (int i = block.Slots.Length - 1; i >= 0; i--)
			{
				GUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Slot "+(block.Slots.Length-i));
				if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
				{
					ArrayUtility.RemoveAt(ref block.Slots, i);
					GUILayout.EndHorizontal();
					continue;
				}
				
				GUILayout.EndHorizontal();
				EditorGUI.indentLevel++;
				DrawSlot(block.Slots[i]);
				EditorGUI.indentLevel--;
			}
			GUILayout.BeginHorizontal();
			GUILayout.Label("", GUILayout.Width(EditorGUI.indentLevel*20));
			if (GUILayout.Button("Add Slot"))
			{
				var newSlot = new UMAPieceSlot();
				ArrayUtility.Insert(ref block.Slots, 0, newSlot);
			}
			GUILayout.EndHorizontal();
			EditorGUI.indentLevel--;
			
			
			EditorGUILayout.Space();
		}

		private void DrawSlot(UMAPieceSlot slot)
		{
			slot.Slot = EditorGUILayout.ObjectField("Slot Data Asset", slot.Slot, typeof(SlotDataAsset), false) as SlotDataAsset;
			
			EditorGUI.indentLevel++;
			slot.Operation = (UMAPieceSlot.SlotOperation)EditorGUILayout.EnumPopup("Operation", slot.Operation);
			if (slot.Operation != UMAPieceSlot.SlotOperation.Remove)
			{
				EditorGUILayout.LabelField("Overlays");
				EditorGUI.indentLevel++;
				for (int i = slot.Overlays.Length - 1; i >= 0; i--)
				{
					GUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Overlay "+(slot.Overlays.Length-i));
					if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
					{
						ArrayUtility.RemoveAt(ref slot.Overlays, i);
						GUILayout.EndHorizontal();
						continue;
					}
					GUILayout.EndHorizontal();
					
					EditorGUI.indentLevel++;
					DrawOverlay(slot.Overlays[i]);
					EditorGUI.indentLevel--;
				}
				EditorGUI.indentLevel--;
				GUILayout.BeginHorizontal();
				GUILayout.Label("", GUILayout.Width(EditorGUI.indentLevel*20));
				if (GUILayout.Button("Add Overlay"))
				{
					var newOverlay = new UMAPieceOverlay();
					ArrayUtility.Insert(ref slot.Overlays, 0, newOverlay);
				}
				GUILayout.EndHorizontal();
			}
			EditorGUI.indentLevel--;
			EditorGUILayout.Space();
		}

		private void DrawOverlay(UMAPieceOverlay overlay)
		{
			overlay.Overlay = EditorGUILayout.ObjectField("Overlay Data Asset", overlay.Overlay, typeof(OverlayDataAsset), false) as OverlayDataAsset;

			EditorGUI.indentLevel++;
			overlay.Operation = (UMAPieceOverlay.OverlayOperation)EditorGUILayout.EnumPopup("Operation", overlay.Operation);
			EditorGUI.indentLevel--;
		}

		public static void DrawScriptableObject(ScriptableObject so)
		{
			Editor editor = Editor.CreateEditor(so);
			editor.OnInspectorGUI();
		}

		public static void AddScriptableObjectToAsset(UMAPiece piece, ScriptableObject so)
		{
			AssetDatabase.AddObjectToAsset(so, AssetDatabase.GetAssetPath(piece));
		}
	}
}