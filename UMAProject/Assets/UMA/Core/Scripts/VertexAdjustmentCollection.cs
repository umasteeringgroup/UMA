using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using Unity.Collections;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    public enum VertexAdjustmentGizmo
    {
        Move, Rotate, Scale, None
    }

    #region vertexAdjustments
    [Serializable]
    public abstract class VertexAdjustment
    {
        public int vertexIndex;
        public float weight;

        public abstract string Name { get; }
        public abstract VertexAdjustmentCollection VertexAdjustmentCollection { get; }

        public abstract VertexAdjustment ShallowCopy();

        public VertexAdjustment()
        {
            weight = 1.0f;
#if UNITY_EDITOR
            active = false;
#endif
        }

#if UNITY_EDITOR
        public bool active;
        public string slotName;
        public abstract void DoGUI();
        public abstract VertexAdjustmentGizmo Gizmo { get; }
        public abstract void Init(UMAMeshData meshData);
#endif
    }


    [Serializable]
    public class VertexColorAdjustment : VertexAdjustment
    {
        public Color32 color;

#if UMA_BURSTCOMPILE
		[BurstCompile]
#endif
        public static void Apply(MeshDetails mesh, MeshDetails original, VertexColorAdjustment[] adjustments)
        {
            if (!mesh.colors32Modified)
            {
                if (original.colors32 == null)
                {
                    mesh.colors32 = new Color32[original.vertices.Length];
                }
                else
                {
                    mesh.colors32 = (Color32[])original.colors32.Clone();
                }
                mesh.colors32 = (Color32[])original.colors32.Clone();
                mesh.colors32Modified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                mesh.colors32[adjustments[i].vertexIndex] = adjustments[i].color;
            }
        }
#if UMA_BURSTCOMPILE
		[BurstCompile]
#endif
        public static void ApplyScaled(MeshDetails mesh, MeshDetails original, VertexColorAdjustment[] adjustments, float scale)
        {
            if (!mesh.colors32Modified)
            {
                mesh.colors32 = (Color32[])original.colors32.Clone();
                mesh.colors32Modified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                Color startColor = mesh.colors32[vertIndex];
                Color newColor = adjustments[vertIndex].color;
                Color lerpColor = Color.Lerp(startColor, newColor, scale);
                mesh.colors32[vertIndex] = lerpColor;
            }
        }

        public override string Name
        {
            get
            {
                return "Set Color";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection 
        { 
            get
            {
                return new VertexColorAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexColorAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                color = color
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }
#if UNITY_EDITOR
        public override void DoGUI()
        {
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            color = EditorGUILayout.ColorField("Color", color);
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.None;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            if (meshData.colors32 != null)
            {
                color = meshData.colors32[vertexIndex];
            }
        }

#endif
    }

    [Serializable]
    public class VertexDeltaAdjustment : VertexAdjustment
    {
        public Vector3 delta;
        public static void Apply(MeshDetails mesh, MeshDetails src, VertexDeltaAdjustment[] adjustments)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                mesh.vertices[adjustments[i].vertexIndex] += adjustments[i].delta;
            }
        }
        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, VertexDeltaAdjustment[] adjustments, float scale)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                mesh.vertices[adjustments[i].vertexIndex] += (adjustments[i].delta * scale);
            }
        }

        public override string Name
        {
            get
            {
                return "Delta Movement";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexDeltaAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexDeltaAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                delta = delta
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR
        public override void DoGUI()
        {
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            delta = EditorGUILayout.Vector3Field("Delta", delta);
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.Move;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            delta = Vector3.zero;
        }

#endif


    }

    [Serializable]
    public class VertexScaleAdjustment : VertexAdjustment
    {
        public float scale;
        public static void Apply(MeshDetails mesh, MeshDetails src, VertexScaleAdjustment[] adjustments)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                mesh.vertices[vertIndex] += mesh.normals[vertIndex] * adjustments[i].scale;
            }
        }
        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, VertexScaleAdjustment[] adjustments, float scale)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                mesh.vertices[vertIndex] += mesh.normals[vertIndex] * (adjustments[i].scale * scale);
            }
        }

        public override string Name
        {
            get
            {
                return "Scale Along Normal";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexScaleAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexScaleAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                scale = scale
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR
        public override void DoGUI()
        {
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            scale = EditorGUILayout.FloatField("Scale", scale);
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.Scale;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            scale = 1.0f;
        }

#endif


    }

    [Serializable]
    public class VertexNormalAdjustment : VertexAdjustment
    {
        public Vector3 normal;
        public Vector3 tangent;

        public Quaternion rotation;
        public static void Apply(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments)
        {
            if (!mesh.normalsModified)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified)
            {
                mesh.tangents = (Vector4[])src.tangents.Clone();
                mesh.tangentsModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                VertexNormalAdjustment van = adjustments[i] as VertexNormalAdjustment;
                mesh.normals[vertIndex] = van.rotation * van.normal;
                mesh.tangents[vertIndex] = van.rotation * van.tangent;
            }
        }

        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments, float scale)
        {
            if (!mesh.normalsModified)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified)
            {
                mesh.tangents = (Vector4[])src.tangents.Clone();
                mesh.tangentsModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                VertexNormalAdjustment van = adjustments[i] as VertexNormalAdjustment;
                int vertIndex = adjustments[i].vertexIndex;
                Vector3 startNormal = mesh.normals[vertIndex];
                Vector3 newNormal = van.rotation * van.normal;
                Vector3 lerpNormal = Vector3.Lerp(startNormal, newNormal, scale);
                mesh.normals[vertIndex] = lerpNormal;
                Vector3 startTangent = mesh.tangents[vertIndex];
                Vector3 newTangent = van.rotation * van.tangent;
                Vector3 lerpTangent = Vector3.Lerp(startTangent, newTangent, scale);
                mesh.tangents[vertIndex] = new Vector4(lerpTangent.x, lerpTangent.y, lerpTangent.z, 1);
            }
        }

        public override string Name
        {
            get
            {
                return "Set Normal";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexNormalAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexNormalAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                normal = normal,
                tangent = tangent
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR
        public override void DoGUI()
        {
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            normal = EditorGUILayout.Vector3Field("Normal", normal);
            tangent = EditorGUILayout.Vector3Field("Tangent", tangent);
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.Rotate;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            normal = meshData.normals[vertexIndex];
            tangent = meshData.tangents[vertexIndex];
            // create a quaterion to rotate the normal
            rotation = Quaternion.identity;
        }

        public void SetRotation(Quaternion rot)
        {
            rotation = rot;
        }
#endif
    }

    [Serializable]
    public class VertexUVAdjustment : VertexAdjustment
    {
        public Vector2 uv;
        public static void Apply(MeshDetails mesh, MeshDetails src, VertexUVAdjustment[] adjustments)
        {
            if (!mesh.uvModified)
            {
                mesh.uv = (Vector2[])src.uv.Clone();
                mesh.uvModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                mesh.uv[adjustments[i].vertexIndex] = adjustments[i].uv;
            }
        }
        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, VertexUVAdjustment[] adjustments, float scale)
        {
            if (!mesh.uvModified)
            {
                mesh.uv = (Vector2[])src.uv.Clone();
                mesh.uvModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                Vector2 startUV = mesh.uv[vertIndex];
                Vector2 newUV = adjustments[vertIndex].uv;
                Vector2 lerpUV = Vector2.Lerp(startUV, newUV, scale);
                mesh.uv[vertIndex] = lerpUV;
            }
        }

        public override string Name
        {
            get
            {
                return "Set UV";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexUVAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexUVAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                uv = uv
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR
        public override void DoGUI()
        {

            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            uv = EditorGUILayout.Vector2Field("UV", uv);
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.None;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            uv = meshData.uv[vertexIndex];
        }
#endif
    }

    [Serializable]
    public class VertexBlendshapeAdjustment : VertexAdjustment
    {
        public Vector3 delta;
        public Vector3 normal;
        public Vector3 tangent;
        public static void Apply(MeshDetails mesh, MeshDetails src, VertexBlendshapeAdjustment[] adjustments)
        {
            bool tangents = mesh.tangents != null;
            bool normals = mesh.normals != null;

            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            if (!mesh.normalsModified && normals)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified && tangents)
            {
                mesh.tangents = (Vector4[])src.tangents.Clone();
                mesh.tangentsModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                mesh.vertices[vertIndex] += adjustments[i].delta;
                if (normals) mesh.normals[vertIndex] = adjustments[i].normal;
                if (tangents) mesh.tangents[vertIndex] = new Vector4(adjustments[i].tangent.x, adjustments[i].tangent.y, adjustments[i].tangent.z, 1);
            }
        }
        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, VertexBlendshapeAdjustment[] adjustments, float scale)
        {
            bool tangents = mesh.tangents != null;
            bool normals = mesh.normals != null;

            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            if (!mesh.normalsModified && normals)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified && tangents)
            {
                mesh.tangents = (Vector4[])src.tangents.Clone();
                mesh.tangentsModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                mesh.vertices[vertIndex] += (adjustments[i].delta * scale);
                if (normals)
                {
                    Vector3 startNormal = mesh.normals[vertIndex];
                    Vector3 newNormal = adjustments[vertIndex].normal;
                    Vector3 lerpNormal = Vector3.Lerp(startNormal, newNormal, scale);
                    mesh.normals[vertIndex] = lerpNormal;
                }
                if (tangents)
                {
                    Vector3 startTangent = mesh.tangents[vertIndex];
                    Vector3 newTangent = adjustments[vertIndex].tangent;
                    Vector3 lerpTangent = Vector3.Lerp(startTangent, newTangent, scale);
                    mesh.tangents[vertIndex] = new Vector4(lerpTangent.x, lerpTangent.y, lerpTangent.z, 1);
                }
            }
        }

        public override string Name
        {
            get
            {
                return "Vertex Blendshape";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexBlendshapeAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexBlendshapeAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                delta = delta,
                normal = normal,
                tangent = tangent
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }
#if UNITY_EDITOR
        public override void DoGUI()
        {
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            delta = EditorGUILayout.Vector3Field("Delta", delta);
            normal = EditorGUILayout.Vector3Field("Normal", normal);
            tangent = EditorGUILayout.Vector3Field("Tangent", tangent);
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.None;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            delta = Vector3.zero;
            normal = meshData.normals[vertexIndex];
            tangent = meshData.tangents[vertexIndex];
        }
#endif
    }

    // This is a reset adjustment, it resets the vertex to its original position, but it can also be used to store a value.
    [Serializable]
    public class VertexResetAdjustment : VertexAdjustment
    {
        public int value;
        public Vector3 initialPosition;
        public static void Apply(MeshDetails mesh, MeshDetails src, VertexResetAdjustment[] adjustments)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                mesh.vertices[adjustments[i].vertexIndex] = adjustments[i].initialPosition;
            }
        }

        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, VertexResetAdjustment[] adjustments, float scale)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                Vector3 start = mesh.vertices[vertIndex];
                Vector3 lerp = Vector3.Lerp(start, adjustments[i].initialPosition, scale);
                mesh.vertices[vertIndex] = lerp;
            }
        }

        public override string Name
        {
            get
            {
                return "Position Reset";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexResetAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexResetAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                value = value,
                initialPosition = initialPosition
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR
        public override void DoGUI()
        {
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            value = EditorGUILayout.IntField("Value", value);
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.None;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            initialPosition = meshData.vertices[vertexIndex];
        }
#endif

    }
    #endregion

    #region VertexGroups
    public struct VertexGroupMember
    {
        public int vertexIndex;
        public float weight;
    }

    public struct VertexGroup
    {
        string Name;
        public VertexGroupMember[] members;
    }
    #endregion

    public abstract class VertexAdjustmentCollection
    {
        public virtual bool SupportWeightedAdjustments
        {
            get { return false; }
        }

        public List<VertexAdjustment> vertexAdjustments = new List<VertexAdjustment>();
        // when the asset is saved, a collection for each slot is added.
        // When the recipe is loaded, after the slotdatas are merged, 
        // the adjustments for the specific slot are added to the SlotData.
        // When the MeshCombiner builds the CombineInstances, it will process the adjustments for each slot. 
        public abstract void Apply(MeshDetails mesh, MeshDetails src);
        public abstract void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale);

        public void Add(VertexAdjustment adjustment)
        {
            vertexAdjustments.Add(adjustment);
        }

#if UNITY_EDITOR
        public abstract VertexAdjustment Create();
        public abstract string Name { get; }
        public abstract Type AdjustmentType { get; }
        public abstract string Help { get; }

        public VertexAdjustmentCollection GetCollection()
        {
            return Activator.CreateInstance(AdjustmentType) as VertexAdjustmentCollection;
        }
#endif
    }

    [Serializable]
    public class VertexColorAdjustmentCollection: VertexAdjustmentCollection
    {
        //[SerializeField]
        public VertexColorAdjustment[] vertexColorAdjustments
        {
            get { return ((VertexColorAdjustment[])vertexAdjustments.ToArray()); }
        }


        public override bool SupportWeightedAdjustments
        {
            get { return true; }
        }

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexColorAdjustment.Apply(mesh, src, vertexColorAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexColorAdjustment.ApplyScaled(mesh, src, vertexColorAdjustments, scale);
        }

#if UNITY_EDITOR


        public override VertexAdjustment Create()
        {
            return new VertexColorAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Color";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexColorAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to change the color of a vertex.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexDeltaAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexDeltaAdjustment[] vertexDeltaAdjustments
        {
            get { return (VertexDeltaAdjustment[])vertexAdjustments.ToArray(); }
        }

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexDeltaAdjustment.Apply(mesh, src, vertexDeltaAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexDeltaAdjustment.ApplyScaled(mesh, src, vertexDeltaAdjustments, scale);
        }
    
#if UNITY_EDITOR
        public override VertexAdjustment Create()
        {
            return new VertexDeltaAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Delta";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexDeltaAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to move the base vertex position by a delta.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexScaleAdjustmentCollection : VertexAdjustmentCollection
    {
        override public bool SupportWeightedAdjustments
        {
            get { return true; }
        }

        public VertexScaleAdjustment[] vertexScaleAdjustments
        {
            get { return (VertexScaleAdjustment[])vertexAdjustments.ToArray(); }
        }

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexScaleAdjustment.Apply(mesh, src, vertexScaleAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexScaleAdjustment.ApplyScaled(mesh, src, vertexScaleAdjustments, scale);
        }

#if UNITY_EDITOR

        public override VertexAdjustment Create()
        {
            return new VertexScaleAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Scale";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexScaleAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to scale the base vertex position along the normal.";
            }
        }

#endif

    }

    [Serializable]
    public class VertexNormalAdjustmentCollection : VertexAdjustmentCollection
    {
        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexNormalAdjustment.Apply(mesh, src, vertexAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexNormalAdjustment.ApplyScaled(mesh, src, vertexAdjustments, scale);
        }
#if UNITY_EDITOR


        public override VertexAdjustment Create()
        {
            return new VertexNormalAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Normal";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexNormalAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to change the normal and tangent of a vertex.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexUVAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexUVAdjustment[] vertexUVAdjustments
        {
            get { return (VertexUVAdjustment[])vertexAdjustments.ToArray(); }
        }

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexUVAdjustment.Apply(mesh, src, vertexUVAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexUVAdjustment.ApplyScaled(mesh, src, vertexUVAdjustments, scale);
        }
#if UNITY_EDITOR


        public override VertexAdjustment Create()
        {
            return new VertexUVAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex UV";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexUVAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to change the UV of a vertex.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexBlendshapeAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexBlendshapeAdjustment[] vertexBlendshapeAdjustments
        {
            get { return (VertexBlendshapeAdjustment[])vertexAdjustments.ToArray(); }
        }

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexBlendshapeAdjustment.Apply(mesh, src, vertexBlendshapeAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexBlendshapeAdjustment.ApplyScaled(mesh, src, vertexBlendshapeAdjustments, scale);
        }
#if UNITY_EDITOR
        public override VertexAdjustment Create()
        {
            return new VertexBlendshapeAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Blendshape";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexBlendshapeAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to change the vertex position, normal and tangent of a vertex.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexResetAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexResetAdjustment[] vertexResetAdjustments
        {
            get { return (VertexResetAdjustment[])vertexAdjustments.ToArray(); }
        }

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexResetAdjustment.Apply(mesh, src, vertexResetAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexResetAdjustment.ApplyScaled(mesh, src, vertexResetAdjustments, scale);
        }
#if UNITY_EDITOR

        public override VertexAdjustment Create()
        {
            return new VertexResetAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex User";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexResetAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment resets the position of a vertex to the default position based on the weight. You can also assign a generic user value to the vertex";
            }
        }
#endif
    }
}