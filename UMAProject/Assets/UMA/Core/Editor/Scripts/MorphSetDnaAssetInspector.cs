#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UMA.Editors;

namespace UMA.Editors
{
    [CustomEditor(typeof(MorphSetDnaAsset))]
    public class MorphSetDnaAssetInspector : Editor
    {
        private ReorderableList _morphList;
        private bool _morphsFoldout = true;
        private string[] _excludeProperties = { "dnaMorphs" };

        private bool _editorFoldout = true;
        private SlotDataAsset _slotAsset;
        private DynamicUMADnaAsset _dnaAsset;

        //private bool _helpBox = false; //TODO help boxes with descriptions of what everything does.

        public void OnEnable()
        {
            _morphList = new ReorderableList(serializedObject, serializedObject.FindProperty("dnaMorphs"), true, true, true, true);
            _morphList.drawElementCallback = DrawMorphElement;
            _morphList.onAddCallback = OnAddCallback;

            _morphList.drawHeaderCallback = rect => { EditorGUI.LabelField(rect, "DNA Morphs"); };
            _morphList.elementHeightCallback = (index) => { return (EditorGUIUtility.singleLineHeight + 2) * 7 + 8; };
        }

        public override void OnInspectorGUI()
        {
            //base.DrawDefaultInspector();

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, _excludeProperties);

            GUILayout.Space(10);
            GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
            _editorFoldout = EditorGUILayout.Foldout(_editorFoldout, "Editor Tools", true);
            if (_editorFoldout)
            {
                if(GUILayout.Button("Clear DNA Morphs List", GUILayout.MaxWidth(200)))
                {
                    if (EditorUtility.DisplayDialog("Warning", "Are you sure you want to clear the DNA Morphs list?", "Yes", "Cancel"))
                        ClearDnaMorphs();
                }                    
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Extract BlendShape Names", "This will find all the blendshape names in the supplied SlotData->MeshData and add them to the dna morph list. Warning! Will clear existing morphs."), GUILayout.MaxWidth(200)))
                {
                    if (EditorUtility.DisplayDialog("Warning", "This will clear your current dna morph list. Are you sure to proceed?", "Yes", "Cancel"))
                        ExtractBlendShapeNames();
                }
                _slotAsset = EditorGUILayout.ObjectField(_slotAsset, typeof(SlotDataAsset), false) as SlotDataAsset;

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Write to DynamicDNA", "This will write all the dna entry names from the dna morph list to the supplied DynamicUMADnaAsset.  Warning! This will clear the existing Dna Names on the DynamicUMADnaAsset."), GUILayout.MaxWidth(200)))
                {
                    if (EditorUtility.DisplayDialog("Warning", "This will clear your DynamicDnaName list. Are you sure to proceed?", "Yes", "Cancel"))
                        WriteToDynamicDNA();
                }
                _dnaAsset = EditorGUILayout.ObjectField(_dnaAsset, typeof(DynamicUMADnaAsset), false) as DynamicUMADnaAsset;
                EditorGUILayout.EndHorizontal();
            }
            GUIHelper.EndVerticalPadded(3);

            GUILayout.Space(10);
            GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
            _morphsFoldout = EditorGUILayout.Foldout(_morphsFoldout, "Open/Close DNA Morphs", true);
            
            if(_morphsFoldout)
                _morphList.DoLayoutList();

            GUIHelper.EndVerticalPadded(3);

            serializedObject.ApplyModifiedProperties();
        }

        private void ClearDnaMorphs()
        {
            SerializedProperty dnaMorphs = serializedObject.FindProperty("dnaMorphs");
            if(dnaMorphs != null)
                dnaMorphs.ClearArray();
        }

        private void ExtractBlendShapeNames()
        {
            if (_slotAsset == null)
                return;
            if (_slotAsset.meshData == null)
                return;
            if (_slotAsset.meshData.blendShapes == null)
                return;

            SerializedProperty dnaMorphs = serializedObject.FindProperty("dnaMorphs");
            dnaMorphs.ClearArray();

            UMABlendShape[] blendshapes = _slotAsset.meshData.blendShapes;
            dnaMorphs.arraySize = blendshapes.Length;

            for(int i = 0; i < blendshapes.Length; i++ )
            {
                var element = dnaMorphs.GetArrayElementAtIndex(i);
                var dnaEntry = element.FindPropertyRelative("dnaEntryName");
                //var zero = element.FindPropertyRelative("blendShapeZero");
                var one = element.FindPropertyRelative("blendShapeOne");

                dnaEntry.stringValue = blendshapes[i].shapeName;
                //zero.stringValue = blendshapes[i].shapeName;
                one.stringValue = blendshapes[i].shapeName;
            }
        }

        private void WriteToDynamicDNA()
        {
            if (_dnaAsset == null)
                return;

            SerializedProperty dnaMorphs = serializedObject.FindProperty("dnaMorphs");

            if (dnaMorphs.arraySize <= 0)
                return;

            _dnaAsset.Names = new string[dnaMorphs.arraySize];

            for(int i = 0; i < dnaMorphs.arraySize; i++ )
            {
                SerializedProperty element = dnaMorphs.GetArrayElementAtIndex(i);
                _dnaAsset.Names[i] = element.FindPropertyRelative("dnaEntryName").stringValue;
            }

        }

