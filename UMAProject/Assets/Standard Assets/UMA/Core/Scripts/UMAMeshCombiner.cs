using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class of UMA mesh combiners.
	/// </summary>
    public abstract class UMAMeshCombiner : MonoBehaviour
    {
        public abstract void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, int atlasResolution);
    }
}
