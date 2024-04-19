using UnityEngine;
using UnityEditor;
using UMA.Editors;

/// <summary>
/// This partial class implements the editor specific functions for the properties.
/// </summary>

namespace UMA
{
    public static class UMAMaterialPropertyBlockDrawer
    {
        static int TypeIndex = 0;



        /// <summary>
        /// Performs editing on a UMAMaterialPropertyBlock. Returns true if changed, false if not changed
        /// </summary>
        /// <param name="umpb">UMAMaterialPropertyBlock</param>
        /// <returns></returns>
        public static bool OnGUI(UMAMaterialPropertyBlock umpb)
        {
            UMAMaterialPropertyBlock.CheckInitialize();
            GUILayout.Space(5);

            bool changed = false;
            EditorGUI.BeginChangeCheck();               

            GUIHelper.BeginVerticalPadded(5, new Color(0.65f, 0.675f, 1f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shader Properties",GUILayout.ExpandWidth(true));
            GUILayout.Label("Always Update",GUILayout.ExpandWidth(false));
            umpb.alwaysUpdate = GUILayout.Toggle(umpb.alwaysUpdate, "",GUILayout.ExpandWidth(false));
            GUILayout.Label("Parms Only", GUILayout.ExpandWidth(false));
            umpb.alwaysUpdateParms = GUILayout.Toggle(umpb.alwaysUpdateParms, "", GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();



            GUILayout.BeginHorizontal();

            TypeIndex = EditorGUILayout.Popup(TypeIndex, UMAMaterialPropertyBlock.PropertyTypeStrings);
            if (GUILayout.Button("Add Type"))
            {
                umpb.AddProperty(UMAMaterialPropertyBlock.availableTypes[TypeIndex], UMAMaterialPropertyBlock.PropertyTypeStrings[TypeIndex]);
            }

            GUILayout.EndHorizontal(); 


            bool dark = false;
            UMAProperty delme = null;

            if (umpb.shaderProperties != null)
            {
                foreach (UMAProperty up in umpb.shaderProperties)
                {
                    if (up == null)
                    {
                        continue;
                    }

                    GUIHelper.BeginVerticalIndented(3, new Color(0.75f, 0.75f, 1f));
                    if (dark) 
                    {
                        GUIHelper.BeginVerticalPadded(5, new Color(0.85f, 0.85f, 1f));
                        dark = false;
                    }
                    else
                    {
                        GUIHelper.BeginVerticalPadded(5, new Color(0.65f, 0.65f, 0.9f));
                        dark = true;
                    }

                    if (up.OnGUI())
                    {
                        delme = up;
                    }

                    GUIHelper.EndVerticalPadded(5);

                    GUIHelper.EndVerticalIndented();
                }
                if (delme != null)
                {
                    umpb.shaderProperties.Remove(delme);
                }
            }
            GUIHelper.EndVerticalPadded(5);
            GUILayout.Space(5);
            changed = EditorGUI.EndChangeCheck();
            return changed;
        }
    }
}