using UnityEngine;
using UnityEditor;
using System;
using UnityEditorInternal;
using System.Collections.Generic;

namespace UMA
{
	[CustomEditor(typeof(UMAPiece))]
	public class UMAPieceInspector : UMAPropertyAssetInspector
	{
		private bool _showBlocks;
		UMAPiece piece { get { return target as UMAPiece; } }
		ReorderableList blocksROL;
		List<ReorderableList> slotsROL = new List<ReorderableList>();
		List<ReorderableList> overlaysROL = new List<ReorderableList>();
		UMAPieceBlock rolBlock;
		string[] _propertyStrings;
		List<Rect> slotROLRects = new List<Rect>();
		List<Rect> overlayROLRects = new List<Rect>();
		UMAPieceOverlay draggedOverlay;
		UMAPieceSlot draggedSlot;

		static Func<ReorderableList, bool> _getDragging;
		bool IsDragging(ReorderableList rol)
		{
			if (_getDragging == null)
			{
				var mi = typeof(ReorderableList).GetMethod("IsDragging", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				_getDragging = Delegate.CreateDelegate(typeof(Func<ReorderableList, bool>), mi) as Func<ReorderableList, bool>;
			}
			return _getDragging(rol);
		}

		protected override void OnEnable()
		{
			_showBlocks = EditorPrefs.GetBool("UMAPieceInspector_ShowBlocks", true);
			if (piece.Blocks == null)
			{
				piece.Blocks = new UMAPieceBlock[0];
				EditorUtility.SetDirty(piece);
			}
			base.OnEnable();

			blocksROL = new ReorderableList(piece.Blocks, typeof(UMAPieceBlock));
			blocksROL.drawElementCallback = ROL_blocks_drawBlock;
			blocksROL.drawElementBackgroundCallback = ROL_AlternatingBackground;
			blocksROL.drawHeaderCallback = ROL_blocks_drawHeader;
			blocksROL.onAddCallback = ROL_blocks_addCallBack;
			blocksROL.onRemoveCallback = ROL_blocks_removeCallBack;
			blocksROL.elementHeightCallback = ROL_blocks_heightCallback;
		}

		#region ROL Overlays
		private float ROL_overlays_heightCallback(int index)
		{
			return GetOverlayHeight(rolBlock.Overlays[index]);
		}

		private void ROL_overlays_addCallBack(ReorderableList list)
		{
			var newOverlay = new UMAPieceOverlay();
			ArrayUtility.Insert(ref rolBlock.Overlays, rolBlock.Overlays.Length, newOverlay);
			list.list = rolBlock.Overlays;
		}

		private void ROL_overlays_removeCallBack(ReorderableList list)
		{
			ArrayUtility.RemoveAt(ref rolBlock.Overlays, list.index);
			list.list = rolBlock.Overlays;
		}

		private void ROL_overlays_drawHeader(Rect rect)
		{
			GUI.Label(rect, "Overlays");
		}

		private void ROL_overlays_drawOverlay(Rect uRect, int index, bool isActive, bool isFocused)
		{
			DrawOverlayGUI(rolBlock.Overlays[index], new InspectorRect(uRect), isActive, isFocused);
		}
		#endregion

		#region ROL Slots
		private float ROL_slots_heightCallback(int index)
		{
			return GetSlotHeight(rolBlock.Slots[index]);
		}

		private void ROL_slots_addCallBack(ReorderableList list)
		{
			var newSlot = new UMAPieceSlot();
			ArrayUtility.Insert(ref rolBlock.Slots, rolBlock.Slots.Length, newSlot);
			list.list = rolBlock.Slots;
		}

		private void ROL_slots_removeCallBack(ReorderableList list)
		{
			ArrayUtility.RemoveAt(ref rolBlock.Slots, list.index);
			list.list = rolBlock.Slots;
		}

		private void ROL_slots_drawHeader(Rect rect)
		{
			GUI.Label(rect, "Slots");
		}

		private void ROL_slots_drawSlot(Rect uRect, int index, bool isActive, bool isFocused)
		{
			DrawSlotGUI(rolBlock.Slots[index], new InspectorRect(uRect), isActive, isFocused);
		}
		#endregion

		#region ROL Blocks
		private float ROL_blocks_heightCallback(int index)
		{
			var block = piece.Blocks[index];
			float customHeight = 6f;
			customHeight += block.Condition.GetInspectorHeight();
			
			rolBlock = block;

			slotsROL[index].list = block.Slots;
			customHeight += slotsROL[index].GetHeight();

			overlaysROL[index].list = block.Overlays;
			customHeight += overlaysROL[index].GetHeight();
			return CalculateElementHeight(1, customHeight);
		}

		private void ROL_blocks_addCallBack(ReorderableList list)
		{
			var newBlock = new UMAPieceBlock();
			newBlock.Condition = BaseCondition.CreateCondition(typeof(AlwaysCondition));
			AddScriptableObjectToAsset(piece, newBlock.Condition);
			ArrayUtility.Insert(ref piece.Blocks, piece.Blocks.Length, newBlock);
			list.list = piece.Blocks;
		}

		private void ROL_blocks_removeCallBack(ReorderableList list)
		{
			DestroyImmediate(piece.Blocks[list.index].Condition, true);
			ArrayUtility.RemoveAt(ref piece.Blocks, list.index);
			list.list = piece.Blocks;
		}

		private void ROL_blocks_drawHeader(Rect rect)
		{
			GUI.Label(rect, "Blocks");
		}


		private void ROL_blocks_drawBlock(Rect uRect, int index, bool isActive, bool isFocused)
		{
			var rect = new InspectorRect(uRect);
			var block = piece.Blocks[index];

			EditorGUI.BeginChangeCheck();
			var propertyType = UMAEditorGUILayout.ConditionTypeField(rect.GetLineRect(), "Condition Type", block.Condition.GetType());
			if (EditorGUI.EndChangeCheck())
			{
				var newCondition = BaseCondition.CreateCondition(propertyType);
				AddScriptableObjectToAsset(piece, newCondition);
				DestroyImmediate(block.Condition, true);
				block.Condition = newCondition;
			}

			block.Condition.DrawInspectorProperties(new InspectorRect(rect.GetLineRect(block.Condition.GetInspectorHeight())), isActive, isFocused);

			rolBlock = block;
			var wasDragging = IsDragging(slotsROL[index]);
			slotsROL[index].list = block.Slots;
			var slotRect = rect.GetLineRect(slotsROL[index].GetHeight());
			if (slotRect.width > 0)
			{
				slotROLRects[index] = slotRect;
			}
			slotsROL[index].DoList(slotRect);
			var isDragging = IsDragging(slotsROL[index]);
			if (isDragging)
			{
				var mp = Event.current.mousePosition;
				int destIndex = GetRectIndex(mp, slotROLRects, piece.Blocks.Length);
				draggedSlot = destIndex == index ? null : block.Slots[slotsROL[index].index];
			}
			if (wasDragging && !isDragging)
			{
				var mp = Event.current.mousePosition;
				int destIndex = GetRectIndex(mp, slotROLRects, piece.Blocks.Length);
				if (destIndex != -1 && destIndex != index)
				{
					var destBlock = piece.Blocks[destIndex];

					var slot = block.Slots[slotsROL[index].index];
					ArrayUtility.RemoveAt(ref block.Slots, slotsROL[index].index);
					slotsROL[index].list = block.Slots;

					ArrayUtility.Add(ref destBlock.Slots, slot);
					slotsROL[destIndex].list = destBlock.Slots;
				}
			}

			wasDragging = IsDragging(overlaysROL[index]);
			overlaysROL[index].list = block.Overlays;
			var overlayRect = rect.GetLineRect(overlaysROL[index].GetHeight());
			if (overlayRect.width > 0)
			{
				overlayROLRects[index] = overlayRect;
			}
			overlaysROL[index].DoList(overlayRect);

			isDragging = IsDragging(overlaysROL[index]);
			if (isDragging)
			{
				var mp = Event.current.mousePosition;
				int destIndex = GetRectIndex(mp, overlayROLRects, piece.Blocks.Length);
				draggedOverlay = destIndex == index ? null : block.Overlays[overlaysROL[index].index];
			}
			if (wasDragging && !isDragging)
			{
				var mp = Event.current.mousePosition;
				int destIndex = GetRectIndex(mp, overlayROLRects, piece.Blocks.Length);
				if (destIndex != -1 && destIndex != index)
				{
					var destBlock = piece.Blocks[destIndex];

					var overlay = block.Overlays[overlaysROL[index].index];
					ArrayUtility.RemoveAt(ref block.Overlays, overlaysROL[index].index);
					overlaysROL[index].list = block.Overlays;

					ArrayUtility.Add(ref destBlock.Overlays, overlay);
					overlaysROL[destIndex].list = destBlock.Overlays;
				}
			}



			Event evt = Event.current;
			switch (evt.type)
			{
				case EventType.DragUpdated:
				case EventType.DragPerform:
					if (uRect.Contains(evt.mousePosition))
					{

						DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

						if (evt.type == EventType.DragPerform)
						{
							DragAndDrop.AcceptDrag();
							var folders = new List<string>();
							foreach (UnityEngine.Object dragged_object in DragAndDrop.objectReferences)
							{
								SlotDataAsset tempSlotDataAsset = dragged_object as SlotDataAsset;
								if (tempSlotDataAsset)
								{
									AddSlotToBlock(tempSlotDataAsset);
									continue;
								}

								OverlayDataAsset tempOverlayDataAsset = dragged_object as OverlayDataAsset;
								if (tempOverlayDataAsset)
								{
									AddOverlayToBlock(tempOverlayDataAsset);
									continue;
								}

								var path = AssetDatabase.GetAssetPath(dragged_object);
								if (System.IO.Directory.Exists(path))
								{
									folders.Add(path);
								}
							}
							if (folders.Count > 0)
							{
								var assetGuids = AssetDatabase.FindAssets("t:SlotDataAsset t:OverlayDataAsset", folders.ToArray());
								foreach (var assetGuid in assetGuids)
								{
									var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(assetGuid));

									SlotDataAsset tempSlotDataAsset = asset as SlotDataAsset;
									if (tempSlotDataAsset)
									{
										AddSlotToBlock(tempSlotDataAsset);
										continue;
									}

									OverlayDataAsset tempOverlayDataAsset = asset as OverlayDataAsset;
									if (tempOverlayDataAsset)
									{
										AddOverlayToBlock(tempOverlayDataAsset);
										continue;
									}
								}
							}
						}
					}
					break;
			}
		}

