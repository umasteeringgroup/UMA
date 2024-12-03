using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using UMA;
using Unity.Collections;
using UnityEngine;

namespace UMA
{
    public abstract class VertexAdjustmentCollection
    {
        public abstract int Key { get; }
        public int SerializedKey;
        public abstract void PreprocessMeshVertices(NativeArray<Vector3> verts);
        public abstract VertexAdjustmentCollection Clone();

        public static VertexAdjustmentCollection fromstring()
        {
            return null;
        }

        public static void RegisterType<T>(int key) where T : VertexAdjustmentCollection
        {
            var type = typeof(T);
            if (type.IsAbstract)
            {
                throw new System.Exception("Cannot register abstract types");
            }
            if (type.IsSubclassOf(typeof(VertexAdjustmentCollection)))
            {
                throw new System.Exception("Cannot register types that do not inherit from VertexAdjustmentCollection");
            }
            if (type.GetConstructor(System.Type.EmptyTypes) == null)
            {
                throw new System.Exception("Cannot register types that do not have a parameterless constructor");
            }

        }
    }


    [Serializable]
    public struct VertexColorAdjustment
    {
        public int vertexIndex;
        public Color32 color;
#if UMA_BURSTCOMPILE
		[BurstCompile]
#endif
        public static void Apply(MeshDetails mesh, VertexColorAdjustment[] vertexColorAdjustments)
        {
            for (int i=0;i<vertexColorAdjustments.Length; i++)
            {
                mesh.colors32[vertexColorAdjustments[i].vertexIndex] = vertexColorAdjustments[i].color;
            }
        }
    }

    [Serializable]
    public struct VertexDeltaAdjustment
    {
        public int vertexIndex;
        public Vector3 delta;
        public void Apply(ref NativeArray<Vector3> verts)
        {
            verts[vertexIndex] += delta;
        }
    }

    [Serializable]
    public struct VertexScaleAdjustment
    {
        public int vertexIndex;
        public float scale;
        public void Apply(ref NativeArray<Vector3> verts)
        {
            verts[vertexIndex] *= scale;
        }
    }

    [Serializable]
    public struct VertexNormalAdjustment
    {
        public int vertexIndex;
        public Vector3 normal;
    }

    [Serializable]
    public struct VertexUVAdjustment
    {
        public int vertexIndex;
        public Vector2 uv;
    }

    // This is a user defined adjustment, it is up to the user to define what it does
    [Serializable]
    public struct VertexUserAdjustment
    {
        public int vertexIndex;
        public float value;
    }

    [Serializable]
    public class VertexAdjustmentColorCollection : VertexAdjustmentCollection
    {
        static VertexAdjustmentColorCollection()
        { 
            RegisterType<VertexAdjustmentColorCollection>(1001); 
        }
        // used to identify this collection, so we can have 0 or 1 of each type on a slot
        public override int Key => 1001;

        public override void PreprocessMeshVertices(NativeArray<Vector3> verts)
        {

        }

        public override VertexAdjustmentCollection Clone()
        {
            return new VertexAdjustmentColorCollection();
        }

        public override string ToString()
        {
            SerializedKey = Key;
            return JsonUtility.ToJson(this);
        }
    }
}