#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

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

        // Validation state
        private List<string> _validationIssues = new List<string>();
        private Vector2 _validationScrollPos;
        private bool _showValidationResults;

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
            DrawValidateButton();
            DrawValidationResults();
            DrawFooter();

            serializedObject.ApplyModifiedProperties();
        }

        private new void DrawHeader()
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

        private void DrawValidateButton()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f, 0.7f);

            if (GUILayout.Button("Validate Mesh", GUILayout.Width(160), GUILayout.Height(24)))
            {
                ValidateMesh();
                _showValidationResults = true;
            }

            GUI.backgroundColor = oldBg;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawValidationResults()
        {
            if (!_showValidationResults) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_validationIssues.Count == 0)
            {
                EditorGUILayout.HelpBox("All checks passed — no issues found.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField($"Issues Found ({_validationIssues.Count})", new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = Color.red }
                });
                _validationScrollPos = EditorGUILayout.BeginScrollView(_validationScrollPos, GUILayout.Height(200));
                foreach (var issue in _validationIssues)
                {
                    EditorGUILayout.LabelField(issue, new GUIStyle(EditorStyles.wordWrappedLabel)
                    {
                        normal = { textColor = new Color(1f, 0.7f, 0.7f) },
                        padding = new RectOffset(4, 4, 2, 2)
                    });
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void ValidateMesh()
        {
            _validationIssues.Clear();
            var info = (UMAMeshInformation)target;
            if (info == null) return;

            var smr = info.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) smr = info.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = smr != null ? smr.sharedMesh : null;

            if (smr == null)
            {
                _validationIssues.Add("No SkinnedMeshRenderer found on this GameObject or children.");
                return;
            }

            // 1. Disposed mesh
            if (mesh == null)
            {
                _validationIssues.Add("MESH IS NULL — the SkinnedMeshRenderer has no sharedMesh assigned.");
                return;
            }
            try { var _ = mesh.vertexCount; }
            catch (System.Exception) { _validationIssues.Add("MESH APPEARS DISPOSED — accessing vertexCount threw an exception."); return; }

            // 2. Renderer enabled
            if (!smr.enabled)
                _validationIssues.Add("RENDERER IS DISABLED — SkinnedMeshRenderer.enabled = false.");
            if (!smr.gameObject.activeInHierarchy)
                _validationIssues.Add("GAMEOBJECT IS INACTIVE — the renderer's GameObject or a parent is disabled.");

            // 3. Layer check
            int layer = smr.gameObject.layer;
            string layerName = UnityEngine.LayerMask.LayerToName(layer);
            if (string.IsNullOrEmpty(layerName))
                _validationIssues.Add($"LAYER IS EMPTY — GameObject is on layer {layer} which has no name (may be ignored by cameras).");

            // 4. rootBone
            if (smr.rootBone == null)
                _validationIssues.Add("rootBone IS NULL — the SkinnedMeshRenderer has no root bone assigned.");

            // 5. Bounds
            var bounds = mesh.bounds;
            if (bounds.size.magnitude < 0.0001f)
                _validationIssues.Add($"BOUNDS ARE NEAR-ZERO — size={bounds.size:F6}, center={bounds.center:F6}. Renderer may be culled.");
            if (float.IsNaN(bounds.size.x) || float.IsNaN(bounds.size.y) || float.IsNaN(bounds.size.z))
                _validationIssues.Add("BOUNDS CONTAIN NaN values.");
            if (float.IsInfinity(bounds.size.x) || float.IsInfinity(bounds.size.y) || float.IsInfinity(bounds.size.z))
                _validationIssues.Add("BOUNDS CONTAIN Infinity values.");

            // 6. Vertices
            var verts = mesh.vertices;
            int nanVerts = 0, infVerts = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) nanVerts++;
                if (float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)) infVerts++;
            }
            if (nanVerts > 0)
                _validationIssues.Add($"VERTICES HAVE NaN — {nanVerts} of {verts.Length} vertices contain NaN values.");
            if (infVerts > 0)
                _validationIssues.Add($"VERTICES HAVE Infinity — {infVerts} of {verts.Length} vertices contain Infinity values.");

            // 7. Normals
            var norms = mesh.normals;
            if (norms == null || norms.Length == 0)
                _validationIssues.Add("NORMALS ARE MISSING — mesh has no normals.");
            else if (norms.Length != verts.Length)
                _validationIssues.Add($"NORMALS COUNT MISMATCH — {norms.Length} normals vs {verts.Length} vertices.");
            else
            {
                int nanNorms = 0, infNorms = 0;
                for (int i = 0; i < norms.Length; i++) { if (float.IsNaN(norms[i].x)) nanNorms++; if (float.IsInfinity(norms[i].x)) infNorms++; }
                if (nanNorms > 0) _validationIssues.Add($"NORMALS HAVE NaN — {nanNorms} normals contain NaN.");
                if (infNorms > 0) _validationIssues.Add($"NORMALS HAVE Infinity — {infNorms} normals contain Infinity.");
            }

            // 8. Tangents
            var tans = mesh.tangents;
            if (tans != null && tans.Length > 0 && tans.Length != verts.Length)
                _validationIssues.Add($"TANGENTS COUNT MISMATCH — {tans.Length} tangents vs {verts.Length} vertices.");
            if (tans != null)
            {
                int nanTans = 0, infTans = 0;
                for (int i = 0; i < tans.Length; i++) { if (float.IsNaN(tans[i].x)) nanTans++; if (float.IsInfinity(tans[i].x)) infTans++; }
                if (nanTans > 0) _validationIssues.Add($"TANGENTS HAVE NaN — {nanTans} tangents contain NaN.");
                if (infTans > 0) _validationIssues.Add($"TANGENTS HAVE Infinity — {infTans} tangents contain Infinity.");
            }

            // 9. Submesh triangle index validity
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t++)
                {
                    if (tris[t] < 0 || tris[t] >= verts.Length)
                    {
                        _validationIssues.Add($"SUBMESH {s} REFERENCES INVALID VERTEX INDEX — triangle index {t} = {tris[t]} (vertex count = {verts.Length}).");
                        break;
                    }
                }
            }

            // 10. Bones
            var bones = smr.bones;
            var bindposes = mesh.bindposes;
            if (bones == null || bones.Length == 0)
                _validationIssues.Add("BONES ARRAY IS EMPTY — SkinnedMeshRenderer has no bones.");
            else
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null)
                    {
                        _validationIssues.Add($"BONE [{i}] IS NULL — entry {i} in the bones array is null.");
                        break;
                    }
                }
            }

            // 11. Bindposes
            if (bindposes == null || bindposes.Length == 0)
                _validationIssues.Add("BINDPOSES ARE MISSING — mesh has no bindposes.");
            else if (bones != null && bindposes.Length != bones.Length)
                _validationIssues.Add($"BINDPOSE/BONE COUNT MISMATCH — {bindposes.Length} bindposes vs {bones.Length} bones.");

            // 12. Bone weights
            var bpv = mesh.GetBonesPerVertex();
            var bws = mesh.GetAllBoneWeights();
            if (bpv.Length == 0 || bws.Length == 0)
                _validationIssues.Add("BONE WEIGHTS ARE EMPTY — mesh has no bone weight data. All vertices at origin?");
            else if (bpv.Length != verts.Length)
                _validationIssues.Add($"BONES-PER-VERTEX COUNT MISMATCH — {bpv.Length} entries vs {verts.Length} vertices.");
            else if (bones != null)
            {
                int boneCount = bones.Length;
                int bwi = 0;
                int zeroWeightVerts = 0;
                int sumDeviationCount = 0;
                for (int v = 0; v < bpv.Length; v++)
                {
                    byte count = bpv[v];
                    if (count == 0) { zeroWeightVerts++; continue; }
                    float sum = 0f;
                    for (int b = 0; b < count; b++)
                    {
                        var bw = bws[bwi + b];
                        sum += bw.weight;
                        if (bw.boneIndex < 0 || bw.boneIndex >= boneCount)
                        {
                            _validationIssues.Add($"BONE WEIGHT REFERENCES INVALID BONE — vertex {v} references bone index {bw.boneIndex} (bone count = {boneCount}).");
                            bwi += count;
                            goto nextVertex;
                        }
                    }
                    if (sum < 0.9f || sum > 1.1f) sumDeviationCount++;
                    bwi += count;
                    nextVertex:;
                }
                if (zeroWeightVerts > 0)
                    _validationIssues.Add($"ZERO-WEIGHT VERTICES — {zeroWeightVerts} of {verts.Length} vertices have zero bone influences (will render at origin).");
                if (sumDeviationCount > 0)
                    _validationIssues.Add($"BONE WEIGHT SUM DEVIATION — {sumDeviationCount} vertices have bone weight sums not ~1.0 (range allowed: 0.9-1.1).");
            }

            // 13. Bone lossyScale check
            if (bones != null)
            {
                int zeroScale = 0;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null) continue;
                    var ls = bones[i].lossyScale;
                    if (Mathf.Abs(ls.x) < 0.0001f || Mathf.Abs(ls.y) < 0.0001f || Mathf.Abs(ls.z) < 0.0001f)
                    {
                        if (zeroScale == 0)
                            _validationIssues.Add($"BONE [{i}] '{bones[i].name}' HAS NEAR-ZERO LOSSYSCALE — ({ls.x:F6}, {ls.y:F6}, {ls.z:F6}). Geometry weighted to this bone will collapse.");
                        zeroScale++;
                    }
                }
                if (zeroScale > 1)
                    _validationIssues.Add($"MULTIPLE BONES WITH NEAR-ZERO SCALE — {zeroScale} bones have near-zero lossyScale.");
            }

            // 14. Skin quality
            if (QualitySettings.skinWeights == SkinWeights.OneBone)
                _validationIssues.Add("SKIN WEIGHTS LIMITED TO 1 BONE — QualitySettings.skinWeights = OneBone. Vertex may not deform correctly.");
            else if (QualitySettings.skinWeights == SkinWeights.TwoBones)
                _validationIssues.Add("SKIN WEIGHTS LIMITED TO 2 BONES — QualitySettings.skinWeights = TwoBones.");

            // 15. UpdateWhenOffscreen
#if UNITY_2022_1_OR_NEWER
            if (smr.updateWhenOffscreen)
                _validationIssues.Add("updateWhenOffscreen = true (informational).");
#endif

            // 16. Material check
            var mats = smr.sharedMaterials;
            if (mats == null || mats.Length == 0)
                _validationIssues.Add("NO MATERIALS ASSIGNED — SkinnedMeshRenderer has no materials.");
            else
            {
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null)
                        _validationIssues.Add($"MATERIAL [{m}] IS NULL — no material assigned for submesh {m}.");
                }
            }

            // 17. Index format / very large mesh
            if (verts.Length > 65535 && mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
                _validationIssues.Add("INDEX FORMAT IS UInt16 WITH >65535 VERTICES — mesh may not render correctly. Use 32-bit indices.");
        }

        private void DrawFooter()
        {
            GUILayout.Space(2);
            EditorGUILayout.LabelField("UMAMeshInformation", _footerStyle);
        }
    }
}
#endif
