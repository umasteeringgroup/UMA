using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UMA;

[CustomPropertyDrawer(typeof(DynamicDNAConverterBehaviour.SkeletonModifier))]
public class SkeletonModifierPropertyDrawer : PropertyDrawer
{
    float padding = 2f;
    public List<string> hashNames = new List<string>();
    public List<int> hashes = new List<int>();
    public string[] dnaNames;
    public bool enableSkelModValueEditing = false;
    spValModifierPropertyDrawer thisSpValDrawer = null;

    public void Init(List<string> _hashNames, List<int> _hashes, string[] _dnaNames = null)
    {
        hashNames = _hashNames;
        hashes = _hashes;
        dnaNames = _dnaNames;
        if(thisSpValDrawer == null)
            thisSpValDrawer = new spValModifierPropertyDrawer();
        thisSpValDrawer.dnaNames = dnaNames;
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
        property.isExpanded = EditorGUI.Foldout(valR, property.isExpanded, betterLabel, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            valR = new Rect(valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
            if (hashNames.Count > 0)
            {
                string thisHashName = property.FindPropertyRelative("hashName").stringValue;
                int hashNameIndex = hashNames.IndexOf(thisHashName);
                int newHashNameIndex = hashNameIndex;
                EditorGUI.BeginChangeCheck();
                newHashNameIndex = EditorGUI.Popup(valR, "Hash Name", hashNameIndex, hashNames.ToArray());
                if (EditorGUI.EndChangeCheck())
                {
                    if (newHashNameIndex != hashNameIndex)
                    {
                        property.FindPropertyRelative("hashName").stringValue = hashNames[newHashNameIndex];
                        property.FindPropertyRelative("hash").intValue = hashes[newHashNameIndex];
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
            if(dnaNames == null)
            {
                dnaNames = DynamicDNAConverterBehaviour.SkeletonModifier.spVal.spValValue.spValModifier.spValDNATypeFallback;
            }
            int selectedIndex = -1;
            string[] niceDnaNames = new string[dnaNames.Length +1];
            niceDnaNames[0] = "None";
            for(int i = 0; i < dnaNames.Length; i++)
            {
                niceDnaNames[i+1] = dnaNames[i].BreakupCamelCase();
                if (dnaNames[i] == currentVal)
                {
                    selectedIndex = i;
                }
            }
            int newSelectedIndex = selectedIndex == -1 ? 0 : selectedIndex +1;
            EditorGUI.BeginChangeCheck();
            newSelectedIndex = EditorGUI.Popup(ddTwo, newSelectedIndex, niceDnaNames);
            if (EditorGUI.EndChangeCheck())
            {
                if(newSelectedIndex != selectedIndex +1)
                {
                    property.FindPropertyRelative("DNATypeName").stringValue = dnaNames[newSelectedIndex -1];
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
