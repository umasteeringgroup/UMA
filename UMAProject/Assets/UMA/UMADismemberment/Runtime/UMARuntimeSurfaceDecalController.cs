using System;
using System.Collections.Generic;
using System.Threading;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Dismemberment
{
    /// <summary>
    /// Non-destructive runtime compositor for flowing cut fluids and fadeable UMA RT stamps.
    /// Generated UMA atlases remain immutable; only owned output textures are rebound.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DynamicCharacterAvatar))]
    public sealed class UMARuntimeSurfaceDecalController : MonoBehaviour
    {
        private const string ResourceRoot = "UMA/Dismemberment/";
        private const float MinimumInterval = 0.001f;
        private static long nextControllerSession;

        private enum EffectKind { Fluid, Stamp, SurfaceCut }

        private sealed class OutputChannel
        {
            public AtlasContext context;
            public int channelIndex;
            public int channelMode;
            public string propertyName;
            public Texture baseTexture;
            public RenderTexture output;
        }

        private sealed class AtlasContext
        {
            public UMAData.GeneratedMaterial generated;
            public SkinnedMeshRenderer renderer;
            public readonly List<OutputChannel> outputs = new List<OutputChannel>();
            public RenderTexture surfacePosition;
            public RenderTexture surfaceFlow;
            public RenderTexture surfaceDepth;
            public RenderTexture injection;
            public Texture2D seamLinks;
            public CommandBuffer surfaceCommand;
            public RenderTargetIdentifier[] fieldTargets;
            public int simulationWidth;
            public int simulationHeight;
            public float surfaceAccumulator;
            public float compositeAccumulator;
            public bool surfaceValid;
            public bool compositeDirty;
        }

        private sealed class FluidBinding
        {
            public AtlasContext context;
            public readonly List<InjectionSource> sources = new List<InjectionSource>();
            public RenderTexture stateA;
            public RenderTexture stateB;
            public RenderTexture speedA;
            public RenderTexture speedB;
            public RenderTexture flux;
            public bool currentIsA = true;
            public float simulationAccumulator;
            public RenderTexture Current => currentIsA ? stateA : stateB;
            public RenderTexture Next => currentIsA ? stateB : stateA;
            public RenderTexture CurrentSpeed => currentIsA ? speedA : speedB;
            public RenderTexture NextSpeed => currentIsA ? speedB : speedA;
        }

        private sealed class InjectionSource
        {
            public Mesh mesh;
            public bool ownsMesh = true;
            public Texture mask;
            public float intensity = 1f;
            public float speedMultiplier = 1f;
            public bool useRadialLimit;
            public Vector2 radialCenter = new Vector2(0.5f, 0.5f);
            public float radialRadius = 0.05f;
            public float radialFeather = 0.02f;
        }

        private sealed class StampBinding
        {
            public OutputChannel output;
            public Mesh mesh;
            public Texture texture;
            public Texture mask;
        }

        private sealed class SurfaceCutBinding
        {
            public OutputChannel output;
            public Mesh mesh;
            public Color centerColor;
            public Color edgeColor;
            public float halfWidthMeters;
            public float lengthMeters;
            public float centerFraction;
            public float edgeSoftness;
            public float endTaperFraction;
        }

        private sealed class EffectRecord
        {
            public RuntimeDecalHandle handle;
            public EffectKind kind;
            public RuntimeDecalState state;
            public float elapsed;
            public float stateElapsed;
            public bool stopRequested;
            public bool fadeRequested;
            public UMASurfaceFluidProfile profile;
            public RuntimeDecalFadeSettings fade;
            public DecalRTStampAsset stamp;
            public DismembermentCutSurface[] sourceSurfaces;
            public bool dismembermentSource;
            public bool hasFallbackOrigin;
            public Vector3 fallbackWorldPosition;
            public Vector3 fallbackWorldNormal;
            public bool awaitingCleanBase;
            public bool persistentStamp;
            public bool allowSlotIdentityWithoutOverlayGroup;
            public readonly List<FluidBinding> fluids = new List<FluidBinding>();
            public readonly List<StampBinding> stamps = new List<StampBinding>();
            public readonly List<SurfaceCutBinding> surfaceCuts =
                new List<SurfaceCutBinding>();
            public readonly List<UMASurfaceFluidFallbackTrail> fallbacks =
                new List<UMASurfaceFluidFallbackTrail>();
        }

        private readonly struct VertexSignature : IEquatable<VertexSignature>
        {
            private readonly int px, py, pz, nx, ny, nz;
            private readonly int weightHash;
            private readonly int slotKey;

            public VertexSignature(Vector3 position, Vector3 normal, int weightHash,
                int slotKey, float tolerance)
            {
                float scale = 1f / Mathf.Max(0.000001f, tolerance);
                px = Mathf.RoundToInt(position.x * scale);
                py = Mathf.RoundToInt(position.y * scale);
                pz = Mathf.RoundToInt(position.z * scale);
                nx = Mathf.RoundToInt(normal.x * 1000f);
                ny = Mathf.RoundToInt(normal.y * 1000f);
                nz = Mathf.RoundToInt(normal.z * 1000f);
                this.weightHash = weightHash;
                this.slotKey = slotKey;
            }

            public bool Equals(VertexSignature other) => px == other.px && py == other.py &&
                pz == other.pz && nx == other.nx && ny == other.ny && nz == other.nz &&
                weightHash == other.weightHash && slotKey == other.slotKey;
            public override bool Equals(object obj) => obj is VertexSignature other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = px;
                    hash = (hash * 397) ^ py; hash = (hash * 397) ^ pz;
                    hash = (hash * 397) ^ nx; hash = (hash * 397) ^ ny;
                    hash = (hash * 397) ^ nz;
                    hash = (hash * 397) ^ weightHash;
                    return (hash * 397) ^ slotKey;
                }
            }
        }

        [Header("Diagnostics")]
        [SerializeField] private bool logDiagnostics = true;

        public bool IsGpuSimulationAvailable => gpuReady;
        public int ActiveEffectCount => effects.Count;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        private DynamicCharacterAvatar avatar;
        private UMAData umaData;
        private bool subscribed;
        private bool gpuReady;
        private long controllerSession;
        private long nextSequence;
        private ComputeShader fluidCompute;
        private Material surfaceFieldMaterial;
        private Material compositeMaterial;
        private Material sourceMaskMaterial;
        private Material surfaceCutMaterial;
        private Material fallbackTrailMaterial;
        private int clearKernel = -1, clearScalarKernel = -1, injectKernel = -1,
            fluxKernel = -1, applyKernel = -1;
        private readonly List<AtlasContext> contexts = new List<AtlasContext>();
        private readonly List<EffectRecord> effects = new List<EffectRecord>();
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<string> atlasMetadata = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Interlocked.Exchange(ref nextControllerSession, 0);
        }

        private void OnEnable()
        {
            controllerSession = Interlocked.Increment(ref nextControllerSession);
            nextSequence = 0;
            avatar = GetComponent<DynamicCharacterAvatar>();
            LoadResources();
            SubscribeAvatar();
            if (avatar != null && avatar.umaData != null) HandleCharacterUpdated(avatar.umaData);
        }

        private void OnDisable()
        {
            UnsubscribeAvatar();
            ClearAll();
            ReleaseMaterials();
        }

        private void OnDestroy()
        {
            UnsubscribeAvatar();
            ClearAll();
            ReleaseMaterials();
        }

        private void LateUpdate()
        {
            if (effects.Count == 0 || umaData == null) return;
            float delta = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            if (delta <= 0f) return;

            for (int i = effects.Count - 1; i >= 0; i--)
            {
                EffectRecord effect = effects[i];
                AdvanceEffect(effect, delta);
                if (effect.state == RuntimeDecalState.Complete)
                {
                    ReleaseEffect(effect);
                    effects.RemoveAt(i);
                }
            }

            for (int i = 0; i < contexts.Count; i++) UpdateContext(contexts[i], delta);
            RemoveUnusedContexts();
        }

        public RuntimeDecalHandle StartBleed(DismembermentResult cut,
            UMASurfaceFluidProfile profile)
        {
            if (cut == null || profile == null)
            {
                AddDiagnostic("StartBleed requires a completed cut and a fluid profile.");
                return default;
            }
            if (cut.cutSurfaces == null || cut.cutSurfaces.Length == 0)
            {
                AddDiagnostic("The cut contains no closed UV boundary. Using the bounded " +
                    "non-compute fallback when a source renderer is available.");
            }

            var record = new EffectRecord
            {
                handle = CreateHandle(),
                kind = EffectKind.Fluid,
                state = RuntimeDecalState.Emitting,
                profile = profile,
                sourceSurfaces = CloneSurfaceArray(cut.cutSurfaces),
                dismembermentSource = true
            };

            if (profile.detachedRoute == SurfaceFluidDetachedRoute.IndependentDetachedPiece)
            {
                if (!gpuReady || !BindIndependentFluidRecord(record, cut))
                    StartIndependentFallbacks(record, cut);
            }
            else if (gpuReady && BindFluidRecord(record))
            {
                if (profile.detachedRoute == SurfaceFluidDetachedRoute.SourceBody)
                    IsolateDetachedMaterials(cut, record);
            }
            else
            {
                StartFallbacks(record, cut);
            }

            if (record.fluids.Count == 0 && record.fallbacks.Count == 0)
            {
                ReleaseEffect(record);
                AddDiagnostic("No compatible surface renderer or fallback origin could be " +
                    "resolved for the cut.");
                return default;
            }
            effects.Add(record);
            return record.handle;
        }

        /// <summary>
        /// Starts surface flow from the cached UV geometry of an existing decal stamp. This does
        /// not draw the decal again; it only uses its target geometry and alpha as an emitter.
        /// </summary>
        public RuntimeDecalHandle StartBleedFromDecal(DecalRTStampAsset decalStamp,
            UMASurfaceFluidProfile profile)
        {
            return StartBleedFromDecalInternal(decalStamp, profile, Vector3.zero,
                Vector3.up, false);
        }

        /// <summary>
        /// Starts surface flow from an existing decal and supplies its world-space hit for the
        /// non-compute fallback. Pass DecalLayerResult.hitPoint and hitNormal here.
        /// </summary>
        public RuntimeDecalHandle StartBleedFromDecal(DecalRTStampAsset decalStamp,
            UMASurfaceFluidProfile profile, Vector3 worldPosition, Vector3 worldNormal)
        {
            return StartBleedFromDecalInternal(decalStamp, profile, worldPosition,
                worldNormal, true);
        }

        /// <summary>
        /// Starts flow from a successful decal result. The result supplies the world-space origin
        /// used by the bounded fallback when compute simulation is unavailable.
        /// </summary>
        public RuntimeDecalHandle StartBleedFromDecal(DecalRTStampAsset decalStamp,
            UMASurfaceFluidProfile profile, DecalRenderTexture.DecalLayerResult decalResult)
        {
            if (!decalResult.success)
            {
                AddDiagnostic("StartBleedFromDecal requires a successful decal layer result.");
                return default;
            }
            return StartBleedFromDecalInternal(decalStamp, profile, decalResult.hitPoint,
                decalResult.hitNormal, true);
        }

        private RuntimeDecalHandle StartBleedFromDecalInternal(DecalRTStampAsset decalStamp,
            UMASurfaceFluidProfile profile, Vector3 worldPosition, Vector3 worldNormal,
            bool hasFallbackOrigin)
        {
            if (decalStamp == null || profile == null ||
                umaData?.generatedMaterials?.materials == null)
            {
                AddDiagnostic("StartBleedFromDecal requires a cached decal stamp, a fluid " +
                    "profile, and a generated UMA avatar.");
                return default;
            }
            var record = new EffectRecord
            {
                handle = CreateHandle(),
                kind = EffectKind.Fluid,
                state = RuntimeDecalState.Emitting,
                profile = profile,
                stamp = decalStamp,
                sourceSurfaces = Array.Empty<DismembermentCutSurface>(),
                hasFallbackOrigin = hasFallbackOrigin,
                fallbackWorldPosition = worldPosition,
                fallbackWorldNormal = worldNormal.sqrMagnitude > 0.000001f
                    ? worldNormal.normalized : Vector3.up
            };
            if (!gpuReady || !BindFluidStampRecord(record))
            {
                if (hasFallbackOrigin)
                    StartFallback(record, record.fallbackWorldPosition,
                        record.fallbackWorldNormal);
            }
            if (record.fluids.Count == 0 && record.fallbacks.Count == 0)
            {
                ReleaseEffect(record);
                RemoveUnusedContexts();
                AddDiagnostic($"Decal stamp '{decalStamp.name}' did not match a generated target. " +
                    "On non-compute platforms, use the overload that also supplies the decal hit " +
                    "position and normal.");
                return default;
            }
            effects.Add(record);
            return record.handle;
        }

        public RuntimeDecalHandle AddFadeableStamp(DecalRTStampAsset stamp,
            RuntimeDecalFadeSettings fade)
        {
            if (stamp == null || compositeMaterial == null ||
                umaData?.generatedMaterials?.materials == null)
            {
                AddDiagnostic("AddFadeableStamp requires a stamp and a generated UMA avatar.");
                return default;
            }
            var record = new EffectRecord
            {
                handle = CreateHandle(),
                kind = EffectKind.Stamp,
                state = RuntimeDecalState.Holding,
                fade = NormalizeFade(fade),
                stamp = stamp
            };
            BindStampRecord(record);
            if (record.stamps.Count == 0)
            {
                ReleaseEffect(record);
                AddDiagnostic($"Runtime stamp '{stamp.name}' did not match a generated slot, " +
                    "material property, or source texture.");
                return default;
            }
            effects.Add(record);
            MarkContextsDirty(record);
            return record.handle;
        }

        /// <summary>
        /// Keeps a decal visible as an owned compositor layer until Clear or ClearAll is called.
        /// Runtime click stamps may use their recorded slot identity when no replay overlay group
        /// was supplied. The record is rebound after an UMA atlas rebuild.
        /// </summary>
        public RuntimeDecalHandle AddPersistentStamp(DecalRTStampAsset stamp)
        {
            if (stamp == null || compositeMaterial == null ||
                umaData?.generatedMaterials?.materials == null)
            {
                AddDiagnostic("AddPersistentStamp requires a stamp and a generated UMA avatar.");
                return default;
            }
            var record = new EffectRecord
            {
                handle = CreateHandle(),
                kind = EffectKind.Stamp,
                state = RuntimeDecalState.Holding,
                stamp = stamp,
                persistentStamp = true,
                allowSlotIdentityWithoutOverlayGroup = true
            };
            BindStampRecord(record);
            if (record.stamps.Count == 0)
            {
                ReleaseEffect(record);
                AddDiagnostic($"Persistent runtime stamp '{stamp.name}' did not match a generated " +
                    "slot, material property, or source texture.");
                return default;
            }
            effects.Add(record);
            MarkContextsDirty(record);
            return record.handle;
        }

        /// <summary>
        /// Adds a persistent, tapered surface cut represented by an atlas-space mesh. The
        /// controller takes ownership of cutMesh whether binding succeeds or fails.
        /// </summary>
        internal RuntimeDecalHandle AddSurfaceCut(SkinnedMeshRenderer renderer, int submesh,
            Mesh cutMesh, UMASurfaceCutProfile profile, float lengthMeters)
        {
            if (renderer == null || cutMesh == null || profile == null ||
                surfaceCutMaterial == null || umaData?.generatedMaterials?.materials == null)
            {
                DestroyOwned(cutMesh);
                AddDiagnostic("AddSurfaceCut requires a generated renderer, cut mesh, profile, " +
                    "and the surface-cut compositor shader.");
                return default;
            }
            UMAData.GeneratedMaterial generated = FindGeneratedMaterial(renderer, submesh);
            if (generated == null)
            {
                DestroyOwned(cutMesh);
                AddDiagnostic("The selected surface-cut renderer and material do not match a " +
                    "generated UMA material.");
                return default;
            }
            AtlasContext context = FindContext(generated);
            if (context == null)
            {
                UMASurfaceFluidProfile budget = CreateBudgetProfile();
                context = CreateContext(generated, budget, false);
                DestroyOwned(budget);
                if (context != null) contexts.Add(context);
            }
            int channel = FindFirstAlbedoChannel(generated.umaMaterial);
            OutputChannel output = channel >= 0 && context != null
                ? EnsureOutput(context, channel, 0) : null;
            if (output == null)
            {
                DestroyOwned(cutMesh);
                AddDiagnostic("The selected surface has no compatible albedo channel for a cut.");
                return default;
            }

            var record = new EffectRecord
            {
                handle = CreateHandle(),
                kind = EffectKind.SurfaceCut,
                state = RuntimeDecalState.Holding
            };
            record.surfaceCuts.Add(new SurfaceCutBinding
            {
                output = output,
                mesh = cutMesh,
                centerColor = profile.centerColor,
                edgeColor = profile.edgeColor,
                halfWidthMeters = profile.widthMeters * 0.5f,
                lengthMeters = lengthMeters,
                centerFraction = profile.centerFraction,
                edgeSoftness = profile.edgeSoftness,
                endTaperFraction = profile.endTaperFraction
            });
            effects.Add(record);
            MarkContextsDirty(record);
            return record.handle;
        }

        /// <summary>
        /// Starts one fluid layer with several metric emitters distributed along a surface-cut
        /// mesh. The controller takes ownership of sourceMesh.
        /// </summary>
        internal RuntimeDecalHandle StartBleedFromSurfaceCut(SkinnedMeshRenderer renderer,
            int submesh, Mesh sourceMesh, UMASurfaceFluidProfile profile,
            float[] sourceDistancesMeters, Vector3[] worldPositions, Vector3[] worldNormals,
            float[] speedMultipliers, float[] sizeMultipliers)
        {
            if (renderer == null || sourceMesh == null || profile == null ||
                sourceDistancesMeters == null || sourceDistancesMeters.Length == 0)
            {
                DestroyOwned(sourceMesh);
                AddDiagnostic("Surface-cut bleeding requires a renderer, source mesh, fluid " +
                    "profile, and at least one source distance.");
                return default;
            }
            var record = new EffectRecord
            {
                handle = CreateHandle(),
                kind = EffectKind.Fluid,
                state = RuntimeDecalState.Emitting,
                profile = profile,
                sourceSurfaces = Array.Empty<DismembermentCutSurface>()
            };

            if (gpuReady)
            {
                UMAData.GeneratedMaterial generated = FindGeneratedMaterial(renderer, submesh);
                AtlasContext context = generated != null ? FindContext(generated) : null;
                if (context == null && generated != null)
                {
                    context = CreateContext(generated, profile, true);
                    if (context != null) contexts.Add(context);
                }
                if (context != null)
                {
                    EnsureFluidOutputs(context, profile);
                    FluidBinding binding = context.outputs.Count > 0
                        ? CreateFluidBinding(context, profile) : null;
                    if (binding != null)
                    {
                        float radius = Mathf.Max(0.0001f, profile.emissionRadiusMeters);
                        for (int i = 0; i < sourceDistancesMeters.Length; i++)
                        {
                            float sourceSize = sizeMultipliers != null &&
                                (uint)i < (uint)sizeMultipliers.Length
                                ? Mathf.Max(0.05f, sizeMultipliers[i]) : 1f;
                            float sourceRadius = radius * sourceSize;
                            binding.sources.Add(new InjectionSource
                            {
                                mesh = sourceMesh,
                                ownsMesh = i == 0,
                                speedMultiplier = speedMultipliers != null &&
                                    (uint)i < (uint)speedMultipliers.Length
                                    ? Mathf.Max(0.05f, speedMultipliers[i]) : 1f,
                                useRadialLimit = true,
                                radialCenter = new Vector2(0f, sourceDistancesMeters[i]),
                                radialRadius = sourceRadius,
                                radialFeather = Mathf.Max(0.0001f, sourceRadius * 0.45f)
                            });
                        }
                        record.fluids.Add(binding);
                    }
                }
            }

            if (record.fluids.Count == 0)
            {
                DestroyOwned(sourceMesh);
                int count = worldPositions != null && worldNormals != null
                    ? Mathf.Min(worldPositions.Length, worldNormals.Length) : 0;
                for (int i = 0; i < count; i++)
                {
                    float speed = speedMultipliers != null &&
                        (uint)i < (uint)speedMultipliers.Length ? speedMultipliers[i] : 1f;
                    float size = sizeMultipliers != null &&
                        (uint)i < (uint)sizeMultipliers.Length ? sizeMultipliers[i] : 1f;
                    StartFallback(record, worldPositions[i], worldNormals[i], speed, size);
                }
            }
            if (record.fluids.Count == 0 && record.fallbacks.Count == 0)
            {
                ReleaseEffect(record);
                AddDiagnostic("The surface cut could not bind fluid outputs or fallback origins.");
                return default;
            }
            effects.Add(record);
            return record.handle;
        }

        /// <summary>
        /// Explicit migration path for a stamp already baked into the generated atlas. It asks
        /// UMA for one clean rebuild, then registers the stamp dynamically; normal fade/clear
        /// operations after that rebuild never rebuild the avatar.
        /// </summary>
        public RuntimeDecalHandle AddPreviouslyBakedFadeableStamp(DecalRTStampAsset stamp,
            RuntimeDecalFadeSettings fade)
        {
            if (stamp == null || avatar == null || compositeMaterial == null) return default;
            var record = new EffectRecord
            {
                handle = CreateHandle(),
                kind = EffectKind.Stamp,
                state = RuntimeDecalState.Holding,
                fade = NormalizeFade(fade),
                stamp = stamp,
                awaitingCleanBase = true
            };
            effects.Add(record);
            try
            {
                avatar.BuildCharacter(true, !avatar.BundleCheck);
                return record.handle;
            }
            catch (Exception exception)
            {
                effects.Remove(record);
                AddDiagnostic("The clean-base rebuild for a legacy baked stamp failed: " +
                    exception.Message);
                return default;
            }
        }

        public Texture GetDebugTexture(RuntimeSurfaceDebugTexture kind, int contextIndex = 0)
        {
            if ((uint)contextIndex >= (uint)contexts.Count) return null;
            AtlasContext context = contexts[contextIndex];
            switch (kind)
            {
                case RuntimeSurfaceDebugTexture.CompositedOutput:
                    return context.outputs.Count > 0 ? context.outputs[0].output : null;
                case RuntimeSurfaceDebugTexture.SurfaceWorldPosition:
                    return context.surfacePosition;
                case RuntimeSurfaceDebugTexture.SurfaceFlow:
                    return context.surfaceFlow;
                case RuntimeSurfaceDebugTexture.InjectionMask:
                    return context.injection;
                case RuntimeSurfaceDebugTexture.SeamLinks:
                    return context.seamLinks;
                case RuntimeSurfaceDebugTexture.MobileFluidState:
                    for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                        for (int bindingIndex = 0;
                            bindingIndex < effects[effectIndex].fluids.Count; bindingIndex++)
                            if (effects[effectIndex].fluids[bindingIndex].context == context)
                                return effects[effectIndex].fluids[bindingIndex].Current;
                    return null;
                case RuntimeSurfaceDebugTexture.MobileSpeedState:
                    for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                        for (int bindingIndex = 0;
                            bindingIndex < effects[effectIndex].fluids.Count; bindingIndex++)
                            if (effects[effectIndex].fluids[bindingIndex].context == context)
                                return effects[effectIndex].fluids[bindingIndex].CurrentSpeed;
                    return null;
                default:
                    return null;
            }
        }

        public bool StopFlow(RuntimeDecalHandle handle)
        {
            EffectRecord effect = FindEffect(handle);
            if (effect == null || effect.kind != EffectKind.Fluid) return false;
            effect.stopRequested = true;
            for (int i = 0; i < effect.fallbacks.Count; i++)
                if (effect.fallbacks[i] != null) effect.fallbacks[i].StopFlow();
            return true;
        }

        public bool FadeNow(RuntimeDecalHandle handle)
        {
            EffectRecord effect = FindEffect(handle);
            if (effect == null || effect.kind == EffectKind.SurfaceCut) return false;
            effect.fadeRequested = true;
            for (int i = 0; i < effect.fallbacks.Count; i++)
                if (effect.fallbacks[i] != null) effect.fallbacks[i].FadeNow();
            return true;
        }

        public bool Clear(RuntimeDecalHandle handle)
        {
            EffectRecord effect = FindEffect(handle);
            if (effect == null) return false;
            effect.state = RuntimeDecalState.Complete;
            MarkContextsDirty(effect);
            return true;
        }

        public void ClearAll()
        {
            for (int i = 0; i < effects.Count; i++) ReleaseEffect(effects[i]);
            effects.Clear();
            ReleaseContexts();
        }

        public bool TryGetState(RuntimeDecalHandle handle, out RuntimeDecalState state)
        {
            EffectRecord effect = FindEffect(handle);
            state = effect != null ? effect.state : RuntimeDecalState.Complete;
            return effect != null;
        }

        internal void ClearForDismembermentReset()
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                EffectRecord effect = effects[i];
                if (!effect.dismembermentSource) continue;
                if (effect.kind == EffectKind.Fluid && effect.profile != null &&
                    effect.profile.persistAcrossAvatarRebuild &&
                    effect.profile.detachedRoute !=
                        SurfaceFluidDetachedRoute.IndependentDetachedPiece) continue;
                ReleaseEffect(effect);
                effects.RemoveAt(i);
            }
            RemoveUnusedContexts();
        }

        private void LoadResources()
        {
            fluidCompute = Resources.Load<ComputeShader>(ResourceRoot + "SurfaceFluid");
            Shader field = Resources.Load<Shader>(ResourceRoot + "SurfaceField");
            Shader composite = Resources.Load<Shader>(ResourceRoot + "RuntimeDecalComposite");
            Shader source = Resources.Load<Shader>(ResourceRoot + "SourceMask");
            Shader cut = Resources.Load<Shader>(ResourceRoot + "SurfaceCutComposite");
            Shader fallback = Resources.Load<Shader>(ResourceRoot + "FallbackTrail");
            if (field != null) surfaceFieldMaterial = new Material(field)
                { hideFlags = HideFlags.HideAndDontSave };
            if (composite != null) compositeMaterial = new Material(composite)
                { hideFlags = HideFlags.HideAndDontSave };
            if (source != null) sourceMaskMaterial = new Material(source)
                { hideFlags = HideFlags.HideAndDontSave };
            if (cut != null) surfaceCutMaterial = new Material(cut)
                { hideFlags = HideFlags.HideAndDontSave };
            if (fallback != null) fallbackTrailMaterial = new Material(fallback)
                { hideFlags = HideFlags.HideAndDontSave };
            gpuReady = SystemInfo.supportsComputeShaders && fluidCompute != null &&
                surfaceFieldMaterial != null && compositeMaterial != null &&
                sourceMaskMaterial != null;
            if (!gpuReady)
            {
                AddDiagnostic("Compute surface fluid is unavailable. Runtime bleeding will use " +
                    "the bounded no-rebuild trail fallback.");
                return;
            }
            try
            {
                clearKernel = fluidCompute.FindKernel("CSClear");
                clearScalarKernel = fluidCompute.FindKernel("CSClearScalar");
                injectKernel = fluidCompute.FindKernel("CSInject");
                fluxKernel = fluidCompute.FindKernel("CSFlux");
                applyKernel = fluidCompute.FindKernel("CSApply");
                gpuReady = fluidCompute.IsSupported(clearKernel) &&
                    fluidCompute.IsSupported(clearScalarKernel) &&
                    fluidCompute.IsSupported(injectKernel) &&
                    fluidCompute.IsSupported(fluxKernel) &&
                    fluidCompute.IsSupported(applyKernel);
                if (!gpuReady)
                {
                    AddDiagnostic("One or more surface-fluid compute kernels are unsupported " +
                        "on this graphics device. Runtime bleeding will use the bounded " +
                        "no-rebuild trail fallback.");
                }
            }
            catch (Exception exception)
            {
                gpuReady = false;
                AddDiagnostic("Surface fluid kernels could not be loaded: " + exception.Message);
            }
        }

        private void SubscribeAvatar()
        {
            if (subscribed || avatar == null) return;
            avatar.CharacterBegun ??= new UMADataEvent();
            avatar.CharacterCreated ??= new UMADataEvent();
            avatar.CharacterUpdated ??= new UMADataEvent();
            avatar.CharacterBegun.AddListener(HandleCharacterBegun);
            avatar.CharacterCreated.AddListener(HandleCharacterUpdated);
            avatar.CharacterUpdated.AddListener(HandleCharacterUpdated);
            subscribed = true;
        }

        private void UnsubscribeAvatar()
        {
            if (!subscribed || avatar == null) return;
            avatar.CharacterBegun?.RemoveListener(HandleCharacterBegun);
            avatar.CharacterCreated?.RemoveListener(HandleCharacterUpdated);
            avatar.CharacterUpdated?.RemoveListener(HandleCharacterUpdated);
            subscribed = false;
            SubscribeAtlas(null);
        }

        private void SubscribeAtlas(UMAData data)
        {
            if (umaData != null) umaData.OnAtlasUpdated -= HandleAtlasUpdated;
            umaData = data;
            if (umaData != null) umaData.OnAtlasUpdated += HandleAtlasUpdated;
        }

        private void HandleCharacterBegun(UMAData data)
        {
            SubscribeAtlas(null);
            atlasMetadata.Clear();
            ReleaseContexts();
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                EffectRecord effect = effects[i];
                bool persist = effect.awaitingCleanBase || effect.persistentStamp ||
                    (effect.kind == EffectKind.Fluid && effect.profile != null &&
                    effect.profile.persistAcrossAvatarRebuild &&
                    (effect.stamp != null || effect.profile.detachedRoute !=
                        SurfaceFluidDetachedRoute.IndependentDetachedPiece));
                ReleaseBindings(effect, persist);
                if (!persist) effects.RemoveAt(i);
            }
        }

        private void HandleCharacterUpdated(UMAData data)
        {
            if (data == null) return;
            SubscribeAtlas(data);
            for (int i = 0; i < effects.Count; i++)
            {
                EffectRecord effect = effects[i];
                if (effect.kind == EffectKind.Fluid && effect.fluids.Count == 0)
                {
                    if (gpuReady && effect.stamp != null)
                        BindFluidStampRecord(effect);
                    else if (gpuReady && effect.profile.detachedRoute !=
                             SurfaceFluidDetachedRoute.IndependentDetachedPiece)
                        BindFluidRecord(effect);
                    else
                        StartFallbacksFromStoredSurfaces(effect);
                }
                else if (effect.kind == EffectKind.Stamp && effect.stamps.Count == 0)
                {
                    BindStampRecord(effect);
                    effect.awaitingCleanBase = false;
                }
            }
        }

        private void HandleAtlasUpdated(UMAData data, TextureEventParms parameters)
        {
            if (parameters == null) return;
            string key = (parameters.UMAMaterial != null ? parameters.UMAMaterial.name : "?") +
                "/" + (parameters.slotData?.asset != null
                    ? parameters.slotData.asset.slotGroup : parameters.slotData?.slotName) +
                "/" + parameters.materialPropertyName;
            if (!atlasMetadata.Contains(key)) atlasMetadata.Add(key);
            // Defer all texture work until LateUpdate, after UMA and permanent stamp callbacks
            // have finished with the active render target.
            for (int i = 0; i < contexts.Count; i++)
                if (contexts[i].generated?.umaMaterial == parameters.UMAMaterial)
                    contexts[i].compositeDirty = true;
        }

        private RuntimeDecalHandle CreateHandle()
        {
            nextSequence++;
            return new RuntimeDecalHandle(controllerSession, nextSequence);
        }

        private EffectRecord FindEffect(RuntimeDecalHandle handle)
        {
            if (!handle.IsValid || handle.ControllerSession != controllerSession) return null;
            for (int i = 0; i < effects.Count; i++)
                if (effects[i].handle == handle) return effects[i];
            return null;
        }

        private bool BindFluidRecord(EffectRecord record)
        {
            if (record.sourceSurfaces == null || umaData?.generatedMaterials?.materials == null)
                return false;
            var bindingsByContext = new Dictionary<AtlasContext, FluidBinding>();
            for (int i = 0; i < record.sourceSurfaces.Length; i++)
            {
                DismembermentCutSurface surface = record.sourceSurfaces[i];
                if (!SurfaceMatchesProfile(surface, record.profile)) continue;
                AtlasContext context = ResolveContext(surface, record.profile);
                if (context == null) continue;
                if (!bindingsByContext.TryGetValue(context, out FluidBinding binding))
                {
                    binding = CreateFluidBinding(context, record.profile);
                    if (binding == null) continue;
                    bindingsByContext.Add(context, binding);
                    record.fluids.Add(binding);
                }
                Mesh sourceMesh = BuildSourceRibbon(surface, record.profile,
                    context.simulationWidth, context.simulationHeight);
                if (sourceMesh != null)
                    binding.sources.Add(new InjectionSource { mesh = sourceMesh });
            }
            for (int i = record.fluids.Count - 1; i >= 0; i--)
            {
                if (record.fluids[i].sources.Count > 0) continue;
                ReleaseFluidBinding(record.fluids[i]);
                record.fluids.RemoveAt(i);
            }
            return record.fluids.Count > 0;
        }

        private bool BindIndependentFluidRecord(EffectRecord record, DismembermentResult cut)
        {
            if (cut.root == null || cut.detachedRenderers == null ||
                cut.sourceRenderers == null || record.sourceSurfaces == null) return false;
            DismemberedPieceMaterialOwner owner =
                cut.root.GetComponent<DismemberedPieceMaterialOwner>();
            if (owner == null) owner = cut.root.gameObject.AddComponent<DismemberedPieceMaterialOwner>();
            var bindingsByContext = new Dictionary<AtlasContext, FluidBinding>();
            var detachedSurfaces = new List<DismembermentCutSurface>();
            for (int surfaceIndex = 0; surfaceIndex < record.sourceSurfaces.Length; surfaceIndex++)
            {
                DismembermentCutSurface sourceSurface = record.sourceSurfaces[surfaceIndex];
                if (!SurfaceMatchesProfile(sourceSurface, record.profile)) continue;
                int rendererIndex = FindRendererIndex(cut.sourceRenderers,
                    sourceSurface.sourceRenderer);
                if ((uint)rendererIndex >= (uint)cut.detachedRenderers.Length ||
                    cut.detachedRenderers[rendererIndex] == null) continue;
                DismembermentCutSurface detachedSurface = CloneSurfaceForRenderer(sourceSurface,
                    cut.detachedRenderers[rendererIndex]);
                AtlasContext context = ResolveIndependentContext(sourceSurface, detachedSurface,
                    record.profile, owner);
                if (context == null) continue;
                detachedSurfaces.Add(detachedSurface);
                if (!bindingsByContext.TryGetValue(context, out FluidBinding binding))
                {
                    binding = CreateFluidBinding(context, record.profile);
                    if (binding == null) continue;
                    bindingsByContext.Add(context, binding);
                    record.fluids.Add(binding);
                }
                Mesh sourceMesh = BuildSourceRibbon(detachedSurface, record.profile,
                    context.simulationWidth, context.simulationHeight);
                if (sourceMesh != null)
                    binding.sources.Add(new InjectionSource { mesh = sourceMesh });
            }
            for (int i = record.fluids.Count - 1; i >= 0; i--)
            {
                if (record.fluids[i].sources.Count > 0) continue;
                ReleaseFluidBinding(record.fluids[i]);
                record.fluids.RemoveAt(i);
            }
            if (record.fluids.Count > 0) record.sourceSurfaces = detachedSurfaces.ToArray();
            return record.fluids.Count > 0;
        }

        private AtlasContext ResolveIndependentContext(DismembermentCutSurface sourceSurface,
            DismembermentCutSurface detachedSurface, UMASurfaceFluidProfile profile,
            DismemberedPieceMaterialOwner owner)
        {
            int submesh = detachedSurface.sourceSubmeshIndex;
            for (int i = 0; i < contexts.Count; i++)
            {
                AtlasContext existing = contexts[i];
                if (existing.renderer == detachedSurface.sourceRenderer &&
                    existing.generated.materialIndex == submesh)
                {
                    EnsureFluidOutputs(existing, profile);
                    return existing;
                }
            }
            UMAData.GeneratedMaterial sourceGenerated = FindGeneratedMaterial(sourceSurface);
            if (sourceGenerated == null || sourceGenerated.material == null ||
                sourceGenerated.resultingAtlasList == null) return null;
            Material[] detachedMaterials = detachedSurface.sourceRenderer.sharedMaterials;
            int materialIndex = submesh >= 0 ? submesh : sourceGenerated.materialIndex;
            if ((uint)materialIndex >= (uint)detachedMaterials.Length) return null;
            Material clone = new Material(sourceGenerated.material)
            {
                name = sourceGenerated.material.name + " Independent Detached Fluid",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (sourceGenerated.umaMaterial?.channels != null)
            {
                for (int channel = 0; channel < sourceGenerated.umaMaterial.channels.Length;
                    channel++)
                {
                    if ((uint)channel >= (uint)sourceGenerated.resultingAtlasList.Length) break;
                    string property =
                        sourceGenerated.umaMaterial.channels[channel].materialPropertyName;
                    if (!string.IsNullOrEmpty(property) &&
                        sourceGenerated.resultingAtlasList[channel] != null)
                        clone.SetTexture(property, sourceGenerated.resultingAtlasList[channel]);
                }
            }
            owner.Add(clone);
            detachedMaterials[materialIndex] = clone;
            detachedSurface.sourceRenderer.sharedMaterials = detachedMaterials;
            detachedSurface.sourceMaterial = clone;
            var generated = new UMAData.GeneratedMaterial
            {
                umaMaterial = sourceGenerated.umaMaterial,
                material = clone,
                secondPassMaterial = null,
                resultingAtlasList = sourceGenerated.resultingAtlasList,
                cropResolution = sourceGenerated.cropResolution,
                resolutionScale = sourceGenerated.resolutionScale,
                textureNameList = sourceGenerated.textureNameList,
                rendererAsset = sourceGenerated.rendererAsset,
                skinnedMeshRenderer = detachedSurface.sourceRenderer,
                materialIndex = materialIndex
            };
            AtlasContext context = CreateContext(generated, profile, true);
            if (context == null) return null;
            contexts.Add(context);
            EnsureFluidOutputs(context, profile);
            return context.outputs.Count > 0 ? context : null;
        }

        private UMAData.GeneratedMaterial FindGeneratedMaterial(
            DismembermentCutSurface surface)
        {
            List<UMAData.GeneratedMaterial> generatedMaterials =
                umaData?.generatedMaterials?.materials;
            if (generatedMaterials == null) return null;
            UMAData.GeneratedMaterial resolved = null;
            for (int i = 0; i < generatedMaterials.Count; i++)
            {
                UMAData.GeneratedMaterial candidate = generatedMaterials[i];
                if (candidate == null || candidate.material == null) continue;
                bool rendererMatches = surface.sourceRenderer != null &&
                    candidate.skinnedMeshRenderer == surface.sourceRenderer;
                bool materialMatches = surface.sourceMaterial != null &&
                    candidate.material == surface.sourceMaterial;
                bool identityMatches = !string.IsNullOrEmpty(surface.umaMaterialName) &&
                    candidate.umaMaterial != null &&
                    string.Equals(candidate.umaMaterial.name, surface.umaMaterialName,
                        StringComparison.Ordinal);
                if ((rendererMatches && (surface.sourceSubmeshIndex < 0 ||
                    candidate.materialIndex == surface.sourceSubmeshIndex)) || materialMatches ||
                    identityMatches)
                {
                    resolved = candidate;
                    if (rendererMatches) break;
                }
            }
            return resolved;
        }

        private UMAData.GeneratedMaterial FindGeneratedMaterial(
            SkinnedMeshRenderer renderer, int submesh)
        {
            List<UMAData.GeneratedMaterial> generatedMaterials =
                umaData?.generatedMaterials?.materials;
            if (renderer == null || generatedMaterials == null) return null;
            UMAData.GeneratedMaterial rendererFallback = null;
            for (int i = 0; i < generatedMaterials.Count; i++)
            {
                UMAData.GeneratedMaterial candidate = generatedMaterials[i];
                if (candidate?.skinnedMeshRenderer != renderer) continue;
                rendererFallback ??= candidate;
                if (candidate.materialIndex == submesh) return candidate;
            }
            return rendererFallback;
        }

        private static int FindFirstAlbedoChannel(UMAMaterial material)
        {
            if (material?.channels == null) return -1;
            for (int i = 0; i < material.channels.Length; i++)
            {
                UMAMaterial.ChannelType type = material.channels[i].channelType;
                if (type == UMAMaterial.ChannelType.DiffuseTexture ||
                    type == UMAMaterial.ChannelType.TintedTexture ||
                    type == UMAMaterial.ChannelType.Texture) return i;
            }
            return -1;
        }

        private AtlasContext ResolveContext(DismembermentCutSurface surface,
            UMASurfaceFluidProfile profile)
        {
            UMAData.GeneratedMaterial resolved = FindGeneratedMaterial(surface);
            if (resolved == null) return null;
            AtlasContext context = FindContext(resolved);
            if (context == null)
            {
                context = CreateContext(resolved, profile, true);
                if (context == null) return null;
                contexts.Add(context);
            }
            EnsureFluidOutputs(context, profile);
            return context.outputs.Count > 0 ? context : null;
        }

        private AtlasContext CreateContext(UMAData.GeneratedMaterial generated,
            UMASurfaceFluidProfile profile, bool createSimulationResources)
        {
            Texture reference = FirstUsableAtlas(generated);
            if (reference == null || generated.skinnedMeshRenderer == null) return null;
            int cap = Mathf.Clamp(profile.simulationResolutionCap, 64, 1024);
            float scale = Mathf.Min(1f, cap / (float)Mathf.Max(reference.width, reference.height));
            int width = Mathf.Max(32, Mathf.RoundToInt(reference.width * scale));
            int height = Mathf.Max(32, Mathf.RoundToInt(reference.height * scale));
            var context = new AtlasContext
            {
                generated = generated,
                renderer = generated.skinnedMeshRenderer,
                simulationWidth = width,
                simulationHeight = height,
                surfaceCommand = new CommandBuffer { name = "UMA Surface Fluid Field" }
            };
            if (!createSimulationResources) return context;
            try
            {
                context.surfacePosition = CreateSimulationRT(width, height,
                    RenderTextureFormat.ARGBFloat, false, "UMA Fluid Surface Position");
                context.surfaceFlow = CreateSimulationRT(width, height,
                    RenderTextureFormat.ARGBHalf, false, "UMA Fluid Surface Flow");
                context.surfaceDepth = new RenderTexture(width, height, 24,
                    RenderTextureFormat.Depth) { name = "UMA Fluid Surface Depth" };
                context.surfaceDepth.Create();
                context.injection = CreateSimulationRT(width, height,
                    RenderTextureFormat.ARGBHalf, false, "UMA Fluid Injection");
                context.seamLinks = BuildSeamLinks(context);
                context.fieldTargets = new[]
                {
                    new RenderTargetIdentifier(context.surfacePosition),
                    new RenderTargetIdentifier(context.surfaceFlow)
                };
            }
            catch (Exception exception)
            {
                AddDiagnostic("Could not allocate the GPU surface context: " + exception.Message);
                ReleaseContext(context);
                return null;
            }
            return context;
        }

        private void EnsureFluidOutputs(AtlasContext context, UMASurfaceFluidProfile profile)
        {
            UMAMaterial material = context.generated.umaMaterial;
            if (material?.channels == null) return;
            bool albedoAdded = false;
            for (int i = 0; i < material.channels.Length; i++)
            {
                UMAMaterial.MaterialChannel channel = material.channels[i];
                int mode = ResolveChannelMode(channel, profile, albedoAdded);
                if (mode < 0) continue;
                if (mode == 1 && ResolveFluidTexture(profile,
                    channel.materialPropertyName) == null)
                    continue;
                OutputChannel output = EnsureOutput(context, i, mode);
                if (output != null && mode == 0) albedoAdded = true;
            }
        }

        private static int ResolveChannelMode(UMAMaterial.MaterialChannel channel,
            UMASurfaceFluidProfile profile, bool albedoAlreadyAdded)
        {
            SurfaceFluidChannels requested = profile.channels;
            if ((requested & SurfaceFluidChannels.Normal) != 0 &&
                (channel.channelType == UMAMaterial.ChannelType.NormalMap ||
                 channel.channelType == UMAMaterial.ChannelType.DetailNormalMap)) return 1;
            string property = channel.materialPropertyName ?? string.Empty;
            if ((requested & SurfaceFluidChannels.Wetness) != 0 &&
                !string.IsNullOrEmpty(profile.wetnessMaterialPropertyName) &&
                string.Equals(property, profile.wetnessMaterialPropertyName,
                    StringComparison.Ordinal)) return 2;
            bool colorTexture = channel.channelType == UMAMaterial.ChannelType.DiffuseTexture ||
                channel.channelType == UMAMaterial.ChannelType.TintedTexture ||
                channel.channelType == UMAMaterial.ChannelType.Texture;
            return !albedoAlreadyAdded && colorTexture &&
                (requested & SurfaceFluidChannels.Albedo) != 0 ? 0 : -1;
        }

        private OutputChannel EnsureOutput(AtlasContext context, int channelIndex, int mode)
        {
            for (int i = 0; i < context.outputs.Count; i++)
                if (context.outputs[i].channelIndex == channelIndex) return context.outputs[i];
            UMAData.GeneratedMaterial generated = context.generated;
            if (generated.resultingAtlasList == null ||
                (uint)channelIndex >= (uint)generated.resultingAtlasList.Length ||
                generated.umaMaterial?.channels == null ||
                (uint)channelIndex >= (uint)generated.umaMaterial.channels.Length) return null;
            Texture baseTexture = generated.resultingAtlasList[channelIndex];
            string property = generated.umaMaterial.channels[channelIndex].materialPropertyName;
            if (baseTexture == null || string.IsNullOrEmpty(property)) return null;
            RenderTexture output = CreateOutputRT(baseTexture, property);
            if (output == null) return null;
            Graphics.Blit(baseTexture, output);
            var result = new OutputChannel
            {
                context = context,
                channelIndex = channelIndex,
                channelMode = mode,
                propertyName = property,
                baseTexture = baseTexture,
                output = output
            };
            context.outputs.Add(result);
            BindOutput(result);
            context.compositeDirty = true;
            return result;
        }

        private FluidBinding CreateFluidBinding(AtlasContext context,
            UMASurfaceFluidProfile profile)
        {
            try
            {
                EnsureSimulationResources(context);
                var binding = new FluidBinding { context = context };
                binding.stateA = CreateSimulationRT(context.simulationWidth,
                    context.simulationHeight, RenderTextureFormat.ARGBHalf, true,
                    "UMA Fluid State A");
                binding.stateB = CreateSimulationRT(context.simulationWidth,
                    context.simulationHeight, RenderTextureFormat.ARGBHalf, true,
                    "UMA Fluid State B");
                binding.speedA = CreateSimulationRT(context.simulationWidth,
                    context.simulationHeight, RenderTextureFormat.RFloat, true,
                    "UMA Fluid Speed A");
                binding.speedB = CreateSimulationRT(context.simulationWidth,
                    context.simulationHeight, RenderTextureFormat.RFloat, true,
                    "UMA Fluid Speed B");
                binding.flux = CreateSimulationRT(context.simulationWidth,
                    context.simulationHeight, RenderTextureFormat.ARGBHalf, true,
                    "UMA Fluid Flux");
                ClearState(binding.stateA, context);
                ClearState(binding.stateB, context);
                ClearScalar(binding.speedA, context);
                ClearScalar(binding.speedB, context);
                return binding;
            }
            catch (Exception exception)
            {
                AddDiagnostic("Could not allocate a fluid state layer: " + exception.Message);
                return null;
            }
        }

        private void EnsureSimulationResources(AtlasContext context)
        {
            if (context.surfaceFlow != null && context.injection != null) return;
            int width = context.simulationWidth;
            int height = context.simulationHeight;
            context.surfacePosition = CreateSimulationRT(width, height,
                RenderTextureFormat.ARGBFloat, false, "UMA Fluid Surface Position");
            context.surfaceFlow = CreateSimulationRT(width, height,
                RenderTextureFormat.ARGBHalf, false, "UMA Fluid Surface Flow");
            context.surfaceDepth = new RenderTexture(width, height, 24,
                RenderTextureFormat.Depth) { name = "UMA Fluid Surface Depth" };
            if (!context.surfaceDepth.Create())
                throw new InvalidOperationException("The surface depth texture could not be created.");
            context.injection = CreateSimulationRT(width, height,
                RenderTextureFormat.ARGBHalf, false, "UMA Fluid Injection");
            context.seamLinks = BuildSeamLinks(context);
            context.fieldTargets = new[]
            {
                new RenderTargetIdentifier(context.surfacePosition),
                new RenderTargetIdentifier(context.surfaceFlow)
            };
        }

        private void BindStampRecord(EffectRecord record)
        {
            OverlayDataAsset sourceOverlay = ResolveStampSource(record.stamp);
            if (sourceOverlay == null || sourceOverlay.material == null ||
                sourceOverlay.textureList == null) return;
            List<UMAData.GeneratedMaterial> materials = umaData.generatedMaterials.materials;
            for (int materialIndex = 0; materialIndex < materials.Count; materialIndex++)
            {
                UMAData.GeneratedMaterial generated = materials[materialIndex];
                if (generated?.umaMaterial?.channels == null) continue;
                AtlasContext context = FindContext(generated);
                Mesh mesh = BuildStampMesh(record.stamp, generated, null,
                    !record.allowSlotIdentityWithoutOverlayGroup);
                if (mesh == null) continue;
                bool usedMesh = false;
                for (int channelIndex = 0; channelIndex < generated.umaMaterial.channels.Length;
                    channelIndex++)
                {
                    string property = generated.umaMaterial.channels[channelIndex].materialPropertyName;
                    int sourceChannel = sourceOverlay.material.GetChannelIndex(property);
                    if ((uint)sourceChannel >= (uint)sourceOverlay.textureList.Length ||
                        sourceOverlay.textureList[sourceChannel] == null) continue;
                    if (context == null)
                    {
                        UMASurfaceFluidProfile budget = CreateBudgetProfile();
                        context = CreateContext(generated, budget, false);
                        DestroyOwned(budget);
                        if (context == null) break;
                        contexts.Add(context);
                    }
                    OutputChannel output = EnsureOutput(context, channelIndex, 0);
                    if (output == null) continue;
                    Texture channelTexture = sourceOverlay.textureList[sourceChannel];
                    Texture sharedMask = sourceOverlay.alphaMask;
                    if (sharedMask == null && sourceOverlay.textureList.Length > 0 &&
                        sourceOverlay.textureList[0] != channelTexture)
                        sharedMask = sourceOverlay.textureList[0];
                    record.stamps.Add(new StampBinding
                    {
                        output = output,
                        mesh = usedMesh ? InstantiateMesh(mesh) : mesh,
                        texture = channelTexture,
                        mask = sharedMask
                    });
                    usedMesh = true;
                }
                if (!usedMesh) DestroyOwned(mesh);
            }
        }

        private bool BindFluidStampRecord(EffectRecord record)
        {
            OverlayDataAsset sourceOverlay = ResolveStampSource(record.stamp);
            if (sourceOverlay == null || record.profile == null ||
                umaData?.generatedMaterials?.materials == null) return false;

            List<UMAData.GeneratedMaterial> materials = umaData.generatedMaterials.materials;
            for (int materialIndex = 0; materialIndex < materials.Count; materialIndex++)
            {
                UMAData.GeneratedMaterial generated = materials[materialIndex];
                if (generated?.umaMaterial?.channels == null) continue;
                Mesh mesh = BuildStampMesh(record.stamp, generated, record.profile, false);
                if (mesh == null) continue;

                AtlasContext context = FindContext(generated);
                if (context == null)
                {
                    context = CreateContext(generated, record.profile, true);
                    if (context != null) contexts.Add(context);
                }
                if (context == null)
                {
                    DestroyOwned(mesh);
                    continue;
                }

                EnsureFluidOutputs(context, record.profile);
                if (context.outputs.Count == 0)
                {
                    DestroyOwned(mesh);
                    continue;
                }

                FluidBinding binding = CreateFluidBinding(context, record.profile);
                if (binding == null)
                {
                    DestroyOwned(mesh);
                    continue;
                }
                binding.sources.Add(new InjectionSource
                {
                    mesh = mesh,
                    // The visible bullet texture commonly has a transparent center. A puncture
                    // emitter is already bounded by a metric radial mask, so multiplying by the
                    // decal alpha can incorrectly suppress all fluid at the hit point.
                    mask = null,
                    useRadialLimit = true,
                    radialRadius = ResolveStampSourceRadiusUv(record.stamp, record.profile,
                        context, mesh),
                    radialFeather = ResolveStampSourceFeatherUv(record.stamp, record.profile,
                        context, mesh)
                });
                record.fluids.Add(binding);
            }
            return record.fluids.Count > 0;
        }

        private static float ResolveStampSourceRadiusUv(DecalRTStampAsset stamp,
            UMASurfaceFluidProfile profile, AtlasContext context, Mesh sourceMesh)
        {
            float radius = stamp != null && stamp.projectionRadiusMeters > 0.0001f
                ? profile.emissionRadiusMeters / stamp.projectionRadiusMeters * 0.5f
                : 0.04f;
            float texelMinimum = ResolveStampOverlayRadiusForSimulationTexels(sourceMesh,
                context.simulationWidth, context.simulationHeight, 1.25f);
            return Mathf.Clamp(Mathf.Max(radius, texelMinimum), 0.01f, 0.35f);
        }

        private static float ResolveStampSourceFeatherUv(DecalRTStampAsset stamp,
            UMASurfaceFluidProfile profile, AtlasContext context, Mesh sourceMesh)
        {
            float radius = ResolveStampSourceRadiusUv(stamp, profile, context, sourceMesh);
            return Mathf.Max(0.005f, radius * 0.45f);
        }

        private static float ResolveStampOverlayRadiusForSimulationTexels(Mesh mesh, int width,
            int height, float texelRadius)
        {
            if (mesh == null) return 0.08f;
            Vector2[] atlasUv = mesh.uv;
            Vector2[] overlayUv = mesh.uv2;
            int[] indices = mesh.triangles;
            if (atlasUv == null || overlayUv == null || indices == null ||
                atlasUv.Length != overlayUv.Length) return 0.08f;

            Vector2 center = new Vector2(0.5f, 0.5f);
            float overlayPerPixel = 0f;
            bool foundCenterTriangle = false;
            for (int triangle = 0; triangle + 2 < indices.Length; triangle += 3)
            {
                int ia = indices[triangle];
                int ib = indices[triangle + 1];
                int ic = indices[triangle + 2];
                if ((uint)ia >= (uint)overlayUv.Length ||
                    (uint)ib >= (uint)overlayUv.Length ||
                    (uint)ic >= (uint)overlayUv.Length) continue;
                if (!PointInTriangle(center, overlayUv[ia], overlayUv[ib], overlayUv[ic]))
                    continue;
                foundCenterTriangle = true;
                overlayPerPixel = Mathf.Max(overlayPerPixel,
                    OverlayUvPerSimulationPixel(atlasUv[ia], atlasUv[ib], overlayUv[ia],
                        overlayUv[ib], width, height));
                overlayPerPixel = Mathf.Max(overlayPerPixel,
                    OverlayUvPerSimulationPixel(atlasUv[ib], atlasUv[ic], overlayUv[ib],
                        overlayUv[ic], width, height));
                overlayPerPixel = Mathf.Max(overlayPerPixel,
                    OverlayUvPerSimulationPixel(atlasUv[ic], atlasUv[ia], overlayUv[ic],
                        overlayUv[ia], width, height));
            }
            return foundCenterTriangle && overlayPerPixel > 0f
                ? overlayPerPixel * Mathf.Max(0.5f, texelRadius) : 0.08f;
        }

        private static float OverlayUvPerSimulationPixel(Vector2 atlasA, Vector2 atlasB,
            Vector2 overlayA, Vector2 overlayB, int width, int height)
        {
            Vector2 atlasPixels = new Vector2((atlasB.x - atlasA.x) * Mathf.Max(1, width),
                (atlasB.y - atlasA.y) * Mathf.Max(1, height));
            float pixelDistance = atlasPixels.magnitude;
            return pixelDistance > 0.0001f
                ? Vector2.Distance(overlayA, overlayB) / pixelDistance : 0f;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross2D(point - b, a - b);
            float d2 = Cross2D(point - c, b - c);
            float d3 = Cross2D(point - a, c - a);
            bool negative = d1 < -0.00001f || d2 < -0.00001f || d3 < -0.00001f;
            bool positive = d1 > 0.00001f || d2 > 0.00001f || d3 > 0.00001f;
            return !(negative && positive);
        }

        private static float Cross2D(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private void UpdateContext(AtlasContext context, float delta)
        {
            bool visible = context.renderer != null && context.renderer.isVisible;
            float rateMultiplier = visible ? 1f : 0.25f;
            bool hasFluid = false;
            float highestSurfaceRate = 1f;
            float highestCompositeRate = 1f;
            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                EffectRecord effect = effects[effectIndex];
                for (int bindingIndex = 0; bindingIndex < effect.fluids.Count; bindingIndex++)
                {
                    FluidBinding binding = effect.fluids[bindingIndex];
                    if (binding.context != context) continue;
                    hasFluid = true;
                    highestSurfaceRate = Mathf.Max(highestSurfaceRate,
                        effect.profile.surfaceFieldRate);
                    highestCompositeRate = Mathf.Max(highestCompositeRate,
                        effect.profile.compositeRate);
                    SimulateBinding(effect, binding, delta, visible ? 1f :
                        effect.profile.reduceRateWhenOffscreen ? 0.25f : 1f);
                }
                for (int bindingIndex = 0; bindingIndex < effect.stamps.Count; bindingIndex++)
                    if (effect.stamps[bindingIndex].output.context == context)
                        highestCompositeRate = Mathf.Max(highestCompositeRate, 15f);
            }

            if (hasFluid)
            {
                context.surfaceAccumulator += delta * rateMultiplier;
                float interval = 1f / Mathf.Max(1f, highestSurfaceRate);
                if (!context.surfaceValid || context.surfaceAccumulator >= interval)
                {
                    context.surfaceAccumulator = 0f;
                    RenderSurfaceField(context);
                }
            }

            context.compositeAccumulator += delta * rateMultiplier;
            float compositeInterval = 1f / Mathf.Max(1f, highestCompositeRate);
            if (context.compositeDirty && context.compositeAccumulator >= compositeInterval)
            {
                context.compositeAccumulator = 0f;
                ComposeContext(context);
            }
        }

        private void SimulateBinding(EffectRecord effect, FluidBinding binding, float delta,
            float rateMultiplier)
        {
            if (effect.state == RuntimeDecalState.Holding ||
                effect.state == RuntimeDecalState.Fading ||
                effect.state == RuntimeDecalState.Complete) return;
            UMASurfaceFluidProfile profile = effect.profile;
            float fixedStep = 1f / Mathf.Max(10f, profile.simulationRate);
            binding.simulationAccumulator += delta * rateMultiplier;
            int steps = 0;
            while (binding.simulationAccumulator >= fixedStep &&
                steps < profile.maximumSubsteps)
            {
                if (!binding.context.surfaceValid) RenderSurfaceField(binding.context);
                if (effect.state == RuntimeDecalState.Emitting && !effect.stopRequested)
                {
                    RenderInjection(binding, profile);
                    DispatchInjection(binding, profile, fixedStep);
                }
                DispatchFlow(binding, profile, fixedStep);
                binding.simulationAccumulator -= fixedStep;
                steps++;
                binding.context.compositeDirty = true;
            }
            if (steps == profile.maximumSubsteps)
                binding.simulationAccumulator = Mathf.Min(binding.simulationAccumulator, fixedStep);
        }

        private void RenderSurfaceField(AtlasContext context)
        {
            if (context.renderer == null || context.renderer.sharedMesh == null) return;
            CommandBuffer command = context.surfaceCommand;
            command.Clear();
            command.SetRenderTarget(context.fieldTargets,
                new RenderTargetIdentifier(context.surfaceDepth));
            command.ClearRenderTarget(true, true, Color.clear);
            Vector3 gravity = Physics.gravity.sqrMagnitude > 0.000001f
                ? Physics.gravity.normalized : Vector3.down;
            surfaceFieldMaterial.SetVector("_SurfaceGravity", gravity);
            int submesh = context.generated.materialIndex;
            if ((uint)submesh < (uint)context.renderer.sharedMesh.subMeshCount)
                command.DrawRenderer(context.renderer, surfaceFieldMaterial, submesh, 0);
            else
                for (int i = 0; i < context.renderer.sharedMesh.subMeshCount; i++)
                    command.DrawRenderer(context.renderer, surfaceFieldMaterial, i, 0);
            Graphics.ExecuteCommandBuffer(command);
            context.surfaceValid = true;
        }

        private void RenderInjection(FluidBinding binding, UMASurfaceFluidProfile profile)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = binding.context.injection;
            GL.Viewport(new Rect(0f, 0f, binding.context.injection.width,
                binding.context.injection.height));
            GL.Clear(false, true, Color.clear);
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                for (int i = 0; i < binding.sources.Count; i++)
                {
                    InjectionSource source = binding.sources[i];
                    if (source?.mesh == null) continue;
                    sourceMaskMaterial.SetFloat("_Intensity", source.intensity);
                    sourceMaskMaterial.SetFloat("_SpeedMultiplier",
                        Mathf.Max(0.05f, source.speedMultiplier));
                    sourceMaskMaterial.SetFloat("_UseMask", source.mask != null ? 1f : 0f);
                    sourceMaskMaterial.SetTexture("_MaskTex", source.mask);
                    sourceMaskMaterial.SetFloat("_UseRadialLimit",
                        source.useRadialLimit ? 1f : 0f);
                    sourceMaskMaterial.SetVector("_RadialCenter", new Vector4(
                        source.radialCenter.x, source.radialCenter.y, 0f, 0f));
                    sourceMaskMaterial.SetFloat("_RadialRadius", source.radialRadius);
                    sourceMaskMaterial.SetFloat("_RadialFeather", source.radialFeather);
                    sourceMaskMaterial.SetPass(0);
                    Graphics.DrawMeshNow(source.mesh, Matrix4x4.identity);
                }
            }
            finally
            {
                GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private void DispatchInjection(FluidBinding binding, UMASurfaceFluidProfile profile,
            float delta)
        {
            SetCommonCompute(binding.context, profile, delta);
            fluidCompute.SetFloat("_EmissionAmount", profile.emissionRate * delta);
            fluidCompute.SetTexture(injectKernel, "_StateRead", binding.Current);
            fluidCompute.SetTexture(injectKernel, "_StateWrite", binding.Next);
            fluidCompute.SetTexture(injectKernel, "_SpeedRead", binding.CurrentSpeed);
            fluidCompute.SetTexture(injectKernel, "_SpeedWrite", binding.NextSpeed);
            fluidCompute.SetTexture(injectKernel, "_SurfaceFlow", binding.context.surfaceFlow);
            fluidCompute.SetTexture(injectKernel, "_Injection", binding.context.injection);
            Dispatch(injectKernel, binding.context);
            binding.currentIsA = !binding.currentIsA;
        }

        private void DispatchFlow(FluidBinding binding, UMASurfaceFluidProfile profile,
            float delta)
        {
            AtlasContext context = binding.context;
            SetCommonCompute(context, profile, delta);
            fluidCompute.SetTexture(fluxKernel, "_StateRead", binding.Current);
            fluidCompute.SetTexture(fluxKernel, "_SpeedRead", binding.CurrentSpeed);
            fluidCompute.SetTexture(fluxKernel, "_Flux", binding.flux);
            fluidCompute.SetTexture(fluxKernel, "_SurfaceFlow", context.surfaceFlow);
            fluidCompute.SetTexture(fluxKernel, "_SeamLinks", context.seamLinks);
            Dispatch(fluxKernel, context);

            fluidCompute.SetTexture(applyKernel, "_StateRead", binding.Current);
            fluidCompute.SetTexture(applyKernel, "_StateWrite", binding.Next);
            fluidCompute.SetTexture(applyKernel, "_SpeedRead", binding.CurrentSpeed);
            fluidCompute.SetTexture(applyKernel, "_SpeedWrite", binding.NextSpeed);
            fluidCompute.SetTexture(applyKernel, "_Flux", binding.flux);
            fluidCompute.SetTexture(applyKernel, "_SurfaceFlow", context.surfaceFlow);
            fluidCompute.SetTexture(applyKernel, "_SeamLinks", context.seamLinks);
            Dispatch(applyKernel, context);
            binding.currentIsA = !binding.currentIsA;
        }

        private void SetCommonCompute(AtlasContext context, UMASurfaceFluidProfile profile,
            float delta)
        {
            fluidCompute.SetInts("_Dimensions", context.simulationWidth,
                context.simulationHeight);
            fluidCompute.SetFloat("_DeltaTime", delta);
            fluidCompute.SetFloat("_FallSpeed", profile.fallSpeedMetersPerSecond);
            fluidCompute.SetFloat("_Viscosity", profile.viscosity);
            fluidCompute.SetFloat("_Adhesion", profile.adhesion);
            fluidCompute.SetFloat("_Spread", profile.lateralSpread);
            fluidCompute.SetFloat("_Pooling", profile.pooling);
            fluidCompute.SetFloat("_TrailDepositionPerMeter",
                profile.trailDepositionPerMeter);
            fluidCompute.SetFloat("_Evaporation", profile.evaporation);
            fluidCompute.SetFloat("_MinimumThickness", profile.minimumVisibleThickness);
            fluidCompute.SetFloat("_MobileLifetime", profile.mobileLifetime);
            fluidCompute.SetFloat("_MaximumTravel", profile.maximumTravelMeters);
            fluidCompute.SetFloat("_BreakupStrength", profile.breakupStrength);
            fluidCompute.SetFloat("_BreakupScalePixels", Mathf.Max(1f,
                profile.breakupScaleMeters * Mathf.Max(context.simulationWidth,
                    context.simulationHeight) * 0.5f));
            fluidCompute.SetInt("_BreakupOctaves", profile.breakupOctaves);
            fluidCompute.SetInt("_BreakupSeed", profile.breakupSeed);
        }

        private void Dispatch(int kernel, AtlasContext context)
        {
            fluidCompute.Dispatch(kernel, Mathf.CeilToInt(context.simulationWidth / 8f),
                Mathf.CeilToInt(context.simulationHeight / 8f), 1);
        }

        private void ComposeContext(AtlasContext context)
        {
            for (int outputIndex = 0; outputIndex < context.outputs.Count; outputIndex++)
            {
                OutputChannel output = context.outputs[outputIndex];
                if (output.baseTexture == null || output.output == null) continue;
                Graphics.Blit(output.baseTexture, output.output);
                DrawSurfaceCuts(output);
                DrawFluids(output);
                DrawStamps(output);
                if (output.output.useMipMap) output.output.GenerateMips();
                BindOutput(output);
            }
            context.compositeDirty = false;
        }

        private void DrawSurfaceCuts(OutputChannel output)
        {
            if (surfaceCutMaterial == null) return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = output.output;
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                {
                    EffectRecord effect = effects[effectIndex];
                    if (effect.kind != EffectKind.SurfaceCut) continue;
                    for (int bindingIndex = 0; bindingIndex < effect.surfaceCuts.Count;
                        bindingIndex++)
                    {
                        SurfaceCutBinding binding = effect.surfaceCuts[bindingIndex];
                        if (binding.output != output || binding.mesh == null) continue;
                        surfaceCutMaterial.SetColor("_CenterColor", binding.centerColor);
                        surfaceCutMaterial.SetColor("_EdgeColor", binding.edgeColor);
                        surfaceCutMaterial.SetFloat("_HalfWidthMeters",
                            binding.halfWidthMeters);
                        surfaceCutMaterial.SetFloat("_LengthMeters", binding.lengthMeters);
                        surfaceCutMaterial.SetFloat("_CenterFraction", binding.centerFraction);
                        surfaceCutMaterial.SetFloat("_EdgeSoftness", binding.edgeSoftness);
                        surfaceCutMaterial.SetFloat("_EndTaperFraction",
                            binding.endTaperFraction);
                        surfaceCutMaterial.SetFloat("_FlipY",
                            SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
                        surfaceCutMaterial.SetPass(0);
                        Graphics.DrawMeshNow(binding.mesh, Matrix4x4.identity);
                    }
                }
            }
            finally
            {
                GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private void DrawStamps(OutputChannel output)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = output.output;
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                {
                    EffectRecord effect = effects[effectIndex];
                    if (effect.kind != EffectKind.Stamp) continue;
                    float opacity = EffectOpacity(effect);
                    if (opacity <= 0f) continue;
                    for (int bindingIndex = 0; bindingIndex < effect.stamps.Count; bindingIndex++)
                    {
                        StampBinding binding = effect.stamps[bindingIndex];
                        if (binding.output != output || binding.mesh == null ||
                            binding.texture == null) continue;
                        compositeMaterial.SetTexture("_OverlayTex", binding.texture);
                        compositeMaterial.SetTexture("_MaskTex", binding.mask);
                        compositeMaterial.SetFloat("_UseMask", binding.mask != null ? 1f : 0f);
                        compositeMaterial.SetFloat("_Opacity", opacity);
                        compositeMaterial.SetFloat("_FlipY",
                            SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
                        compositeMaterial.SetPass(1);
                        Graphics.DrawMeshNow(binding.mesh, Matrix4x4.identity);
                    }
                }
            }
            finally
            {
                GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private void DrawFluids(OutputChannel output)
        {
            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                EffectRecord effect = effects[effectIndex];
                if (effect.kind != EffectKind.Fluid) continue;
                float opacity = EffectOpacity(effect);
                if (opacity <= 0f) continue;
                for (int bindingIndex = 0; bindingIndex < effect.fluids.Count; bindingIndex++)
                {
                    FluidBinding binding = effect.fluids[bindingIndex];
                    if (binding.context != output.context || binding.Current == null) continue;
                    Texture appearance = ResolveFluidTexture(effect.profile, output.propertyName);
                    compositeMaterial.SetColor("_FluidColor",
                        ResolveFluidChannelColor(effect.profile, output.channelMode,
                            appearance != null));
                    Texture appearanceMask = effect.profile.sourceOverlay != null
                        ? effect.profile.sourceOverlay.alphaMask : null;
                    compositeMaterial.SetTexture("_FluidTexture", appearance);
                    compositeMaterial.SetTexture("_FluidMask", appearanceMask);
                    compositeMaterial.SetFloat("_UseFluidTexture", appearance != null ? 1f : 0f);
                    compositeMaterial.SetFloat("_UseFluidMask", appearanceMask != null ? 1f : 0f);
                    compositeMaterial.SetFloat("_FluidTextureScale", 4f);
                    compositeMaterial.SetFloat("_Opacity", opacity);
                    compositeMaterial.SetFloat("_ThicknessScale",
                        effect.profile.appearanceThicknessScale);
                    compositeMaterial.SetFloat("_ThicknessThreshold",
                        effect.profile.appearanceThicknessThreshold);
                    compositeMaterial.SetFloat("_DepositedTrailOpacityBoost",
                        effect.profile.depositedTrailOpacityBoost);
                    compositeMaterial.SetFloat("_DepositedTrailAlpha",
                        effect.profile.depositedTrailAlpha);
                    Graphics.Blit(binding.Current, output.output, compositeMaterial, 0);
                }
            }
        }

        private static Color ResolveFluidChannelColor(UMASurfaceFluidProfile profile, int mode,
            bool hasAppearanceTexture)
        {
            if (mode == 1 && hasAppearanceTexture)
                return new Color(1f, 1f, 1f, profile.color.a * 0.15f);
            if (mode == 1) return new Color(0.5f, 0.5f, 1f, profile.color.a * 0.15f);
            if (mode == 2) return new Color(1f, 1f, 1f, profile.color.a * 0.65f);
            return profile.color;
        }

        private static Texture ResolveFluidTexture(UMASurfaceFluidProfile profile,
            string propertyName)
        {
            OverlayDataAsset overlay = profile.sourceOverlay;
            if (overlay == null) return null;
            overlay.EnsureMaterial();
            if (overlay.material == null || overlay.textureList == null) return null;
            int channel = overlay.material.GetChannelIndex(propertyName);
            return (uint)channel < (uint)overlay.textureList.Length
                ? overlay.textureList[channel] : null;
        }

        private void AdvanceEffect(EffectRecord effect, float delta)
        {
            RuntimeDecalState previousState = effect.state;
            effect.elapsed += delta;
            effect.stateElapsed += delta;
            if (effect.fadeRequested && effect.state != RuntimeDecalState.Fading)
                SetState(effect, RuntimeDecalState.Fading);

            if (effect.kind == EffectKind.Stamp)
            {
                if (effect.persistentStamp) return;
                if (effect.state == RuntimeDecalState.Holding &&
                    effect.stateElapsed >= effect.fade.holdSeconds)
                    SetState(effect, RuntimeDecalState.Fading);
                else if (effect.state == RuntimeDecalState.Fading &&
                    effect.stateElapsed >= effect.fade.fadeSeconds)
                    SetState(effect, RuntimeDecalState.Complete);
                if (effect.state == RuntimeDecalState.Fading || effect.state != previousState)
                    MarkContextsDirty(effect);
                return;
            }
            if (effect.kind == EffectKind.SurfaceCut) return;

            UMASurfaceFluidProfile profile = effect.profile;
            switch (effect.state)
            {
                case RuntimeDecalState.Emitting:
                    if (effect.stopRequested || effect.stateElapsed >= profile.emissionDuration)
                        SetState(effect, RuntimeDecalState.Flowing);
                    break;
                case RuntimeDecalState.Flowing:
                    if (effect.stateElapsed >= profile.mobileLifetime)
                        SetState(effect, RuntimeDecalState.Settling);
                    break;
                case RuntimeDecalState.Settling:
                    if (effect.stateElapsed >= 1f) SetState(effect, RuntimeDecalState.Holding);
                    break;
                case RuntimeDecalState.Holding:
                    if (effect.stateElapsed >= profile.holdingDuration)
                        SetState(effect, RuntimeDecalState.Fading);
                    break;
                case RuntimeDecalState.Fading:
                    if (effect.stateElapsed >= profile.fadeDuration)
                        SetState(effect, RuntimeDecalState.Complete);
                    break;
            }
            if (effect.state == RuntimeDecalState.Fading || effect.state != previousState)
                MarkContextsDirty(effect);
        }

        private static void SetState(EffectRecord effect, RuntimeDecalState state)
        {
            effect.state = state;
            effect.stateElapsed = 0f;
        }

        private static float EffectOpacity(EffectRecord effect)
        {
            if (effect.state == RuntimeDecalState.Complete) return 0f;
            if (effect.state != RuntimeDecalState.Fading) return 1f;
            if (effect.kind == EffectKind.Stamp)
                return effect.fade.Evaluate(effect.stateElapsed /
                    Mathf.Max(0.01f, effect.fade.fadeSeconds));
            return 1f - Mathf.Clamp01(effect.stateElapsed /
                Mathf.Max(0.01f, effect.profile.fadeDuration));
        }

        private void MarkContextsDirty(EffectRecord effect)
        {
            for (int i = 0; i < effect.fluids.Count; i++)
                effect.fluids[i].context.compositeDirty = true;
            for (int i = 0; i < effect.stamps.Count; i++)
                effect.stamps[i].output.context.compositeDirty = true;
            for (int i = 0; i < effect.surfaceCuts.Count; i++)
                effect.surfaceCuts[i].output.context.compositeDirty = true;
        }

        private Mesh BuildSourceRibbon(DismembermentCutSurface surface,
            UMASurfaceFluidProfile profile, int width, int height)
        {
            Vector2[] uv = surface.boundaryUV;
            Vector3[] positions = surface.boundaryLocalPositions;
            if (uv == null || positions == null || uv.Length < 3 || positions.Length != uv.Length)
                return null;
            float meters = 0f, uvLength = 0f;
            for (int i = 0; i < uv.Length; i++)
            {
                int next = (i + 1) % uv.Length;
                meters += Vector3.Distance(positions[i], positions[next]);
                uvLength += Vector2.Distance(uv[i], uv[next]);
            }
            float metersPerUV = meters > 0.000001f && uvLength > 0.000001f
                ? meters / uvLength : 1f;
            float radiusUV = Mathf.Clamp(profile.emissionRadiusMeters / metersPerUV,
                1f / Mathf.Max(width, height), 0.05f);
            int segmentCount = uv.Length;
            var vertices = new Vector3[segmentCount * 4];
            var triangles = new int[segmentCount * 6];
            for (int segment = 0; segment < segmentCount; segment++)
            {
                Vector2 a = uv[segment];
                Vector2 b = uv[(segment + 1) % segmentCount];
                Vector2 direction = (b - a).normalized;
                if (direction.sqrMagnitude <= 0.000001f) direction = Vector2.right;
                Vector2 offset = new Vector2(-direction.y, direction.x) * radiusUV;
                int vertex = segment * 4;
                vertices[vertex] = UVToClip(a - offset);
                vertices[vertex + 1] = UVToClip(a + offset);
                vertices[vertex + 2] = UVToClip(b + offset);
                vertices[vertex + 3] = UVToClip(b - offset);
                int triangle = segment * 6;
                triangles[triangle] = vertex; triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2; triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 2; triangles[triangle + 5] = vertex + 3;
            }
            var mesh = new Mesh { name = "UMA Fluid Cut Source" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.UploadMeshData(true);
            return mesh;
        }

        private Mesh BuildStampMesh(DecalRTStampAsset stamp,
            UMAData.GeneratedMaterial generated, UMASurfaceFluidProfile targetProfile = null,
            bool requireOverlayGroup = true)
        {
            if (stamp?.slots == null || umaData?.umaRecipe?.slotDataList == null) return null;
            var vertices = new List<Vector3>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var triangles = new List<int>();
            for (int stampIndex = 0; stampIndex < stamp.slots.Count; stampIndex++)
            {
                DecalRTStampAsset.SlotStamp slotStamp = stamp.slots[stampIndex];
                if (slotStamp == null || slotStamp.debugDontUse || slotStamp.normBaseUV == null ||
                    slotStamp.overlayUV == null || slotStamp.triangles == null) continue;
                SlotData slot = FindRuntimeSlot(slotStamp, generated.umaMaterial,
                    stamp.overlayGroup, requireOverlayGroup);
                if (slot == null) continue;
                if (targetProfile != null && !SlotMatchesProfile(slot, targetProfile)) continue;
                int baseVertex = vertices.Count;
                for (int vertex = 0; vertex < slotStamp.normBaseUV.Length; vertex++)
                {
                    Vector2 normalized = slotStamp.normBaseUV[vertex];
                    Vector2 atlasUV = new Vector2(slot.UVArea.x + slot.UVArea.width * normalized.x,
                        slot.UVArea.y + slot.UVArea.height * normalized.y);
                    vertices.Add(UVToClip(atlasUV));
                    uv0.Add(atlasUV);
                    Vector2 overlayUV = vertex < slotStamp.overlayUV.Length
                        ? slotStamp.overlayUV[vertex] : Vector2.zero;
                    if (stamp.invertY) overlayUV.y = 1f - overlayUV.y;
                    uv1.Add(overlayUV);
                }
                for (int triangle = 0; triangle < slotStamp.triangles.Length; triangle++)
                {
                    int local = slotStamp.triangles[triangle];
                    if ((uint)local < (uint)slotStamp.normBaseUV.Length)
                        triangles.Add(baseVertex + local);
                }
            }
            if (vertices.Count == 0 || triangles.Count == 0) return null;
            var mesh = new Mesh { name = "UMA Dynamic RT Stamp" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(triangles, 0, false);
            mesh.UploadMeshData(false);
            return mesh;
        }

        private SlotData FindRuntimeSlot(DecalRTStampAsset.SlotStamp stamp,
            UMAMaterial material, string overlayGroup, bool requireOverlayGroup)
        {
            SlotData[] slots = umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot?.asset == null || slot.material != material) continue;
                bool identity = !string.IsNullOrEmpty(stamp.slotGroup)
                    ? string.Equals(stamp.slotGroup, slot.asset.slotGroup, StringComparison.Ordinal)
                    : string.Equals(stamp.slotName, slot.slotName, StringComparison.Ordinal);
                if (!identity || (requireOverlayGroup &&
                    !SlotContainsOverlayGroup(slot, overlayGroup))) continue;
                return slot;
            }
            return null;
        }

        private static bool SlotContainsOverlayGroup(SlotData slot, string group)
        {
            if (string.IsNullOrEmpty(group)) return false;
            List<OverlayData> overlays = slot.GetOverlayList();
            if (overlays == null) return false;
            for (int i = 0; i < overlays.Count; i++)
                if (overlays[i]?.asset != null && string.Equals(overlays[i].asset.overlayGroup,
                    group, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool SlotMatchesProfile(SlotData slot, UMASurfaceFluidProfile profile)
        {
            if (slot?.asset == null || profile == null) return false;
            if (profile.targetSlotGroups != null && profile.targetSlotGroups.Length > 0 &&
                !Contains(profile.targetSlotGroups, slot.asset.slotGroup)) return false;
            if (profile.targetOverlayGroups == null || profile.targetOverlayGroups.Length == 0)
                return true;
            for (int i = 0; i < profile.targetOverlayGroups.Length; i++)
                if (SlotContainsOverlayGroup(slot, profile.targetOverlayGroups[i])) return true;
            return false;
        }

        private Texture2D BuildSeamLinks(AtlasContext context)
        {
            int width = context.simulationWidth, height = context.simulationHeight;
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(-1f, -1f, 0f, 0f);
            Mesh mesh = context.renderer.sharedMesh;
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            int[] weightHashes = BuildWeightHashes(mesh);
            int submesh = context.generated.materialIndex;
            if (uv != null && uv.Length == positions.Length && normals != null &&
                normals.Length == positions.Length && weightHashes.Length == positions.Length &&
                (uint)submesh < (uint)mesh.subMeshCount)
            {
                var used = new bool[positions.Length];
                var slotKeys = new int[positions.Length];
                var slotLookup = new Dictionary<SlotData, int>();
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i < indices.Length; i++)
                    if ((uint)indices[i] < (uint)used.Length) used[indices[i]] = true;
                var groups = new Dictionary<VertexSignature, List<int>>();
                for (int vertex = 0; vertex < positions.Length; vertex++)
                {
                    if (!used[vertex]) continue;
                    SlotData slot = FindSlotForVertexSafe(
                        umaData?.umaRecipe?.slotDataList, vertex);
                    // Unknown ownership is deliberately unique: stopping at an unresolved seam
                    // is safer than bridging layered clothing by position alone.
                    int slotKey = vertex + 1;
                    if (slot != null && !slotLookup.TryGetValue(slot, out slotKey))
                    {
                        slotKey = slotLookup.Count + 1;
                        slotLookup.Add(slot, slotKey);
                    }
                    slotKeys[vertex] = slotKey;
                    var signature = new VertexSignature(positions[vertex], normals[vertex],
                        weightHashes[vertex], slotKeys[vertex],
                        DismembermentMeshBuildOptions.DefaultSeamWeldTolerance);
                    if (!groups.TryGetValue(signature, out List<int> group))
                    {
                        group = new List<int>(2);
                        groups.Add(signature, group);
                    }
                    group.Add(vertex);
                }
                foreach (List<int> group in groups.Values)
                {
                    if (group.Count < 2) continue;
                    for (int first = 0; first < group.Count - 1; first++)
                    for (int second = first + 1; second < group.Count; second++)
                    {
                        Vector2 a = uv[group[first]], b = uv[group[second]];
                        if (Vector2.Distance(a, b) <= 2f / Mathf.Max(width, height)) continue;
                        WriteSeamLink(pixels, width, height, a, b);
                        WriteSeamLink(pixels, width, height, b, a);
                    }
                }
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            {
                name = "UMA Fluid Seam Links",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static SlotData FindSlotForVertexSafe(SlotData[] slots, int vertex)
        {
            if (slots == null) return null;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot?.asset?.meshData == null || vertex < slot.vertexOffset) continue;
                int localVertex = vertex - slot.vertexOffset;
                if (localVertex < slot.asset.meshData.vertexCount) return slot;
            }
            return null;
        }

        private static int[] BuildWeightHashes(Mesh mesh)
        {
            var result = new int[mesh.vertexCount];
            NativeArray<byte> counts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            int offset = 0;
            for (int vertex = 0; vertex < result.Length; vertex++)
            {
                int hash = 17;
                int count = vertex < counts.Length ? counts[vertex] : 0;
                for (int influence = 0; influence < count && offset < weights.Length;
                    influence++, offset++)
                {
                    BoneWeight1 weight = weights[offset];
                    unchecked
                    {
                        hash = (hash * 397) ^ weight.boneIndex;
                        hash = (hash * 397) ^ Mathf.RoundToInt(weight.weight * 10000f);
                    }
                }
                result[vertex] = hash;
            }
            return result;
        }

        private static void WriteSeamLink(Color[] pixels, int width, int height,
            Vector2 source, Vector2 target)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(source.x * (width - 1)), 0, width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(source.y * (height - 1)), 0, height - 1);
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int px = x + ox, py = y + oy;
                if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;
                pixels[py * width + px] = new Color(target.x, target.y, 0f, 1f);
            }
        }

        private void StartFallbacks(EffectRecord record, DismembermentResult cut)
        {
            DismembermentCutSurface[] surfaces = cut.cutSurfaces;
            if (surfaces != null)
            {
                for (int i = 0; i < surfaces.Length; i++)
                {
                    DismembermentCutSurface surface = surfaces[i];
                    if (surface?.sourceRenderer == null) continue;
                    StartFallback(record, ResolvePosedCenter(surface), surface.WorldNormal);
                }
            }
            if (record.fallbacks.Count == 0 && cut.sourceTargetBone != null)
                StartFallback(record, cut.sourceTargetBone.position, cut.sourceTargetBone.forward);
        }

        private void StartIndependentFallbacks(EffectRecord record, DismembermentResult cut)
        {
            var detachedSurfaces = new List<DismembermentCutSurface>();
            if (record.sourceSurfaces != null && cut.sourceRenderers != null &&
                cut.detachedRenderers != null)
            {
                for (int i = 0; i < record.sourceSurfaces.Length; i++)
                {
                    DismembermentCutSurface source = record.sourceSurfaces[i];
                    int rendererIndex = FindRendererIndex(cut.sourceRenderers,
                        source.sourceRenderer);
                    if ((uint)rendererIndex >= (uint)cut.detachedRenderers.Length ||
                        cut.detachedRenderers[rendererIndex] == null) continue;
                    DismembermentCutSurface detached = CloneSurfaceForRenderer(source,
                        cut.detachedRenderers[rendererIndex]);
                    detachedSurfaces.Add(detached);
                    StartFallback(record, ResolvePosedCenter(detached), detached.WorldNormal);
                }
            }
            if (detachedSurfaces.Count > 0)
            {
                record.sourceSurfaces = detachedSurfaces.ToArray();
                return;
            }
            if (cut.targetBone != null)
                StartFallback(record, cut.targetBone.position, cut.targetBone.forward);
        }

        private void StartFallbacksFromStoredSurfaces(EffectRecord record)
        {
            if (record.sourceSurfaces != null)
            {
                for (int i = 0; i < record.sourceSurfaces.Length; i++)
                {
                    DismembermentCutSurface surface = record.sourceSurfaces[i];
                    if (surface?.sourceRenderer == null) continue;
                    StartFallback(record, ResolvePosedCenter(surface), surface.WorldNormal);
                }
            }
            if (record.fallbacks.Count == 0 && record.hasFallbackOrigin)
                StartFallback(record, record.fallbackWorldPosition, record.fallbackWorldNormal);
        }

        private void StartFallback(EffectRecord record, Vector3 origin, Vector3 normal,
            float speedMultiplier = 1f, float sizeMultiplier = 1f)
        {
            var host = new GameObject("UMA Surface Fluid Fallback");
            host.transform.SetParent(transform, true);
            UMASurfaceFluidFallbackTrail trail =
                host.AddComponent<UMASurfaceFluidFallbackTrail>();
            trail.Initialize(origin, normal, record.profile, fallbackTrailMaterial,
                Mathf.Max(0.05f, speedMultiplier), Mathf.Max(0.05f, sizeMultiplier));
            record.fallbacks.Add(trail);
        }

        private static Vector3 ResolvePosedCenter(DismembermentCutSurface surface)
        {
            Mesh baked = new Mesh();
            try
            {
                surface.sourceRenderer.BakeMesh(baked);
                Vector3[] vertices = baked.vertices;
                Vector3 center = Vector3.zero;
                int count = 0;
                for (int i = 0; i < surface.sourceVertexIndices.Length; i++)
                {
                    int index = surface.sourceVertexIndices[i];
                    if ((uint)index >= (uint)vertices.Length) continue;
                    center += vertices[index];
                    count++;
                }
                if (count > 0)
                    return surface.sourceRenderer.transform.TransformPoint(center / count);
                return surface.WorldCenter;
            }
            finally { DestroyOwned(baked); }
        }

        private void IsolateDetachedMaterials(DismembermentResult cut, EffectRecord record)
        {
            if (cut.root == null || cut.detachedRenderers == null) return;
            DismemberedPieceMaterialOwner owner =
                cut.root.GetComponent<DismemberedPieceMaterialOwner>();
            if (owner == null) owner = cut.root.gameObject.AddComponent<DismemberedPieceMaterialOwner>();
            for (int rendererIndex = 0; rendererIndex < cut.detachedRenderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = cut.detachedRenderers[rendererIndex];
                if (renderer == null) continue;
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material original = materials[materialIndex];
                    if (original == null) continue;
                    AtlasContext matched = null;
                    for (int bindingIndex = 0; bindingIndex < record.fluids.Count; bindingIndex++)
                    {
                        AtlasContext candidate = record.fluids[bindingIndex].context;
                        if (candidate.generated.material == original)
                        {
                            matched = candidate;
                            break;
                        }
                    }
                    if (matched == null) continue;
                    Material clone = new Material(original)
                    {
                        name = original.name + " Detached Base",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    for (int outputIndex = 0; outputIndex < matched.outputs.Count; outputIndex++)
                    {
                        OutputChannel output = matched.outputs[outputIndex];
                        clone.SetTexture(output.propertyName, output.baseTexture);
                    }
                    owner.Add(clone);
                    materials[materialIndex] = clone;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private void ClearState(RenderTexture texture, AtlasContext context)
        {
            fluidCompute.SetInts("_Dimensions", context.simulationWidth,
                context.simulationHeight);
            fluidCompute.SetTexture(clearKernel, "_StateMutable", texture);
            Dispatch(clearKernel, context);
        }

        private void ClearScalar(RenderTexture texture, AtlasContext context)
        {
            fluidCompute.SetInts("_Dimensions", context.simulationWidth,
                context.simulationHeight);
            fluidCompute.SetTexture(clearScalarKernel, "_ScalarMutable", texture);
            Dispatch(clearScalarKernel, context);
        }

        private void BindOutput(OutputChannel output)
        {
            if (output.context.generated.material != null)
                output.context.generated.material.SetTexture(output.propertyName, output.output);
            if (output.context.generated.secondPassMaterial != null)
                output.context.generated.secondPassMaterial.SetTexture(output.propertyName,
                    output.output);
        }

        private static void RestoreOutput(OutputChannel output)
        {
            if (output.context.generated?.material != null &&
                output.context.generated.material.GetTexture(output.propertyName) == output.output)
                output.context.generated.material.SetTexture(output.propertyName, output.baseTexture);
            if (output.context.generated?.secondPassMaterial != null &&
                output.context.generated.secondPassMaterial.GetTexture(output.propertyName) ==
                output.output)
                output.context.generated.secondPassMaterial.SetTexture(output.propertyName,
                    output.baseTexture);
        }

        private void RemoveUnusedContexts()
        {
            for (int i = contexts.Count - 1; i >= 0; i--)
            {
                AtlasContext context = contexts[i];
                if (ContextIsUsed(context)) continue;
                ReleaseContext(context);
                contexts.RemoveAt(i);
            }
        }

        private bool ContextIsUsed(AtlasContext context)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                EffectRecord effect = effects[i];
                for (int f = 0; f < effect.fluids.Count; f++)
                    if (effect.fluids[f].context == context) return true;
                for (int s = 0; s < effect.stamps.Count; s++)
                    if (effect.stamps[s].output.context == context) return true;
                for (int c = 0; c < effect.surfaceCuts.Count; c++)
                    if (effect.surfaceCuts[c].output.context == context) return true;
            }
            return false;
        }

        private void ReleaseEffect(EffectRecord effect)
        {
            ReleaseBindings(effect, false);
        }

        private void ReleaseBindings(EffectRecord effect, bool preserveRecord)
        {
            for (int i = 0; i < effect.fluids.Count; i++)
                ReleaseFluidBinding(effect.fluids[i]);
            effect.fluids.Clear();
            for (int i = 0; i < effect.stamps.Count; i++)
                DestroyOwned(effect.stamps[i].mesh);
            effect.stamps.Clear();
            for (int i = 0; i < effect.surfaceCuts.Count; i++)
                DestroyOwned(effect.surfaceCuts[i].mesh);
            effect.surfaceCuts.Clear();
            for (int i = 0; i < effect.fallbacks.Count; i++)
                if (effect.fallbacks[i] != null)
                    DestroyOwned(effect.fallbacks[i].gameObject);
            effect.fallbacks.Clear();
            if (!preserveRecord) effect.state = RuntimeDecalState.Complete;
        }

        private static void ReleaseFluidBinding(FluidBinding binding)
        {
            for (int i = 0; i < binding.sources.Count; i++)
                if (binding.sources[i]?.ownsMesh == true)
                    DestroyOwned(binding.sources[i].mesh);
            binding.sources.Clear();
            ReleaseRT(binding.stateA); ReleaseRT(binding.stateB);
            ReleaseRT(binding.speedA); ReleaseRT(binding.speedB); ReleaseRT(binding.flux);
        }

        private void ReleaseContexts()
        {
            for (int i = 0; i < contexts.Count; i++) ReleaseContext(contexts[i]);
            contexts.Clear();
        }

        private static void ReleaseContext(AtlasContext context)
        {
            for (int i = 0; i < context.outputs.Count; i++)
            {
                RestoreOutput(context.outputs[i]);
                ReleaseRT(context.outputs[i].output);
            }
            context.outputs.Clear();
            ReleaseRT(context.surfacePosition); ReleaseRT(context.surfaceFlow);
            ReleaseRT(context.surfaceDepth); ReleaseRT(context.injection);
            DestroyOwned(context.seamLinks);
            context.surfaceCommand?.Release();
            context.surfaceCommand = null;
            context.fieldTargets = null;
        }

        private void ReleaseMaterials()
        {
            DestroyOwned(surfaceFieldMaterial); DestroyOwned(compositeMaterial);
            DestroyOwned(sourceMaskMaterial); DestroyOwned(surfaceCutMaterial);
            DestroyOwned(fallbackTrailMaterial);
            surfaceFieldMaterial = null; compositeMaterial = null; sourceMaskMaterial = null;
            surfaceCutMaterial = null; fallbackTrailMaterial = null;
        }

        private static RenderTexture CreateSimulationRT(int width, int height,
            RenderTextureFormat format, bool randomWrite, string name)
        {
            var texture = new RenderTexture(width, height, 0, format,
                RenderTextureReadWrite.Linear)
            {
                name = name,
                enableRandomWrite = randomWrite,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            if (!texture.Create()) throw new InvalidOperationException(
                $"RenderTexture '{name}' could not be created in format {format}.");
            return texture;
        }

        private static RenderTexture CreateOutputRT(Texture source, string property)
        {
            RenderTexture result;
            if (source is RenderTexture sourceRT)
            {
                RenderTextureDescriptor descriptor = sourceRT.descriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.enableRandomWrite = false;
                descriptor.autoGenerateMips = false;
                result = new RenderTexture(descriptor);
            }
            else
            {
                result = new RenderTexture(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    useMipMap = source.mipmapCount > 1,
                    autoGenerateMips = false
                };
            }
            result.name = "UMA Runtime " + property;
            result.filterMode = source.filterMode;
            result.wrapMode = source.wrapMode;
            if (!result.Create())
            {
                DestroyOwned(result);
                return null;
            }
            return result;
        }

        private static void ReleaseRT(RenderTexture texture)
        {
            if (texture == null) return;
            if (texture.IsCreated()) texture.Release();
            DestroyOwned(texture);
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private AtlasContext FindContext(UMAData.GeneratedMaterial generated)
        {
            for (int i = 0; i < contexts.Count; i++)
                if (contexts[i].generated == generated) return contexts[i];
            return null;
        }

        private static Texture FirstUsableAtlas(UMAData.GeneratedMaterial generated)
        {
            if (generated?.resultingAtlasList == null) return null;
            for (int i = 0; i < generated.resultingAtlasList.Length; i++)
                if (generated.resultingAtlasList[i] != null) return generated.resultingAtlasList[i];
            return null;
        }

        private static OverlayDataAsset ResolveStampSource(DecalRTStampAsset stamp)
        {
            OverlayDataAsset result = stamp.sourceOverlay;
            if (result == null && !string.IsNullOrEmpty(stamp.sourceOverlayName) &&
                UMAAssetIndexer.Instance != null)
                result = UMAAssetIndexer.Instance.GetAsset<OverlayDataAsset>(stamp.sourceOverlayName);
            result?.EnsureMaterial();
            return result;
        }

        private static RuntimeDecalFadeSettings NormalizeFade(RuntimeDecalFadeSettings fade)
        {
            fade.holdSeconds = Mathf.Max(0f, fade.holdSeconds);
            fade.fadeSeconds = Mathf.Max(0.01f, fade.fadeSeconds);
            if (fade.opacity == null || fade.opacity.length == 0)
                fade.opacity = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            return fade;
        }

        private static UMASurfaceFluidProfile CreateBudgetProfile()
        {
            // Transient settings object is not retained or used for visual appearance.
            var profile = ScriptableObject.CreateInstance<UMASurfaceFluidProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            return profile;
        }

        private static Mesh InstantiateMesh(Mesh source)
        {
            Mesh result = Instantiate(source);
            result.name = source.name;
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }

        private static Vector3 UVToClip(Vector2 uv) =>
            new Vector3(uv.x * 2f - 1f, uv.y * 2f - 1f, 0f);

        private static bool SurfaceMatchesProfile(DismembermentCutSurface surface,
            UMASurfaceFluidProfile profile)
        {
            if (surface == null || !surface.IsValid) return false;
            if (profile.targetSlotGroups != null && profile.targetSlotGroups.Length > 0 &&
                !Contains(profile.targetSlotGroups, surface.slotGroup)) return false;
            if (profile.targetOverlayGroups != null && profile.targetOverlayGroups.Length > 0 &&
                !ContainsAny(profile.targetOverlayGroups, surface.overlayGroups,
                    surface.overlayGroup)) return false;
            return true;
        }

        private static bool ContainsAny(string[] requested, string[] candidates,
            string legacyCandidate)
        {
            if (Contains(requested, legacyCandidate)) return true;
            if (candidates == null) return false;
            for (int i = 0; i < candidates.Length; i++)
                if (Contains(requested, candidates[i])) return true;
            return false;
        }

        private static bool Contains(string[] values, string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i], candidate, StringComparison.Ordinal)) return true;
            return false;
        }

        private static DismembermentCutSurface[] CloneSurfaceArray(
            DismembermentCutSurface[] surfaces)
        {
            if (surfaces == null || surfaces.Length == 0)
                return Array.Empty<DismembermentCutSurface>();
            var result = new DismembermentCutSurface[surfaces.Length];
            for (int i = 0; i < surfaces.Length; i++) result[i] = surfaces[i];
            return result;
        }

        private static int FindRendererIndex(SkinnedMeshRenderer[] renderers,
            SkinnedMeshRenderer renderer)
        {
            if (renderers == null) return -1;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] == renderer) return i;
            return -1;
        }

        private static DismembermentCutSurface CloneSurfaceForRenderer(
            DismembermentCutSurface source, SkinnedMeshRenderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            Material material = (uint)source.sourceSubmeshIndex < (uint)materials.Length
                ? materials[source.sourceSubmeshIndex] : null;
            return new DismembermentCutSurface
            {
                sourceRenderer = renderer,
                sourceSubmeshIndex = source.sourceSubmeshIndex,
                sourceMaterial = material,
                sourceVertexIndices = source.sourceVertexIndices,
                boundaryUV = source.boundaryUV,
                boundaryLocalPositions = source.boundaryLocalPositions,
                loopStarts = source.loopStarts,
                loopCounts = source.loopCounts,
                uvBounds = source.uvBounds,
                localCenter = source.localCenter,
                localNormal = source.localNormal,
                slotName = source.slotName,
                slotGroup = source.slotGroup,
                overlayGroup = source.overlayGroup,
                overlayGroups = source.overlayGroups,
                umaMaterialName = source.umaMaterialName
            };
        }

        private void AddDiagnostic(string message)
        {
            diagnostics.Add(message);
            if (logDiagnostics) Debug.LogWarning("UMA Runtime Surface Decals: " + message, this);
        }
    }
}
