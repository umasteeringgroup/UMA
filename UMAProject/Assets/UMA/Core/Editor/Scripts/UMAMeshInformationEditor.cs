#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAMeshInformation))]
    public class UMAMeshInformationEditor : Editor
    {
        private SerializedProperty _meshTypeProp;
        private SerializedProperty _meshNameProp;
        private SerializedProperty _vertexCountProp;
        private SerializedProperty _boneCountProp;
        private SerializedProperty _boneWeightCountProp;
        private SerializedProperty _bindPoseCountProp;
        private SerializedProperty _indexFormatProp;
        private SerializedProperty _blendShapeCountProp;
        private SerializedProperty _subMeshCountProp;
        private SerializedProperty _subMeshTriangleCountsProp;
        private SerializedProperty _subMeshMaterialNamesProp;

        // Vertex data presence
        private SerializedProperty _hasNormalsProp;
        private SerializedProperty _normalCountProp;
        private SerializedProperty _hasTangentsProp;
        private SerializedProperty _tangentCountProp;
        private SerializedProperty _hasColorsProp;
        private SerializedProperty _colorCountProp;
        private SerializedProperty _uvChannelCountProp;
        private SerializedProperty _uvChannelVertexCountsProp;

        private GUIStyle _headerStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _propertyLabelStyle;
        private GUIStyle _propertyValueStyle;
        private GUIStyle _subMeshHeaderStyle;
        private GUIStyle _subMeshRowStyle;
        private GUIStyle _footerStyle;

        private bool _stylesBuilt;

        private void OnEnable()
        {
            _meshTypeProp = serializedObject.FindProperty("_meshType");
            _meshNameProp = serializedObject.FindProperty("_meshName");
            _vertexCountProp = serializedObject.FindProperty("_vertexCount");
            _boneCountProp = serializedObject.FindProperty("_boneCount");
            _boneWeightCountProp = serializedObject.FindProperty("_boneWeightCount");
            _bindPoseCountProp = serializedObject.FindProperty("_bindPoseCount");
            _indexFormatProp = serializedObject.FindProperty("_indexFormat");
            _blendShapeCountProp = serializedObject.FindProperty("_blendShapeCount");
            _subMeshCountProp = serializedObject.FindProperty("_subMeshCount");
            _subMeshTriangleCountsProp = serializedObject.FindProperty("_subMeshTriangleCounts");
            _subMeshMaterialNamesProp = serializedObject.FindProperty("_subMeshMaterialNames");

            _hasNormalsProp = serializedObject.FindProperty("_hasNormals");
            _normalCountProp = serializedObject.FindProperty("_normalCount");
            _hasTangentsProp = serializedObject.FindProperty("_hasTangents");
            _tangentCountProp = serializedObject.FindProperty("_tangentCount");
            _hasColorsProp = serializedObject.FindProperty("_hasColors");
            _colorCountProp = serializedObject.FindProperty("_colorCount");
            _uvChannelCountProp = serializedObject.FindProperty("_uvChannelCount");
            _uvChannelVertexCountsProp = serializedObject.FindProperty("_uvChannelVertexCounts");
        }

        private void BuildStyles()
        {
            if (_stylesBuilt)
                return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 6, 6),
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                padding = new RectOffset(8, 8, 4, 4),
                normal = { textColor = new Color(0.75f, 0.8f, 0.9f) }
            };

            _propertyLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                padding = new RectOffset(4, 4, 2, 2),
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f) }
            };

            _propertyValueStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                padding = new RectOffset(4, 4, 2, 2),
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                fontStyle = FontStyle.Bold
            };

            _subMeshHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                padding = new RectOffset(6, 6, 3, 1),
                normal = { textColor = new Color(0.6f, 0.65f, 0.7f) }
            };

            _subMeshRowStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                padding = new RectOffset(10, 6, 2, 2),
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };

            _footerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(6, 6, 2, 2),
                normal = { textColor = new Color(0.4f, 0.4f, 0.4f) }
            };

            _stylesBuilt = true;
        }

        public override void OnInspectorGUI()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorGUILayout.HelpBox("Unity is compiling or updating...", MessageType.Info);
                return;
            }

            serializedObject.Update();
            BuildStyles();

            DrawHeader();
            DrawMeshOverview();
            DrawVertexDataPresence();
            DrawSubMeshes();
            DrawRefreshButton();
            DrawFooter();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader()
        {
            GUILayout.Space(4);

            string meshType = _meshTypeProp.stringValue;
            string meshName = _meshNameProp.stringValue;

            // Color the header based on mesh type
            Color headerBg = meshType switch
            {
                "SkinnedMeshRenderer" => new Color(0.18f, 0.28f, 0.38f, 0.6f),
                "MeshFilter" => new Color(0.28f, 0.22f, 0.18f, 0.6f),
                _ => new Color(0.2f, 0.2f, 0.2f, 0.6f)
            };

            Rect headerRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.DrawRect(headerRect, headerBg);

            if (!string.IsNullOrEmpty(meshName) && meshType != "None")
            {
                EditorGUILayout.LabelField(meshName, _headerStyle);
                EditorGUILayout.LabelField(meshType, new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.55f, 0.6f, 0.7f) }
                });
            }
            else
            {
                EditorGUILayout.LabelField("No Mesh Found", _headerStyle);
                EditorGUILayout.HelpBox(
                    "Attach this component to a GameObject with a SkinnedMeshRenderer or MeshFilter, " +
                    "or add one as a child. Then click Refresh.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private void DrawMeshOverview()
        {
            if (_meshTypeProp.stringValue == "None")
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Mesh Overview", _sectionTitleStyle);

            // Separator line
            Rect sepRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sepRect, new Color(0.3f, 0.3f, 0.3f));

            DrawStatRow("Vertices", _vertexCountProp.intValue.ToString("N0"));
            DrawStatRow("Index Format", _indexFormatProp.stringValue);
            DrawStatRow("Bones", _boneCountProp.intValue.ToString("N0"));
            DrawStatRow("Bone Weights", _boneWeightCountProp.intValue.ToString("N0"));
            DrawStatRow("Bind Poses", _bindPoseCountProp.intValue.ToString("N0"));

            if (_blendShapeCountProp.intValue > 0)
            {
                DrawStatRow("Blend Shapes", _blendShapeCountProp.intValue.ToString("N0"));
            }

            // Total triangle summary
            int totalTriangles = 0;
            for (int i = 0; i < _subMeshTriangleCountsProp.arraySize; i++)
            {
                totalTriangles += _subMeshTriangleCountsProp.GetArrayElementAtIndex(i).intValue;
            }
            DrawStatRow("Total Triangles", totalTriangles.ToString("N0"));

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private void DrawVertexDataPresence()
        {
            if (_meshTypeProp.stringValue == "None")
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Vertex Data", _sectionTitleStyle);

            Rect sepRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sepRect, new Color(0.3f, 0.3f, 0.3f));

            // Normals
            DrawPresenceRow("Normals", _hasNormalsProp.boolValue, _normalCountProp.intValue);

            // Tangents
            DrawPresenceRow("Tangents", _hasTangentsProp.boolValue, _tangentCountProp.intValue);

            // Colors
            DrawPresenceRow("Vertex Colors", _hasColorsProp.boolValue, _colorCountProp.intValue);

            // UV channels
            int uvCount = _uvChannelCountProp.intValue;
            if (uvCount > 0)
            {
                EditorGUILayout.LabelField($"UV Channels ({uvCount})", _subMeshHeaderStyle);
                for (int i = 0; i < uvCount; i++)
                {
                    int vertCount = i < _uvChannelVertexCountsProp.arraySize
                        ? _uvChannelVertexCountsProp.GetArrayElementAtIndex(i).intValue
                        : 0;
                    DrawStatRow($"  UV{i}", vertCount.ToString("N0"));
                }
            }
            else
            {
                DrawPresenceRow("UV Channels", false, 0);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private void DrawPresenceRow(string label, bool present, int count)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, _propertyLabelStyle, GUILayout.Width(110));

            Color oldColor = GUI.color;
            GUI.color = present ? new Color(0.4f, 0.85f, 0.4f) : new Color(0.65f, 0.35f, 0.35f);
            string display = present ? $"{count:N0}" : "\u2014";
            EditorGUILayout.LabelField(display, _propertyValueStyle);
            GUI.color = oldColor;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, _propertyLabelStyle, GUILayout.Width(110));
            EditorGUILayout.LabelField(value, _propertyValueStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSubMeshes()
        {
            if (_meshTypeProp.stringValue == "None")
                return;

            int subMeshCount = _subMeshCountProp.intValue;
            if (subMeshCount <= 0)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"SubMeshes ({subMeshCount})", _sectionTitleStyle);

            // Separator line
            Rect sepRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sepRect, new Color(0.3f, 0.3f, 0.3f));

            // Column headers
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("#", _subMeshHeaderStyle, GUILayout.Width(24));
            EditorGUILayout.LabelField("Triangles", _subMeshHeaderStyle, GUILayout.Width(100));
            EditorGUILayout.LabelField("Material", _subMeshHeaderStyle);
            EditorGUILayout.EndHorizontal();

            // Thin separator below header
            Rect subSepRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(subSepRect, new Color(0.25f, 0.25f, 0.25f));

            // Submesh rows
            for (int i = 0; i < subMeshCount; i++)
            {
                DrawSubMeshRow(i);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private void DrawSubMeshRow(int index)
        {
            int triangleCount = 0;
            string materialName = "(Missing)";

            if (index < _subMeshTriangleCountsProp.arraySize)
            {
                triangleCount = _subMeshTriangleCountsProp.GetArrayElementAtIndex(index).intValue;
            }

            if (index < _subMeshMaterialNamesProp.arraySize)
            {
                materialName = _subMeshMaterialNamesProp.GetArrayElementAtIndex(index).stringValue;
                if (string.IsNullOrEmpty(materialName))
                    materialName = "(Missing)";
            }

            // Get a full-width rect for the row
            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            // Alternate row background for readability
            Color rowBg = index % 2 == 0
                ? new Color(0.22f, 0.22f, 0.22f, 0.3f)
                : new Color(0.18f, 0.18f, 0.18f, 0.3f);
            EditorGUI.DrawRect(rowRect, rowBg);

            // Index
            Rect indexRect = new Rect(rowRect.x + 4, rowRect.y, 24, rowRect.height);
            EditorGUI.LabelField(indexRect, $"{index + 1}", _subMeshRowStyle);

            // Triangle count
            Rect triRect = new Rect(rowRect.x + 28, rowRect.y, 100, rowRect.height);
            EditorGUI.LabelField(triRect, $"▲ {triangleCount:N0}", _subMeshRowStyle);

            // Material name
            Rect matRect = new Rect(rowRect.x + 128, rowRect.y, rowRect.width - 136, rowRect.height);
            EditorGUI.LabelField(matRect, materialName, _subMeshRowStyle);
        }

        private void DrawRefreshButton()
        {
            UMAMeshInformation info = (UMAMeshInformation)target;
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.7f);

            if (GUILayout.Button("↻  Refresh Mesh Info", GUILayout.Width(160), GUILayout.Height(24)))
            {
                Undo.RecordObject(info, "Refresh Mesh Info");
                info.GatherMeshInfo();
                EditorUtility.SetDirty(info);
            }

            GUI.backgroundColor = oldBg;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            GUILayout.Space(2);
            EditorGUILayout.LabelField("UMAMeshInformation", _footerStyle);
        }
    }
}
#endif
