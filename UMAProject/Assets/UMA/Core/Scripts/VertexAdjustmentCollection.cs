using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using UMA;
using Unity.Collections;
using UnityEngine;
using JetBrains.Annotations;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    #region vertexAdjustments
    [Serializable]
    public abstract class VertexAdjustment
    {
        public int vertexIndex;
        public float weight;
        public abstract string Name { get; }
        public abstract VertexAdjustmentCollection VertexAdjustmentCollection { get; }

        public abstract VertexAdjustment ShallowCopy();
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
                return "Vertex Color";
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
            };
        }
    }

    [Serializable]
    public class VertexDeltaAdjustment : VertexAdjustment
    {
        public int vertexIndex;
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
                return "Vertex Delta";
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
            };
        }

    }

    [Serializable]
    public class VertexScaleAdjustment : VertexAdjustment
    {
        public int vertexIndex;
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
                return "Vertex Scale";
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
            };
        }
    }

    [Serializable]
    public class VertexNormalAdjustment : VertexAdjustment
    {
        public int vertexIndex;
        public Vector3 normal;
        public Vector3 tangent;
        public static void Apply(MeshDetails mesh, MeshDetails src, VertexNormalAdjustment[] adjustments)
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
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                mesh.normals[vertIndex] = adjustments[i].normal;
                mesh.tangents[vertIndex] = new Vector4(adjustments[i].tangent.x, adjustments[i].tangent.y, adjustments[i].tangent.z, 1);
            }
        }

        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, VertexNormalAdjustment[] adjustments, float scale)
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
            for (int i = 0; i < adjustments.Length; i++)
            {
                int vertIndex = adjustments[i].vertexIndex;
                Vector3 startNormal = mesh.normals[vertIndex];
                Vector3 newNormal = adjustments[vertIndex].normal;
                Vector3 lerpNormal = Vector3.Lerp(startNormal, newNormal, scale);
                mesh.normals[vertIndex] = lerpNormal;
                Vector3 startTangent = mesh.tangents[vertIndex];
                Vector3 newTangent = adjustments[vertIndex].tangent;
                Vector3 lerpTangent = Vector3.Lerp(startTangent, newTangent, scale);
                mesh.tangents[vertIndex] = new Vector4(lerpTangent.x, lerpTangent.y, lerpTangent.z, 1);
            }
        }

        public override string Name
        {
            get
            {
                return "Vertex Normal";
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
            };
        }

    }

    [Serializable]
    public class VertexUVAdjustment : VertexAdjustment
    {
        public int vertexIndex;
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
                return "Vertex UV";
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
            };
        }
    }

    [Serializable]
    public class VertexBlendshapeAdjustment : VertexAdjustment
    {
        public int vertexIndex;
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
            };
        }

    }

    // This is a user defined adjustment, it is up to the user to define what it does
    [Serializable]
    public class VertexUserAdjustment : VertexAdjustment
    {
        public int vertexIndex;
        public Vector3 OriginalPosition;
        public float value;
        public static void Apply(MeshDetails mesh, MeshDetails src, VertexUserAdjustment[] adjustments)
        {
            // Send an event if setup. 
            // Do something with the value
        }

        public override string Name
        {
            get
            {
                return "Vertex User";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexUserAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexUserAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                value = value
            };
        }
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

        public abstract void Apply(MeshDetails mesh, MeshDetails src);
        public abstract void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale);
#if UNITY_EDITOR
        public abstract void DoGUI(VertexAdjustment vertAdj);
        public abstract VertexAdjustment Create();
        public abstract string Name { get; }
        public abstract Type AdjustmentType { get; }
#endif
    }

    [Serializable]
    public class VertexColorAdjustmentCollection: VertexAdjustmentCollection
    {
        [SerializeField]
        public VertexColorAdjustment[] vertexColorAdjustments;

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
        public override void DoGUI(VertexAdjustment vertAdj)
        {
            VertexColorAdjustment VA = (VertexColorAdjustment)vertAdj;
            if (VA == null)
                return;
            VA.weight = EditorGUILayout.Slider("Weight", VA.weight, 0.0f, 1.0f);
            VA.color = EditorGUILayout.ColorField("Color", VA.color);
        }

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
#endif
    }

    [Serializable]
    public class VertexDeltaAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexDeltaAdjustment[] vertexDeltaAdjustments;

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexDeltaAdjustment.Apply(mesh, src, vertexDeltaAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexDeltaAdjustment.ApplyScaled(mesh, src, vertexDeltaAdjustments, scale);
        }
    
