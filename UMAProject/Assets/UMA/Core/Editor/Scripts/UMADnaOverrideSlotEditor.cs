using System;
using System.Collections.Generic;
using System.Linq;
using UMA.PoseTools;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMADnaOverrideSlot))]
    public sealed class UMADnaOverrideSlotEditor : Editor
    {
        private static UMAData referenceCharacter;
        private VisualElement content;

        public override VisualElement CreateInspectorGUI()
        {
            content = new VisualElement();
            Rebuild();
            return content;
        }

        private void Rebuild()
        {
            content.Unbind();
            content.Clear();
            var reference = new ObjectField("Reference Character")
            {
                name = "referenceCharacter", objectType = typeof(UMAData), allowSceneObjects = true, value = referenceCharacter,
                tooltip = "A built character supplies choices and current DNA values. Shared by override slot inspectors for this editor session; not saved in the slot or prefab."
            };
            reference.RegisterValueChangedCallback(e => { referenceCharacter = e.newValue as UMAData; Rebuild(); });
            content.Add(reference);
            content.Add(new Button(Rebuild) { text = "Refresh Character Choices" });
            content.Add(new HelpBox("Wire OnRecipePrepared to the slot's Recipe Prepared event, and OnDnaApplied to DNA Applied. Values use UMA's 0–1 scale: 1 = 100%. Later entries override earlier names; bone poses compose in list order.", HelpBoxMessageType.Info));
            AddSection("Baked Blendshapes", "blendshapes", false, GetBlendshapeChoices(referenceCharacter));
            AddSection("DNA Overrides", "dna", false, GetDNAChoices(referenceCharacter));
            AddSection("Bone Poses", "bonePoses", true, Array.Empty<string>());
            content.Bind(serializedObject);
        }

        private void AddSection(string title, string field, bool poses, string[] choices)
        {
            var foldout = new Foldout { text = title, value = true, viewDataKey = "UMADnaOverrideSlot." + field };
            content.Add(foldout);
            var menu = new ToolbarMenu { text = poses ? "Add Compatible Bone Pose" : "Add From Character" };
            if (poses)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:UMABonePose"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var pose = AssetDatabase.LoadAssetAtPath<UMABonePose>(path);
                    if (referenceCharacter?.skeleton != null && pose?.poses != null &&
                        pose.poses.Any(p => p != null && referenceCharacter.skeleton.HasBone(p.hash)))
                        menu.menu.AppendAction(path, _ => AddEntry(field, null, pose, 1));
                }
            }
            else
            {
                foreach (var choice in choices)
                    menu.menu.AppendAction(choice, _ => AddEntry(field, choice, null, field == "blendshapes" ? 1 : GetDNAValue(choice)));
            }
            if (menu.menu.MenuItems().Count == 0)
                menu.menu.AppendAction("No choices — assign a built reference character", null, DropdownMenuAction.Status.Disabled);
            foldout.Add(menu);
            var property = serializedObject.FindProperty(field);
            var list = new ListView
            {
                bindingPath = field, reorderable = true, reorderMode = ListViewReorderMode.Animated,
                showAddRemoveFooter = true, showBorder = true,
                selectionType = SelectionType.Multiple, virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                makeItem = () =>
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.flexWrap = Wrap.Wrap;
                    BaseField<UnityEngine.Object> poseField = null;
                    VisualElement nameField;
                    if (poses)
                    {
                        poseField = new ObjectField { objectType = typeof(UMABonePose), allowSceneObjects = false };
                        nameField = poseField;
                    }
                    else nameField = new TextField { tooltip = "Exact blendshape or DNA name" };
                    nameField.name = "entryName";
                    nameField.style.flexGrow = 1;
                    nameField.style.minWidth = 170;
                    row.Add(nameField);
                    var value = new FloatField(poses ? "Weight" : "Value") { name = "entryValue" };
                    value.style.width = 130;
                    value.labelElement.style.minWidth = 45;
                    value.labelElement.style.width = 45;
                    row.Add(value);
                    return row;
                },
                bindItem = (row, index) =>
                {
                    var entry = property.GetArrayElementAtIndex(index);
                    if (poses) row.Q<ObjectField>("entryName").BindProperty(entry.FindPropertyRelative("pose"));
                    else row.Q<TextField>("entryName").BindProperty(entry.FindPropertyRelative("name"));
                    row.Q<FloatField>("entryValue").BindProperty(entry.FindPropertyRelative(poses ? "weight" : "value"));
                },
                unbindItem = (row, _) => row.Unbind()
            };
            foldout.Add(list);
        }

        private void AddEntry(string field, string name, UMABonePose pose, float value)
        {
            serializedObject.Update();
            var array = serializedObject.FindProperty(field);
            var entry = array.GetArrayElementAtIndex(array.arraySize++);
            if (field == "bonePoses") entry.FindPropertyRelative("pose").objectReferenceValue = pose;
            else entry.FindPropertyRelative("name").stringValue = name;
            entry.FindPropertyRelative(field == "bonePoses" ? "weight" : "value").floatValue = value;
            serializedObject.ApplyModifiedProperties();
        }

        private float GetDNAValue(string name)
        {
            var instance = referenceCharacter?.umaRecipe?.dnaInstanceCollection?.dnaInstances?.FirstOrDefault(d => d != null && d.Name == name);
            if (instance != null) return instance.Value;
            if (referenceCharacter?.umaRecipe != null)
                foreach (var block in referenceCharacter.umaRecipe.GetAllDna())
                {
                    if (block?.Names == null) continue;
                    int index = Array.IndexOf(block.Names, name);
                    if (index >= 0) return block.GetValue(index);
                }
            return 0.5f;
        }

        public static string[] GetBlendshapeChoices(UMAData character)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (character != null)
            {
                foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.sharedMesh != null)
                        for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++) names.Add(renderer.sharedMesh.GetBlendShapeName(i));
                foreach (var slot in character.umaRecipe?.slotDataList ?? Array.Empty<SlotData>())
                    if (slot != null && !slot.Suppressed && slot.asset?.meshData?.blendShapes != null)
                        foreach (var shape in slot.asset.meshData.blendShapes)
                            if (shape != null && !string.IsNullOrEmpty(shape.shapeName)) names.Add(shape.shapeName);
                if (character.umaRecipe?.BlendshapeSlots != null)
                    foreach (var sources in character.umaRecipe.BlendshapeSlots.Values)
                        foreach (var source in sources)
                            if (source?.blendShapes != null)
                                foreach (var shape in source.blendShapes)
                                    if (shape != null && !string.IsNullOrEmpty(shape.shapeName)) names.Add(shape.shapeName);
            }
            return names.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        public static string[] GetDNAChoices(UMAData character)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (character?.umaRecipe != null)
            {
                foreach (var block in character.umaRecipe.GetAllDna())
                    if (block?.Names != null) foreach (var name in block.Names) names.Add(name);
                var instances = character.umaRecipe.dnaInstanceCollection?.dnaInstances;
                if (instances != null) foreach (var dna in instances) if (dna != null) names.Add(dna.Name);
            }
            return names.Where(n => !string.IsNullOrEmpty(n)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        [MenuItem("Assets/Create/UMA/DNA Override Utility Slot")]
        public static void CreateUtilitySlot()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create DNA Override Utility Slot", "DnaOverrides_slot", "asset", "Choose where to save the slot and its settings prefab.");
            if (string.IsNullOrEmpty(path)) return;
            Selection.activeObject = CreateAssets(path);
        }

        public static SlotDataAsset CreateAssets(string slotPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(slotPath) != null)
                throw new ArgumentException("An asset already exists at " + slotPath);
            var prefabPath = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(slotPath, ".prefab"));
            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(prefabPath));
            try
            {
                root.AddComponent<UMADnaOverrideSlot>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                var controller = prefab.GetComponent<UMADnaOverrideSlot>();
                var slot = CreateInstance<SlotDataAsset>();
                slot.name = System.IO.Path.GetFileNameWithoutExtension(slotPath);
                slot.isLegacySlot = false;
                slot.RecipePrepared = new UMADataEvent();
                slot.DNAApplied = new UMADataEvent();
                UnityEventTools.AddPersistentListener(slot.RecipePrepared, controller.OnRecipePrepared);
                UnityEventTools.AddPersistentListener(slot.DNAApplied, controller.OnDnaApplied);
                slot.RecipePrepared.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                slot.DNAApplied.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                AssetDatabase.CreateAsset(slot, slotPath);
                AssetDatabase.SaveAssetIfDirty(slot);
                return slot;
            }
            finally { DestroyImmediate(root); }
        }
    }
}
