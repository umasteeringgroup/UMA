#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[CustomEditor(typeof(UMAAnimationParameterSetter))]
public class UMAAnimationParameterSetterEditor : Editor
{
    private UMAAnimationParameterSetter _setter;
    private bool _showClips = true;
    private Vector2 _clipScroll;
    private Vector2 _paramScroll;

    private void OnEnable()
    {
        _setter = (UMAAnimationParameterSetter)target;
    }

    public override void OnInspectorGUI()
    {
        if (_setter == null) return;

        var animator = _setter.GetComponent<Animator>();
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Target", _setter, typeof(UMAAnimationParameterSetter), true);
            EditorGUILayout.ObjectField("Animator", animator, typeof(Animator), true);
        }

        if (animator == null)
        {
            EditorGUILayout.HelpBox("No Animator found on this GameObject.", MessageType.Warning);
            return;
        }

        var controller = ResolveAnimatorController(animator);
        if (controller == null)
        {
            EditorGUILayout.HelpBox("Animator has no valid AnimatorController.", MessageType.Warning);
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(controller == null))
        {
            if (GUILayout.Button("Read Animations & Parameters"))
            {
                Undo.RecordObject(_setter, "Read Animations & Parameters");
                ReadClips(controller, _setter.controllerClips);
                SyncParameters(animator, controller, _setter.parameters);
                EditorUtility.SetDirty(_setter);
            }
        }

