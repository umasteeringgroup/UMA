using UnityEditor;
using UnityEngine;
using UMA;
using System;
using System.Collections.Generic;
using System.Reflection;
using UMA.Editors;
using UnityEngine.SceneManagement;
using UMA.CharacterSystem;
using System.Text;
using UMA.CharacterSystem.Editors;

[CustomEditor(typeof(DNA))]
public class DNAEditor : Editor
{
    SerializedProperty nameProp;
    SerializedProperty descriptionProp;
    SerializedProperty defaultValueProp;
    SerializedProperty effectsProp;

    // For adding new DNAEffect
    private int selectedEffectTypeIndex = 0;
    private DNAEffect newEffectInstance = null;
    private Type[] effectTypes;
    private string[] effectTypeNames;
    private bool editorExpanded = true;
    private bool initialized = false;
    private bool showHelp = false;
    private const string PrefKey_AddNewExpanded = "UMA.DNAEditor.AddNewEffectExpanded";
    public DynamicCharacterAvatarEditor dcaContext = null;

    // Static, editor-only context bridge (weak reference to avoid leaks)
    private static WeakReference<DynamicCharacterAvatarEditor> _lastDcaCtx;

    private static DNAEffect CreateEffect(Type effectType, bool logFailure = true)
    {
        if (effectType == null || !typeof(DNAEffect).IsAssignableFrom(effectType) || effectType.IsAbstract || effectType.IsGenericType)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(effectType) as DNAEffect;
        }
        catch (Exception e)
        {
            if (logFailure)
            {
                Debug.LogError($"Unable to create DNA effect type '{effectType.FullName}': {e.Message}");
            }
            return null;
        }
    }

    public static void SetDcaContext(DynamicCharacterAvatarEditor editor)
    {
        if (editor == null) return;
        _lastDcaCtx = new WeakReference<DynamicCharacterAvatarEditor>(editor);
    }

    private void OnEnable()
    {
        // Load persisted UI state
        editorExpanded = EditorPrefs.GetBool(PrefKey_AddNewExpanded, true);

        // Attempt to capture the calling DCA editor if provided
        if (dcaContext == null && _lastDcaCtx != null && _lastDcaCtx.TryGetTarget(out var ctx) && ctx != null)
        {
            dcaContext = ctx;
        }
    }

    private void OnDisable()
    {
        // Save UI state
        EditorPrefs.SetBool(PrefKey_AddNewExpanded, editorExpanded);
        dcaContext = null;
    }

    void Initialize()
    {
        if (initialized) return;
        initialized = true;
        // Find all non-abstract, non-generic subclasses of DNAEffect
        var baseType = typeof(DNAEffect);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var typesList = new List<Type>();
        var namesList = new List<string>();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            if (types == null) continue;
            foreach (var t in types)
            {
                if (t == null) continue;
                if (baseType.IsAssignableFrom(t) && CreateEffect(t, false) != null)
                {
                    typesList.Add(t);
                    namesList.Add(t.Name);
                }
            }
        }
        effectTypes = typesList.ToArray();
        effectTypeNames = namesList.ToArray();
    }

    public override void OnInspectorGUI()
    {
        Initialize();
        DNA targetDNA = target as DNA;

        serializedObject.Update();

        GUILayout.Label("DNA Editor", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Ping DNA Asset", GUILayout.Width(150)))
        {
            EditorGUIUtility.PingObject(target);
        }
        if (GUILayout.Button("Save Now", GUILayout.Width(100)))
        {
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
        if (GUILayout.Button("Rebuild Characters", GUILayout.Width(150)))
        {
            UMAAssetIndexer.RebuildAllUMAS();
        }
        GUILayout.EndHorizontal();
        targetDNA.displayName = EditorGUILayout.DelayedTextField("Display Name", targetDNA.displayName);
        targetDNA.description = EditorGUILayout.DelayedTextField("Description", targetDNA.description);
        targetDNA.defaultValue = EditorGUILayout.Slider("Default Value", targetDNA.defaultValue, 0f, 1f);
        EditorGUILayout.Space();


        // Foldout for Add New Effect with persistence
        bool prevExpanded = editorExpanded;
        editorExpanded = GUIHelper.FoldoutBar(editorExpanded, "Add New Effect Settings");
        if (editorExpanded != prevExpanded)
        {
            EditorPrefs.SetBool(PrefKey_AddNewExpanded, editorExpanded);
        }
        if (editorExpanded)
        {
            ShowAddNew(targetDNA);
        }

        // Draw existing effects
        EditorGUILayout.Space();
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Existing Effects", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        showHelp = GUILayout.Toggle(showHelp, "Show Help", "Button", GUILayout.Width(100));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All", GUILayout.Width(80)))
        {
            foreach (var effect in targetDNA.effects)
            {
                effect.selected = true;
            }
        }
        if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
        {
            foreach (var effect in targetDNA.effects)
            {
                effect.selected = false;
            }
        }
        if (GUILayout.Button("Remove Selected", GUILayout.Width(130)))
        {
            targetDNA.effects.RemoveAll(e => e.selected);
            serializedObject.Update();
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Expand All", GUILayout.Width(100)))
        {
            foreach (var effect in targetDNA.effects)
            {
                effect.expanded = true;
            }
        }
        if (GUILayout.Button("Collapse All", GUILayout.Width(100)))
        {
            foreach (var effect in targetDNA.effects)
            {
                effect.expanded = false;
            }
        }
        GUILayout.EndHorizontal();
        int deleteme = -1;
        for (int i = 0; i < targetDNA.effects.Count; i++)
        {
            deleteme = ShowEffect(targetDNA, deleteme, i);
        }
        if (deleteme >= 0)
        {
            targetDNA.effects.RemoveAt(deleteme);
            serializedObject.Update();
        }

        EditorGUILayout.Space();


        serializedObject.ApplyModifiedProperties();
    }

    private int ShowEffect(DNA targetDNA, int deleteme, int i)
    {
        EditorGUI.BeginChangeCheck();
        var effect = targetDNA.effects[i];
        if (effect == null)
        {
            // draw "effect is null" message. Give option to remove it
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Effect at index {i} is null. Remove it?", EditorStyles.boldLabel);
            if (GUILayout.Button("Remove"))
            {
                deleteme = i;
            }
            EditorGUILayout.EndVertical();
            return deleteme;
        }
        bool deletemeFlag = false;
        bool selected = effect.selected;
        effect.expanded = GUIHelper.FoldoutBarWithDeleteAndSelect(effect.expanded, effect.title, ref selected, out deletemeFlag);
        effect.selected = selected;

        if (deletemeFlag)
        {
            deleteme = i; // Mark this index for deletion
            return deleteme;
        }
        // Draw the effect's GUI
        if (effect.expanded)
        {
            GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{effect.GetType().Name}: {effect.EffectName}", EditorStyles.boldLabel);
            if (GUILayout.Button("Duplicate", GUILayout.Width(100)))
            {
                // Deep clone the effect and add it
                var clone = CloneDNAEffect(effect);
                (target as DNA).effects.Add(clone);
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                BuildCharacterIfPossible();
            }
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                effectsProp?.DeleteArrayElementAtIndex(i);
                deleteme = i; // Mark this index for deletion
            }
            EditorGUILayout.EndHorizontal();

            // Custom drawing for bone effects to add bone picker next to text field
            if (!DrawBoneEffectGUIWithPicker(effect, targetDNA, showHelp))
            {
                // Fallback to default GUI
                effect.DoGui(true, showHelp, out AnimationCurve curveToCopy);
                CopyCurveToSelected(effect, targetDNA, curveToCopy);
            }

            // Extra utility button for bone-based effects
            DrawSelectBoneButtonIfApplicable(effect);

            GUIHelper.EndVerticalPadded();
        }
        if (EditorGUI.EndChangeCheck())
        {
            BuildCharacterIfPossible();
        }
        return deleteme;
    }

    private void CopyCurveToSelected(DNAEffect effect, DNA targetDNA, AnimationCurve curveToCopy)
    {
        if (curveToCopy != null)
        {
            // Copy curve data to all effects that are selected
            foreach (var e in targetDNA.effects)
            {
                if (e != effect && e != null)
                {
                    if (e.selected)
                    {
                        e.curve = curveToCopy;
                    }
                }
            }
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            BuildCharacterIfPossible();
        }
    }

    private void BuildCharacterIfPossible()
    {
        // If we have a DCA context, trigger a rebuild
        if (dcaContext != null)
        {
            dcaContext.GenerateSingleUMA();
        }
    }

    private void ShowAddNew(DNA targetDNA)
    {
        GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
        EditorGUILayout.LabelField("Add the New Effect", EditorStyles.boldLabel);

        if (effectTypes != null && effectTypes.Length > 0)
        {
            selectedEffectTypeIndex = EditorGUILayout.Popup("Effect Type", selectedEffectTypeIndex, effectTypeNames);

            // Create a new instance if needed or if type changed
            Type selectedType = effectTypes[selectedEffectTypeIndex];
            if (newEffectInstance == null || newEffectInstance.GetType() != selectedType)
            {
                newEffectInstance = CreateEffect(selectedType);
            }

            // Draw fields for the new effect instance
            if (newEffectInstance != null)
            {
                // If it's a bone effect, draw using our picker-enabled GUI to match inspector behavior
                if (!DrawBoneEffectGUIWithPicker(newEffectInstance, target as DNA, showHelp))
                {
                    newEffectInstance.DoGui(true, showHelp, out AnimationCurve curveToCopy);
                    if (curveToCopy != null)
                    {
                        CopyCurveToSelected(null, targetDNA, curveToCopy);
                    }
                }

                DrawCopyEffectsDropPad(targetDNA);

                if (GUILayout.Button("Add Effect"))
                {
                    // Deep clone to avoid sharing the temp instance
                    var clone = CloneDNAEffect(newEffectInstance);
                    if (clone != null)
                    {
                        (target as DNA).effects.Add(clone);
                        newEffectInstance = CreateEffect(selectedType); // reset for next add
                        EditorUtility.SetDirty(target);
                        AssetDatabase.SaveAssetIfDirty(target);
                        BuildCharacterIfPossible();
                    }
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No DNAEffect types found.", MessageType.Warning);
        }
        GUIHelper.EndVerticalPadded();
    }

    // Helper to deep clone a DNAEffect (since Unity doesn't serialize managedReferenceValue directly from a temp object)
    private DNAEffect CloneDNAEffect(DNAEffect effect)
    {
        var type = effect.GetType();
        var clone = CreateEffect(type);
        if (clone == null)
        {
            return null;
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.IsNotSerialized) continue;
            if (field.Name.ToLower() == "curve") 
            {
                var originalCurve = field.GetValue(effect) as AnimationCurve;
                if (originalCurve != null)
                {
                    var curveCopy = new AnimationCurve(originalCurve.keys);
                    field.SetValue(clone, curveCopy);
                }
            }
            else
            {
                field.SetValue(clone, field.GetValue(effect));
            }
            //field.SetValue(clone, field.GetValue(effect));
        }
        return clone;
    }

    // Draws the bone selection button for applicable DNAEffects
    private void DrawSelectBoneButtonIfApplicable(DNAEffect effect)
    {
        string boneName = null;
        if (effect is DNAEffect_BoneTranslate bt)
        {
            boneName = bt.BoneName;
        }
        else if (effect is DNAEffect_BoneRotate br)
        {
            boneName = br.BoneName;
        }
        else if (effect is DNAEffect_BoneScale bs)
        {
            boneName = bs.BoneName;
        }
        else if (effect is DNAEffect_BoneTransform btf)
        {
            boneName = btf.boneName;
        }

        if (boneName == null)
        {
            return; // not a bone effect
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = !string.IsNullOrEmpty(boneName);
        if (GUILayout.Button("Select Bone in Hierarchy"))
        {
            SelectBoneInSelectedDCA(boneName);
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    private static void SelectBoneInSelectedDCA(string boneName)
    {
        if (string.IsNullOrEmpty(boneName)) return;
        var active = Selection.activeGameObject;
        if (active == null) return; // nothing selected

        var dca = active.GetComponent<DynamicCharacterAvatar>();
        if (dca == null) return; // do nothing if no DCA on the selection

        GameObject boneGO = null;
        try
        {
            // Prefer UMA skeleton lookup if available
            boneGO = dca.GetBoneGameObject(boneName);
        }
        catch { }

        if (boneGO == null)
        {
            // Fallback: search by name in hierarchy (include inactive)
            var t = FindChildRecursive(active.transform, boneName);
            if (t != null) boneGO = t.gameObject;
        }

        if (boneGO != null)
        {
            Selection.activeGameObject = boneGO;
            EditorGUIUtility.PingObject(boneGO);
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
        }
        // else: silently do nothing
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent == null) return null;
        // breadth-first search
        var queue = new Queue<Transform>();
        queue.Enqueue(parent);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.name == name) return current;
            for (int i = 0; i < current.childCount; i++)
            {
                queue.Enqueue(current.GetChild(i));
            }
        }
        return null;
    }

    // Returns true if handled with custom drawer
    private bool DrawBoneEffectGUIWithPicker(DNAEffect effect, DNA owner, bool showHelp)
    {
        // Only show picker when active selection has a DCA
        var activeGO = Selection.activeGameObject;
        var dca = activeGO != null ? activeGO.GetComponent<DynamicCharacterAvatar>() : null;

        if (effect is DNAEffect_BoneTranslate e1)
        {
            DrawBoneFieldWithMenu(() => e1.BoneName, v => e1.BoneName = v, dca, owner, "Bone Name", effect);
            e1.Translation = EditorGUILayout.Vector3Field("Translation", e1.Translation);
            // Draw common controls from base
            DrawEffectCommon(effect, showHelp);
            return true;
        }
        if (effect is DNAEffect_BoneRotate e2)
        {
            DrawBoneFieldWithMenu(() => e2.BoneName, v => e2.BoneName = v, dca, owner, "Bone Name", effect);
            e2.RotationAxis = EditorGUILayout.Vector3Field("Rotation Axis", e2.RotationAxis);
            e2.RotationAngle = EditorGUILayout.FloatField("Rotation Angle (degrees)", e2.RotationAngle);
            DrawEffectCommon(e2, showHelp);
            return true;
        }
        if (effect is DNAEffect_BoneScale e3)
        {
            DrawBoneFieldWithMenu(() => e3.BoneName, v => e3.BoneName = v, dca, owner, "Bone Name", effect);
            GUILayout.BeginHorizontal();
            e3.ScaleFactor = EditorGUILayout.Vector3Field("Scale Factor", e3.ScaleFactor);
            if (GUILayout.Button("Uniform", GUILayout.MaxWidth(80)))
            {
                float uniform = EditorGUILayout.FloatField("Uniform Scale", e3.ScaleFactor.x);
                e3.ScaleFactor = new Vector3(uniform, uniform, uniform);
            }
            if (GUILayout.Button("Copy to Selected", GUILayout.MaxWidth(120)))
            {
                foreach (var otherEffect in (owner as DNA).effects)
                {
                    if (otherEffect != effect && otherEffect is DNAEffect_BoneScale otherScaleEffect)
                    {
                        if (otherScaleEffect.selected)
                        {
                            otherScaleEffect.ScaleFactor = e3.ScaleFactor;
                        }
                    }
                }
                EditorUtility.SetDirty(owner);
                AssetDatabase.SaveAssetIfDirty(owner);
                BuildCharacterIfPossible();
            }
            GUILayout.EndHorizontal();
            DrawEffectCommon(effect, showHelp);
            return true;
        }
        if (effect is DNAEffect_BoneTransform e4)
        {
            DrawBoneFieldWithMenu(() => e4.boneName, v => e4.boneName = v, dca, owner, "Bone Name", effect);
            e4.Position = EditorGUILayout.Vector3Field("Position", e4.Position);
            e4.Rotation = EditorGUILayout.Vector3Field("Rotation", e4.Rotation);
            e4.Scale = EditorGUILayout.Vector3Field("Scale", e4.Scale);
            DrawEffectCommon(effect, showHelp);
            return true;
        }
        return false;
    }

    private void DrawEffectCommon(DNAEffect effect, bool showHelp)
    {
        // Replicate the common fields from DNAEffect.DoGui
        effect.EffectName = EditorGUILayout.DelayedTextField("Effect Name", effect.EffectName);
        GUILayout.BeginHorizontal();
        effect.curve = EditorGUILayout.CurveField("Curve", effect.curve);
        if (effect.curve != null)
        {
            if (GUILayout.Button("Reset", GUILayout.MaxWidth(50)))
            {
                effect.curve = null;
            }
            if (GUILayout.Button("Zero", GUILayout.MaxWidth(50)))
            {
                effect.curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 0));
            }
            if (GUILayout.Button("Copy to Selected", GUILayout.MaxWidth(120)))
            {
                // Copy this curve to all selected effects
                // Fix: Use owner reference instead of missing targetDNA
                CopyCurveToSelected(effect, target as DNA, effect.curve);
            }
        }
        else
        {
            if (GUILayout.Button("Set Linear", GUILayout.MaxWidth(100)))
            {
                effect.curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
            }
        }
        GUILayout.EndHorizontal();
        if (showHelp)
        {
            EditorGUILayout.HelpBox(effect.Description, MessageType.Info);
        }
        effect.minMapping = EditorGUILayout.DelayedFloatField("Min", effect.minMapping);
        effect.maxMapping = EditorGUILayout.DelayedFloatField("Max", effect.maxMapping);
        EditorGUILayout.HelpBox("You can load a template curve here. This will set the Min, Max and Curve values to the values in the template curve. The template curve is not saved.", MessageType.Info);
        var curveAsset = (DNACurve)EditorGUILayout.ObjectField("Template Curve", null, typeof(DNACurve), false);
        if (curveAsset != null)
        {
            effect.minMapping = curveAsset.minMapping;
            effect.maxMapping = curveAsset.maxMapping;
            effect.curve = curveAsset.Curve;
        }
    }

    private void DrawCopyEffectsDropPad(DNA owner)
    {
        Rect dropArea = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drop DNA here to copy the effects to this DNA");

        Event evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition))
        {
            return;
        }

        if (evt.type == EventType.DragUpdated)
        {
            bool validDrop = false;
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                DNA droppedDNA = DragAndDrop.objectReferences[i] as DNA;
                if (droppedDNA != null && droppedDNA != owner)
                {
                    validDrop = true;
                    break;
                }
            }

            DragAndDrop.visualMode = validDrop ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            evt.Use();
            return;
        }

        if (evt.type == EventType.DragPerform)
        {
            DNA sourceDNA = null;
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                DNA droppedDNA = DragAndDrop.objectReferences[i] as DNA;
                if (droppedDNA != null && droppedDNA != owner)
                {
                    sourceDNA = droppedDNA;
                    break;
                }
            }

            if (sourceDNA == null)
            {
                evt.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            Undo.RecordObject(owner, "Copy DNA Effects");
            owner.effects.Clear();
            if (sourceDNA.effects != null)
            {
                for (int i = 0; i < sourceDNA.effects.Count; i++)
                {
                    DNAEffect effect = sourceDNA.effects[i];
                    if (effect != null)
                    {
                        owner.effects.Add(CloneDNAEffect(effect));
                    }
                }
            }

            EditorUtility.SetDirty(owner);
            AssetDatabase.SaveAssetIfDirty(owner);
            serializedObject.Update();
            BuildCharacterIfPossible();
            Repaint();
            evt.Use();
        }
    }

    private void DrawBoneFieldWithMenu(Func<string> getBone, Action<string> setBone, DynamicCharacterAvatar dca, DNA owner, string label, DNAEffect effect)
    {
        EditorGUILayout.BeginHorizontal();
        string current = getBone();
        string edited = EditorGUILayout.DelayedTextField(label, current);
        if (!string.Equals(edited, current, StringComparison.Ordinal))
        {
            Undo.RecordObject(owner, "Set Bone Name");
            setBone(edited);
            EditorUtility.SetDirty(owner);
            BuildCharacterIfPossible();
        }
        using (new EditorGUI.DisabledScope(dca == null))
        {
            if (GUILayout.Button("\u25BE", GUILayout.Width(24)))
            {
                ShowBonePickerMenu(dca, (picked) =>
                {
                    Undo.RecordObject(owner, "Set Bone Name");
                    setBone(picked);
                    // If EffectName is blank, set a helpful default label
                    if (string.IsNullOrEmpty(effect.EffectName))
                    {
                        effect.EffectName = $"{effect.baseEffectName}: {picked}";
                    }
                    EditorUtility.SetDirty(owner);
                    Repaint();
                });
            }
        }
        EditorGUILayout.EndHorizontal();
        if (dca == null && showHelp)
        {
            EditorGUILayout.HelpBox("Select a DynamicCharacterAvatar in the hierarchy to enable the bone picker.", MessageType.Info);
        }
    }

    private void ShowBonePickerMenu(DynamicCharacterAvatar dca, Action<string> onPicked)
    {
        if (dca == null) return;
        var names = CollectBoneNames(dca);
        var menu = new GenericMenu();
        // Group by first camel word
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            string first;
            string rest;
            SplitCamelFirstAndRest(name, out first, out rest);
            if (string.IsNullOrEmpty(first)) first = name;
            if (string.IsNullOrEmpty(rest)) rest = name; // ensure selectable item
            var content = new GUIContent(first + "/" + rest);
            menu.AddItem(content, false, (obj) =>
            {
                onPicked?.Invoke((string)obj);
            }, name);
        }
        if (names.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("No bones found"));
        }
        menu.ShowAsContext();
    }

    private List<string> CollectBoneNames(DynamicCharacterAvatar dca)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var skeleton = dca != null ? dca.skeleton : null;
            if (skeleton != null)
            {
                var pairs = skeleton.GetBoneHashNames();
                if (pairs != null)
                {
                    for (int i = 0; i < pairs.Count; i++)
                    {
                        var name = pairs[i].Value;
                        if (!string.IsNullOrEmpty(name)) set.Add(name);
                    }
                }
            }
        }
        catch { }

        if (set.Count == 0 && dca != null)
        {
            // Fallback: traverse hierarchy
            var trs = dca.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trs.Length; i++)
            {
                var n = trs[i].name;
                if (!string.IsNullOrEmpty(n)) set.Add(n);
            }
        }
        var list = new List<string>(set.Count);
        foreach (string entry in set)
        {
            list.Add(entry);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static void SplitCamelFirstAndRest(string src, out string first, out string rest)
    {
        first = string.Empty;
        rest = string.Empty;
        if (string.IsNullOrEmpty(src)) return;
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            if (i > 0 && char.IsUpper(c))
            {
                if (sb.Length > 0)
                {
                    parts.Add(sb.ToString());
                    sb.Length = 0;
                }
            }
            sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        if (parts.Count == 0)
        {
            first = src;
            rest = src;
            return;
        }
        first = parts[0];
        if (parts.Count > 1)
        {
            var restBuilder = new StringBuilder();
            for (int partIndex = 1; partIndex < parts.Count; partIndex++)
            {
                restBuilder.Append(parts[partIndex]);
            }
            rest = restBuilder.ToString();
        }
        else
        {
            rest = src; // single-part names still select the full name
        }
    }
}