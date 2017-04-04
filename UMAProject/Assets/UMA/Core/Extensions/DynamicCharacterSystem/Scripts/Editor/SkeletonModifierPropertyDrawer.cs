using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

namespace UMA.CharacterSystem.Editors
{
    [CustomPropertyDrawer(typeof(DynamicDNAConverterBehaviour.SkeletonModifier))]
    public class SkeletonModifierPropertyDrawer : PropertyDrawer
    {
        float padding = 2f;
        public List<string> hashNames = new List<string>();
        //used when editing in playmode has the actual boneHashes from the avatars skeleton
        public List<string> bonesInSkeleton = null;
        public List<int> hashes = new List<int>();
        public string[] dnaNames;
        public bool enableSkelModValueEditing = false;
        spValModifierPropertyDrawer thisSpValDrawer = null;
        Texture warningIcon;
        GUIStyle warningStyle;

        public void Init(List<string> _hashNames, List<int> _hashes, string[] _dnaNames = null)
        {
            hashNames = _hashNames;
            hashes = _hashes;
            dnaNames = _dnaNames;
            if(thisSpValDrawer == null)
                thisSpValDrawer = new spValModifierPropertyDrawer();
            thisSpValDrawer.dnaNames = dnaNames;
            if (warningIcon == null)
            {
                warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
                warningStyle = new GUIStyle(EditorStyles.label);
                warningStyle.fixedHeight = warningIcon.height + 4f;
                warningStyle.contentOffset = new Vector2(0, -2f);
            }
        }

