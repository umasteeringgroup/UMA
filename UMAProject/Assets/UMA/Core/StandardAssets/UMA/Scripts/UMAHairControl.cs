using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Drives the per-character inputs of the UMA hair shader: the point its "volume" is centred on, and a light
    /// sway of the hair tips.
    ///
    /// Volume center. The hair shader's Volume Shading (Recalculate Normals, Center Darkening) works from a center
    /// point. On its own the shader uses the middle of the renderer bounds, which is right for a separate hair mesh
    /// but not for a UMA character: there the bounds cover the whole body and do not follow the head. This
    /// component writes the head position, in world space, to every material below it that has a
    /// <c>_HairCenterWS</c> property, once per frame after animation. While it does, the shader leaves the
    /// material's own Center Offset out (that one corrects the bounds center); move the center with
    /// <see cref="centerOffset"/> instead.
    ///
    /// Sway. In a slot marked <see cref="SlotDataAsset.HairControlReady"/> every vertex can tell where it is along
    /// its strand, from the scalp to the tip: by its vertex color (dark at the scalp, white at the tip) or by the
    /// hair material's ID Map, as <see cref="SlotDataAsset.HairControlSource"/> says. For those slots the component
    /// keeps one small spring per character, pushed by the movement of the head and by wind, and hands the result
    /// to the shader, which moves each vertex by its place along the strand: nothing before
    /// <see cref="SlotDataAsset.HairControlStart"/>, up to <see cref="SlotDataAsset.HairControlAmount"/> at the
    /// tip. The vertices are moved in the vertex shader, so no mesh is written and no bone is added.
    ///
    /// The values are written per renderer (a MaterialPropertyBlock), because UMA can share one generated material
    /// between several characters. Add the component to the character (the object with the UMAData or
    /// DynamicCharacterAvatar), or to any object above the hair renderers of a character that is not a UMA; the
    /// sway needs a UMA character, because its settings are on the slots.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(11000)] // after the UMA bone animators, which run up to 10000
    [AddComponentMenu("UMA/Hair Control")]
    public class UMAHairControl : MonoBehaviour
    {
        /// <summary>Shader property this component writes: xyz = center in world space, w = 1 while it is valid.</summary>
        public static readonly int HairCenterProperty = Shader.PropertyToID("_HairCenterWS");

        /// <summary>
        /// Shader property this component writes: xyz = sway in world space, length 0 to 1; w = ripple phase in radians.
        /// </summary>
        public static readonly int HairSwayProperty = Shader.PropertyToID("_HairSwayWS");

        /// <summary>
        /// Shader property this component writes: x = place along the strand where movement starts, y = distance
        /// in meters a tip moves at full sway, z = ripple strength, w = where the place along the strand is read
        /// (a <see cref="SlotDataAsset.HairControlSourceType"/>). All zero while the material slot does not sway.
        /// </summary>
        public static readonly int HairSwayParamsProperty = Shader.PropertyToID("_HairSwayParams");

        /// <summary>Wind added to every Hair Control, in world space. A length of 1 is a clear breeze.</summary>
        public static Vector3 GlobalWind;

        [Tooltip("Name of the bone the hair volume is centred on. The sway also follows the movement of this bone.")]
        public string headBoneName = "Head";

        [Tooltip("Center of the hair volume, measured from the head bone in the rest pose. It is given in the " +
                 "renderer's object space (the space of its root bone: X right, Y up for a UMA) and in its units, " +
                 "and it turns and scales with the head bone afterwards. It replaces the Center Offset on the " +
                 "hair material, which is not used while this component is active.")]
        public Vector3 centerOffset = new Vector3(0f, 0.09f, 0f);

        [Header("Hair sway (slots marked Hair Control Ready)")]
        [Tooltip("Moves the hair tips of slots marked Hair Control Ready. Where the movement starts and how far it " +
                 "goes are set on each slot.")]
        public bool sway = true;

        [Tooltip("How much the hair trails behind when the head moves: the sway reached per meter per second of " +
                 "head speed. Full sway is 1.")]
        [Min(0f)]
        public float movementSway = 0.2f;

        [Tooltip("How much the hair leans with the wind: the sway reached per unit of wind. Full sway is 1.")]
        [Min(0f)]
        public float windSway = 0.5f;

        [Tooltip("Wind for this character only, in world space. It is added to UMAHairControl.GlobalWind and to " +
                 "the Wind Zones in the scene.")]
        public Vector3 wind = Vector3.zero;

        [Tooltip("Adds the Wind Zones in the scene: the Main value of each, along its direction (Directional) or " +
                 "away from it inside its radius (Spherical).")]
        public bool useWindZones = true;

        [Tooltip("How much the wind strength rises and falls over time. 0 is a steady wind.")]
        [Range(0f, 1f)]
        public float gustiness = 0.5f;

        [Tooltip("How fast the hair swings back, in swings per second.")]
        [Range(0.2f, 4f)]
        public float springFrequency = 1.5f;

        [Tooltip("How quickly a swing dies out. Low values keep swinging, 1 settles without swinging back.")]
        [Range(0.05f, 1.5f)]
        public float damping = 0.4f;

        [Tooltip("A wave that runs from the scalp to the tips while the hair is swayed, as a part of the sway. " +
                 "0 moves each card as one piece.")]
        [Range(0f, 1f)]
        public float ripple = 0.3f;

        [Tooltip("Waves per second of the ripple.")]
        [Range(0f, 8f)]
        public float rippleFrequency = 1.5f;

        private const float MaxStep = 1f / 15f;        // longer frames are simulated as this long
        private const float TeleportSpeed = 50f;       // m/s; a head moving faster than this was repositioned
        private const float TwoPi = Mathf.PI * 2f;

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
        private Material warnedMixedMaterial;

        private Vector3 swayValue;
        private Vector3 swayVelocity;
        private Vector3 lastHeadPosition;
        private bool hasLastHeadPosition;
        private float ripplePhase;
        private float gustTime;
        private float gustSeed;
        private static int enabledCount;

#if UMA_WIND_MODULE
        private static WindZone[] windZones;
        private static float windZonesFoundAt = float.NegativeInfinity;
        private const float WindZoneSearchInterval = 1f;
#endif

        /// <summary>The bone the center follows, or null when none was found.</summary>
        public Transform HeadBone => head;

        /// <summary>The current sway, in world space. Its length is 0 (at rest) to 1 (full sway).</summary>
        public Vector3 CurrentSway => swayValue;

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
            // Spreads the gusts of different characters apart, so a crowd does not sway in step.
            enabledCount++;
            gustSeed = Mathf.Repeat(enabledCount * 0.618034f, 1f) * 100f;
            ResetSway();
            UpdateHair(0f);
        }

        private void OnDisable()
        {
            ClearAll();
        }

        private void LateUpdate()
        {
            // The sway is only simulated while playing; in Edit mode the hair stays at rest.
            UpdateHair(Application.isPlaying ? Time.deltaTime : 0f);
        }

        /// <summary>Looks for the renderers, hair materials and head bone again on the next update.</summary>
        public void Refresh()
        {
            ClearAll();
            headDirty = true;
            lookForUmaData = true;
        }

        /// <summary>Puts the hair back at rest, for example after the character was moved to another place.</summary>
        public void ResetSway()
        {
            swayValue = Vector3.zero;
            swayVelocity = Vector3.zero;
            hasLastHeadPosition = false;
        }

        /// <summary>
        /// Writes the current center and sway to the hair materials without moving the sway on. Call it yourself
        /// only when the head is moved after LateUpdate and before rendering.
        /// </summary>
        public void UpdateCenter()
        {
            UpdateHair(0f);
        }

        /// <summary>
        /// Moves the sway on by <paramref name="deltaTime"/> seconds and writes the center and the sway to the hair
        /// materials. Runs every LateUpdate.
        /// </summary>
        public void UpdateHair(float deltaTime)
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
                // Nothing to follow: remove our values so the shader goes back to the renderer bounds.
                for (int i = 0; i < states.Count; i++)
                {
                    ClearState(states[i]);
                }
                ResetSway();
                return;
            }

            Vector3 center = head.TransformPoint(headLocalCenter);
            Vector4 centerValue = new Vector4(center.x, center.y, center.z, 1f);

            // First pass: find out what each hair material slot sways by, so the spring only runs when needed.
            bool anySway = false;
            if (sway && umaData != null)
            {
                for (int i = 0; i < states.Count && !anySway; i++)
                {
                    RendererState state = states[i];
                    for (int h = 0; h < state.hairIndices.Count && !anySway; h++)
                    {
                        anySway = TryGetSlotSway(state.renderer, state.materials[state.hairIndices[h]], out _, out _, out _);
                    }
                }
            }

            if (anySway)
            {
                StepSway(deltaTime);
            }
            else
            {
                ResetSway();
            }

            Vector4 swayVector = new Vector4(swayValue.x, swayValue.y, swayValue.z, ripplePhase);
            for (int i = 0; i < states.Count; i++)
            {
                RendererState state = states[i];
                for (int h = 0; h < state.hairIndices.Count; h++)
                {
                    int index = state.hairIndices[h];
                    Vector4 swayParams = Vector4.zero;
                    if (anySway && TryGetSlotSway(state.renderer, state.materials[index], out float start, out float amount, out float source))
                    {
                        swayParams = new Vector4(start, amount, ripple, source);
                    }

                    // Read the block first, so values other components put there are kept.
                    state.renderer.GetPropertyBlock(block, index);
                    block.SetVector(HairCenterProperty, centerValue);
                    block.SetVector(HairSwayProperty, swayVector);
                    block.SetVector(HairSwayParamsProperty, swayParams);
                    state.renderer.SetPropertyBlock(block, index);
                }
            }
        }

        // The sway settings of one material slot, taken from the slots UMA built into that submesh. The shader gets
        // one set of values per material slot, so every slot in the submesh has to be Hair Control Ready: an
        // unprepared one would move by whatever its vertex colors or ID Map happen to be, and a slot without
        // vertex colors is filled with white, which moves all of it. Slots that share a submesh use the values
        // of the first of them.
        private bool TryGetSlotSway(Renderer renderer, Material material, out float start, out float amount, out float source)
        {
            start = 0f;
            amount = 0f;
            source = 0f;
            if (material == null || umaData.generatedMaterials == null)
            {
                return false;
            }

            List<UMAData.GeneratedMaterial> generated = umaData.generatedMaterials.materials;
            bool found = false;
            SlotDataAsset unprepared = null;
            for (int g = 0; g < generated.Count; g++)
            {
                UMAData.GeneratedMaterial generatedMaterial = generated[g];
                // Hair is often drawn in two passes: the second pass is a material slot of its own, built from the
                // same slots, and has to move exactly like the first.
                if (generatedMaterial == null ||
                    (generatedMaterial.material != material && generatedMaterial.secondPassMaterial != material))
                {
                    continue;
                }
                if (generatedMaterial.skinnedMeshRenderer != null && generatedMaterial.skinnedMeshRenderer != renderer)
                {
                    continue;
                }

                List<UMAData.MaterialFragment> fragments = generatedMaterial.materialFragments;
                for (int f = 0; f < fragments.Count; f++)
                {
                    SlotData slot = fragments[f] != null ? fragments[f].slotData : null;
                    SlotDataAsset asset = slot != null ? slot.asset : null;
                    if (asset == null)
                    {
                        continue;
                    }
                    if (!asset.HairControlReady)
                    {
                        unprepared = asset;
                    }
                    else if (!found)
                    {
                        found = true;
                        start = Mathf.Clamp01(asset.HairControlStart);
                        amount = Mathf.Max(0f, asset.HairControlAmount);
                        source = (int)asset.HairControlSource;
                    }
                }
            }

            if (found && unprepared != null)
            {
                if (warnedMixedMaterial != material)
                {
                    warnedMixedMaterial = material;
                    Debug.LogWarning("UMAHairControl on '" + name + "': the hair does not sway on material '" + material.name +
                        "', because slot '" + unprepared.slotName + "' is built into the same submesh and is not Hair Control " +
                        "Ready. Mark it ready (with vertex colors or an ID Map that keep it still where it must not " +
                        "move), or give it another UMA material.", this);
                }
                return false;
            }
            return found && amount > 0f;
        }

        // One damped spring for the whole head of hair. It is pulled towards a target made of the head's speed
        // (hair trails behind a moving head, and swings forward when the head stops) and of the wind.
        private void StepSway(float deltaTime)
        {
            Vector3 headPosition = head.position;
            if (!hasLastHeadPosition)
            {
                lastHeadPosition = headPosition;
                hasLastHeadPosition = true;
            }
            if (deltaTime <= 0f)
            {
                return;
            }

            Vector3 headVelocity = (headPosition - lastHeadPosition) / deltaTime;
            lastHeadPosition = headPosition;
            if (headVelocity.sqrMagnitude > TeleportSpeed * TeleportSpeed)
            {
                headVelocity = Vector3.zero; // moved to another place, not a movement the hair should follow
            }

            float step = Mathf.Min(deltaTime, MaxStep);
            gustTime += step;
            float gust = 1f + gustiness * (Mathf.PerlinNoise(gustTime * 0.6f + gustSeed, gustSeed) * 2f - 1f);
            Vector3 target = Vector3.ClampMagnitude(
                -headVelocity * movementSway + CurrentWind(headPosition) * (windSway * gust), 1f);

            float omega = TwoPi * springFrequency;
            int substeps = Mathf.Clamp(Mathf.CeilToInt(omega * step * 2f), 1, 8); // keeps the integration stable
            float dt = step / substeps;
            for (int i = 0; i < substeps; i++)
            {
                Vector3 acceleration = omega * omega * (target - swayValue) - 2f * damping * omega * swayVelocity;
                swayVelocity += acceleration * dt;
                swayValue += swayVelocity * dt;
            }
            if (swayValue.sqrMagnitude > 1f)
            {
                swayValue.Normalize();
                // Take out the part of the velocity that pushes further out, so it does not stick at the limit.
                float outward = Vector3.Dot(swayVelocity, swayValue);
                if (outward > 0f)
                {
                    swayVelocity -= swayValue * outward;
                }
            }

            ripplePhase = Mathf.Repeat(ripplePhase + step * rippleFrequency * TwoPi, TwoPi);
        }

        /// <summary>
        /// The Wind Zones in the scene are looked up once a second, for all Hair Controls together. Call this after
        /// adding or removing a Wind Zone when the change has to show at once.
        /// </summary>
        public static void RefreshWindZones()
        {
#if UMA_WIND_MODULE
            windZones = null;
#endif
        }

        private Vector3 CurrentWind(Vector3 position)
        {
            Vector3 result = GlobalWind + wind;
#if UMA_WIND_MODULE
            if (useWindZones)
            {
                // One search a second for all Hair Controls together, not one per character per frame.
                float now = Time.realtimeSinceStartup;
                if (windZones == null || now - windZonesFoundAt > WindZoneSearchInterval || now < windZonesFoundAt)
                {
                    windZonesFoundAt = now;
                    windZones = FindObjectsByType<WindZone>(FindObjectsSortMode.None);
                }
                for (int i = 0; i < windZones.Length; i++)
                {
                    WindZone zone = windZones[i];
                    if (zone == null || !zone.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    if (zone.mode == WindZoneMode.Directional)
                    {
                        result += zone.transform.forward * zone.windMain;
                    }
                    else
                    {
                        Vector3 away = position - zone.transform.position;
                        float distance = away.magnitude;
                        if (distance < zone.radius && distance > 1e-4f)
                        {
                            result += away * (zone.windMain * (1f - distance / zone.radius) / distance);
                        }
                    }
                }
            }
#endif
            return result;
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
                warnedMixedMaterial = null;
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
            Transform previousHead = head;
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

            if (head != previousHead)
            {
                // Another bone is somewhere else: the jump between the two is not a movement of the head.
                hasLastHeadPosition = false;
            }

            if (head == null && !warnedNoHead && HasHairOnSkinnedRenderer())
            {
                warnedNoHead = true;
                Debug.LogWarning("UMAHairControl on '" + name + "': no bone named '" + headBoneName +
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

        // Takes our values off a renderer. A block that held nothing before we used it is removed altogether, which
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
                    block.SetVector(HairSwayProperty, Vector4.zero);
                    block.SetVector(HairSwayParamsProperty, Vector4.zero);
                    state.renderer.SetPropertyBlock(block, index);
                }
            }
        }
    }
}
