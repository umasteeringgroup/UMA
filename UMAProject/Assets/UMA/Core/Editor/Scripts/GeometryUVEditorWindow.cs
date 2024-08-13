using System.Collections;
using UnityEngine;
using UnityEditor;
using System;


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
        private bool occlusionMapMode = false;
        private Texture2D occlusionMap = null;
        private Color32[] straightWhiteLine = new Color32[(int)dimension];


        public static void Init(GeometrySelector obj, bool OcclusionMapMode)
        {
            // Get existing open window or if none, make a new one:
            GeometryUVEditorWindow window = (GeometryUVEditorWindow)EditorWindow.GetWindow(typeof(GeometryUVEditorWindow));
            window.titleContent = new GUIContent("UV Layout");
            window.minSize = new Vector2(dimension, dimension);
            window.geometrySelector = obj;
            window.occlusionMapMode = OcclusionMapMode;
            if (OcclusionMapMode)
            {
                window.GenerateOcclusionMap();
            }
            window.Show();
        }

        private void OnDestroy()
        {
            if (occlusionMap != null)
            {
                DestroyImmediate(occlusionMap);
            }
        }

        void OnGUI()
        {
            if (occlusionMap != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(startPosition.x, startPosition.y, occlusionMap.width, occlusionMap.height), occlusionMap);//, ScaleMode.ScaleToFit, false);
            }
            else
            {
            DrawGrid(20, 0.2f, Color.gray);
            DrawGrid(100, 0.4f, Color.gray);
            DrawUVLines();
            }

            if(GUILayout.Button("Export", GUILayout.MaxWidth(100)))
            {
                SaveTexture();
            }

            ProcessEvents(Event.current);

            if (GUI.changed)
            {
                Repaint();
            }
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
            {
                return;
            }

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
                {
                    Handles.color = Color.red;
                }
                else
                {
                    Handles.color = Color.white;
                }

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


        private void GenerateOcclusionMap()
        {
            BitArray selectedTris = geometrySelector.selectedTriangles;
            Mesh mesh = geometrySelector.sharedMesh;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            Vector2 uv0;
            Vector2 uv1;
            Vector2 uv2;
            Color currentColor = Color.white;


            for (int i = 0; i < straightWhiteLine.Length; i++)
            {
                straightWhiteLine[i] = Color.white;
            }

            int length = (int)dimension * (int)dimension;
            Color32[] colors = new Color32[length];
            for (int i = 0; i < length; i++)
            {
                colors[i] = Color.black;
            }

            if (occlusionMap != null)
            {
                DestroyImmediate(occlusionMap);
            }
            occlusionMap = new Texture2D((int)dimension, (int)dimension, TextureFormat.ARGB32, false);
            //Set the background to black

            occlusionMap.SetPixels32(0, 0, (int)dimension - 1, (int)dimension - 1, colors);
            for (int i = 0; i < selectedTris.Length; i++)
            {
                if (selectedTris[i])
                {
                    int triIndex = i * 3;

                    uv0 = uvs[triangles[triIndex]];
                    uv1 = uvs[triangles[triIndex + 1]];
                    uv2 = uvs[triangles[triIndex + 2]];

                    uv0.x = Mathf.RoundToInt(uv0.x * dimension);
                    uv0.y = Mathf.RoundToInt(uv0.y * dimension);
                    uv1.x = Mathf.RoundToInt(uv1.x * dimension);
                    uv1.y = Mathf.RoundToInt(uv1.y * dimension);
                    uv2.x = Mathf.RoundToInt(uv2.x * dimension);
                    uv2.y = Mathf.RoundToInt(uv2.y * dimension);


                    DrawTriangle(occlusionMap, uv0, uv1, uv2);
                }
            }
            occlusionMap.Apply(); 
        }

        public static bool LineIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, ref Vector2 intersection)
        {
            float Ax, Bx, Cx, Ay, By, Cy, d, e, f, num/*,offset*/;
            float x1lo, x1hi, y1lo, y1hi;

            Ax = p2.x - p1.x;
            Bx = p3.x - p4.x;

            // X bound box test/

            if (Ax < 0)
            {

                x1lo = p2.x; x1hi = p1.x;

            }
            else
            {

                x1hi = p2.x; x1lo = p1.x;

            }



            if (Bx > 0)
            {

                if (x1hi < p4.x || p3.x < x1lo)
                {
                    return false;
                }
            }
            else
            {

                if (x1hi < p3.x || p4.x < x1lo)
                {
                    return false;
                }
            }

            Ay = p2.y - p1.y;
            By = p3.y - p4.y;

            // Y bound box test//
            if (Ay < 0)
            {

                y1lo = p2.y; y1hi = p1.y;

            }
            else
            {
                y1hi = p2.y; y1lo = p1.y;
            }

            if (By > 0)
            {
                if (y1hi < p4.y || p3.y < y1lo)
                {
                    return false;
                }
            }
            else
            {
                if (y1hi < p3.y || p4.y < y1lo)
                {
                    return false;
                }
            }

            Cx = p1.x - p3.x;
            Cy = p1.y - p3.y;
            d = By * Cx - Bx * Cy;  // alpha numerator//
            f = Ay * Bx - Ax * By;  // both denominator//

            // alpha tests//
            if (f > 0)
            {
                if (d < 0 || d > f)
                {
                    return false;
                }
            }
            else
            {
                if (d > 0 || d < f)
                {
                    return false;
                }
            }

            e = Ax * Cy - Ay * Cx;  // beta numerator//

            // beta tests //
            if (f > 0)
            {
                if (e < 0 || e > f)
                {
                    return false;
                }
            }
            else
            {
                if (e > 0 || e < f)
                {
                    return false;
                }
            }

            // check if they are parallel
            if (f == 0)
            {
                return false;
            }

            // compute intersection coordinates //

            num = d * Ax; // numerator //
            intersection.x = p1.x + num / f;
            num = d * Ay;
            intersection.y = p1.y + num / f;
            return true;
        }
        private void DrawTriangle(Texture2D tex, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            Vector2[] vertices = { v1, v2, v3 };

            //Sort the vertices by y
            Array.Sort(vertices, (x, y) => x.y.CompareTo(y.y));

            v1 = vertices[0];
            v2 = vertices[1];
            v3 = vertices[2];

            // Draw a square around each vertex, to make sure we don't miss any pixels
            DrawSquare(tex, v1, Color.white, dimension /256);
            DrawSquare(tex, v2, Color.white, dimension / 256);
            DrawSquare(tex, v3, Color.white, dimension / 256);

            /* here we know that v1.y <= v2.y <= v3.y */
            /* check for trivial case of bottom-flat triangle */
            if ((int)v2.y == (int)v3.y)
            {
                fillBottomFlatTriangle(tex, v1, v2, v3);
            }
            else if ((int)v1.y == (int)v2.y)
            {
                fillTopFlatTriangle(tex, v1, v2, v3);
            }
            else
            {
                // general case - split the triangle in a topflat and bottom-flat one

                Vector2 temp1;
                Vector2 temp2;

                Vector2 intersection = Vector2.zero;

                temp1.x = 0;
                temp1.y = v2.y;
                temp2.x = dimension;
                temp2.y = v2.y;

                float invslopev1v3 = (v3.x - v1.x) / (v3.y - v1.y);
                float xintersect = v1.x + invslopev1v3 * (v2.y - v1.y);

                Vector2 v4 = new Vector2(xintersect, v2.y);
                fillBottomFlatTriangle(tex, v1, v2, v4);
                fillTopFlatTriangle(tex, v2, v4, v3);


                return;
            }
        }

        private void DrawSquare(Texture2D tex, Vector2 v3, Color white, float v)
        {
            int x = (int)(v3.x - v);
            int y = (int)(v3.y - v);
            int height = (int)v * 2;

            for(int i=0; i<height; i++)
            {
                DrawStraightLine(tex, x, y + i, x+height);
            }
        }

        private void fillBottomFlatTriangle(Texture2D tex, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            float invslope1 = (v2.x - v1.x) / (v2.y - v1.y);
            float invslope2 = (v3.x - v1.x) / (v3.y - v1.y);

            float curx1 = v1.x;
            float curx2 = v1.x;

            for (int scanlineY = (int)v1.y; scanlineY <= (int)v2.y; scanlineY++)
            {
                DrawStraightLine(tex, (int)curx1, scanlineY, (int)curx2);
                curx1 += invslope1;
                curx2 += invslope2;
            }
        }

        private void fillTopFlatTriangle(Texture2D tex, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            float invslope1 = (v3.x - v1.x) / (v3.y - v1.y);
            float invslope2 = (v3.x - v2.x) / (v3.y - v2.y);

            float curx1 = v3.x;
            float curx2 = v3.x;

            for (int scanlineY = (int)v3.y; scanlineY > (int)v1.y; scanlineY--)
            {
                DrawStraightLine(tex, (int)curx1, scanlineY, (int)curx2);
                curx1 -= invslope1;
                curx2 -= invslope2;
            }
        }

        private void SaveTexture()
        {
            string filename = geometrySelector.meshAsset.name;
            if (occlusionMap != null)
            {
                filename += "_MHA_OcclusionMap.png";
            }
            else
            {
                filename += "_MHA_UV.png";
            }

            string path = EditorUtility.SaveFilePanelInProject("Save Texture(s)", filename, "png", "Base Filename to save PNG files to.");
            if (!string.IsNullOrEmpty(path))
            {
                bool freemap = false;
                Texture2D tex;

                if (occlusionMapMode)
                {
                    tex = occlusionMap;
                }
                else
                {
                    freemap = true;
                    tex = new Texture2D((int)dimension, (int)dimension, TextureFormat.ARGB32, false);
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
                        {
                        currentColor = Color.red;
                        }
                    else
                        {
                        currentColor = Color.white;
                        }

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
                }
                
                byte[] data = tex.EncodeToPNG();
                System.IO.File.WriteAllBytes(path, data);
                if (freemap)
                {
                    DestroyImmediate(tex);
                }
                AssetDatabase.Refresh();
                TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                textureImporter.textureType = TextureImporterType.Default;
                textureImporter.isReadable = true;
                textureImporter.mipmapEnabled = false;
                AssetDatabase.ImportAsset(path);
            }
        }

        private void DrawStraightLine(Texture2D tex, int x0, int y0, int x1)
        {
            if (x0 > x1)
            {
                int tmp = x0;
                x0 = x1;
                x1 = tmp;
            }
            if (x1 >= dimension)
            {
                x1 = (int)dimension-1;
            }
            int width = (x1 - x0) + 1;

            if (width < 0)
            {
                return;
            }

            if (width > 0)
            {
                tex.SetPixels32(x0, y0, width, 1, straightWhiteLine);
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