		private void AddSlotToBlock(SlotDataAsset slot)
		{
			ArrayUtility.Add(ref rolBlock.Slots, new UMAPieceSlot() { Slot = slot, Operation = UMAPieceSlot.SlotOperation.Add });
		}

		private void AddOverlayToBlock(OverlayDataAsset overlay)
		{
			ArrayUtility.Add(ref rolBlock.Overlays, new UMAPieceOverlay() { Overlay = overlay, MappedProperties = new PropertyMapping[0], Operation = UMAPieceOverlay.OverlayOperation.Add });
		}
		#endregion



		public float GetOverlayHeight(UMAPieceOverlay overlay)
		{
			int lines = 2;
			if (overlay.Overlay != null)
			{
				var propertyCount = overlay.Overlay.GetPublicPropertyCount();
				if (propertyCount > 0)
				{
					lines += 1 + propertyCount;
				}
			}

			return InspectableAsset.CalculateElementHeight(lines);
		}

		public void DrawOverlayGUI(UMAPieceOverlay overlay, InspectorRect rect, bool isActive, bool isFocused)
		{
			overlay.Overlay = EditorGUI.ObjectField(rect.GetLineRect(), "Overlay Data Asset", overlay.Overlay, typeof(OverlayDataAsset), false) as OverlayDataAsset;
			overlay.Operation = (UMAPieceOverlay.OverlayOperation)EditorGUI.EnumPopup(rect.GetLineRect(), "Operation", overlay.Operation);

			if (overlay.Overlay != null)
			{
				var publicProperties = overlay.Overlay.GetPublicPropertyCount();
				var properties = new BasePieceProperty[publicProperties];

				if (publicProperties > 0)
				{
					EditorGUI.LabelField(rect.GetLineRect(), "Overlay Property Mappings");
					EditorGUI.indentLevel++;

					overlay.Overlay.GetPublicProperties(properties);
					for (int i = 0; i < publicProperties; i++)
					{
						var destProperty = properties[i];
						int propertyIndex = 0;
						int mapIndex = 0;
						for (int j = 0; j < overlay.MappedProperties.Length; j++)
						{
							if (overlay.MappedProperties[j].Dest == destProperty)
							{
								propertyIndex = Array.IndexOf(_propertyStrings, overlay.MappedProperties[j].Source.name);
								mapIndex = j;
								break;
							}
						}

						var newPropertyIndex = EditorGUI.Popup(rect.GetLineRect(), destProperty.name, propertyIndex, _propertyStrings);
						if (propertyIndex != newPropertyIndex)
						{
							if (propertyIndex == 0)
							{
								// add
								if (destProperty.GetValue().CanSetValueFrom(piece.Properties[newPropertyIndex - 1].GetValue()))
								{
									ArrayUtility.Add(ref overlay.MappedProperties, new PropertyMapping() { Source = piece.Properties[newPropertyIndex - 1], Dest = destProperty });
								}
							}
							else if (newPropertyIndex == 0)
							{
								// remove
								ArrayUtility.RemoveAt(ref overlay.MappedProperties, mapIndex);
							}
							else
							{
								// modify
								if (destProperty.GetValue().CanSetValueFrom(piece.Properties[newPropertyIndex - 1].GetValue()))
								{
									overlay.MappedProperties[mapIndex].Source = piece.Properties[newPropertyIndex - 1];
								}
							}
						}
					}
					EditorGUI.indentLevel--;
				}
			}
		}


