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
    /// keeps two small springs per character, one pushed by the movement of the head and by wind, one by the head
    /// turning (a character turning on the spot, or the head turning or nodding), and hands the results
    /// to the shader, which turns each strand about the point where it starts to move
    /// (<see cref="SlotDataAsset.HairControlStart"/>): the strand curves into an arc as long as the straight strand
    /// was, and its tip has turned by <see cref="SlotDataAsset.HairControlAngle"/> about that point at full sway.
    /// So a tip swings round and comes back a little, as a rotating strand does; the hair is not slid sideways or
    /// pulled longer, and a push along a strand does not move it. For that the shader has to know how long a
    /// strand is and which way it runs. Both are measured once per slot from its mesh
    /// (<see cref="TryMeasureStrands"/>): the direction is kept as parts of the vertex tangent and bitangent, which
    /// turn with the skinned mesh. The vertices are moved in the vertex shader, so no mesh is written and no bone
    /// is added.
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
        /// Shader property this component writes: x = place along the strand where movement starts, y = angle
        /// in radians a strand turns at full sway, z = ripple strength, w = where the place along the strand is read
        /// (a <see cref="SlotDataAsset.HairControlSourceType"/>). All zero while the material slot does not sway.
        /// </summary>
        public static readonly int HairSwayParamsProperty = Shader.PropertyToID("_HairSwayParams");

        /// <summary>
        /// Shader property this component writes: x = length of a strand from scalp to tip in world units, yz = the
        /// direction a strand runs in, as parts of the vertex tangent and bitangent.
        /// </summary>
        public static readonly int HairSwayShapeProperty = Shader.PropertyToID("_HairSwayShape");

        /// <summary>
        /// Shader property this component writes: xyz = the lag of the turning head in world space, length 0 to 1. Each
        /// strand is pushed by it sideways around the head, so hair on opposite sides swings opposite ways.
        /// </summary>
        public static readonly int HairSpinProperty = Shader.PropertyToID("_HairSpinWS");

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
        [Tooltip("Swings the hair of slots marked Hair Control Ready. Where along the strand the movement starts and " +
                 "how far the strand turns are set on each slot.")]
        public bool sway = true;

        [Tooltip("How much the hair trails behind when the head moves: the sway reached per meter per second of " +
                 "head speed. Full sway is 1.")]
        [Min(0f)]
        public float movementSway = 0.2f;

        [Tooltip("How much the hair swings when the head turns (the character turning, or the head turning or nodding): " +
                 "the sway reached per radian per second of turning. At 0.15, turning half a circle a second sways it about half.")]
        [Min(0f)]
        public float turnSway = 0.15f;

        [Tooltip("How much the hair leans with the wind: the sway reached per unit of wind. Full sway is 1.")]
        [Min(0f)]
        public float windSway = 0.5f;

        [Tooltip("Wind for this character only, in world space. It is added to UMAHairControl.GlobalWind and to " +
                 "the Wind Zones in the scene.")]
        public Vector3 wind = Vector3.zero;

        [Tooltip("Adds the Wind Zones in the scene: the Main value of each, along its direction (Directional) or " +
                 "away from it inside its radius (Spherical).")]
        public bool useWindZones = true;

        [Tooltip("How much the wind changes over time: its strength rises and falls, and its direction swings from " +
                 "side to side. 0 is a steady wind, which only leans the hair.")]
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
        private const float TeleportTurn = 40f;        // rad/s; a head turning faster than this was set to a new rotation
        private const float TwoPi = Mathf.PI * 2f;
        private const float GustVeer = 0.7f;           // at full gustiness the wind swings up to about 35 degrees to a side

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
        private Vector3 spinValue;
        private Vector3 spinVelocity;
        private Quaternion lastHeadRotation;
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

        /// <summary>The current lag of the turning head, in world space. Its length is 0 (at rest) to 1 (full sway).</summary>
        public Vector3 CurrentSpin => spinValue;

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
            spinValue = Vector3.zero;
            spinVelocity = Vector3.zero;
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
                        Material material = state.materials[state.hairIndices[h]];
                        SlotDataAsset slot = FindSwaySlot(state.renderer, material);
                        anySway = slot != null && TryGetStrands(slot, material, out _);
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
            Vector4 spinVector = new Vector4(spinValue.x, spinValue.y, spinValue.z, 0f);
            Vector3 headScale = head.lossyScale;
            float scale = (Mathf.Abs(headScale.x) + Mathf.Abs(headScale.y) + Mathf.Abs(headScale.z)) / 3f;
            for (int i = 0; i < states.Count; i++)
            {
                RendererState state = states[i];
                for (int h = 0; h < state.hairIndices.Count; h++)
                {
                    int index = state.hairIndices[h];
                    Vector4 swayParams = Vector4.zero;
                    Vector4 swayShape = Vector4.zero;
                    SlotDataAsset slot = anySway ? FindSwaySlot(state.renderer, state.materials[index]) : null;
                    if (slot != null && TryGetStrands(slot, state.materials[index], out StrandInfo strands))
                    {
                        // The slot is read every update, so tuning it while the game runs shows at once.
                        swayParams = new Vector4(Mathf.Clamp01(slot.HairControlStart), slot.HairControlAngle * Mathf.Deg2Rad, ripple, (int)slot.HairControlSource);
                        float length = slot.HairControlLength > 0f ? slot.HairControlLength : strands.length;
                        swayShape = new Vector4(length * scale, strands.frameDirection.x, strands.frameDirection.y, 0f);
                    }

                    // Read the block first, so values other components put there are kept.
                    state.renderer.GetPropertyBlock(block, index);
                    block.SetVector(HairCenterProperty, centerValue);
                    block.SetVector(HairSwayProperty, swayVector);
                    block.SetVector(HairSwayParamsProperty, swayParams);
                    block.SetVector(HairSwayShapeProperty, swayShape);
                    block.SetVector(HairSpinProperty, swayParams.y > 0f ? spinVector : Vector4.zero);
                    state.renderer.SetPropertyBlock(block, index);
                }
            }
        }

        // The slot whose sway settings one material slot uses, from the slots UMA built into that submesh; null when
        // it does not sway. The shader gets one set of values per material slot, so every slot in the submesh has
        // to be Hair Control Ready: an unprepared one would move by whatever its vertex colors or ID Map happen to
        // be, and a slot without vertex colors is filled with white, which moves all of it. Slots that share a
        // submesh use the values of the first of them.
        private SlotDataAsset FindSwaySlot(Renderer renderer, Material material)
        {
            if (material == null || umaData.generatedMaterials == null)
            {
                return null;
            }

            List<UMAData.GeneratedMaterial> generated = umaData.generatedMaterials.materials;
            SlotDataAsset found = null;
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
                    else if (found == null)
                    {
                        found = asset;
                    }
                }
            }

            if (found != null && unprepared != null)
            {
                if (warnedMixedMaterial != material)
                {
                    warnedMixedMaterial = material;
                    Debug.LogWarning("UMAHairControl on '" + name + "': the hair does not sway on material '" + material.name +
                        "', because slot '" + unprepared.slotName + "' is built into the same submesh and is not Hair Control " +
                        "Ready. Mark it ready (with vertex colors or an ID Map that keep it still where it must not " +
                        "move), or give it another UMA material.", this);
                }
                return null;
            }
            return found != null && found.HairControlAngle > 0f ? found : null;
        }

        /// <summary>What the shader has to know about the strands of one slot. See <see cref="TryMeasureStrands"/>.</summary>
        public struct StrandInfo
        {
            /// <summary>Length of a strand from the place 0 (scalp) to the place 1 (tip), in mesh units.</summary>
            public float length;
            /// <summary>
            /// The direction a strand runs in, from scalp to tip, as parts of the vertex tangent (x) and bitangent
            /// (y). Those two turn with the skinned mesh, so the direction is right in any pose.
            /// </summary>
            public Vector2 frameDirection;
            /// <summary>
            /// How well one direction fits the whole slot: 1 when every card runs the same way in its tangent frame,
            /// near 0 when the cards run in all directions.
            /// </summary>
            public float agreement;
        }

        private struct CachedStrands
        {
            public SlotDataAsset.HairControlSourceType source;
            public Texture idMap;
            public bool valid;
            public StrandInfo info;
        }

        private static readonly Dictionary<SlotDataAsset, CachedStrands> strandCache = new Dictionary<SlotDataAsset, CachedStrands>();
        private static readonly int IDMapProperty = Shader.PropertyToID("_ID_Map");
        private const int IDMapReadSize = 512;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            strandCache.Clear();
        }

        /// <summary>
        /// Each slot's strands are measured once, when a Hair Control first meets the slot. Call this after changing a
        /// slot's mesh, vertex colors or ID Map while the game runs, so they are measured again.
        /// </summary>
        public static void ForgetStrandMeasurements()
        {
            strandCache.Clear();
        }

        // The measurement of a slot's strands, kept per slot. It is done again when the slot's source or the ID Map
        // it is read from changes.
        private static bool TryGetStrands(SlotDataAsset asset, Material material, out StrandInfo info)
        {
            Texture idMap = null;
            if (asset.HairControlSource != SlotDataAsset.HairControlSourceType.VertexColor && material != null && material.HasProperty(IDMapProperty))
            {
                idMap = material.GetTexture(IDMapProperty);
            }
            if (strandCache.TryGetValue(asset, out CachedStrands cached) && cached.source == asset.HairControlSource && cached.idMap == idMap)
            {
                info = cached.info;
                return cached.valid;
            }

            cached.source = asset.HairControlSource;
            cached.idMap = idMap;
            cached.valid = TryMeasureStrands(asset, idMap, out cached.info);
            strandCache[asset] = cached;
            if (!cached.valid)
            {
                Debug.LogWarning("UMAHairControl: the hair of slot '" + asset.slotName + "' does not sway, because its strands could not " +
                    "be measured. The slot needs normals, tangents and " +
                    (asset.HairControlSource == SlotDataAsset.HairControlSourceType.VertexColor
                        ? "vertex colors that run from dark at the scalp to white at the tip."
                        : "UVs, and its material an ID Map that runs from the scalp to the tip."), asset);
            }
            else if (cached.info.agreement < 0.5f)
            {
                Debug.LogWarning("UMAHairControl: the cards of slot '" + asset.slotName + "' do not run the same way in their tangent " +
                    "frames (agreement " + cached.info.agreement.ToString("F2") + " of 1), so some of its hair will bend the wrong way.", asset);
            }
            info = cached.info;
            return cached.valid;
        }

        /// <summary>
        /// Measures the strands of a slot from its mesh: how long a strand is, and which way it runs in the tangent
        /// frame of its vertices. The place of each vertex along its strand is read as the slot's
        /// <see cref="SlotDataAsset.HairControlSource"/> says; <paramref name="idMap"/> is only used for the ID Map
        /// sources, and is read with the slot's own UVs. False when the slot has nothing to measure.
        /// </summary>
        public static bool TryMeasureStrands(SlotDataAsset asset, Texture idMap, out StrandInfo info)
        {
            info = default;
            UMAMeshData meshData = asset != null ? asset.meshData : null;
            if (meshData == null || meshData.vertices == null || meshData.submeshes == null)
            {
                return false;
            }
            int count = meshData.vertices.Length;
            float[] place = new float[count];
            if (asset.HairControlSource == SlotDataAsset.HairControlSourceType.VertexColor)
            {
                if (meshData.colors32 == null || meshData.colors32.Length != count)
                {
                    return false;
                }
                for (int i = 0; i < count; i++)
                {
                    place[i] = meshData.colors32[i].r / 255f;
                }
            }
            else
            {
                if (idMap == null || meshData.uv == null || meshData.uv.Length != count)
                {
                    return false;
                }
                bool tip = asset.HairControlSource == SlotDataAsset.HairControlSourceType.IDMapTip;
                Color32[] pixels = ReadTexture(idMap, out int width, out int height);
                for (int i = 0; i < count; i++)
                {
                    // UVs outside 0..1 repeat; a UV of exactly 1 is the last texel, not the first.
                    Vector2 uv = meshData.uv[i];
                    float u = uv.x < 0f || uv.x > 1f ? Mathf.Repeat(uv.x, 1f) : uv.x;
                    float v = uv.y < 0f || uv.y > 1f ? Mathf.Repeat(uv.y, 1f) : uv.y;
                    int x = Mathf.Clamp((int)(u * width), 0, width - 1);
                    int y = Mathf.Clamp((int)(v * height), 0, height - 1);
                    Color32 pixel = pixels[y * width + x];
                    place[i] = tip ? pixel.g / 255f : 1f - pixel.r / 255f;
                }
            }

            int[] triangles = null;
            int submesh = asset.subMeshIndex;
            if (submesh >= 0 && submesh < meshData.submeshes.Length && meshData.submeshes[submesh] != null)
            {
                triangles = meshData.submeshes[submesh].getManagedTriangles(0);
            }
            return MeasureStrands(meshData.vertices, meshData.normals, meshData.tangents, triangles, place, out info);
        }

        /// <summary>
        /// The measurement itself. <paramref name="place"/> holds, per vertex, where it is along its strand: 0 at the
        /// scalp, 1 at the tip. On every triangle the place changes fastest along the strand, and how fast it
        /// changes there gives the length: a strand whose place goes from 0 to 1 over 12 cm changes by 1/0.12 a meter.
        /// </summary>
        public static bool MeasureStrands(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, int[] triangles, float[] place, out StrandInfo info)
        {
            info = default;
            if (vertices == null || triangles == null || place == null || normals == null || tangents == null)
            {
                return false;
            }
            int count = vertices.Length;
            if (place.Length != count || normals.Length != count || tangents.Length != count)
            {
                return false;
            }

            Vector3[] slope = new Vector3[count]; // per vertex: the direction the place rises in, weighted by area
            double area = 0d, slopeArea = 0d;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count)
                {
                    continue;
                }
                float rise1 = place[b] - place[a], rise2 = place[c] - place[a];
                if (Mathf.Abs(rise1) < 1e-4f && Mathf.Abs(rise2) < 1e-4f)
                {
                    continue; // the place does not change here (a part that stays still): nothing to learn from it
                }
                Vector3 edge1 = vertices[b] - vertices[a], edge2 = vertices[c] - vertices[a];
                Vector3 normal = Vector3.Cross(edge1, edge2);
                float normalSquared = normal.sqrMagnitude;
                if (normalSquared < 1e-20f)
                {
                    continue;
                }
                Vector3 rise = (rise1 * Vector3.Cross(edge2, normal) + rise2 * Vector3.Cross(normal, edge1)) / normalSquared;
                float triangleArea = 0.5f * Mathf.Sqrt(normalSquared);
                area += triangleArea;
                slopeArea += rise.magnitude * triangleArea;
                Vector3 weighted = rise * triangleArea;
                slope[a] += weighted;
                slope[b] += weighted;
                slope[c] += weighted;
            }
            if (area <= 0d || slopeArea <= 1e-12d)
            {
                return false;
            }

            // The same strand direction, written in each vertex's tangent frame and added up.
            double alongTangent = 0d, alongBitangent = 0d, weight = 0d;
            for (int i = 0; i < count; i++)
            {
                float length = slope[i].magnitude;
                if (length < 1e-12f)
                {
                    continue;
                }
                Vector3 direction = slope[i] / length;
                Vector3 tangent = tangents[i];
                Vector3 bitangent = Vector3.Cross(normals[i], tangent) * (tangents[i].w < 0f ? -1f : 1f);
                alongTangent += Vector3.Dot(direction, tangent) * length;
                alongBitangent += Vector3.Dot(direction, bitangent) * length;
                weight += length;
            }
            double frameLength = System.Math.Sqrt(alongTangent * alongTangent + alongBitangent * alongBitangent);
            if (weight <= 0d || frameLength <= 1e-12d)
            {
                return false;
            }
            Vector2 frame = new Vector2((float)(alongTangent / frameLength), (float)(alongBitangent / frameLength));

            double agreement = 0d;
            for (int i = 0; i < count; i++)
            {
                float length = slope[i].magnitude;
                if (length < 1e-12f)
                {
                    continue;
                }
                Vector3 tangent = tangents[i];
                Vector3 bitangent = Vector3.Cross(normals[i], tangent) * (tangents[i].w < 0f ? -1f : 1f);
                Vector3 fromFrame = (tangent * frame.x + bitangent * frame.y).normalized;
                agreement += Vector3.Dot(fromFrame, slope[i] / length) * length;
            }

            info.length = (float)(area / slopeArea);
            info.frameDirection = frame;
            info.agreement = (float)(agreement / weight);
            return true;
        }

        // A small copy of a texture that can be read, with the values the shader sees. Works for textures that are
        // not readable themselves.
        private static Color32[] ReadTexture(Texture texture, out int width, out int height)
        {
            width = Mathf.Clamp(texture.width, 1, IDMapReadSize);
            height = Mathf.Clamp(texture.height, 1, IDMapReadSize);
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            copy.Apply(false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Color32[] pixels = copy.GetPixels32();
            if (Application.isPlaying)
            {
                Destroy(copy);
            }
            else
            {
                DestroyImmediate(copy);
            }
            return pixels;
        }

        // Two damped springs for the whole head of hair. The sway is pulled towards a target made of the head's speed
        // (hair trails behind a moving head, and swings forward when the head stops) and of the wind. The spin is
        // pulled towards the head's turn rate, the other way round: hair lags behind a turning head, and swings on
        // when it stops. The shader pushes each strand by the spin sideways around the head.
        private void StepSway(float deltaTime)
        {
            Vector3 headPosition = head.position;
            Quaternion headRotation = head.rotation;
            if (!hasLastHeadPosition)
            {
                lastHeadPosition = headPosition;
                lastHeadRotation = headRotation;
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

            Vector3 headTurn = TurnRate(lastHeadRotation, headRotation, deltaTime);
            lastHeadRotation = headRotation;
            if (headTurn.sqrMagnitude > TeleportTurn * TeleportTurn)
            {
                headTurn = Vector3.zero; // set to a new rotation, not a turn the hair should follow
            }

            float step = Mathf.Min(deltaTime, MaxStep);
            gustTime += step;
            // A gust is stronger or weaker than the wind, and comes from a little to one side of it. The two change
            // at their own pace, so the hair does not just lean and stay there.
            float gust = 1f + gustiness * (Mathf.PerlinNoise(gustTime * 0.6f + gustSeed, gustSeed) * 2f - 1f);
            float veer = gustiness * GustVeer * (Mathf.PerlinNoise(gustSeed, gustTime * 0.85f + gustSeed) * 2f - 1f);
            Vector3 windNow = CurrentWind(headPosition);
            windNow = windNow * gust + Vector3.Cross(Vector3.up, windNow) * veer;
            Vector3 target = Vector3.ClampMagnitude(-headVelocity * movementSway + windNow * windSway, 1f);

            Vector3 spinTarget = Vector3.ClampMagnitude(-headTurn * turnSway, 1f);

            float omega = TwoPi * springFrequency;
            int substeps = Mathf.Clamp(Mathf.CeilToInt(omega * step * 2f), 1, 8); // keeps the integration stable
            float dt = step / substeps;
            for (int i = 0; i < substeps; i++)
            {
                swayVelocity += (omega * omega * (target - swayValue) - 2f * damping * omega * swayVelocity) * dt;
                swayValue += swayVelocity * dt;
                spinVelocity += (omega * omega * (spinTarget - spinValue) - 2f * damping * omega * spinVelocity) * dt;
                spinValue += spinVelocity * dt;
            }
            LimitToFullSway(ref swayValue, ref swayVelocity);
            LimitToFullSway(ref spinValue, ref spinVelocity);

            ripplePhase = Mathf.Repeat(ripplePhase + step * rippleFrequency * TwoPi, TwoPi);
        }

        // Keeps a spring at full sway or less, taking out the part of its velocity that pushes further out, so it
        // does not stick at the limit.
        private static void LimitToFullSway(ref Vector3 value, ref Vector3 velocity)
        {
            if (value.sqrMagnitude <= 1f)
            {
                return;
            }
            value.Normalize();
            float outward = Vector3.Dot(velocity, value);
            if (outward > 0f)
            {
                velocity -= value * outward;
            }
        }

        /// <summary>The rotation from <paramref name="from"/> to <paramref name="to"/>, as an axis times radians per second.</summary>
        internal static Vector3 TurnRate(Quaternion from, Quaternion to, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return Vector3.zero;
            }
            Quaternion delta = to * Quaternion.Inverse(from);
            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || axis.sqrMagnitude < 1e-12f)
            {
                return Vector3.zero;
            }
            if (degrees > 180f)
            {
                degrees -= 360f; // the short way round
            }
            return axis.normalized * (degrees * Mathf.Deg2Rad / deltaTime);
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
                    block.SetVector(HairSwayShapeProperty, Vector4.zero);
                    block.SetVector(HairSpinProperty, Vector4.zero);
                    state.renderer.SetPropertyBlock(block, index);
                }
            }
        }
    }
}
