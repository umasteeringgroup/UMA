using System.Collections.Generic;
using UMA;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    public class SlotToMesh : EditorWindow
    {

        [Tooltip("The SlotDataAsset that you want to convert")]
        public SlotDataAsset baseObject;
        [Tooltip("The folder where the Mesh will be created")]
        public UnityEngine.Object slotFolder;


        [MenuItem("UMA/Tools/Mesh Tools/Slot To Mesh", priority = 120)]
        public static void OpenSlotToMeshWindow()
        {
            SlotToMesh window = (SlotToMesh)EditorWindow.GetWindow(typeof(SlotToMesh));
            window.titleContent.text = "UMA Slot To Mesh";
        }


        public string GetFolder(ref UnityEngine.Object folderObject)
        {
            if (folderObject != null)
            {
                string destpath = AssetDatabase.GetAssetPath(folderObject);
                if (string.IsNullOrEmpty(destpath))
                {
                    folderObject = null;
                }
                else if (!System.IO.Directory.Exists(destpath))
                {
                    destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
                }
                return destpath;
            }
            return null;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("UMA Slot To Mesh", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This will convert an UMA slot into a Mesh. Once converted, it can be then be saved as an FBX using unity tools", MessageType.None, false);
            baseObject = (SlotDataAsset)EditorGUILayout.ObjectField("Slot Data Asset", baseObject, typeof(SlotDataAsset), true);
            slotFolder = EditorGUILayout.ObjectField("Dest Folder", slotFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;

            string folder = GetFolder(ref slotFolder);

            if (slotFolder != null && baseObject != null)
            {
                if (GUILayout.Button("Make Mesh") && slotFolder != null)
                {
                    Mesh mesh = ConvertSlotToMesh(baseObject);

                    string meshName = folder + "/" + baseObject.slotName + "_Mesh.asset";
                    string goName = folder + "/" + baseObject.slotName + "_Go.prefab";
                    // Save Mesh to disk.
                    // smr.sharedMesh.Optimize(); This blows up some versions of Unity.
                    //CustomAssetUtility.SaveAsset<Mesh>(mesh, meshName);
                    AssetDatabase.CreateAsset(mesh, meshName);

                    GameObject go = null;
                    try
                    {
                        go = new GameObject(baseObject.slotName);
                        go.hideFlags = HideFlags.HideInHierarchy;
                        MeshFilter mf = go.AddComponent<MeshFilter>();
                        mf.mesh = mesh;

                        MeshRenderer mr = go.AddComponent<MeshRenderer>();
                        mr.materials = new Material[mesh.subMeshCount];
                        for (int i = 0; i < mesh.subMeshCount; i++)
                        {
                            mr.materials[i] = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
                        }

                        PrefabUtility.SaveAsPrefabAsset(go, goName);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                    }
                    finally
                    {
                        if (go != null)
                        {
                            DestroyImmediate(go);
                        }
                    }
                    EditorUtility.DisplayDialog("UMA Prefab Saver", "Conversion complete", "OK");
                }
            }
            else
            {
                if (baseObject == null)
                {
                    EditorGUILayout.HelpBox("A valid SlotDataAsset must be supplied", MessageType.Error);
                }
                if (slotFolder == null)
                {
                    EditorGUILayout.HelpBox("A valid base folder must be supplied", MessageType.Error);
                }
            }
        }

        public static BoneWeight[] ConvertBoneweight1(BoneWeight1[] weights, byte[] bonesPerVertex)
        {
            List<BoneWeight> bones = new List<BoneWeight>();

            int boneIndex = 0;
            for (int i = 0; i < bonesPerVertex.Length; i++)
            {
                int bonecount = bonesPerVertex[boneIndex];
                BoneWeight bw = new BoneWeight();
                for (int j = 0; j < bonecount; j++)
                {
                    if (j == 0)
                    {
                        bw.boneIndex0 = weights[boneIndex].boneIndex;
                        bw.weight0 = weights[boneIndex].weight;
                    }
                    if (j == 1)
                    {
                        bw.boneIndex1 = weights[boneIndex].boneIndex;
                        bw.weight1 = weights[boneIndex].weight;
                    }
                    if (j == 2)
                    {
                        bw.boneIndex2 = weights[boneIndex].boneIndex;
                        bw.weight2 = weights[boneIndex].weight;
                    }
                    if (j == 3)
                    {
                        bw.boneIndex3 = weights[boneIndex].boneIndex;
                        bw.weight3 = weights[boneIndex].weight;
                    }
                    boneIndex++;
                }
            }
            return bones.ToArray();
        }

        public static BoneWeight[] ConvertBoneweights(UMABoneWeight[] umaBones)
        {
            BoneWeight[] boneWeights = new BoneWeight[umaBones.Length];
            for (int i = 0; i < umaBones.Length; i++)
            {
                boneWeights[i].boneIndex0 = umaBones[i].boneIndex0;
                boneWeights[i].boneIndex1 = umaBones[i].boneIndex1;
                boneWeights[i].boneIndex2 = umaBones[i].boneIndex2;
                boneWeights[i].boneIndex3 = umaBones[i].boneIndex3;
                boneWeights[i].weight0 = umaBones[i].weight0;
                boneWeights[i].weight1 = umaBones[i].weight1;
                boneWeights[i].weight2 = umaBones[i].weight2;
                boneWeights[i].weight3 = umaBones[i].weight3;
            }
            return boneWeights;
        }

        private static Mesh BuildStaticMesh(UMAMeshData meshData, Matrix4x4 meshFromRoot, bool includeBlendShapes)
        {
            return BuildStaticMesh(meshData, meshFromRoot, includeBlendShapes, 0);
        }

        private static Mesh BuildStaticMesh(UMAMeshData meshData, Matrix4x4 meshFromRoot, bool includeBlendShapes, int lodLevel)
        {
            Mesh mesh = new Mesh() { indexFormat = IndexFormat.UInt32 };

            if (UMAMeshData.IsNullOrEmptyMeshData(meshData))
            {
                return mesh;
            }

            Matrix4x4 normalTransform = meshFromRoot.inverse.transpose;
            bool flipHandedness = IsMirrored(meshFromRoot);

            if (meshData.vertices != null && meshData.vertices.Length > 0)
            {
                mesh.vertices = TransformPositions(meshData.vertices, meshFromRoot);
            }

            int vertexCount = mesh.vertexCount;
            if (vertexCount > 0)
            {
                if (meshData.normals != null && meshData.normals.Length == vertexCount)
                {
                    mesh.normals = TransformDirections(meshData.normals, normalTransform, true);
                }

                if (meshData.tangents != null && meshData.tangents.Length == vertexCount)
                {
                    mesh.tangents = TransformTangents(meshData.tangents, meshFromRoot, flipHandedness);
                }

                if (meshData.colors32 != null && meshData.colors32.Length == vertexCount)
                {
                    mesh.colors32 = (Color32[])meshData.colors32.Clone();
                }
            }

            if (meshData.uv != null)
            {
                mesh.uv = (Vector2[])meshData.uv.Clone();
            }
            if (meshData.uv2 != null)
            {
                mesh.uv2 = (Vector2[])meshData.uv2.Clone();
            }
            if (meshData.uv3 != null)
            {
                mesh.uv3 = (Vector2[])meshData.uv3.Clone();
            }
            if (meshData.uv4 != null)
            {
                mesh.uv4 = (Vector2[])meshData.uv4.Clone();
            }

            mesh.subMeshCount = meshData.subMeshCount;
            int selectedLodLevel = Mathf.Max(0, lodLevel);
            for (int i = 0; i < meshData.subMeshCount; i++)
            {
                var tris = GetTriangles(meshData, i, selectedLodLevel);
                mesh.SetIndices(tris, MeshTopology.Triangles, i);
            }

            if (includeBlendShapes)
            {
                CopyBlendShapes(mesh, meshData, meshFromRoot, normalTransform);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private static void CopyBlendShapes(Mesh mesh, UMAMeshData meshData, Matrix4x4 deltaTransform, Matrix4x4 deltaNormalTransform)
        {
            if (mesh == null || UMAMeshData.IsNullOrEmptyMeshData(meshData) || meshData.blendShapes == null || meshData.blendShapes.Length == 0)
            {
                return;
            }

            int vertexCount = mesh.vertexCount;
            for (int shapeIndex = 0; shapeIndex < meshData.blendShapes.Length; shapeIndex++)
            {
                var shape = meshData.blendShapes[shapeIndex];
                if (shape == null || string.IsNullOrEmpty(shape.shapeName) || shape.frames == null)
                {
                    continue;
                }

                for (int frameIndex = 0; frameIndex < shape.frames.Length; frameIndex++)
                {
                    var frame = shape.frames[frameIndex];
                    if (frame == null || frame.deltaVertices == null || frame.deltaVertices.Length != vertexCount)
                    {
                        continue;
                    }

                    var deltaVertices = TransformDirections(frame.deltaVertices, deltaTransform, false);
                    Vector3[] deltaNormals = null;
                    Vector3[] deltaTangents = null;

                    if (frame.deltaNormals != null && frame.deltaNormals.Length == vertexCount)
                    {
                        deltaNormals = TransformDirections(frame.deltaNormals, deltaNormalTransform, false);
                    }

                    if (frame.deltaTangents != null && frame.deltaTangents.Length == vertexCount)
                    {
                        deltaTangents = TransformDirections(frame.deltaTangents, deltaTransform, false);
                    }

                    mesh.AddBlendShapeFrame(shape.shapeName, frame.frameWeight, deltaVertices, deltaNormals, deltaTangents);
                }
            }
        }

        private static Vector3[] TransformPositions(Vector3[] source, Matrix4x4 transform)
        {
            var transformed = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                transformed[i] = transform.MultiplyPoint3x4(source[i]);
            }
            return transformed;
        }

        private static Vector3[] TransformDirections(Vector3[] source, Matrix4x4 transform, bool normalize)
        {
            var transformed = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 direction = transform.MultiplyVector(source[i]);
                if (normalize && direction.sqrMagnitude > 1e-8f)
                {
                    direction.Normalize();
                }
                transformed[i] = direction;
            }
            return transformed;
        }

        private static Vector4[] TransformTangents(Vector4[] source, Matrix4x4 transform, bool flipHandedness)
        {
            var transformed = new Vector4[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 tangent = transform.MultiplyVector(new Vector3(source[i].x, source[i].y, source[i].z));
                if (tangent.sqrMagnitude > 1e-8f)
                {
                    tangent.Normalize();
                }

                float tangentW = flipHandedness ? -source[i].w : source[i].w;
                transformed[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangentW);
            }
            return transformed;
        }

        private static bool IsMirrored(Matrix4x4 transform)
        {
            Vector3 xAxis = transform.MultiplyVector(Vector3.right);
            Vector3 yAxis = transform.MultiplyVector(Vector3.up);
            Vector3 zAxis = transform.MultiplyVector(Vector3.forward);
            return Vector3.Dot(Vector3.Cross(xAxis, yAxis), zAxis) < 0f;
        }

        private static bool TryGetCanonicalMeshFromRootMatrix(SlotDataAsset slot, out Matrix4x4 meshFromRoot)
        {
            meshFromRoot = Matrix4x4.identity;
            return slot != null && SlotDataAsset.TryGetCanonicalMeshFromRootMatrix(slot.meshData,
                slot.slotName, out meshFromRoot);
        }

        public static Mesh ConvertSlotToMesh(SlotDataAsset slot)
        {
            return ConvertSlotToMesh(slot, true);
        }

        public static Mesh ConvertSlotToMesh(SlotDataAsset slot, bool preciseCharacterSpace)
        {
            return ConvertSlotToMesh(slot, preciseCharacterSpace, 0);
        }

        public static Mesh ConvertSlotToMesh(SlotDataAsset slot, bool preciseCharacterSpace, int lodLevel)
        {
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
            {
                return null;
            }

            Matrix4x4 meshFromRoot = Matrix4x4.identity;
            bool reconstructed = preciseCharacterSpace && TryGetCanonicalMeshFromRootMatrix(slot, out meshFromRoot);
            if (preciseCharacterSpace && !reconstructed && slot.meshData.bindPoses != null && slot.meshData.bindPoses.Length > 0)
            {
                Debug.LogWarning($"[SlotToMesh] Could not reconstruct canonical character-space transform for slot '{slot.slotName}'. Falling back to raw slot mesh data.", slot);
            }

            return BuildStaticMesh(slot.meshData, meshFromRoot, true, lodLevel);
        }

        public static Mesh ConvertSlotToMeshLTOW(SlotDataAsset slot, Quaternion Rotation, int VertexHighlight, Transform modelRoot = null)
        {
            return ConvertSlotToMeshLTOW(slot, Rotation, VertexHighlight, 0, modelRoot);
        }

        public static Mesh ConvertSlotToMeshLTOW(SlotDataAsset slot, Quaternion Rotation, int VertexHighlight, int lodLevel, Transform modelRoot = null)
        {
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
            {
                return null;
            }

            var src = slot.meshData;

            Matrix4x4 meshFromRoot = Matrix4x4.identity;
            TryGetCanonicalMeshFromRootMatrix(slot, out meshFromRoot);

            Matrix4x4 rootLocalToTarget = Matrix4x4.identity;

            if (src.rootBone != null)
            {
                Matrix4x4 rootBoneWorld = src.rootBone.localToWorldMatrix;
                if (modelRoot != null)
                {
                    rootLocalToTarget = modelRoot.worldToLocalMatrix * rootBoneWorld;
                }
                else
                {
                    rootLocalToTarget = rootBoneWorld;
                }
            }

            Matrix4x4 rotMat = Matrix4x4.TRS(Vector3.zero, Rotation, Vector3.one);
            Matrix4x4 total = rotMat * rootLocalToTarget * meshFromRoot;
            Mesh mesh = BuildStaticMesh(src, total, true, lodLevel);
            return AddVertexHighlight(mesh, VertexHighlight);
        }


        public static Mesh ConvertSlotToMesh(SlotDataAsset slot, Quaternion Rotation, int VertexHighlight)
        {
            return ConvertSlotToMesh(slot, Rotation, VertexHighlight, 0);
        }

        public static Mesh ConvertSlotToMesh(SlotDataAsset slot, Quaternion Rotation, int VertexHighlight, int lodLevel)
        {
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
            {
                return null;
            }

            Matrix4x4 meshFromRoot = Matrix4x4.identity;
            bool reconstructed = TryGetCanonicalMeshFromRootMatrix(slot, out meshFromRoot);
            if (!reconstructed && slot.meshData.bindPoses != null && slot.meshData.bindPoses.Length > 0)
            {
                Debug.LogWarning($"[SlotToMesh] Could not reconstruct canonical character-space transform for slot '{slot.slotName}' in rotated conversion. Falling back to raw slot mesh data.", slot);
            }

            Matrix4x4 rot = Matrix4x4.TRS(Vector3.zero, Rotation, Vector3.one);
            Mesh mesh = BuildStaticMesh(slot.meshData, rot * meshFromRoot, true, lodLevel);
            return AddVertexHighlight(mesh, VertexHighlight);
        }

        private static Mesh AddVertexHighlight(Mesh mesh, int vertexHighlight)
        {
            if (mesh == null || vertexHighlight < 0 || mesh.vertexCount == 0)
            {
                return mesh;
            }

            if (vertexHighlight >= mesh.vertexCount)
            {
                vertexHighlight = mesh.vertexCount - 1;
            }

            Vector3 pos = mesh.vertices[vertexHighlight];
            GameObject throwAway = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh sphereMesh = throwAway.GetComponent<MeshFilter>().sharedMesh;
            Mesh sphere = Object.Instantiate(sphereMesh);

            Vector3[] vertices = sphere.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = pos + (vertices[i] * 0.005f);
            }
            sphere.vertices = vertices;
            sphere.RecalculateBounds();

            Mesh combinedMesh = new Mesh() { indexFormat = IndexFormat.UInt32 };
            CombineInstance[] combine = new CombineInstance[2];
            combine[0].mesh = mesh;
            combine[0].transform = Matrix4x4.identity;
            combine[1].mesh = sphere;
            combine[1].transform = Matrix4x4.identity;
            combinedMesh.CombineMeshes(combine, false, true, false);
            GameObject.DestroyImmediate(throwAway);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(sphere);
            return combinedMesh;
        }



        public static int[] GetTriangles(UMAMeshData meshData, int subMesh)
        {
            return GetTriangles(meshData, subMesh, 0);
        }

        public static int[] GetTriangles(UMAMeshData meshData, int subMesh, int lodLevel)
        {
            if (meshData == null || meshData.submeshes == null || subMesh < 0 || subMesh >= meshData.submeshes.Length || meshData.submeshes[subMesh] == null)
            {
                return new int[0];
            }

            int[] triangles = meshData.submeshes[subMesh].getManagedTriangles(Mathf.Max(0, lodLevel));
            return triangles ?? new int[0];
        }
    }
}