		private void DrawSlotGUI(UMAPieceSlot slot, InspectorRect rect, bool isActive, bool isFocused)
		{
			slot.Slot = EditorGUI.ObjectField(rect.GetLineRect(), "Slot Data Asset", slot.Slot, typeof(SlotDataAsset), false) as SlotDataAsset;
			slot.Operation = (UMAPieceSlot.SlotOperation)EditorGUI.EnumPopup(rect.GetLineRect(), "Operation", slot.Operation);
		}

		private float GetSlotHeight(UMAPieceSlot slot)
		{
			return CalculateElementHeight(2);
		}

		private int GetRectIndex(Vector2 point, List<Rect> Rects, int count)
		{
			for (int i = 0; i < count; i++)
			{
				if (Rects[i].Contains(point))
					return i;
			}
			return -1;
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
			piece.Location = EditorGUILayout.ObjectField("Location", piece.Location, typeof(UMALocation), false) as UMALocation;
			
			base.OnInspectorGUI();

			if (_propertyStrings == null || _propertyStrings.Length != piece.Properties.Length + 1)
				_propertyStrings = new string[piece.Properties.Length + 1];
			_propertyStrings[0] = "None";
			for (int i = 0; i < piece.Properties.Length; i++)
			{
				_propertyStrings[i + 1] = piece.Properties[i].name;
			}

			while (slotsROL.Count < piece.Blocks.Length)
			{
				var slotROL = new ReorderableList(null, typeof(UMAPieceSlot));
				slotROL.drawElementCallback = ROL_slots_drawSlot;
				slotROL.drawElementBackgroundCallback = ROL_AlternatingBackground;
				slotROL.drawHeaderCallback = ROL_slots_drawHeader;
				slotROL.onAddCallback = ROL_slots_addCallBack;
				slotROL.onRemoveCallback = ROL_slots_removeCallBack;
				slotROL.elementHeightCallback = ROL_slots_heightCallback;
				slotROL.draggable = true;

				slotsROL.Add(slotROL);
				slotROLRects.Add(new Rect());
			}

			while (overlaysROL.Count < piece.Blocks.Length)
			{
				var overlayROL = new ReorderableList(null, typeof(UMAPieceOverlay));
				overlayROL.drawElementCallback = ROL_overlays_drawOverlay;
				overlayROL.drawElementBackgroundCallback = ROL_AlternatingBackground;
				overlayROL.drawHeaderCallback = ROL_overlays_drawHeader;
				overlayROL.onAddCallback = ROL_overlays_addCallBack;
				overlayROL.onRemoveCallback = ROL_overlays_removeCallBack;
				overlayROL.elementHeightCallback = ROL_overlays_heightCallback;
				overlayROL.draggable = true;

				overlaysROL.Add(overlayROL);
				
				overlayROLRects.Add(new Rect());
			}

			draggedSlot = null;
			draggedOverlay = null;

			ShowBlocks = EditorGUILayout.Foldout(ShowBlocks, "Blocks");
			if (ShowBlocks)
			{
				blocksROL.DoLayoutList();
			}

			if (draggedOverlay != null)
			{
				var mp = Event.current.mousePosition;
				int destIndex = GetRectIndex(mp, overlayROLRects, piece.Blocks.Length);
				var height = GetOverlayHeight(draggedOverlay);
				var rect = new Rect(mp.x, mp.y, overlayROLRects[0].width, height);
				EditorGUI.DrawRect(rect, GUI.skin.settings.selectionColor);
				DrawOverlayGUI(draggedOverlay, new InspectorRect(rect), destIndex != -1, true);
			}

			if (draggedSlot != null)
			{
				var mp = Event.current.mousePosition;
				int destIndex = GetRectIndex(mp, slotROLRects, piece.Blocks.Length);
				var height = GetSlotHeight(draggedSlot);
				var rect = new Rect(mp.x, mp.y, slotROLRects[0].width, height);
				EditorGUI.DrawRect(rect, destIndex != -1 ? GUI.skin.settings.selectionColor : Color.grey);
				DrawSlotGUI(draggedSlot, new InspectorRect(rect), destIndex != -1, true);
			}


			if (EditorGUI.EndChangeCheck())
			{
				UnityEditor.EditorUtility.SetDirty(piece);
				UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(piece), ImportAssetOptions.ForceSynchronousImport);
			}
		}

