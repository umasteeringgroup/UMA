using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    public class GeometryUVEditorWindow : EditorWindow
    {
        private GeometrySelector geometrySelector;

        private Vector2 startPosition = new Vector2(0, 0);
        private static float dimension = 512;
        private Vector2 offset;
        private Vector2 drag;

        private Vector2[] box = { new Vector2(0,0), new Vector2(0,1), new Vector2(1,0), new Vector2(1,1) };

        public static void Init(GeometrySelector obj)
        {
            // Get existing open window or if none, make a new one:
            GeometryUVEditorWindow window = (GeometryUVEditorWindow)EditorWindow.GetWindow(typeof(GeometryUVEditorWindow));
            window.titleContent = new GUIContent("UV Layout");
            window.minSize = new Vector2(dimension, dimension);
            window.geometrySelector = obj;
            window.Show();
        }

        void OnGUI()
        {
            DrawGrid(20, 0.2f, Color.gray);
            DrawGrid(100, 0.4f, Color.gray);
            DrawUVLines();

            if(GUILayout.Button("Export", GUILayout.MaxWidth(100)))
            {
                SaveTexture();
            }

            ProcessEvents(Event.current);

            if (GUI.changed) Repaint();
        }

        private void ProcessEvents(Event e)
        {
            drag = Vector2.zero;

            switch(e.type)
            {
                case EventType.MouseDrag:
                    if (e.button == 0)
                    {
                        OnDrag(e.delta);
                    }
                    break;
            }
        }

        private void OnDrag(Vector2 delta)
        {
            drag = delta;

            startPosition += delta;

            GUI.changed = true;
        }

        private void DrawUVLines()
        {
            if (geometrySelector == null)
                return;

            BitArray selectedTris = geometrySelector.selectedTriangles;
            Mesh mesh = geometrySelector.sharedMesh;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            Handles.BeginGUI();

            //Draw UV Space box
            Handles.color = Color.white;
            Handles.DrawLine((box[0] * dimension) + startPosition, (box[1] * dimension) + startPosition);
            Handles.DrawLine((box[0] * dimension) + startPosition, (box[2] * dimension) + startPosition);
            Handles.DrawLine((box[3] * dimension) + startPosition, (box[1] * dimension) + startPosition);
            Handles.DrawLine((box[3] * dimension) + startPosition, (box[2] * dimension) + startPosition);

            Vector2 uv0;
            Vector2 uv1;
            Vector2 uv2;

            for(int i = 0; i < selectedTris.Length; i++)
            {
                if (selectedTris[i])
                    Handles.color = Color.red;
                else
                    Handles.color = Color.white;

                int triIndex = i * 3;

                uv0 = uvs[triangles[triIndex]];
                uv1 = uvs[triangles[triIndex + 1]];
                uv2 = uvs[triangles[triIndex + 2]];

                uv0.y = 1f - uv0.y;
                uv1.y = 1f - uv1.y;
                uv2.y = 1f - uv2.y;

                uv0 = (uv0 * dimension) + startPosition;
                uv1 = (uv1 * dimension) + startPosition;
                uv2 = (uv2 * dimension) + startPosition;

                Handles.DrawLine( uv0, uv1 );
                Handles.DrawLine( uv1, uv2 );
                Handles.DrawLine( uv0, uv2 );
            }

            Handles.EndGUI();
        }

        private void DrawGrid(float gridSpacing, float gridOpacity, Color gridColor)
        {
            int widthDivs = Mathf.CeilToInt(position.width / gridSpacing);
            int heightDivs = Mathf.CeilToInt(position.height / gridSpacing);

            Handles.BeginGUI();
            Handles.color = new Color(gridColor.r, gridColor.g, gridColor.b, gridOpacity);

            offset += drag * 0.5f;
            Vector3 newOffset = new Vector3(offset.x % gridSpacing, offset.y % gridSpacing, 0);

            for (int i = 0; i < widthDivs; i++)
            {
                Handles.DrawLine(new Vector3(gridSpacing * i, -gridSpacing, 0) + newOffset, new Vector3(gridSpacing * i, position.height, 0f) + newOffset);
            }

            for (int j = 0; j < heightDivs; j++)
            {
                Handles.DrawLine(new Vector3(-gridSpacing, gridSpacing * j, 0) + newOffset, new Vector3(position.width, gridSpacing * j, 0f) + newOffset);
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private void SaveTexture()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Texture(s)", "Texture.png", "png", "Base Filename to save PNG files to.");
            if (!string.IsNullOrEmpty(path))
            {
                Texture2D tex = new Texture2D((int)dimension, (int)dimension, TextureFormat.ARGB32, false);

                BitArray selectedTris = geometrySelector.selectedTriangles;
                Mesh mesh = geometrySelector.sharedMesh;
                Vector2[] uvs = mesh.uv;
                int[] triangles = mesh.triangles;

                Vector2 uv0;
                Vector2 uv1;
                Vector2 uv2;
                Color currentColor = Color.white;

                int length = (int)dimension * (int)dimension;
                Color32[] colors = new Color32[length];
                for(int i = 0; i < length; i++ )
                {
                    colors[i] = Color.black;
                }

                //Set the background to black
                tex.SetPixels32(0, 0, (int)dimension-1, (int)dimension-1, colors );

                for (int i = 0; i < selectedTris.Length; i++)
                {
                    if (selectedTris[i])
                        currentColor = Color.red;
                    else
                        currentColor = Color.white;

                    int triIndex = i * 3;

                    uv0 = uvs[triangles[triIndex]];
                    uv1 = uvs[triangles[triIndex + 1]];
                    uv2 = uvs[triangles[triIndex + 2]];

                    uv0 = (uv0 * dimension);
                    uv1 = (uv1 * dimension);
                    uv2 = (uv2 * dimension);

                    //Handles.DrawLine(uv0, uv1);
                    DrawLine(tex, (int)uv0.x, (int)uv0.y, (int)uv1.x, (int)uv1.y, currentColor);
                    //Handles.DrawLine(uv1, uv2);
                    DrawLine(tex, (int)uv1.x, (int)uv1.y, (int)uv2.x, (int)uv2.y, currentColor);
                    //Handles.DrawLine(uv0, uv2);
                    DrawLine(tex, (int)uv0.x, (int)uv0.y, (int)uv2.x, (int)uv2.y, currentColor);
                }
                
                byte[] data = tex.EncodeToPNG();
                System.IO.File.WriteAllBytes(path, data);
            }
        }

        /// <summary>
        /// From wiki at http://wiki.unity3d.com/index.php/TextureDrawLine
        /// </summary>
        /// <param name="tex"></param>
        /// <param name="x1"></param>
        /// <param name="y1"></param>
        /// <param name="x2"></param>
        /// <param name="y2"></param>
        /// <param name="col"></param>
        private void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col)
        {
            int dy = (int)(y1 - y0);
            int dx = (int)(x1 - x0);
            int stepx, stepy;

            if (dy < 0) { dy = -dy; stepy = -1; }
            else { stepy = 1; }
            if (dx < 0) { dx = -dx; stepx = -1; }
            else { stepx = 1; }
            dy <<= 1;
            dx <<= 1;

            float fraction = 0;

            tex.SetPixel(x0, y0, col);
            if (dx > dy)
            {
                fraction = dy - (dx >> 1);
                while (Mathf.Abs(x0 - x1) > 1)
                {
                    if (fraction >= 0)
                    {
                        y0 += stepy;
                        fraction -= dx;
                    }
                    x0 += stepx;
                    fraction += dy;
                    tex.SetPixel(x0, y0, col);
                }
            }
            else
            {
                fraction = dx - (dy >> 1);
                while (Mathf.Abs(y0 - y1) > 1)
                {
                    if (fraction >= 0)
                    {
                        x0 += stepx;
                        fraction -= dy;
                    }
                    y0 += stepy;
                    fraction += dx;
                    tex.SetPixel(x0, y0, col);
                }
            }
        }
    }
}