        public void UpdateHashNames(List<string> _hashNames, List<int> _hashes)
        {
            hashNames = _hashNames;
            hashes = _hashes;
        }
        public void UpdateDnaNames(string[] newDnaNames)
        {
            if(thisSpValDrawer != null)
                thisSpValDrawer.dnaNames = newDnaNames;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            int startingIndent = EditorGUI.indentLevel;
            EditorGUI.BeginProperty(position, label, property);
            var valR = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
            string betterLabel = label.text;
            if (property.FindPropertyRelative("property").enumDisplayNames[property.FindPropertyRelative("property").enumValueIndex] != "")
            {
                betterLabel += " (" + property.FindPropertyRelative("property").enumDisplayNames[property.FindPropertyRelative("property").enumValueIndex] + ")";
            }
            List<string> boneNames = new List<string>();
            if (bonesInSkeleton != null)
            {
                boneNames = new List<string>(bonesInSkeleton);
            }
            else
            {
                boneNames = new List<string>(hashNames);
            }
            string thisHashName = property.FindPropertyRelative("hashName").stringValue;
            int hashNameIndex = boneNames.IndexOf(thisHashName);
            if (hashNameIndex == -1 && bonesInSkeleton != null)
            {
                boneNames.Insert(0, thisHashName + " (missing)");
                hashNameIndex = 0;
                var warningRect = new Rect((valR.xMin), valR.yMin, 20f, valR.height);
                var warningIconGUI = new GUIContent("", thisHashName + " was not a bone in the Avatars Skeleton. Please choose another bone for this modifier or delete it.");
                warningIconGUI.image = warningIcon;
                betterLabel += " (missing)";
                GUI.Label(warningRect, warningIconGUI, warningStyle);
            }
            property.isExpanded = EditorGUI.Foldout(valR, property.isExpanded, betterLabel, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                valR = new Rect(valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
                if (boneNames.Count > 0)
                {
                    int newHashNameIndex = hashNameIndex;
                    EditorGUI.BeginChangeCheck();
                    newHashNameIndex = EditorGUI.Popup(valR, "Hash Name", hashNameIndex, boneNames.ToArray());
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (newHashNameIndex != hashNameIndex)
                        {
                            property.FindPropertyRelative("hashName").stringValue = boneNames[newHashNameIndex];
                            property.FindPropertyRelative("hash").intValue = UMAUtils.StringToHash(boneNames[newHashNameIndex]);
                            property.serializedObject.ApplyModifiedProperties();
                        }
                    }
                }
                else
                {
                    EditorGUI.PropertyField(valR, property.FindPropertyRelative("hashName"));
                }
                valR = new Rect(valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(valR, property.FindPropertyRelative("property"));
                valR = new Rect(valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
                var valXR = valR;
                var valYR = valR;
                var valZR = valR;
                valXR.width = valYR.width = valZR.width = valR.width / 3;
                valYR.x = valYR.x + valXR.width;
                valZR.x = valZR.x + valXR.width + valYR.width;
                SerializedProperty subValsToOpen = null;
                var valuesX = property.FindPropertyRelative("valuesX");
                var valuesY = property.FindPropertyRelative("valuesY");
                var valuesZ = property.FindPropertyRelative("valuesZ");
                if (valuesX.isExpanded)
                {
                    var valXRB = valXR;
                    valXRB.x = valXRB.x + (EditorGUI.indentLevel * 10f);
                    valXRB.width = valXRB.width - (EditorGUI.indentLevel * 10f);
                    EditorGUI.DrawRect(valXRB, new Color32(255, 255, 255, 100));
                }
                valuesX.isExpanded = EditorGUI.Foldout(valXR, valuesX.isExpanded, "ValuesX", true);
                if (valuesX.isExpanded)
                {
                    valuesY.isExpanded = false;
                    valuesZ.isExpanded = false;
                    subValsToOpen = valuesX;
                }
                if (valuesY.isExpanded)
                {
                    EditorGUI.DrawRect(valYR, new Color32(255, 255, 255, 100));
                }
                valuesY.isExpanded = EditorGUI.Foldout(valYR, valuesY.isExpanded, "ValuesY", true);
                if (valuesY.isExpanded)
                {
                    valuesX.isExpanded = false;
                    valuesZ.isExpanded = false;
                    subValsToOpen = valuesY;
                }
                if (valuesZ.isExpanded)
                {
                    EditorGUI.DrawRect(valZR, new Color32(255, 255, 255, 100));
                }
                valuesZ.isExpanded = EditorGUI.Foldout(valZR, valuesZ.isExpanded, "ValuesZ", true);
                if (valuesZ.isExpanded)
                {
                    valuesX.isExpanded = false;
                    valuesY.isExpanded = false;
                    subValsToOpen = valuesZ;
                }
                if (subValsToOpen != null)
                {
                    valR = new Rect(valR.xMin, valR.yMax + padding + 4f, valR.width, EditorGUIUtility.singleLineHeight);
                    valR.width = valR.width - 30;
                    var boxR1 = valR;
                    boxR1.x = boxR1.x + EditorGUI.indentLevel * 10f;
                    boxR1.y = boxR1.y - 6;
                    boxR1.height = boxR1.height + 12;
                    //topbox
                    EditorGUI.DrawRect(boxR1, new Color32(255, 255, 255, 100));
                    var valSXR = valR;
                    var valSYR = valR;
                    var valSZR = valR;
                    valSXR.width = valSYR.width = valSZR.width = valR.width / 3;
                    valSYR.x = valSYR.x + valSXR.width;
                    valSZR.x = valSZR.x + valSXR.width + valSYR.width;
                    var subValuesVal = subValsToOpen.FindPropertyRelative("val");
                    var subValuesMin = subValsToOpen.FindPropertyRelative("min");
                    var subValuesMax = subValsToOpen.FindPropertyRelative("max");
                    var valSXRF = valSXR;
                    valSXRF.x = valSXRF.x + 38f;
                    valSXRF.width = valSXRF.width - 35f;
                    if (!enableSkelModValueEditing) EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.LabelField(valSXR, "Value");
                    subValuesVal.FindPropertyRelative("value").floatValue = EditorGUI.FloatField(valSXRF, subValuesVal.FindPropertyRelative("value").floatValue);
                    subValuesVal.serializedObject.ApplyModifiedProperties();
                    if (!enableSkelModValueEditing) EditorGUI.EndDisabledGroup();
                    var valSYRF = valSYR;
                    valSYRF.x = valSYRF.x + 30f;
                    valSYRF.width = valSYRF.width - 30f;
                    EditorGUI.LabelField(valSYR, "Min");
                    subValuesMin.floatValue = EditorGUI.FloatField(valSYRF, subValuesMin.floatValue);
                    var valSZRF = valSZR;
                    valSZRF.x = valSZRF.x + 30f;
                    valSZRF.width = valSZRF.width - 30f;
                    EditorGUI.LabelField(valSZR, "Max");
                    subValuesMax.floatValue = EditorGUI.FloatField(valSZRF, subValuesMax.floatValue);
                    var thisModifiersProp = subValuesVal.FindPropertyRelative("modifiers");
                    var modifiersi = thisModifiersProp.arraySize;
                    valR = new Rect(valR.xMin, valR.yMax + padding + 4f, valR.width, EditorGUIUtility.singleLineHeight);
                    var boxR = valR;
                    boxR.y = boxR.y - 2f;
                    boxR.x = boxR.x + EditorGUI.indentLevel * 10f;
                    boxR.height = boxR.height + 6f + ((EditorGUIUtility.singleLineHeight + padding) * (modifiersi + 1));
                    //bottombox
                    EditorGUI.DrawRect(boxR, new Color32(255, 255, 255, 100));
                    EditorGUI.LabelField(valR, "Value Modifiers");
                    for (int i = 0; i < modifiersi; i++)
                    {
                        valR = new Rect(valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
                        var propsR = valR;
                        propsR.width = valR.width;
                        var valRBut = new Rect((propsR.width + 35f), valR.y, 20f, EditorGUIUtility.singleLineHeight);
                        thisSpValDrawer.OnGUI(propsR, thisModifiersProp.GetArrayElementAtIndex(i), new GUIContent(""));
                        if (GUI.Button(valRBut, "X"))
                        {
                            thisModifiersProp.DeleteArrayElementAtIndex(i);
                        }
                    }
                    var addBut = new Rect(valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
                    addBut.x = addBut.xMax - 35f;
                    addBut.width = 60f;
                    if (GUI.Button(addBut, "Add"))
                    {
                        thisModifiersProp.InsertArrayElementAtIndex(modifiersi);
                    }
                    thisModifiersProp.serializedObject.ApplyModifiedProperties();
                }

            }
            EditorGUI.indentLevel = startingIndent;
            EditorGUI.EndProperty();
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = EditorGUIUtility.singleLineHeight + padding;
            int extraLines = 1;
            float extrapix = 0;
            if (property.isExpanded)
            {
                extraLines = extraLines + 3;
                var valuesX = property.FindPropertyRelative("valuesX");
                var valuesY = property.FindPropertyRelative("valuesY");
                var valuesZ = property.FindPropertyRelative("valuesZ");
                if (valuesX.isExpanded || valuesY.isExpanded || valuesZ.isExpanded)
                {
                    extraLines++;
                    var activeValues = valuesX.isExpanded ? valuesX : (valuesY.isExpanded ? valuesY : valuesZ);
                    var subValuesVal = activeValues.FindPropertyRelative("val");
                    extraLines++;
                    extraLines++;
                    extraLines += subValuesVal.FindPropertyRelative("modifiers").arraySize;
                    extrapix = 10f;
                }
                h *= (extraLines);
                h += extrapix;
            }
            return h;
        }
    }

    [CustomPropertyDrawer(typeof(DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier))]
    public class spValModifierPropertyDrawer : PropertyDrawer
    {
        public string[] dnaNames;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            int startingIndent = EditorGUI.indentLevel;
            EditorGUI.BeginProperty(position, label, property);
            var valR = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
            var ddOne = valR;
            var ddTwo = valR;
            ddOne.width = valR.width / 2f + ((EditorGUI.indentLevel - 1) * 10);
            ddTwo.width = valR.width / 2f - ((EditorGUI.indentLevel - 1) * 10);
            ddTwo.x = ddTwo.x + ddOne.width;
            var modifieri = property.FindPropertyRelative("modifier").enumValueIndex;
            EditorGUI.PropertyField(ddOne, property.FindPropertyRelative("modifier"), new GUIContent(""));
            EditorGUI.indentLevel = 0;
            if (modifieri > 3)
            {
                string currentVal = property.FindPropertyRelative("DNATypeName").stringValue;
                if (dnaNames == null || dnaNames.Length == 0)
                {
                    //If there are no names show a field with the dna name in it with a warning tooltip
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.LabelField(ddTwo, new GUIContent(property.FindPropertyRelative("DNATypeName").stringValue, "You do not have any DNA Names set up in your DNA asset. Add some names for the Skeleton Modifiers to use."), EditorStyles.textField);
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    int selectedIndex = -1;
                    List<GUIContent> niceDNANames = new List<GUIContent>();
                    niceDNANames.Add(new GUIContent("None"));
                    bool missing = currentVal != "";
                    for (int i = 0; i < dnaNames.Length; i++)
                    {
                        niceDNANames.Add(new GUIContent(dnaNames[i]));
                        if (dnaNames[i] == currentVal)
                        {
                            selectedIndex = i;
                            missing = false;
                        }
                    }
                    if (missing)
                    {
                        niceDNANames[0].text = currentVal+" (missing)";
                        niceDNANames[0].tooltip = currentVal+ " was not in the DNAAssets names list. This modifier wont do anything until you change the dna name it uses or you add this name to your DNA Asset names.";
                    }
                    int newSelectedIndex = selectedIndex == -1 ? 0 : selectedIndex + 1;
                    EditorGUI.BeginChangeCheck();
                    newSelectedIndex = EditorGUI.Popup(ddTwo, newSelectedIndex,  niceDNANames.ToArray());
                    if (EditorGUI.EndChangeCheck())
                    {
                        //if its actually changed
                        if (newSelectedIndex != selectedIndex + 1)
                        {
                            if (newSelectedIndex == 0)
                            {
                                if(niceDNANames[0].text.IndexOf("(missing) ") < 0)
                                    property.FindPropertyRelative("DNATypeName").stringValue = "";
                            }
                            else
                            {
                                property.FindPropertyRelative("DNATypeName").stringValue = dnaNames[newSelectedIndex - 1];
                            }
                        }
                    }
                }
            }
            else
            {
                EditorGUI.PropertyField(ddTwo, property.FindPropertyRelative("modifierValue"), new GUIContent(""));
            }
            EditorGUI.indentLevel = startingIndent;
            EditorGUI.EndProperty();
        }
    }
}
