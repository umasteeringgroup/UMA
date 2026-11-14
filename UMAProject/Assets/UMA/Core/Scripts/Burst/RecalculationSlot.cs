using UMA;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace UMA
{
    public class RecalculationSlot : MonoBehaviour
    {
        public float angle = 60f;
        public string blendShapeNameStartsWith = "";

        public void Recalculate(UMAData umaDATA)
        {
#if UMA_BURSTCOMPILE
            int rendererCount = umaDATA.RendererCount;
            for(int i=0;i<rendererCount;i++)
            {
                var smr = umaDATA.GetRenderer(i);
                if (smr != null)
                {
                    RecalculateMesh(smr);
                }
            }
#endif
        }

#if UMA_BURSTCOMPILE
        private void RecalculateMesh(SkinnedMeshRenderer smr)
        {
            Mesh mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0 || !mesh.isReadable)
            {
                return;
            }

            // A reused mesh is owned by the generated-resource cache and may be
            // bound to other avatars with different blendshape weights. Never
            // write per-renderer results into it.
            if (UMAResourceLeaseOwner.IsSharedMesh(smr))
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning($"RecalculationSlot: skipped '{smr.name}' because its mesh is shared by the generated-resource cache.", smr);
                }
                return;
            }

            // Skipped when the mesh already holds the result for these blendshape weights; see UMARendererNormals.
            UMARendererNormals.Recalculate(smr, angle, blendShapeNameStartsWith);
        }
#endif
    }
}