        private void OnAddCallback(ReorderableList l)
        {
            var index = l.serializedProperty.arraySize;
            l.serializedProperty.arraySize++;
            l.index = index;
            var element = l.serializedProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("dnaEntryName").stringValue = "";
            element.FindPropertyRelative("poseZero").objectReferenceValue = null;
            element.FindPropertyRelative("poseOne").objectReferenceValue = null;
            element.FindPropertyRelative("blendShapeZero").stringValue = "";
            element.FindPropertyRelative("blendShapeOne").stringValue = "";
        }

        private void DrawMorphElement( Rect rect, int index, bool isActive, bool isFocused )
        {
            SerializedProperty element = _morphList.serializedProperty.GetArrayElementAtIndex(index);
            SerializedProperty sizeZero = element.FindPropertyRelative("sizeZero");
            SerializedProperty sizeOne = element.FindPropertyRelative("sizeOne");
#pragma warning disable 0219
            SerializedProperty massRatio = sizeZero.FindPropertyRelative("massRatio");
            SerializedProperty radiusRatio = sizeZero.FindPropertyRelative("radiusRatio");
#pragma warning restore 0219

            rect.y += 2;
            EditorGUI.PrefixLabel(new Rect(rect.x, rect.y, 20, EditorGUIUtility.singleLineHeight), new GUIContent(index.ToString()));
            EditorGUI.PropertyField(new Rect(rect.x + 20, rect.y, EditorGUIUtility.currentViewWidth - 80, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("dnaEntryName"), new GUIContent("DNA Name", "The string name in the dynamic uma dna asset that controls this morph."));

            rect.y += 2 + EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(new Rect(rect.x + 20, rect.y, EditorGUIUtility.currentViewWidth - 80, EditorGUIUtility.singleLineHeight ), element.FindPropertyRelative("poseZero"), new GUIContent("Pose Zero", "The pose to use when the dna goes from 0.5 to 0.0"));

            rect.y += 2 + EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(new Rect(rect.x + 20, rect.y, EditorGUIUtility.currentViewWidth - 80, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("poseOne"), new GUIContent("Pose One", "The pose to use when the dna goes from 0.5 to 1.0"));

            rect.y += 2 + EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(new Rect(rect.x + 20, rect.y, EditorGUIUtility.currentViewWidth - 80, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("blendShapeZero"), new GUIContent("BlendShape Zero", "The blendshape to use when the dna goes from 0.5 to 0.0"));

            rect.y += 2 + EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(new Rect(rect.x + 20, rect.y, EditorGUIUtility.currentViewWidth - 80, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("blendShapeOne"), new GUIContent("BlendShape One", "The blendshape to use when the dna goes from 0.5 to 1.0"));

            EditorGUI.BeginDisabledGroup(true);
            rect.y += 2 + EditorGUIUtility.singleLineHeight;
            EditorGUI.LabelField(new Rect(rect.x + 20, rect.y, 80, EditorGUIUtility.singleLineHeight), "Size Zero:");

            EditorGUI.LabelField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 440, rect.y, 80, EditorGUIUtility.singleLineHeight), new GUIContent("heightRatio:", "Currently unused"));
            EditorGUI.PropertyField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 360, rect.y, 40, EditorGUIUtility.singleLineHeight), sizeZero.FindPropertyRelative("heightRatio"), GUIContent.none);

            EditorGUI.LabelField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 310, rect.y, 80, EditorGUIUtility.singleLineHeight), new GUIContent("massRatio:", "Currently unused"));
            EditorGUI.PropertyField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 230, rect.y, 40, EditorGUIUtility.singleLineHeight), sizeZero.FindPropertyRelative("massRatio"), GUIContent.none);

            EditorGUI.LabelField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 180, rect.y, 80, EditorGUIUtility.singleLineHeight), new GUIContent("radiusRatio:", "Currently unused"));
            EditorGUI.PropertyField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 100, rect.y, 40, EditorGUIUtility.singleLineHeight), sizeZero.FindPropertyRelative("radiusRatio"), GUIContent.none);

            rect.y += 2 + EditorGUIUtility.singleLineHeight;
            EditorGUI.LabelField(new Rect(rect.x + 20, rect.y, 80, EditorGUIUtility.singleLineHeight), "Size One:");

            EditorGUI.LabelField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 440, rect.y, 80, EditorGUIUtility.singleLineHeight), new GUIContent("heightRatio:", "Currently unused"));
            EditorGUI.PropertyField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 360, rect.y, 40, EditorGUIUtility.singleLineHeight), sizeOne.FindPropertyRelative("heightRatio"), GUIContent.none);

            EditorGUI.LabelField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 310, rect.y, 80, EditorGUIUtility.singleLineHeight), new GUIContent("massRatio:", "Currently unused"));
            EditorGUI.PropertyField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 230, rect.y, 40, EditorGUIUtility.singleLineHeight), sizeOne.FindPropertyRelative("massRatio"), GUIContent.none);

            EditorGUI.LabelField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 180, rect.y, 80, EditorGUIUtility.singleLineHeight), new GUIContent("radiusRatio:", "Currently unused"));
            EditorGUI.PropertyField(new Rect(rect.x + EditorGUIUtility.currentViewWidth - 100, rect.y, 40, EditorGUIUtility.singleLineHeight), sizeOne.FindPropertyRelative("radiusRatio"), GUIContent.none);
            EditorGUI.EndDisabledGroup();
        }
    }
}
#endif