        // Clips foldout
        _showClips = EditorGUILayout.Foldout(_showClips, $"Discovered Clips ({_setter.controllerClips.Count})", true);
        if (_showClips)
        {
            _clipScroll = EditorGUILayout.BeginScrollView(_clipScroll, GUILayout.MinHeight(80), GUILayout.MaxHeight(200));
            if (_setter.controllerClips.Count == 0)
            {
                EditorGUILayout.LabelField("(No clips discovered. Click 'Read Animations & Parameters'.)");
            }
            else
            {
                foreach (var clipName in _setter.controllerClips)
                {
                    EditorGUILayout.LabelField(clipName);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animator Parameters", EditorStyles.boldLabel);

        // Parameters UI
        if (_setter.parameters == null || _setter.parameters.Count == 0)
        {
            EditorGUILayout.HelpBox("No parameters found. Click 'Read Animations & Parameters'.", MessageType.Info);
        }
        else
        {
            var so = serializedObject;
            so.Update();

            var parametersProp = so.FindProperty("parameters");
            _paramScroll = EditorGUILayout.BeginScrollView(_paramScroll, GUILayout.MinHeight(140), GUILayout.MaxHeight(320));

            for (int i = 0; i < parametersProp.arraySize; i++)
            {
                var elem = parametersProp.GetArrayElementAtIndex(i);
                var nameProp = elem.FindPropertyRelative("name");
                var typeProp = elem.FindPropertyRelative("type");
                var floatProp = elem.FindPropertyRelative("floatValue");
                var intProp = elem.FindPropertyRelative("intValue");
                var boolProp = elem.FindPropertyRelative("boolValue");

                // Robust enum resolution (enumValueIndex can differ from underlying int)
                AnimatorControllerParameterType pType;
                int rawEnumValue = typeProp.intValue;
                if (System.Enum.IsDefined(typeof(AnimatorControllerParameterType), rawEnumValue))
                    pType = (AnimatorControllerParameterType)rawEnumValue;
                else
                {
                    // Fallback parse
                    string[] enumNames = typeProp.enumNames;
                    int idx = typeProp.enumValueIndex;
                    if (idx >= 0 && idx < enumNames.Length && System.Enum.TryParse(enumNames[idx], out AnimatorControllerParameterType parsed))
                        pType = parsed;
                    else
                        pType = AnimatorControllerParameterType.Float;
                }

                string pName = nameProp.stringValue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(pName, GUILayout.Width(180));

                EditorGUI.BeginChangeCheck();
                switch (pType)
                {
                    case AnimatorControllerParameterType.Float:
                        // Range 1-1000 as requested
                        floatProp.floatValue = EditorGUILayout.Slider(floatProp.floatValue, 1f, 1000f);
                        break;
                    case AnimatorControllerParameterType.Int:
                        intProp.intValue = EditorGUILayout.IntField(intProp.intValue);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        boolProp.boolValue = EditorGUILayout.Toggle(boolProp.boolValue);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        EditorGUI.BeginDisabledGroup(animator == null);
                        if (GUILayout.Button("Set Trigger", GUILayout.Width(110)))
                        {
                            animator.ResetTrigger(pName);
                            animator.SetTrigger(pName);
                        }
                        EditorGUI.EndDisabledGroup();
                        break;
                }
                bool changed = EditorGUI.EndChangeCheck();

                // Immediate apply on change (except trigger handled by button)
                if (changed && animator != null)
                {
                    Undo.RecordObject(animator, "Change Animator Parameter");
                    switch (pType)
                    {
                        case AnimatorControllerParameterType.Float:
                            animator.SetFloat(pName, floatProp.floatValue);
                            break;
                        case AnimatorControllerParameterType.Int:
                            animator.SetInteger(pName, intProp.intValue);
                            break;
                        case AnimatorControllerParameterType.Bool:
                            animator.SetBool(pName, boolProp.boolValue);
                            break;
                    }
                    EditorUtility.SetDirty(animator);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            so.ApplyModifiedProperties();
        }

        EditorGUILayout.Space();

        // Retain batch apply button (optional) if user wants to push all again
        using (new EditorGUI.DisabledScope(animator == null || _setter.parameters == null || _setter.parameters.Count == 0))
        {
            if (GUILayout.Button("Apply Parameter Values To Animator"))
            {
                ApplyParametersToAnimator(animator, _setter.parameters);
            }
        }
    }

    private static AnimatorController ResolveAnimatorController(Animator animator)
    {
        if (animator == null) return null;
        var rac = animator.runtimeAnimatorController;
        if (rac == null) return null;
        if (rac is AnimatorController ac) return ac;
        if (rac is AnimatorOverrideController aoc && aoc.runtimeAnimatorController is AnimatorController baseAc)
            return baseAc;
        return null;
    }

    private static void ReadClips(AnimatorController controller, List<string> targetClipNames)
    {
        targetClipNames.Clear();
        if (controller == null) return;

        var seen = new HashSet<AnimationClip>();
        for (int l = 0; l < controller.layers.Length; l++)
        {
            var sm = controller.layers[l].stateMachine;
            GatherClipsFromStateMachine(sm, seen);
        }

        foreach (var clip in seen)
        {
            if (clip != null) targetClipNames.Add(clip.name);
        }
        targetClipNames.Sort();
    }

    private static void GatherClipsFromStateMachine(AnimatorStateMachine sm, HashSet<AnimationClip> acc)
    {
        if (sm == null) return;
        foreach (var state in sm.states)
        {
            GatherClipsFromMotion(state.state?.motion, acc);
        }
        foreach (var sub in sm.stateMachines)
        {
            GatherClipsFromStateMachine(sub.stateMachine, acc);
        }
    }

    private static void GatherClipsFromMotion(Motion m, HashSet<AnimationClip> acc)
    {
        if (m == null) return;
        if (m is AnimationClip clip)
        {
            acc.Add(clip);
            return;
        }
        if (m is BlendTree bt)
        {
            foreach (var child in bt.children)
            {
                GatherClipsFromMotion(child.motion, acc);
            }
        }
    }

    private static void SyncParameters(Animator animator, AnimatorController controller, List<UMAAnimationParameterSetter.AnimatorParam> targetParams)
    {
        targetParams.Clear();
        if (animator == null || controller == null) return;

        foreach (var p in controller.parameters)
        {
            var ap = new UMAAnimationParameterSetter.AnimatorParam
            {
                name = p.name,
                type = p.type
            };
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float:
                    ap.floatValue = animator.GetFloat(p.name);
                    break;
                case AnimatorControllerParameterType.Int:
                    ap.intValue = animator.GetInteger(p.name);
                    break;
                case AnimatorControllerParameterType.Bool:
                    ap.boolValue = animator.GetBool(p.name);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    ap.boolValue = false;
                    break;
            }
            targetParams.Add(ap);
        }
    }

    private static void ApplyParametersToAnimator(Animator animator, List<UMAAnimationParameterSetter.AnimatorParam> paramValues)
    {
        if (animator == null || paramValues == null) return;
        Undo.RecordObject(animator, "Apply Animator Parameters");

        foreach (var p in paramValues)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float:
                    animator.SetFloat(p.name, p.floatValue);
                    break;
                case AnimatorControllerParameterType.Int:
                    animator.SetInteger(p.name, p.intValue);
                    break;
                case AnimatorControllerParameterType.Bool:
                    animator.SetBool(p.name, p.boolValue);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    if (p.boolValue)
                    {
                        animator.ResetTrigger(p.name);
                        animator.SetTrigger(p.name);
                        p.boolValue = false;
                    }
                    break;
            }
        }
        EditorUtility.SetDirty(animator);
    }
}
#endif