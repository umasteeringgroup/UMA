#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace UMA
{
    /// <summary>
    /// PopupAssetInspector lets you popup an inspector to view a specific instance of a scriptableobject or monobehavior.
    /// This has to live with the base code (not editor code) so the monobehavior can popup the inspector when it's initialized. 
    /// </summary>
    public class PopUpAssetInspector : EditorWindow
    {
        private Object asset;
        private Editor assetEditor;

        public static PopUpAssetInspector Create(Object asset)
        {
            EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            var gameWindow = windows.FirstOrDefault(e => e.titleContent.text.Contains("Inspector"));
            PopUpAssetInspector window = null;
            if (gameWindow != null)
            {
                window = CreateWindow<PopUpAssetInspector>($"{ObjectNames.NicifyVariableName(asset.name)} ({asset.GetType().Name})",gameWindow.GetType());
            }
            else
            {
                window = CreateWindow<PopUpAssetInspector>($"{ObjectNames.NicifyVariableName(asset.name)} ({asset.GetType().Name})");
            }
            window.asset = asset;
            window.assetEditor = Editor.CreateEditor(asset);
            return window;
        }

        private void OnGUI()
        {
            if (assetEditor == null)
            {
                assetEditor = Editor.CreateEditor(asset);
            }
            GUI.enabled = false;
            asset = EditorGUILayout.ObjectField("Asset", asset, asset.GetType(), false);
            GUI.enabled = true;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            assetEditor.OnInspectorGUI();
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
