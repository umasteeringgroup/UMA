using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using UMA;
using Unity.Collections;
using UnityEngine;

public abstract class VertexAdjustmentCollection
{
    public abstract int Key { get;  }
    public abstract void PreprocessMeshVertices(NativeArray<Vector3> verts);
    //public abstract void PostProcessMesh();
}

public class VertexAdjustmentColorCollection : VertexAdjustmentCollection
{
    // used to identify this collection, so we can have 0 or 1 of each type on a slot
    public override int Key => 1001;

    public override void PreprocessMeshVertices(NativeArray<Vector3> verts)
    {

    }

    //public override void DuringVertexRetrieval(UMAData umaData, SlotData slot)
    //{
    //}
}