		private Rect DoLayoutList(ReorderableList list)
		{
			var oldRect = GUILayoutUtility.GetLastRect();
			list.DoLayoutList();
			var newRect = GUILayoutUtility.GetLastRect();
			return new Rect(oldRect.x, oldRect.yMax, oldRect.width, newRect.yMax - oldRect.yMax);
		}

		//private void DrawBlock(UMAPieceBlock block)
		//{
		//	EditorGUI.BeginChangeCheck();
		//	var propertyType = UMAEditorGUILayout.ConditionTypeField("Condition", block.Condition.GetType());
		//	if (EditorGUI.EndChangeCheck())
		//	{
		//		var newCondition = BaseCondition.CreateCondition(propertyType);
		//		AddScriptableObjectToAsset(piece, newCondition);
		//		DestroyImmediate(block.Condition, true);
		//		block.Condition = newCondition;
		//	}
		//	EditorGUI.indentLevel++;
		//	DrawScriptableObject(block.Condition);
		//	EditorGUI.indentLevel--;

		//	EditorGUILayout.LabelField("Slots");
		//	EditorGUI.indentLevel++;
		//	for (int i = block.Slots.Length - 1; i >= 0; i--)
		//	{
		//		GUILayout.BeginHorizontal();
		//		EditorGUILayout.LabelField("Slot "+(block.Slots.Length-i));
		//		if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
		//		{
		//			ArrayUtility.RemoveAt(ref block.Slots, i);
		//			GUILayout.EndHorizontal();
		//			continue;
		//		}

