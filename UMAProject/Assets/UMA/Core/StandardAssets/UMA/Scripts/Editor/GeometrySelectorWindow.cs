using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA.Editors
{
    public class GeometrySelectorWindow : EditorWindow {

        private GeometrySelector _Source;
        private bool doneEditing = false; //set to true to end editing this objects
        private bool showWireframe = true; //whether to switch to wireframe mode or not
        private bool backfaceCull = true; 
        private bool isSelecting = false; //is the user actively selecting
        private bool setSelectedOn = true; //whether to set the triangles to selected or unselection when using selection box
        private Vector2 startMousePos;
        private Rect screenRect; 
        private Texture2D textureMap;

        public static void Init(GeometrySelector source)
        {
            GeometrySelectorWindow window = (GeometrySelectorWindow)EditorWindow.GetWindow(typeof(GeometrySelectorWindow));
            window._Source = source;
            window.minSize = new Vector2(200, 400);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.ObjectField(_Source, typeof(GeometrySelector), true); //temp

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Visual Options");
            GUILayout.BeginHorizontal();
            bool toggled = GUILayout.Toggle(showWireframe, new GUIContent("Show Wireframe", "Toggle showing the Wireframe"), "Button", GUILayout.MinHeight(50));
            if (toggled != showWireframe) { /*UpdateShadingMode(toggled);*/ }           
            showWireframe = toggled;

            backfaceCull = GUILayout.Toggle(backfaceCull, new GUIContent("  Backface Cull  ", "Toggle whether to select back faces"), "Button", GUILayout.MinHeight(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Selection Options");
            GUILayout.BeginHorizontal();
            setSelectedOn = GUILayout.Toggle(setSelectedOn, new GUIContent("Unselect", "Toggle to apply unselected state to triangles highlighted"), "Button", GUILayout.MinHeight(50));
            setSelectedOn = GUILayout.Toggle(!setSelectedOn, new GUIContent("  Select  ", "Toggle to apply selected state to triangles highlighted"), "Button", GUILayout.MinHeight(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear All", GUILayout.MinHeight(50)))
            {
                /*source.ClearAll();*/
            }

            if (GUILayout.Button("Select All", GUILayout.MinHeight(50)))
            {
                /*source.SelectAll();*/
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            textureMap = EditorGUILayout.ObjectField("Set From Texture Map", textureMap, typeof(Texture2D), false) as Texture2D;                
            if (GUILayout.Button("Load Texture Map"))
            {
                /*source.UpdateFromTexture(textureMap);                */
            }

            GUILayout.Space(20);
            if (GUILayout.Button(new GUIContent("Done Editing", "Save the changes and apply them to the MeshHideAsset"), GUILayout.MinHeight(50)))
            {
                doneEditing = true;
            }
        }
    }
}
