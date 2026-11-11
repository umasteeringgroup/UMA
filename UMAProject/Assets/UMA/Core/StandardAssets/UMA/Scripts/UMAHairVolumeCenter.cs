using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Gives hair shaders the point their "volume" is centred on, taken from the head bone.
    ///
    /// The UMA hair shader's Volume Shading (Recalculate Normals, Center Darkening) works from a center point.
    /// On its own the shader uses the middle of the renderer bounds, which is right for a separate hair mesh but
    /// not for a UMA character: there the bounds cover the whole body and do not follow the head. This component
    /// writes the head position, in world space, to every material below it that has a <c>_HairCenterWS</c>
    /// property, once per frame after animation. While it does, the shader leaves the material's own Center
    /// Offset out (that one corrects the bounds center); move the center with <see cref="centerOffset"/> instead.
    ///
    /// The value is written per renderer (a MaterialPropertyBlock), because UMA can share one generated material
    /// between several characters. Add the component to the character (the object with the UMAData or
    /// DynamicCharacterAvatar), or to any object above the hair renderers of a character that is not a UMA.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(11000)] // after the UMA bone animators, which run up to 10000
    [AddComponentMenu("UMA/Hair Volume Center")]
    public class UMAHairVolumeCenter : MonoBehaviour
    {
        /// <summary>Shader property this component writes: xyz = center in world space, w = 1 while it is valid.</summary>
        public static readonly int HairCenterProperty = Shader.PropertyToID("_HairCenterWS");

        [Tooltip("Name of the bone the hair volume is centred on.")]
        public string headBoneName = "Head";

        [Tooltip("Center of the hair volume, measured from the head bone in the rest pose. It is given in the " +
                 "renderer's object space (the space of its root bone: X right, Y up for a UMA) and in its units, " +
                 "and it turns and scales with the head bone afterwards. It replaces the Center Offset on the " +
                 "hair material, which is not used while this component is active.")]
        public Vector3 centerOffset = new Vector3(0f, 0.09f, 0f);

        private sealed class RendererState
        {
            public Renderer renderer;
            public readonly List<Material> materials = new List<Material>();
            public readonly List<int> hairIndices = new List<int>();
            public readonly List<bool> blockWasEmpty = new List<bool>();
        }

        private readonly List<RendererState> states = new List<RendererState>();
        private readonly List<Material> materialScratch = new List<Material>();
        private readonly List<Renderer> rendererScratch = new List<Renderer>();
        private MaterialPropertyBlock block;
        private UMAData umaData;
        private Transform head;
        private Vector3 headLocalCenter;
        private string appliedHeadBoneName;
        private Vector3 appliedCenterOffset;
        private bool headDirty = true;
        private bool lookForUmaData = true;
        private bool warnedNoHead;

        /// <summary>The bone the center follows, or null when none was found.</summary>
        public Transform HeadBone => head;

        /// <summary>
        /// The center last worked out, in world space. False when there is no head bone to follow, in which
        /// case nothing is written and the shader uses the renderer bounds.
        /// </summary>
        public bool TryGetCenter(out Vector3 center)
        {
            if (head == null)
            {
                center = default;
                return false;
            }
            center = head.TransformPoint(headLocalCenter);
            return true;
        }

        private void OnEnable()
        {
            headDirty = true;
            lookForUmaData = true;
            UpdateCenter();
        }

        private void OnDisable()
        {
            ClearAll();
        }

        private void LateUpdate()
        {
            UpdateCenter();
        }

        /// <summary>Looks for the renderers, hair materials and head bone again on the next update.</summary>
        public void Refresh()
        {
            ClearAll();
            headDirty = true;
            lookForUmaData = true;
        }

        /// <summary>
        /// Writes the current center to the hair materials. Runs every LateUpdate; call it yourself only when the
        /// head is moved after that and before rendering.
        /// </summary>
        public void UpdateCenter()
        {
            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            SyncRenderers();

            bool settingsChanged = appliedHeadBoneName != headBoneName || appliedCenterOffset != centerOffset;
            if (headDirty || settingsChanged || head == null)
            {
                ResolveHead();
            }

            if (head == null)
            {
                // Nothing to follow: remove our value so the shader goes back to the renderer bounds.
                for (int i = 0; i < states.Count; i++)
                {
                    ClearState(states[i]);
                }
                return;
            }

            Vector3 center = head.TransformPoint(headLocalCenter);
            Vector4 value = new Vector4(center.x, center.y, center.z, 1f);
            for (int i = 0; i < states.Count; i++)
            {
                RendererState state = states[i];
                for (int h = 0; h < state.hairIndices.Count; h++)
                {
                    // Read the block first, so values other components put there are kept.
                    state.renderer.GetPropertyBlock(block, state.hairIndices[h]);
                    block.SetVector(HairCenterProperty, value);
                    state.renderer.SetPropertyBlock(block, state.hairIndices[h]);
                }
            }
        }

        // Keeps the renderer list and each renderer's hair material slots current. A rebuilt character gets new
        // materials (and sometimes new renderers), so this is checked every update; it only looks at shader
        // properties again when a material list has actually changed.
        private void SyncRenderers()
        {
            // A DynamicCharacterAvatar adds its UMAData after this component is enabled, so keep looking while
            // there is nothing to work on; once renderers are found without one, stop asking every frame.
            if (umaData == null && (lookForUmaData || states.Count == 0))
            {
                lookForUmaData = false;
                umaData = GetComponentInParent<UMAData>();
                if (umaData == null)
                {
                    umaData = GetComponentInChildren<UMAData>();
                }
            }

            rendererScratch.Clear();
            SkinnedMeshRenderer[] umaRenderers = umaData != null ? umaData.GetRenderers() : null;
            if (umaRenderers != null && umaRenderers.Length > 0)
            {
                for (int i = 0; i < umaRenderers.Length; i++)
                {
                    if (umaRenderers[i] != null)
                    {
                        rendererScratch.Add(umaRenderers[i]);
                    }
                }
            }
            else
            {
                GetComponentsInChildren(true, rendererScratch);
            }

            bool sameRenderers = rendererScratch.Count == states.Count;
            for (int i = 0; sameRenderers && i < states.Count; i++)
            {
                sameRenderers = states[i].renderer == rendererScratch[i];
            }
            if (!sameRenderers)
            {
                ClearAll();
                for (int i = 0; i < rendererScratch.Count; i++)
                {
                    states.Add(new RendererState { renderer = rendererScratch[i] });
                }
                headDirty = true;
            }

            for (int i = 0; i < states.Count; i++)
            {
                RendererState state = states[i];
                state.renderer.GetSharedMaterials(materialScratch);
                bool sameMaterials = materialScratch.Count == state.materials.Count;
                for (int m = 0; sameMaterials && m < materialScratch.Count; m++)
                {
                    sameMaterials = materialScratch[m] == state.materials[m];
                }
                if (sameMaterials)
                {
                    continue;
                }

                ClearState(state);
                state.materials.Clear();
                state.materials.AddRange(materialScratch);
                state.hairIndices.Clear();
                state.blockWasEmpty.Clear();
                for (int m = 0; m < materialScratch.Count; m++)
                {
                    Material material = materialScratch[m];
                    if (material != null && material.HasProperty(HairCenterProperty))
                    {
                        state.renderer.GetPropertyBlock(block, m);
                        state.hairIndices.Add(m);
                        state.blockWasEmpty.Add(block.isEmpty);
                    }
                }
                headDirty = true; // a rebuilt mesh can come with a new skeleton
            }
        }

        // Finds the head bone and where the center sits in its local space. The offset is given in the renderer's
        // object space (its root bone) in the rest pose; the two bind poses convert it, so the result does not
        // depend on the pose the character happens to be in right now.
        private void ResolveHead()
        {
            headDirty = false;
            appliedHeadBoneName = headBoneName;
            appliedCenterOffset = centerOffset;
            head = null;

            for (int i = 0; i < states.Count && head == null; i++)
            {
                if (states[i].hairIndices.Count == 0)
                {
                    continue;
                }
                SkinnedMeshRenderer skinned = states[i].renderer as SkinnedMeshRenderer;
                if (skinned == null || skinned.sharedMesh == null)
                {
                    continue;
                }

                Transform[] bones = skinned.bones;
                Transform root = skinned.rootBone != null ? skinned.rootBone : skinned.transform;
                int headIndex = -1, rootIndex = -1;
                for (int b = 0; b < bones.Length; b++)
                {
                    if (bones[b] == null)
                    {
                        continue;
                    }
                    if (headIndex < 0 && bones[b].name == headBoneName)
                    {
                        headIndex = b;
                    }
                    if (rootIndex < 0 && bones[b] == root)
                    {
                        rootIndex = b;
                    }
                }
                if (headIndex < 0)
                {
                    continue;
                }

                head = bones[headIndex];
                var bindposes = skinned.sharedMesh.GetBindposes();
                if (rootIndex >= 0 && headIndex < bindposes.Length && rootIndex < bindposes.Length)
                {
                    // Object space -> mesh space -> head space, all in the rest pose.
                    Vector3 meshOffset = bindposes[rootIndex].inverse.MultiplyVector(centerOffset);
                    headLocalCenter = bindposes[headIndex].MultiplyVector(meshOffset);
                }
                else
                {
                    // The root bone is not skinned to, so there is no rest pose for it: use the current pose.
                    headLocalCenter = head.InverseTransformVector(root.TransformVector(centerOffset));
                }
            }

            if (head == null && !warnedNoHead && HasHairOnSkinnedRenderer())
            {
                warnedNoHead = true;
                Debug.LogWarning("UMAHairVolumeCenter on '" + name + "': no bone named '" + headBoneName +
                    "' in the hair renderer's skeleton. The hair shader will use the renderer bounds instead.", this);
            }
            if (head != null)
            {
                warnedNoHead = false;
            }
        }

        private bool HasHairOnSkinnedRenderer()
        {
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].hairIndices.Count > 0 && states[i].renderer is SkinnedMeshRenderer)
                {
                    return true;
                }
            }
            return false;
        }

        private void ClearAll()
        {
            for (int i = 0; i < states.Count; i++)
            {
                ClearState(states[i]);
            }
            states.Clear();
        }

        // Takes our value off a renderer. A block that held nothing before we used it is removed altogether, which
        // also lets the SRP Batcher take the renderer back.
        private void ClearState(RendererState state)
        {
            if (state.renderer == null || block == null)
            {
                return;
            }
            int slots = state.renderer.sharedMaterials.Length;
            for (int h = 0; h < state.hairIndices.Count; h++)
            {
                int index = state.hairIndices[h];
                if (index >= slots)
                {
                    continue;
                }
                if (state.blockWasEmpty[h])
                {
                    state.renderer.SetPropertyBlock(null, index);
                }
                else
                {
                    state.renderer.GetPropertyBlock(block, index);
                    block.SetVector(HairCenterProperty, Vector4.zero);
                    state.renderer.SetPropertyBlock(block, index);
                }
            }
        }
    }
}
