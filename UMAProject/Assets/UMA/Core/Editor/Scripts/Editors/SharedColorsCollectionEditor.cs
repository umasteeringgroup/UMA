#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public class SharedColorsCollectionEditor
    {
        static bool _foldout = true;
        static int selectedChannelCount = 3;// default 3
        static readonly string[] quickPickNames = new string[3] { "Hair", "Skin", "Eyes" };
        string[] names = new string[16] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16" };
        int[] channels = new int[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        int selectedQuickPickIndex = 0;

        private static void AddSharedColor(UMAData.UMARecipe recipe, int channelCount, string colorName)
        {
            List<OverlayColorData> sharedColors = new List<OverlayColorData>();
            sharedColors.AddRange(recipe.sharedColors);
            sharedColors.Add(new OverlayColorData(channelCount));
            sharedColors[sharedColors.Count - 1].name = colorName;
            recipe.sharedColors = sharedColors.ToArray();
        }

        public void OpenSharedColor(UMAData.UMARecipe recipe, int sharedColorIndex)
        {
            if (recipe == null || recipe.sharedColors == null)
            {
                return;
            }

            _foldout = true;
            for (int i = 0; i < recipe.sharedColors.Length; i++)
            {
                if (recipe.sharedColors[i] != null)
                {
                    recipe.sharedColors[i].foldout = i == sharedColorIndex;
                }
            }
        }

        public bool OnGUI(UMAData.UMARecipe _recipe)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            GUILayout.Space(10);
            _foldout = EditorGUILayout.Foldout(_foldout, "Shared Colors & Properties");
            GUILayout.EndHorizontal();

            if (_foldout)
            {
                bool changed = false;
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

                EditorGUILayout.BeginHorizontal();
                if (_recipe.sharedColors == null)
                {
                    _recipe.sharedColors = new OverlayColorData[0];
                }

                if (_recipe.sharedColors.Length == 0)
                {
                    selectedChannelCount = EditorGUILayout.IntPopup("Channels", selectedChannelCount, names, channels);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
                else
                {
                    selectedChannelCount = _recipe.sharedColors[0].channelMask.Length;
                }

                if (GUILayout.Button("Add Shared Color"))
                {
                    AddSharedColor(_recipe, selectedChannelCount, "Shared Color " + (_recipe.sharedColors.Length + 1));
                    changed = true;
                }

                if (GUILayout.Button("Add Shared Color Parms"))
                {
                    List<OverlayColorData> sharedColors = new List<OverlayColorData>();
                    sharedColors.AddRange(_recipe.sharedColors);
                    sharedColors.Add(new OverlayColorData(0));
                    sharedColors[sharedColors.Count - 1].name = "Shared Color " + sharedColors.Count;
                    _recipe.sharedColors = sharedColors.ToArray();
                    changed = true;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                selectedQuickPickIndex = EditorGUILayout.Popup("Quick pick", selectedQuickPickIndex, quickPickNames);
                if (GUILayout.Button("Add", GUILayout.Width(80f)))
                {
                    AddSharedColor(_recipe, selectedChannelCount, quickPickNames[selectedQuickPickIndex]);
                    changed = true;
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Save Collection"))
                {
                    changed = true;
                }

                for (int i = 0; i < _recipe.sharedColors.Length; i++)
                {
                    bool del = false;
                    OverlayColorData ocd = _recipe.sharedColors[i];

                    GUIHelper.FoldoutBar(ref _recipe.sharedColors[i].foldout, i + ": " + ocd.name, out del);
                    if (del)
                    {
                        List<OverlayColorData> temp = new List<OverlayColorData>();
                        temp.AddRange(_recipe.sharedColors);
                        temp.RemoveAt(i);
                        _recipe.sharedColors = temp.ToArray();
                        changed = true;
                        break;
                    }
                    if (_recipe.sharedColors[i].foldout)
                    {
                        if (ocd.name == null)
                        {
                            ocd.name = "";
                        }

                        string NewName = EditorGUILayout.DelayedTextField("Name", ocd.name);
                        if (NewName != ocd.name)
                        {
                            ocd.name = NewName;
                            changed = true;
                        }

                        EditorGUILayout.BeginHorizontal();
                        int oldChannelCount = ocd.channelCount;
                        int newChannelCount = EditorGUILayout.IntPopup("Channels", ocd.channelCount, names, channels);

                        if (oldChannelCount != newChannelCount)
                        {
                            ocd.SetChannels(newChannelCount);
                            changed = true;
                        }
                        EditorGUILayout.EndHorizontal();

                        if (ocd.HasColors)
                        {
                            Color NewChannelMask = EditorGUILayout.ColorField("Color Multiplier", ocd.channelMask[0]);
                            if (ocd.channelMask[0] != NewChannelMask)
                            {
                                ocd.channelMask[0] = NewChannelMask;
                                changed = true;
                            }

                            Color NewChannelAdditiveMask = EditorGUILayout.ColorField("Color Additive", ocd.channelAdditiveMask[0]);
                            if (ocd.channelAdditiveMask[0] != NewChannelAdditiveMask)
                            {
                                ocd.channelAdditiveMask[0] = NewChannelAdditiveMask;
                                changed = true;
                            }

                            for (int j = 1; j < ocd.channelMask.Length; j++)
                            {
                                NewChannelMask = EditorGUILayout.ColorField("Texture " + j + " multiplier", ocd.channelMask[j]);
                                if (ocd.channelMask[j] != NewChannelMask)
                                {
                                    ocd.channelMask[j] = NewChannelMask;
                                    changed = true;
                                }

                                NewChannelAdditiveMask = EditorGUILayout.ColorField("Texture " + j + " additive", ocd.channelAdditiveMask[j]);
                                if (ocd.channelAdditiveMask[j] != NewChannelAdditiveMask)
                                {
                                    ocd.channelAdditiveMask[j] = NewChannelAdditiveMask;
                                    changed = true;
                                }
                            }
                            if (ocd.PropertyBlock == null)
                            {
                                if (GUILayout.Button("Add Shader Property Block"))
                                {
                                    ocd.PropertyBlock = new UMAMaterialPropertyBlock();
                                }
                            }
                        }
                        if (ocd.PropertyBlock != null)
                        {
                            if (GUILayout.Button("Remove Shader Property Block"))
                            {
                                ocd.PropertyBlock = null;
                            }
                            else
                            {
                                changed |= UMAMaterialPropertyBlockDrawer.OnGUI(ocd.PropertyBlock);
                            }
                        }
                    }
                }
                GUIHelper.EndVerticalPadded(3);
                return changed;
            }
            return false;
        }
    }
}
#endif