		//		GUILayout.EndHorizontal();
		//		EditorGUI.indentLevel++;
		//		DrawSlot(block.Slots[i]);
		//		EditorGUI.indentLevel--;
		//	}
		//	GUILayout.BeginHorizontal();
		//	GUILayout.Label("", GUILayout.Width(EditorGUI.indentLevel*20));
		//	if (GUILayout.Button("Add Slot"))
		//	{
		//		var newSlot = new UMAPieceSlot();
		//		ArrayUtility.Insert(ref block.Slots, 0, newSlot);
		//	}
		//	GUILayout.EndHorizontal();
		//	EditorGUI.indentLevel--;


		//	EditorGUILayout.Space();
		//}

		private void DrawSlot(UMAPieceSlot slot)
		{
			slot.Slot = EditorGUILayout.ObjectField("Slot Data Asset", slot.Slot, typeof(SlotDataAsset), false) as SlotDataAsset;
			
			EditorGUI.indentLevel++;
			slot.Operation = (UMAPieceSlot.SlotOperation)EditorGUILayout.EnumPopup("Operation", slot.Operation);
			//if (slot.Operation != UMAPieceSlot.SlotOperation.Remove)
			//{
			//	EditorGUILayout.LabelField("Overlays");
			//	EditorGUI.indentLevel++;
			//	for (int i = slot.Overlays.Length - 1; i >= 0; i--)
			//	{
			//		GUILayout.BeginHorizontal();
			//		EditorGUILayout.LabelField("Overlay "+(slot.Overlays.Length-i));
			//		if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
			//		{
			//			ArrayUtility.RemoveAt(ref slot.Overlays, i);
			//			GUILayout.EndHorizontal();
			//			continue;
			//		}
			//		GUILayout.EndHorizontal();
					
			//		EditorGUI.indentLevel++;
			//		DrawOverlay(slot.Overlays[i]);
			//		EditorGUI.indentLevel--;
			//	}
			//	EditorGUI.indentLevel--;
			//	GUILayout.BeginHorizontal();
			//	GUILayout.Label("", GUILayout.Width(EditorGUI.indentLevel*20));
			//	if (GUILayout.Button("Add Overlay"))
			//	{
			//		var newOverlay = new UMAPieceOverlay();
			//		ArrayUtility.Insert(ref slot.Overlays, 0, newOverlay);
			//	}
			//	GUILayout.EndHorizontal();
			//}
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
	}
}