#if UNITY_EDITOR
        public override void DoGUI(VertexAdjustment vertAdj)
        {
            VertexDeltaAdjustment VA = (VertexDeltaAdjustment)vertAdj;
            if (VA == null)
            {
                return;
            }

            VA.weight = EditorGUILayout.Slider("Weight", VA.weight, 0.0f, 1.0f);
            VA.delta = EditorGUILayout.Vector3Field("Delta", VA.delta);
        }

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
#endif
    }

    [Serializable]
    public class VertexScaleAdjustmentCollection : VertexAdjustmentCollection
    {
        override public bool SupportWeightedAdjustments
        {
            get { return true; }
        }

        public VertexScaleAdjustment[] vertexScaleAdjustments;

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexScaleAdjustment.Apply(mesh, src, vertexScaleAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexScaleAdjustment.ApplyScaled(mesh, src, vertexScaleAdjustments, scale);
        }

#if UNITY_EDITOR
        public override void DoGUI(VertexAdjustment vertAdj)
        {
            VertexScaleAdjustment VA = (VertexScaleAdjustment)vertAdj;
            if (VA == null)
            {
                return;
            }

            VA.weight = EditorGUILayout.Slider("Weight", VA.weight, 0.0f, 1.0f);
            VA.scale = EditorGUILayout.FloatField("Scale", VA.scale);
        }

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

#endif

    }

    [Serializable]
    public class VertexNormalAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexNormalAdjustment[] vertexNormalAdjustments;

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexNormalAdjustment.Apply(mesh, src, vertexNormalAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexNormalAdjustment.ApplyScaled(mesh, src, vertexNormalAdjustments, scale);
        }
#if UNITY_EDITOR
        public override void DoGUI(VertexAdjustment vertAdj)
        {
            VertexNormalAdjustment VA = (VertexNormalAdjustment)vertAdj;
            if (VA == null)
            {
                return;
            }

            VA.weight = EditorGUILayout.Slider("Weight", VA.weight, 0.0f, 1.0f);
            VA.normal = EditorGUILayout.Vector3Field("Normal", VA.normal);
            VA.tangent = EditorGUILayout.Vector3Field("Tangent", VA.tangent);
        }

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
#endif
    }

    [Serializable]
    public class VertexUVAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexUVAdjustment[] vertexUVAdjustments;

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexUVAdjustment.Apply(mesh, src, vertexUVAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexUVAdjustment.ApplyScaled(mesh, src, vertexUVAdjustments, scale);
        }
#if UNITY_EDITOR
        public override void DoGUI(VertexAdjustment vertAdj)
        {
            VertexUVAdjustment VA = (VertexUVAdjustment)vertAdj;
            if (VA == null)
            {
                return;
            }

            VA.weight = EditorGUILayout.Slider("Weight", VA.weight, 0.0f, 1.0f);
            VA.uv = EditorGUILayout.Vector2Field("UV", VA.uv);
        }

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
#endif
    }

    [Serializable]
    public class VertexBlendshapeAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexBlendshapeAdjustment[] vertexBlendshapeAdjustments;

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexBlendshapeAdjustment.Apply(mesh, src, vertexBlendshapeAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexBlendshapeAdjustment.ApplyScaled(mesh, src, vertexBlendshapeAdjustments, scale);
        }
#if UNITY_EDITOR
        public override void DoGUI(VertexAdjustment vertAdj)
        {
            VertexBlendshapeAdjustment VA = (VertexBlendshapeAdjustment)vertAdj;
            if (VA == null)
            {
                return;
            }

            VA.weight = EditorGUILayout.Slider("Weight", VA.weight, 0.0f, 1.0f);
            VA.delta = EditorGUILayout.Vector3Field("Delta", VA.delta);
            VA.normal = EditorGUILayout.Vector3Field("Normal", VA.normal);
            VA.tangent = EditorGUILayout.Vector3Field("Tangent", VA.tangent);
        }

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
#endif
    }

    [Serializable]
    public class VertexUserAdjustmentCollection : VertexAdjustmentCollection
    {
        public VertexUserAdjustment[] vertexUserAdjustments;

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexUserAdjustment.Apply(mesh, src, vertexUserAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            // Do nothing
        }
#if UNITY_EDITOR
        public override void DoGUI(VertexAdjustment vertAdj)
        {
            VertexUserAdjustment VA = (VertexUserAdjustment)vertAdj;
            if (VA == null)
            {
                return;
            }

            VA.weight = EditorGUILayout.Slider("Weight", VA.weight, 0.0f, 1.0f);
            VA.value = EditorGUILayout.FloatField("Value", VA.value);
        }

        public override VertexAdjustment Create()
        {
            return new VertexUserAdjustment();
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
                return typeof(VertexUserAdjustment);
            }
        }
#endif
    }
}