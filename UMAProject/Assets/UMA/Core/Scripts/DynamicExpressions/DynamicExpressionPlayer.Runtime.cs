using System;
using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UMA.PoseTools;
using UnityEngine;
using UnityEngine.Events;
using Unity.Profiling;

/// <summary>
/// Drives transient, race-specific expression DNA without persisting values in
/// the avatar's body DNA collection.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class DynamicExpressionPlayer
{
    [Serializable]
    public struct ExpressionChange
    {
        public string id;
        public float previousValue;
        public float value;
        public ExpressionSource source;

        public ExpressionChange(string id, float previousValue, float value,
            ExpressionSource source)
        {
            this.id = id;
            this.previousValue = previousValue;
            this.value = value;
            this.source = source;
        }
    }

    [Serializable]
    public sealed class ExpressionChangedEvent : UnityEvent<ExpressionChange> { }

    [Serializable]
    public sealed class ExpressionBoneJoint
    {
        public string boneName;
        public ExpressionJoint joint = ExpressionJoint.Other;
    }

    private sealed class RuntimeExpression
    {
        public UMAExpressionDefinition definition;
        public readonly float[] sourceValues = new float[SourceCount];
        public readonly bool[] sourceActive = new bool[SourceCount];
        public float target;
        public float effective;
        public float velocity;
        public ExpressionSource lastSource;
        public ExpressionEffectPhase phases;
        public DNAInstanceCollection.DNABuildType buildType;
    }

    private struct BuildValue
    {
        public DNA dna;
        public float value;
    }

    private sealed class SourceSnapshot
    {
        public readonly float[] values = new float[SourceCount];
        public readonly bool[] active = new bool[SourceCount];
    }

    private struct BlendShapeTarget
    {
        public SkinnedMeshRenderer renderer;
        public int index;
    }

    private sealed class BlendShapeBinding
    {
        public readonly List<BlendShapeTarget> targets =
            new List<BlendShapeTarget>(2);
    }

    private struct MaterialTarget
    {
        public SkinnedMeshRenderer renderer;
        public int materialIndex;
        public MaterialPropertyBlock propertyBlock;
    }

    private sealed class MaterialBinding
    {
        public int propertyId;
        public readonly List<MaterialTarget> targets =
            new List<MaterialTarget>(2);
    }

    private const int SourceCount = 4;
    private static readonly int[] OverrideSourcePriority = { 0, 1, 3, 2 };
    private static readonly ProfilerMarker SourceResolutionMarker =
        new ProfilerMarker("UMA.Expressions.ResolveSources");
    private static readonly ProfilerMarker RigRestoreMarker =
        new ProfilerMarker("UMA.Expressions.RestoreRig");
    private static readonly ProfilerMarker RigApplyMarker =
        new ProfilerMarker("UMA.Expressions.ApplyRig");
    private static readonly ProfilerMarker RendererApplyMarker =
        new ProfilerMarker("UMA.Expressions.ApplyRenderer");
    private static readonly ProfilerMarker BuildRequestMarker =
        new ProfilerMarker("UMA.Expressions.RequestBuild");

    [Header("Expression Group")]
    [Tooltip("Optional override. Otherwise the active race's group is used.")]
    public UMAExpressionGroup expressionGroupOverride;
    [Tooltip("Legacy inline definitions, used only when no group is available.")]
    public List<DynamicExpression> Expressions = new List<DynamicExpression>();

    [Header("Mecanim Overrides")]
    public bool overrideMecanimEyes = true;
    public bool overrideMecanimJaw = true;
    public bool overrideMecanimNeck;
    public bool overrideMecanimHead;
    public bool overrideMecanimHands;
    [Tooltip("Bone classifications for generic/non-humanoid rigs.")]
    public List<ExpressionBoneJoint> genericBoneJoints =
        new List<ExpressionBoneJoint>();

    [Header("Eye Movement")]
    public bool EnableSaccades = true;
    [Min(0.01f)] public float SaccadeIntervalMin = 0.5f;
    [Min(0.01f)] public float SaccadeIntervalMax = 1.5f;
    [Min(0f)] public float SaccadeMaxOffsetDeg = 6f;
    [Range(0f, 1f)] public float SaccadeVerticalBias = 0.35f;

    [Header("Blink")]
    public bool EnableBlinking = true;
    [Min(0.01f)] public float BlinkIntervalMin = 3f;
    [Min(0.01f)] public float BlinkIntervalMax = 7f;
    [Min(0.01f)] public float BlinkDuration = 0.15f;
    public AnimationCurve BlinkCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.05f, 1f),
        new Keyframe(0.35f, 0f), new Keyframe(1f, 0f));

    [Header("Gaze")]
    public bool EnableLookAt = true;
    public Transform LookAtTarget;
    [Min(0f)] public float LookAtMaxDistance = 25f;
    [Min(0f)] public float LookAtMinDistance = 0.5f;
    [Range(0f, 180f)] public float EyeMaxAngle = 30f;
    [Range(0f, 180f)] public float HeadAssistStartAngle = 15f;
    [Range(0f, 180f)] public float HeadAssistFullAngle = 35f;
    [Range(0f, 1f)] public float GazeWeight = 0.75f;
    [Range(0f, 1f)] public float HeadWeight = 0.5f;
    [Range(0f, 1f)] public float BodyWeight = 0.1f;
    [Range(0f, 1f)] public float EyesWeight = 1f;
    [Range(0f, 90f)] public float ClampVerticalAngle = 20f;

    [Header("Processing")]
    [Tooltip("Zero disables camera-distance culling.")]
    [Min(0f)] public float processDistance = 30f;
    [Min(0f)] public float buildDebounceSeconds = 0.08f;
    [Min(0.01f)] public float meshBuildMinimumInterval = 0.25f;

    [Header("Events")]
    public ExpressionChangedEvent ExpressionChanged =
        new ExpressionChangedEvent();
    public UnityEvent GroupRebound = new UnityEvent();

    [SerializeField, HideInInspector]
    public string dnaCreationFolder = "Assets";

    private readonly List<RuntimeExpression> _runtimeExpressions =
        new List<RuntimeExpression>();
    private readonly Dictionary<string, int> _expressionLookup =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ExpressionJoint> _boneJoints =
        new Dictionary<int, ExpressionJoint>();
    private readonly List<int> _controlledBones = new List<int>();
    private readonly HashSet<int> _controlledBoneSet = new HashSet<int>();
    private readonly List<int> _boneScratch = new List<int>();
    private readonly List<BuildValue> _buildSnapshot =
        new List<BuildValue>();
    private readonly Dictionary<DNAEffect_BlendShape, BlendShapeBinding>
        _blendShapeBindings =
            new Dictionary<DNAEffect_BlendShape, BlendShapeBinding>();
    private readonly Dictionary<DNAEffect_RuntimeMaterialProperty,
        MaterialBinding> _materialBindings =
            new Dictionary<DNAEffect_RuntimeMaterialProperty,
                MaterialBinding>();
    private readonly List<Material> _materialScratch =
        new List<Material>(4);
    private readonly List<UnityEngine.Object> _legacyRuntimeObjects =
        new List<UnityEngine.Object>();

    private DynamicCharacterAvatar _avatar;
    private UMAData _umaData;
    private Animator _animator;
    private Camera _mainCamera;
    private UMAExpressionGroup _resolvedGroup;
    private UMAExpressionSet _resolvedLegacyExpressionSet;
    private bool _subscribed;
    private bool _initialized;
    private bool _bindingsValid;
    private bool _wasProcessing;
    private bool _isBuilding;
    private bool _buildChangedWhileBuilding;
    private bool _buildPending;
    private bool _immediateDirty;
    private int _batchDepth;
    private bool _batchChanged;
    private float _buildRequestTime;
    private float _lastMeshBuildTime = float.NegativeInfinity;
    private DNAInstanceCollection.DNABuildType _pendingBuildType;

    private int _blinkLeftIndex = -1;
    private int _blinkRightIndex = -1;
    private int _eyeHorizontalIndex = -1;
    private int _eyeVerticalIndex = -1;
    private int _eyeHorizontalLeftIndex = -1;
    private int _eyeHorizontalRightIndex = -1;
    private int _eyeVerticalLeftIndex = -1;
    private int _eyeVerticalRightIndex = -1;
    private float _nextBlinkTime;
    private float _blinkStartTime;
    private bool _isBlinking;
    private float _nextSaccadeTime;
    private float _saccadeStartTime;
    private float _saccadeDuration;
    private Vector2 _saccade;
    private Vector2 _saccadeFrom;
    private Vector2 _saccadeTo;
    private Vector3 _ikLookPosition;
    private float _ikHeadWeight;
    private float _ikBodyWeight;
    private float _ikEyesWeight;
    private bool _ikActive;

    public UMAExpressionGroup ResolvedGroup => _resolvedGroup;
    public bool UsingTransientLegacyExpressionSet =>
        _resolvedLegacyExpressionSet != null;
    public int ExpressionCount => _runtimeExpressions.Count;
    public bool HasPendingBuild => _buildPending || _isBuilding;
    public DNAInstanceCollection.DNABuildType PendingBuildType =>
        _pendingBuildType;
    public bool AnimatorLookAtActive => _ikActive;
    public Vector3 AnimatorLookAtPosition => _ikLookPosition;
    public float AnimatorLookAtEyesWeight => _ikEyesWeight;

    public event Action<ExpressionChange> ExpressionChangedAction;
    public event Action<UMAExpressionGroup> GroupReboundAction;

    private void Awake() => Initialize();

    private void OnEnable()
    {
        Initialize();
        Subscribe();
        ScheduleNextBlink();
        ScheduleNextSaccade();
    }

    private void OnDisable()
    {
        RestoreControlledBones();
        Unsubscribe();
        ResetProceduralSources();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        ClearLegacyRuntimeObjects();
    }

    private void OnValidate()
    {
        SaccadeIntervalMin = Mathf.Max(0.01f, SaccadeIntervalMin);
        SaccadeIntervalMax = Mathf.Max(SaccadeIntervalMin, SaccadeIntervalMax);
        BlinkIntervalMin = Mathf.Max(0.01f, BlinkIntervalMin);
        BlinkIntervalMax = Mathf.Max(BlinkIntervalMin, BlinkIntervalMax);
        BlinkDuration = Mathf.Max(0.01f, BlinkDuration);
        buildDebounceSeconds = Mathf.Max(0f, buildDebounceSeconds);
        meshBuildMinimumInterval = Mathf.Max(0.01f,
            meshBuildMinimumInterval);
        if (isActiveAndEnabled)
        {
            Initialize();
            Rebind();
        }
    }

    /// <summary>Retries discovery and group resolution at any lifecycle point.</summary>
    public void Initialize()
    {
        DiscoverAvatarContext();
        if (_initialized && _umaData != null)
        {
            UMAExpressionGroup currentGroup = ResolveGroup();
            UMAExpressionSet currentLegacy =
                currentGroup == null ? ResolveLegacyExpressionSet() : null;
            if (currentGroup != _resolvedGroup ||
                currentLegacy != _resolvedLegacyExpressionSet)
                BindGroup(currentGroup);
            return;
        }

        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        _mainCamera = Camera.main;
        Subscribe();

        UMAExpressionGroup group = ResolveGroup();
        if (!_initialized || group != _resolvedGroup) BindGroup(group);
        _initialized = true;
    }

    /// <summary>
    /// Re-resolves the race/group and retains values for IDs in both groups.
    /// </summary>
    public void Rebind()
    {
        DiscoverAvatarContext();
        Dictionary<string, SourceSnapshot> retained = CaptureSources();
        BindGroup(ResolveGroup());
        RestoreSources(retained);
        _bindingsValid = false;
        RefreshBindings();
        EvaluateValues(0f, true);
        ApplyImmediateEffects();
    }

    private void DiscoverAvatarContext()
    {
        DynamicCharacterAvatar avatar =
            GetComponent<DynamicCharacterAvatar>();
        if (avatar == null)
            avatar = GetComponentInParent<DynamicCharacterAvatar>();

        UMAData data = avatar != null ? avatar : GetComponent<UMAData>();
        if (data == null) data = GetComponentInParent<UMAData>();
        if (_umaData == data && _avatar == avatar) return;

        Unsubscribe();
        _avatar = avatar;
        _umaData = data;
        _initialized = false;
        _animator = null;
        _bindingsValid = false;
    }

    public string GetExpressionId(int index) =>
        index >= 0 && index < _runtimeExpressions.Count
            ? _runtimeExpressions[index].definition.id : null;

    public string[] GetExpressionIds()
    {
        string[] result = new string[_runtimeExpressions.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = _runtimeExpressions[i].definition.id;
        return result;
    }

    public float[] GetValuesSnapshot()
    {
        float[] result = new float[_runtimeExpressions.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = _runtimeExpressions[i].effective;
        return result;
    }

    public void SetValuesSnapshot(IReadOnlyList<float> values,
        ExpressionSource source = ExpressionSource.Manual,
        bool resetMissing = false)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        Initialize();
        BeginExpressionBatch();
        try
        {
            int count = Mathf.Min(values.Count, _runtimeExpressions.Count);
            for (int i = 0; i < count; i++)
                SetExpression(i, values[i], source);
            if (resetMissing)
                for (int i = count; i < _runtimeExpressions.Count; i++)
                    ResetExpression(i, source);
        }
        finally
        {
            EndExpressionBatch();
        }
    }

    public bool TryGetExpressionIndex(string id, out int index)
    {
        Initialize();
        index = -1;
        return !string.IsNullOrWhiteSpace(id) &&
            _expressionLookup.TryGetValue(id, out index);
    }

    public bool SetExpression(string id, float value,
        ExpressionSource source = ExpressionSource.Manual) =>
        TryGetExpressionIndex(id, out int index) &&
        SetExpression(index, value, source);

    /// <summary>Allocation-free setter for animation/network systems.</summary>
    public bool SetExpression(int index, float value,
        ExpressionSource source = ExpressionSource.Manual)
    {
        if (index < 0 || index >= _runtimeExpressions.Count ||
            !IsValidSource(source)) return false;
        RuntimeExpression expression = _runtimeExpressions[index];
        int sourceIndex = (int)source;
        float clamped = Mathf.Clamp01(value);
        if (expression.sourceActive[sourceIndex] &&
            Mathf.Approximately(expression.sourceValues[sourceIndex],
                clamped)) return true;

        expression.sourceActive[sourceIndex] = true;
        expression.sourceValues[sourceIndex] = clamped;
        expression.lastSource = source;
        ValuesChanged();
        return true;
    }

    public bool TryGetExpression(string id, out float value)
    {
        if (TryGetExpressionIndex(id, out int index))
        {
            value = _runtimeExpressions[index].effective;
            return true;
        }
        value = 0f;
        return false;
    }

    public bool TryGetExpression(int index, out float value)
    {
        if (index >= 0 && index < _runtimeExpressions.Count)
        {
            value = _runtimeExpressions[index].effective;
            return true;
        }
        value = 0f;
        return false;
    }

    public bool TryGetSourceValue(int index, ExpressionSource source,
        out float value, out bool active)
    {
        if (index >= 0 && index < _runtimeExpressions.Count &&
            IsValidSource(source))
        {
            RuntimeExpression expression = _runtimeExpressions[index];
            value = expression.sourceValues[(int)source];
            active = expression.sourceActive[(int)source];
            return true;
        }
        value = 0f;
        active = false;
        return false;
    }

    public bool ResetExpression(string id, ExpressionSource source) =>
        TryGetExpressionIndex(id, out int index) &&
        ResetExpression(index, source);

    public bool ResetExpression(int index, ExpressionSource source)
    {
        if (index < 0 || index >= _runtimeExpressions.Count ||
            !IsValidSource(source)) return false;
        RuntimeExpression expression = _runtimeExpressions[index];
        int sourceIndex = (int)source;
        if (!expression.sourceActive[sourceIndex]) return true;
        expression.sourceActive[sourceIndex] = false;
        ValuesChanged();
        return true;
    }

    public void ResetAllExpressions(ExpressionSource source)
    {
        if (!IsValidSource(source)) return;
        BeginExpressionBatch();
        for (int i = 0; i < _runtimeExpressions.Count; i++)
            ResetExpression(i, source);
        EndExpressionBatch();
    }

    public void BeginExpressionBatch() => _batchDepth++;

    public void EndExpressionBatch()
    {
        if (_batchDepth <= 0)
            throw new InvalidOperationException(
                "EndExpressionBatch has no matching begin.");
        _batchDepth--;
        if (_batchDepth == 0 && _batchChanged)
        {
            _batchChanged = false;
            EvaluateValues(0f, false);
        }
    }

    /// <summary>Shared synchronous application path for preview and tests.</summary>
    public void EvaluateExpressionsNow(float deltaTime = 0f)
    {
        Initialize();
        RefreshBindings();
        EvaluateValues(Mathf.Max(0f, deltaTime), true);
        ApplyImmediateEffects();
    }

    /// <summary>Advances definition response-time smoothing without snapping.</summary>
    public void AdvanceExpressionSmoothing(float deltaTime)
    {
        Initialize();
        EvaluateValues(Mathf.Max(0f, deltaTime), false);
        if (_immediateDirty) ApplyImmediateEffects();
    }

    public ExpressionEffectPhase GetExpressionPhases(int index) =>
        index >= 0 && index < _runtimeExpressions.Count
            ? _runtimeExpressions[index].phases : ExpressionEffectPhase.None;

    public ExpressionJoint GetExpressionAffectedJoints(int index) =>
        index >= 0 && index < _runtimeExpressions.Count
            ? _runtimeExpressions[index].definition.affectedJoints
            : ExpressionJoint.None;

    public DNAInstanceCollection.DNABuildType GetExpressionBuildType(int index)
        => index >= 0 && index < _runtimeExpressions.Count
            ? _runtimeExpressions[index].buildType
            : DNAInstanceCollection.DNABuildType.None;

    private void Update()
    {
        Initialize();
        bool shouldProcess = ShouldProcessFrameLane();
        if (_wasProcessing && !shouldProcess) RestoreControlledBones();
        _wasProcessing = shouldProcess;
        if (shouldProcess)
        {
            RefreshBindings();
            RestoreControlledBones();
            BeginExpressionBatch();
            try
            {
                UpdateBlinking();
                UpdateSaccadesAndGaze();
            }
            finally
            {
                EndExpressionBatch();
            }
            EvaluateValues(Time.deltaTime, false);
        }
        ProcessPendingBuild();
    }

    private void LateUpdate()
    {
        if (!_wasProcessing || _isBuilding || _umaData == null) return;
        ApplyRigEffects();
        if (_immediateDirty) ApplyImmediateEffects();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!_ikActive || _animator == null || !_animator.isHuman) return;
        _animator.SetLookAtPosition(_ikLookPosition);
        _animator.SetLookAtWeight(GazeWeight, _ikBodyWeight, _ikHeadWeight,
            _ikEyesWeight, 0.5f);
    }

    private bool ShouldProcessFrameLane()
    {
        if (!isActiveAndEnabled || _isBuilding || _umaData == null)
            return false;
        if (processDistance <= 0f) return true;
        if (_mainCamera == null) _mainCamera = Camera.main;
        return _mainCamera == null ||
            Vector3.SqrMagnitude(_mainCamera.transform.position -
                transform.position) <= processDistance * processDistance;
    }

    private void ValuesChanged()
    {
        if (_batchDepth > 0)
        {
            _batchChanged = true;
            return;
        }
        EvaluateValues(0f, false);
    }

    private void EvaluateValues(float deltaTime, bool force)
    {
        using (SourceResolutionMarker.Auto())
        {
            for (int i = 0; i < _runtimeExpressions.Count; i++)
            {
                RuntimeExpression expression = _runtimeExpressions[i];
                float target = ResolveSources(expression);
                expression.target = target;
                float previous = expression.effective;
                float responseTime = expression.definition.responseTime;
                if (force || responseTime <= 0f)
                {
                    expression.effective = target;
                    expression.velocity = 0f;
                }
                else if (deltaTime > 0f)
                {
                    expression.effective = Mathf.SmoothDamp(
                        expression.effective, target,
                        ref expression.velocity, responseTime,
                        Mathf.Infinity, deltaTime);
                }
                if (Mathf.Approximately(previous, expression.effective))
                    continue;
                _immediateDirty = true;
                if (HasBuildPhase(expression.phases))
                    QueueBuild(expression.buildType);
                ExpressionChange change = new ExpressionChange(
                    expression.definition.id, previous,
                    expression.effective, expression.lastSource);
                ExpressionChanged?.Invoke(change);
                ExpressionChangedAction?.Invoke(change);
            }
        }
    }

    private static float ResolveSources(RuntimeExpression expression)
    {
        float defaultValue = expression.definition.DefaultValue;
        if (expression.definition.blendMode == ExpressionBlendMode.Additive)
        {
            float result = defaultValue;
            bool active = false;
            for (int i = 0; i < SourceCount; i++)
            {
                if (!expression.sourceActive[i]) continue;
                result += expression.sourceValues[i] - defaultValue;
                active = true;
            }
            return active ? Mathf.Clamp01(result) : defaultValue;
        }
        if (expression.definition.blendMode == ExpressionBlendMode.Maximum)
        {
            float result = defaultValue;
            for (int i = 0; i < SourceCount; i++)
                if (expression.sourceActive[i])
                    result = Mathf.Max(result, expression.sourceValues[i]);
            return result;
        }

        // Manual > animation > procedural blink > procedural gaze.
        for (int i = 0; i < OverrideSourcePriority.Length; i++)
        {
            int source = OverrideSourcePriority[i];
            if (expression.sourceActive[source])
                return expression.sourceValues[source];
        }
        return defaultValue;
    }

    private void RestoreControlledBones()
    {
        if (_umaData == null || _umaData.skeleton == null) return;
        using (RigRestoreMarker.Auto())
        {
            for (int i = 0; i < _controlledBones.Count; i++)
            {
                int hash = _controlledBones[i];
                _umaData.skeleton.Restore(hash);
            }
        }
    }

    /// <summary>
    /// Removes the previous frame's expression contribution from all controlled
    /// bones. Custom player loops can call this before animation evaluation.
    /// </summary>
    public void RestoreRigExpressionsNow()
    {
        Initialize();
        RefreshBindings();
        RestoreControlledBones();
    }

    /// <summary>
    /// Applies all active rig expressions without restoring first. Custom
    /// player loops can call this after animation evaluation.
    /// </summary>
    public void ApplyRigExpressionsAfterAnimationNow()
    {
        Initialize();
        RefreshBindings();
        ApplyRigEffects();
    }

    /// <summary>
    /// Restores and applies all active rig expressions synchronously.
    /// </summary>
    public void ApplyRigExpressionsNow()
    {
        RestoreRigExpressionsNow();
        ApplyRigExpressionsAfterAnimationNow();
    }

    private void ApplyRigEffects()
    {
        if (_umaData == null || _umaData.skeleton == null) return;
        using (RigApplyMarker.Auto())
        {
            for (int i = 0; i < _runtimeExpressions.Count; i++)
            {
                RuntimeExpression expression = _runtimeExpressions[i];
                DNA dna = expression.definition.dna;
                if (dna == null || dna.effects == null ||
                    (expression.phases &
                     ExpressionEffectPhase.LateRig) == 0) continue;
                for (int e = 0; e < dna.effects.Count; e++)
                {
                    DNAEffect effect = dna.effects[e];
                    if (effect != null && effect.enabled &&
                        (effect.ExpressionPhases &
                         ExpressionEffectPhase.LateRig) != 0)
                        effect.ApplyExpressionRig(_umaData, dna,
                            expression.effective, ShouldApplyBone);
                }
            }
        }
    }

    private void ApplyImmediateEffects()
    {
        if (_umaData == null) return;
        using (RendererApplyMarker.Auto())
        {
            for (int i = 0; i < _runtimeExpressions.Count; i++)
            {
                RuntimeExpression expression = _runtimeExpressions[i];
                DNA dna = expression.definition.dna;
                if (dna == null || dna.effects == null) continue;
                for (int e = 0; e < dna.effects.Count; e++)
                {
                    DNAEffect effect = dna.effects[e];
                    if (effect == null || !effect.enabled) continue;
                    ExpressionEffectPhase phases = effect.ExpressionPhases;
                    if ((phases &
                         ExpressionEffectPhase.LateBlendShape) != 0)
                    {
                        if (effect is DNAEffect_BlendShape blendShape)
                            ApplyCachedBlendShape(blendShape,
                                expression.effective);
                        else
                            effect.ApplyExpressionBlendShape(_umaData, dna,
                                expression.effective);
                    }
                    if ((phases &
                         ExpressionEffectPhase.RuntimeMaterial) != 0)
                    {
                        if (effect is DNAEffect_RuntimeMaterialProperty
                            material)
                            ApplyCachedMaterialProperty(material,
                                expression.effective);
                        else
                            effect.ApplyExpressionMaterial(_umaData, dna,
                                expression.effective);
                    }
                }
            }
        }
        _immediateDirty = false;
    }

    private void RefreshBindings()
    {
        if (_bindingsValid) return;
        _controlledBones.Clear();
        _controlledBoneSet.Clear();
        _boneJoints.Clear();
        CacheHumanoidJoints();
        CacheGenericJoints();
        for (int i = 0; i < _runtimeExpressions.Count; i++)
        {
            RuntimeExpression expression = _runtimeExpressions[i];
            DNA dna = expression.definition.dna;
            if (dna == null || dna.effects == null) continue;
            for (int e = 0; e < dna.effects.Count; e++)
            {
                DNAEffect effect = dna.effects[e];
                if (effect == null || !effect.enabled ||
                    (effect.ExpressionPhases &
                     ExpressionEffectPhase.EarlyRestore) == 0) continue;
                _boneScratch.Clear();
                effect.CollectExpressionBones(_boneScratch);
                for (int b = 0; b < _boneScratch.Count; b++)
                {
                    int hash = _boneScratch[b];
                    if (_controlledBoneSet.Add(hash))
                        _controlledBones.Add(hash);
                    if (!_boneJoints.ContainsKey(hash) &&
                        expression.definition.affectedJoints !=
                        ExpressionJoint.None)
                        _boneJoints.Add(hash,
                            expression.definition.affectedJoints);
                }
            }
        }
        RebuildImmediateBindings();
        _bindingsValid = true;
    }

    private void RebuildImmediateBindings()
    {
        _blendShapeBindings.Clear();
        _materialBindings.Clear();
        if (_umaData == null) return;

        SkinnedMeshRenderer[] renderers = _umaData.GetRenderers();
        if (renderers == null) return;

        for (int expressionIndex = 0;
             expressionIndex < _runtimeExpressions.Count;
             expressionIndex++)
        {
            DNA dna = _runtimeExpressions[expressionIndex].definition.dna;
            if (dna == null || dna.effects == null) continue;
            for (int effectIndex = 0;
                 effectIndex < dna.effects.Count;
                 effectIndex++)
            {
                DNAEffect effect = dna.effects[effectIndex];
                if (effect == null || !effect.enabled) continue;
                if (effect is DNAEffect_BlendShape blendShape &&
                    !_blendShapeBindings.ContainsKey(blendShape))
                {
                    _blendShapeBindings.Add(blendShape,
                        CreateBlendShapeBinding(blendShape, renderers));
                }
                else if (effect is DNAEffect_RuntimeMaterialProperty
                    material &&
                    !_materialBindings.ContainsKey(material))
                {
                    _materialBindings.Add(material,
                        CreateMaterialBinding(material, renderers));
                }
            }
        }
    }

    private static BlendShapeBinding CreateBlendShapeBinding(
        DNAEffect_BlendShape effect,
        SkinnedMeshRenderer[] renderers)
    {
        BlendShapeBinding binding = new BlendShapeBinding();
        if (string.IsNullOrEmpty(effect.BlendShapeName)) return binding;
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (renderer == null || renderer.sharedMesh == null) continue;
            int index = renderer.sharedMesh.GetBlendShapeIndex(
                effect.BlendShapeName);
            if (index >= 0)
                binding.targets.Add(new BlendShapeTarget
                {
                    renderer = renderer,
                    index = index
                });
        }
        return binding;
    }

    private MaterialBinding CreateMaterialBinding(
        DNAEffect_RuntimeMaterialProperty effect,
        SkinnedMeshRenderer[] renderers)
    {
        MaterialBinding binding = new MaterialBinding
        {
            propertyId = Shader.PropertyToID(effect.propertyName)
        };
        if (string.IsNullOrEmpty(effect.propertyName)) return binding;

        for (int rendererIndex = 0;
             rendererIndex < renderers.Length;
             rendererIndex++)
        {
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            if (renderer == null ||
                (!string.IsNullOrEmpty(effect.rendererName) &&
                 !string.Equals(renderer.gameObject.name,
                     effect.rendererName,
                     StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _materialScratch.Clear();
            renderer.GetSharedMaterials(_materialScratch);
            int first = effect.materialIndex >= 0
                ? effect.materialIndex : 0;
            int last = effect.materialIndex >= 0
                ? effect.materialIndex : _materialScratch.Count - 1;
            for (int materialIndex = first;
                 materialIndex <= last;
                 materialIndex++)
            {
                if (materialIndex < 0 ||
                    materialIndex >= _materialScratch.Count ||
                    !effect.MatchesSharedColor(_umaData, renderer,
                        materialIndex))
                {
                    continue;
                }
                binding.targets.Add(new MaterialTarget
                {
                    renderer = renderer,
                    materialIndex = materialIndex,
                    propertyBlock = new MaterialPropertyBlock()
                });
            }
        }
        return binding;
    }

    private void ApplyCachedBlendShape(
        DNAEffect_BlendShape effect,
        float value)
    {
        if (!_blendShapeBindings.TryGetValue(effect,
            out BlendShapeBinding binding)) return;
        float weight = effect.GetMappedValue(value) * 100f;
        for (int i = 0; i < binding.targets.Count; i++)
        {
            BlendShapeTarget target = binding.targets[i];
            if (target.renderer != null &&
                target.renderer.sharedMesh != null &&
                target.index < target.renderer.sharedMesh.blendShapeCount)
                target.renderer.SetBlendShapeWeight(target.index, weight);
        }
    }

    private void ApplyCachedMaterialProperty(
        DNAEffect_RuntimeMaterialProperty effect,
        float value)
    {
        if (!_materialBindings.TryGetValue(effect,
            out MaterialBinding binding)) return;
        float mapped = effect.GetMappedValue(value);
        float floatValue = Mathf.LerpUnclamped(effect.zeroFloatValue,
            effect.oneFloatValue, mapped);
        Color colorValue = Color.LerpUnclamped(effect.zeroColorValue,
            effect.oneColorValue, mapped);
        Vector4 vectorValue = Vector4.LerpUnclamped(effect.zeroVectorValue,
            effect.oneVectorValue, mapped);
        Texture textureValue = mapped < 0.5f
            ? effect.zeroTextureValue : effect.oneTextureValue;

        for (int i = 0; i < binding.targets.Count; i++)
        {
            MaterialTarget target = binding.targets[i];
            if (target.renderer == null) continue;
            target.renderer.GetPropertyBlock(target.propertyBlock,
                target.materialIndex);
            if ((effect.parameterType &
                 DNAEffect_RuntimeMaterialProperty.ParameterType.Float) != 0)
                target.propertyBlock.SetFloat(binding.propertyId, floatValue);
            if ((effect.parameterType &
                 DNAEffect_RuntimeMaterialProperty.ParameterType.Color) != 0)
                target.propertyBlock.SetColor(binding.propertyId, colorValue);
            if ((effect.parameterType &
                 DNAEffect_RuntimeMaterialProperty.ParameterType.Vector) != 0)
                target.propertyBlock.SetVector(binding.propertyId, vectorValue);
            if ((effect.parameterType &
                 DNAEffect_RuntimeMaterialProperty.ParameterType.Texture) != 0)
                target.propertyBlock.SetTexture(binding.propertyId,
                    textureValue);
            target.renderer.SetPropertyBlock(target.propertyBlock,
                target.materialIndex);
        }
    }

    private void CacheHumanoidJoints()
    {
        if (_animator == null || !_animator.isHuman) return;
        AddHumanJoint(HumanBodyBones.Head, ExpressionJoint.Head);
        AddHumanJoint(HumanBodyBones.Neck, ExpressionJoint.Neck);
        AddHumanJoint(HumanBodyBones.Jaw, ExpressionJoint.Jaw);
        AddHumanJoint(HumanBodyBones.LeftEye, ExpressionJoint.Eyes);
        AddHumanJoint(HumanBodyBones.RightEye, ExpressionJoint.Eyes);
        AddHumanJoint(HumanBodyBones.LeftHand, ExpressionJoint.Hands);
        AddHumanJoint(HumanBodyBones.RightHand, ExpressionJoint.Hands);
        HumanBodyBones[] fingers =
        {
            HumanBodyBones.LeftThumbProximal,
            HumanBodyBones.LeftThumbIntermediate,
            HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal,
            HumanBodyBones.LeftIndexIntermediate,
            HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal,
            HumanBodyBones.LeftMiddleIntermediate,
            HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal,
            HumanBodyBones.LeftRingIntermediate,
            HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal,
            HumanBodyBones.LeftLittleIntermediate,
            HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.RightThumbProximal,
            HumanBodyBones.RightThumbIntermediate,
            HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal,
            HumanBodyBones.RightIndexIntermediate,
            HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal,
            HumanBodyBones.RightMiddleIntermediate,
            HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal,
            HumanBodyBones.RightRingIntermediate,
            HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal,
            HumanBodyBones.RightLittleIntermediate,
            HumanBodyBones.RightLittleDistal
        };
        for (int i = 0; i < fingers.Length; i++)
            AddHumanJoint(fingers[i], ExpressionJoint.Hands);
    }

    private void AddHumanJoint(HumanBodyBones bone, ExpressionJoint joint)
    {
        Transform target = _animator.GetBoneTransform(bone);
        if (target != null)
            _boneJoints[UMAUtils.StringToHash(target.name)] = joint;
    }

    private void CacheGenericJoints()
    {
        if (genericBoneJoints == null) return;
        for (int i = 0; i < genericBoneJoints.Count; i++)
        {
            ExpressionBoneJoint item = genericBoneJoints[i];
            if (item != null && !string.IsNullOrWhiteSpace(item.boneName))
                _boneJoints[UMAUtils.StringToHash(item.boneName)] =
                    item.joint;
        }
    }

    private bool ShouldApplyBone(int hash)
    {
        if (!_boneJoints.TryGetValue(hash, out ExpressionJoint joints))
            return true;
        if ((joints & ExpressionJoint.Eyes) != 0 &&
            !overrideMecanimEyes) return false;
        if ((joints & ExpressionJoint.Jaw) != 0 &&
            !overrideMecanimJaw) return false;
        if ((joints & ExpressionJoint.Neck) != 0 &&
            !overrideMecanimNeck) return false;
        if ((joints & ExpressionJoint.Head) != 0 &&
            !overrideMecanimHead) return false;
        if ((joints & ExpressionJoint.Hands) != 0 &&
            !overrideMecanimHands) return false;
        return true;
    }

    private UMAExpressionGroup ResolveGroup()
    {
        if (expressionGroupOverride != null) return expressionGroupOverride;
        RaceData race = ResolveRace();
        return race != null ? race.expressionGroup : null;
    }

    private RaceData ResolveRace()
    {
        RaceData race = null;
        if (_avatar != null && _avatar.activeRace != null)
            race = _avatar.activeRace.racedata;
        if (race == null && _umaData != null && _umaData.umaRecipe != null)
            race = _umaData.umaRecipe.raceData;
        if (race == null && _avatar != null &&
            _avatar.activeRace != null &&
            !string.IsNullOrWhiteSpace(_avatar.activeRace.name))
        {
            // RaceSetter.racedata is only a fast, nonserialized cache. It is
            // normally empty after a domain reload and before the first avatar
            // build. The data property performs the indexed lookup used by DCA.
            race = _avatar.activeRace.data;
        }
        return race;
    }

    private UMAExpressionSet ResolveLegacyExpressionSet()
    {
        if (expressionGroupOverride != null) return null;
        RaceData race = ResolveRace();
        return race != null && race.expressionGroup == null
            ? race.expressionSet : null;
    }

    private void BindGroup(UMAExpressionGroup group)
    {
        Dictionary<string, SourceSnapshot> retained = CaptureSources();
        ClearLegacyRuntimeObjects();
        _runtimeExpressions.Clear();
        _expressionLookup.Clear();
        _resolvedGroup = group;
        _resolvedLegacyExpressionSet =
            group == null ? ResolveLegacyExpressionSet() : null;
        if (group != null && group.expressions != null)
            for (int i = 0; i < group.expressions.Count; i++)
                AddDefinition(group.expressions[i]);
        else if (_resolvedLegacyExpressionSet != null)
            AddLegacyExpressionSetDefinitions(
                _resolvedLegacyExpressionSet);
        else AddLegacyDefinitions();
        _runtimeExpressions.Sort(CompareExpressions);
        RebuildLookup();
        RestoreSources(retained);
        ResolveRoleIndices();
        _bindingsValid = false;
        _immediateDirty = true;
        GroupRebound?.Invoke();
        GroupReboundAction?.Invoke(_resolvedGroup);
    }

    private void AddLegacyExpressionSetDefinitions(
        UMAExpressionSet expressionSet)
    {
        for (int i = 0; i < ExpressionPlayer.PoseCount; i++)
        {
            string id = ExpressionPlayer.PoseNames[i];
            DNA dna = ScriptableObject.CreateInstance<DNA>();
            dna.hideFlags = HideFlags.DontSave;
            dna.name = id + "_RuntimeExpressionDNA";
            dna.displayName = id.Replace('_', ' ');
            dna.description = "Transient compatibility DNA generated from " +
                expressionSet.name + ".";
            dna.defaultValue = 0.5f;
            _legacyRuntimeObjects.Add(dna);

            UMAExpressionSet.PosePair pair =
                expressionSet.posePairs != null &&
                i < expressionSet.posePairs.Length
                    ? expressionSet.posePairs[i] : null;
            if (pair != null)
            {
                if (pair.primary != null)
                    dna.effects.Add(CreateLegacyPoseEffect(
                        pair.primary, false));
                if (pair.inverse != null)
                    dna.effects.Add(CreateLegacyPoseEffect(
                        pair.inverse, true));
            }

            AddDefinition(new UMAExpressionDefinition
            {
                id = id,
                displayName = id.Replace('_', ' '),
                dna = dna,
                roles = GetLegacyRoles(i),
                affectedJoints = GetLegacyJoints(i),
                priority = i,
                blendMode = ExpressionBlendMode.Override,
                blinkClosedValue = 0f
            });
        }
    }

    private static DNAEffect_BonePose CreateLegacyPoseEffect(
        UMABonePose pose,
        bool inverse)
    {
        return new DNAEffect_BonePose
        {
            EffectName = inverse ? "Inverse Pose" : "Primary Pose",
            bonePose = pose,
            minMapping = 0f,
            maxMapping = 1f,
            curve = inverse
                ? new AnimationCurve(
                    new Keyframe(0f, 1f, -2f, -2f),
                    new Keyframe(0.5f, 0f, -2f, 0f),
                    new Keyframe(1f, 0f, 0f, 0f))
                : new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0f),
                    new Keyframe(0.5f, 0f, 0f, 2f),
                    new Keyframe(1f, 1f, 2f, 2f))
        };
    }

    private static ExpressionRole GetLegacyRoles(int index)
    {
        switch (index)
        {
            case 26: return ExpressionRole.BlinkLeft;
            case 27: return ExpressionRole.BlinkRight;
            case 28: return ExpressionRole.EyeVerticalLeft;
            case 29: return ExpressionRole.EyeVerticalRight;
            case 30: return ExpressionRole.EyeHorizontalLeft;
            case 31: return ExpressionRole.EyeHorizontalRight;
        }
        if (index >= 44) return ExpressionRole.Emotion;
        if (index >= 6 && index <= 35) return ExpressionRole.Viseme;
        return ExpressionRole.Custom;
    }

    private static ExpressionJoint GetLegacyJoints(int index)
    {
        ExpressionPlayer.MecanimJoint legacy =
            index >= 0 &&
            index < ExpressionPlayer.MecanimAlternate.Length
                ? ExpressionPlayer.MecanimAlternate[index]
                : ExpressionPlayer.MecanimJoint.None;
        ExpressionJoint result = ExpressionJoint.None;
        if ((legacy & ExpressionPlayer.MecanimJoint.Head) != 0)
            result |= ExpressionJoint.Head;
        if ((legacy & ExpressionPlayer.MecanimJoint.Neck) != 0)
            result |= ExpressionJoint.Neck;
        if ((legacy & ExpressionPlayer.MecanimJoint.Jaw) != 0)
            result |= ExpressionJoint.Jaw;
        if ((legacy & ExpressionPlayer.MecanimJoint.Eye) != 0)
            result |= ExpressionJoint.Eyes;
        if ((legacy & ExpressionPlayer.MecanimJoint.Hands) != 0)
            result |= ExpressionJoint.Hands;
        return result == ExpressionJoint.None
            ? ExpressionJoint.Other : result;
    }

    private void ClearLegacyRuntimeObjects()
    {
        for (int i = 0; i < _legacyRuntimeObjects.Count; i++)
        {
            UnityEngine.Object item = _legacyRuntimeObjects[i];
            if (item == null) continue;
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }
        _legacyRuntimeObjects.Clear();
        _resolvedLegacyExpressionSet = null;
    }

    private void AddDefinition(UMAExpressionDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.id) ||
            definition.dna == null ||
            _expressionLookup.ContainsKey(definition.id)) return;
        RuntimeExpression runtime = new RuntimeExpression
        {
            definition = definition,
            target = definition.DefaultValue,
            effective = definition.DefaultValue,
            lastSource = ExpressionSource.Manual
        };
        if (definition.dna.effects != null)
            for (int i = 0; i < definition.dna.effects.Count; i++)
            {
                DNAEffect effect = definition.dna.effects[i];
                if (effect == null || !effect.enabled) continue;
                runtime.phases |= effect.ExpressionPhases;
                if (effect.RequiresExpressionBuild)
                    runtime.buildType |= effect.AreaEffect;
            }
        _expressionLookup.Add(definition.id, _runtimeExpressions.Count);
        _runtimeExpressions.Add(runtime);
    }

    private void AddLegacyDefinitions()
    {
        if (Expressions == null) return;
        for (int i = 0; i < Expressions.Count; i++)
        {
            DynamicExpression legacy = Expressions[i];
            if (legacy == null || legacy.ExpressionDNA == null) continue;
            string id = !string.IsNullOrWhiteSpace(legacy.Name)
                ? legacy.Name : legacy.ExpressionDNA.displayName;
            UMAExpressionDefinition definition = new UMAExpressionDefinition
            {
                id = id,
                displayName = id,
                dna = legacy.ExpressionDNA,
                affectedJoints = ExpressionJoint.Other
            };
            AddDefinition(definition);
            if (legacy.ExpressionValue != null &&
                _expressionLookup.TryGetValue(id, out int index))
            {
                RuntimeExpression runtime = _runtimeExpressions[index];
                runtime.sourceValues[(int)ExpressionSource.Manual] =
                    Mathf.Clamp01(legacy.ExpressionValue.Value);
                runtime.sourceActive[(int)ExpressionSource.Manual] = true;
            }
        }
    }

    private static int CompareExpressions(RuntimeExpression left,
        RuntimeExpression right)
    {
        int priority = left.definition.priority.CompareTo(
            right.definition.priority);
        return priority != 0 ? priority : string.Compare(left.definition.id,
            right.definition.id, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildLookup()
    {
        _expressionLookup.Clear();
        for (int i = 0; i < _runtimeExpressions.Count; i++)
        {
            string id = _runtimeExpressions[i].definition.id;
            if (!_expressionLookup.ContainsKey(id))
                _expressionLookup.Add(id, i);
        }
    }

    private void ResolveRoleIndices()
    {
        _blinkLeftIndex = FindRole(ExpressionRole.BlinkLeft);
        _blinkRightIndex = FindRole(ExpressionRole.BlinkRight);
        _eyeHorizontalIndex = FindRole(ExpressionRole.EyeHorizontal);
        _eyeVerticalIndex = FindRole(ExpressionRole.EyeVertical);
        _eyeHorizontalLeftIndex =
            FindRole(ExpressionRole.EyeHorizontalLeft);
        _eyeHorizontalRightIndex =
            FindRole(ExpressionRole.EyeHorizontalRight);
        _eyeVerticalLeftIndex = FindRole(ExpressionRole.EyeVerticalLeft);
        _eyeVerticalRightIndex =
            FindRole(ExpressionRole.EyeVerticalRight);
    }

    private int FindRole(ExpressionRole role)
    {
        for (int i = 0; i < _runtimeExpressions.Count; i++)
            if ((_runtimeExpressions[i].definition.roles & role) != 0)
                return i;
        return -1;
    }

    private void UpdateBlinking()
    {
        if (!EnableBlinking ||
            (_blinkLeftIndex < 0 && _blinkRightIndex < 0))
        {
            if (_isBlinking) EndBlink();
            return;
        }
        if (!_isBlinking && Time.time >= _nextBlinkTime)
        {
            _isBlinking = true;
            _blinkStartTime = Time.time;
        }
        if (!_isBlinking) return;
        float progress = (Time.time - _blinkStartTime) / BlinkDuration;
        if (progress >= 1f)
        {
            EndBlink();
            return;
        }
        float amount = BlinkCurve != null
            ? Mathf.Clamp01(BlinkCurve.Evaluate(progress))
            : Mathf.Sin(progress * Mathf.PI);
        SetProceduralBlinkAmount(amount);
    }

    private void EndBlink()
    {
        _isBlinking = false;
        ResetRoleSource(_blinkLeftIndex, ExpressionSource.ProceduralBlink);
        if (_blinkRightIndex != _blinkLeftIndex)
            ResetRoleSource(_blinkRightIndex,
                ExpressionSource.ProceduralBlink);
        ScheduleNextBlink();
    }

    private void ScheduleNextBlink() =>
        _nextBlinkTime = Time.time + UnityEngine.Random.Range(
            Mathf.Min(BlinkIntervalMin, BlinkIntervalMax),
            Mathf.Max(BlinkIntervalMin, BlinkIntervalMax));

    private void UpdateSaccadesAndGaze()
    {
        UpdateSaccade();
        _ikActive = false;
        _ikEyesWeight = 0f;
        Vector2 gaze = Vector2.zero;
        Transform origin = GetGazeOrigin();
        Vector3 animatorDirection = origin.forward;
        float animatorDistance = 10f;
        bool validGazeTarget = false;
        if (EnableLookAt && LookAtTarget != null)
        {
            Vector3 toTarget = LookAtTarget.position - origin.position;
            float distance = toTarget.magnitude;
            if (distance >= LookAtMinDistance &&
                distance <= LookAtMaxDistance &&
                Vector3.Dot(origin.forward, toTarget) > 0f)
            {
                Vector3 local = origin.InverseTransformDirection(
                    toTarget.normalized);
                float horizontal = Mathf.Atan2(local.x, local.z) *
                    Mathf.Rad2Deg;
                float vertical = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) *
                    Mathf.Rad2Deg;
                gaze.x = Mathf.Clamp(horizontal /
                    Mathf.Max(0.01f, EyeMaxAngle), -1f, 1f);
                gaze.y = Mathf.Clamp(vertical /
                    Mathf.Max(0.01f, ClampVerticalAngle), -1f, 1f);
                float angle = Vector3.Angle(origin.forward, toTarget);
                float assist = Mathf.InverseLerp(HeadAssistStartAngle,
                    Mathf.Max(HeadAssistStartAngle + 0.01f,
                        HeadAssistFullAngle), angle);
                _ikLookPosition = LookAtTarget.position;
                _ikHeadWeight = HeadWeight * assist;
                _ikBodyWeight = BodyWeight * assist;
                animatorDirection = toTarget.normalized;
                animatorDistance = distance;
                validGazeTarget = true;
            }
        }

        Vector2 combined = Vector2.ClampMagnitude(gaze + _saccade, 1f);
        bool hasDNAEyeDirection =
            _eyeHorizontalIndex >= 0 || _eyeVerticalIndex >= 0 ||
            _eyeHorizontalLeftIndex >= 0 ||
            _eyeHorizontalRightIndex >= 0 ||
            _eyeVerticalLeftIndex >= 0 ||
            _eyeVerticalRightIndex >= 0;
        if (hasDNAEyeDirection)
        {
            SetProceduralGazeDirection(combined);
        }

        if (_animator == null || !_animator.isHuman) return;
        if (validGazeTarget)
        {
            _ikActive = true;
            if (hasDNAEyeDirection)
            {
                // DNA owns the eyes; Animator IK remains head/body assistance.
                _ikEyesWeight = 0f;
            }
            else
            {
                SetAnimatorSaccadeTarget(origin, animatorDirection,
                    animatorDistance);
                _ikEyesWeight = EyesWeight;
            }
        }
        else if (!hasDNAEyeDirection && EnableSaccades)
        {
            // A humanoid without DNA eye roles still receives autonomous eye
            // movement, even when no explicit look-at target is assigned.
            _ikHeadWeight = 0f;
            _ikBodyWeight = 0f;
            SetAnimatorSaccadeTarget(origin, origin.forward, 10f);
            _ikEyesWeight = EyesWeight;
            _ikActive = true;
        }
    }

    private void SetAnimatorSaccadeTarget(
        Transform origin,
        Vector3 worldDirection,
        float distance)
    {
        Vector3 localDirection =
            origin.InverseTransformDirection(worldDirection);
        Quaternion offset = Quaternion.Euler(
            _saccade.y * SaccadeMaxOffsetDeg,
            _saccade.x * SaccadeMaxOffsetDeg,
            0f);
        Vector3 adjusted = origin.TransformDirection(
            offset * localDirection).normalized;
        _ikLookPosition = origin.position +
            adjusted * Mathf.Max(0.5f, distance);
    }

    private float GetBlinkValue(int index, float amount)
    {
        if (index < 0 || index >= _runtimeExpressions.Count)
            return 0f;
        UMAExpressionDefinition definition =
            _runtimeExpressions[index].definition;
        return Mathf.Lerp(definition.DefaultValue,
            definition.blinkClosedValue, amount);
    }

    /// <summary>
    /// Writes a normalized blink amount through the role-based procedural
    /// source. Zero releases the procedural source; one is fully closed.
    /// </summary>
    public void SetProceduralBlinkAmount(float amount)
    {
        Initialize();
        amount = Mathf.Clamp01(amount);
        BeginExpressionBatch();
        try
        {
            if (amount <= 0f)
            {
                ResetRoleSource(_blinkLeftIndex,
                    ExpressionSource.ProceduralBlink);
                if (_blinkRightIndex != _blinkLeftIndex)
                    ResetRoleSource(_blinkRightIndex,
                        ExpressionSource.ProceduralBlink);
                return;
            }
            SetProceduralRoleValue(_blinkLeftIndex,
                GetBlinkValue(_blinkLeftIndex, amount),
                ExpressionSource.ProceduralBlink);
            if (_blinkRightIndex != _blinkLeftIndex)
                SetProceduralRoleValue(_blinkRightIndex,
                    GetBlinkValue(_blinkRightIndex, amount),
                    ExpressionSource.ProceduralBlink);
        }
        finally
        {
            EndExpressionBatch();
        }
    }

    private void SetEyeDirection(int sharedIndex, int leftIndex,
        int rightIndex, float normalizedDirection)
    {
        if (sharedIndex >= 0)
        {
            SetDirectionalRoleValue(sharedIndex, normalizedDirection);
            return;
        }
        SetDirectionalRoleValue(leftIndex, normalizedDirection);
        if (rightIndex != leftIndex)
            SetDirectionalRoleValue(rightIndex, normalizedDirection);
    }

    private void SetDirectionalRoleValue(
        int index,
        float normalizedDirection)
    {
        if (index < 0 || index >= _runtimeExpressions.Count) return;
        float neutral = _runtimeExpressions[index].definition.DefaultValue;
        float value = normalizedDirection < 0f
            ? Mathf.Lerp(neutral, 0f, -normalizedDirection)
            : Mathf.Lerp(neutral, 1f, normalizedDirection);
        SetProceduralRoleValue(index, value,
            ExpressionSource.ProceduralGaze);
    }

    /// <summary>
    /// Writes normalized horizontal and vertical eye directions through
    /// shared or sided expression roles.
    /// </summary>
    public void SetProceduralGazeDirection(Vector2 direction)
    {
        Initialize();
        direction.x = Mathf.Clamp(direction.x, -1f, 1f);
        direction.y = Mathf.Clamp(direction.y, -1f, 1f);
        BeginExpressionBatch();
        try
        {
            SetEyeDirection(_eyeHorizontalIndex,
                _eyeHorizontalLeftIndex, _eyeHorizontalRightIndex,
                direction.x);
            SetEyeDirection(_eyeVerticalIndex,
                _eyeVerticalLeftIndex, _eyeVerticalRightIndex,
                direction.y);
        }
        finally
        {
            EndExpressionBatch();
        }
    }

    private Transform GetGazeOrigin()
    {
        if (_animator != null && _animator.isHuman)
        {
            Transform head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head;
        }
        return transform;
    }

    private void UpdateSaccade()
    {
        if (!EnableSaccades)
        {
            _saccade = Vector2.zero;
            return;
        }
        if (Time.time >= _nextSaccadeTime)
        {
            _saccadeFrom = _saccade;
            float scale = Mathf.Clamp01(SaccadeMaxOffsetDeg /
                Mathf.Max(0.01f, EyeMaxAngle));
            _saccadeTo = new Vector2(
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f) *
                SaccadeVerticalBias) * scale;
            _saccadeStartTime = Time.time;
            _saccadeDuration = UnityEngine.Random.Range(0.025f, 0.08f);
            ScheduleNextSaccade();
        }
        float p = Mathf.Clamp01((Time.time - _saccadeStartTime) /
            Mathf.Max(0.001f, _saccadeDuration));
        _saccade = Vector2.Lerp(_saccadeFrom, _saccadeTo,
            p * p * (3f - 2f * p));
    }

    private void ScheduleNextSaccade() =>
        _nextSaccadeTime = Time.time + UnityEngine.Random.Range(
            Mathf.Min(SaccadeIntervalMin, SaccadeIntervalMax),
            Mathf.Max(SaccadeIntervalMin, SaccadeIntervalMax));

    private void SetProceduralRoleValue(int index, float value,
        ExpressionSource source)
    {
        if (index >= 0) SetExpression(index, value, source);
    }

    private void ResetRoleSource(int index, ExpressionSource source)
    {
        if (index >= 0) ResetExpression(index, source);
    }

    private void ResetProceduralSources()
    {
        BeginExpressionBatch();
        ResetAllExpressions(ExpressionSource.ProceduralBlink);
        ResetAllExpressions(ExpressionSource.ProceduralGaze);
        EndExpressionBatch();
    }

    private void QueueBuild(DNAInstanceCollection.DNABuildType buildType)
    {
        _pendingBuildType |= buildType;
        _buildRequestTime = Time.unscaledTime;
        if (_isBuilding) _buildChangedWhileBuilding = true;
        else _buildPending = true;
    }

    private void ProcessPendingBuild()
    {
        if (!_buildPending || _isBuilding || _avatar == null ||
            !_avatar.BuildCharacterEnabled) return;
        if (Time.unscaledTime - _buildRequestTime <
            buildDebounceSeconds) return;
        bool mesh = (_pendingBuildType &
            (DNAInstanceCollection.DNABuildType.Mesh |
             DNAInstanceCollection.DNABuildType.MeshModifiers)) != 0;
        if (mesh && Time.unscaledTime - _lastMeshBuildTime <
            meshBuildMinimumInterval) return;
        bool texture = (_pendingBuildType &
            (DNAInstanceCollection.DNABuildType.Texture |
             DNAInstanceCollection.DNABuildType.SharedColors)) != 0;
        if (mesh) _lastMeshBuildTime = Time.unscaledTime;
        _buildPending = false;
        CaptureBuildSnapshot();
        using (BuildRequestMarker.Auto())
            _avatar.ForceUpdate(true, texture, mesh);
    }

    private void CaptureBuildSnapshot()
    {
        _buildSnapshot.Clear();
        for (int i = 0; i < _runtimeExpressions.Count; i++)
        {
            RuntimeExpression expression = _runtimeExpressions[i];
            if (expression.definition.dna != null &&
                HasBuildPhase(expression.phases))
                _buildSnapshot.Add(new BuildValue
                {
                    dna = expression.definition.dna,
                    value = expression.effective
                });
        }
    }

    private void Subscribe()
    {
        if (_subscribed || _umaData == null) return;
        _umaData.RegisterRuntimeDNAProvider(this);
        _umaData.OnCharacterBegun += OnCharacterBegun;
        _umaData.OnCharacterUpdated += OnCharacterUpdated;
        if (_avatar != null)
            _avatar.BuildCharacterBegun.AddListener(OnBuildCharacterBegun);
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (_umaData != null)
        {
            _umaData.UnregisterRuntimeDNAProvider(this);
            _umaData.OnCharacterBegun -= OnCharacterBegun;
            _umaData.OnCharacterUpdated -= OnCharacterUpdated;
        }
        if (_avatar != null)
            _avatar.BuildCharacterBegun.RemoveListener(OnBuildCharacterBegun);
        _subscribed = false;
    }

    private void OnBuildCharacterBegun(UMAData data) => BeginBuild(data);
    private void OnCharacterBegun(UMAData data) => BeginBuild(data);

    private void BeginBuild(UMAData data)
    {
        _umaData = data != null ? data : _umaData;
        if (_isBuilding)
        {
            return;
        }
        _isBuilding = true;
        _buildChangedWhileBuilding = false;
        _bindingsValid = false;
        RestoreControlledBones();
        CaptureBuildSnapshot();
        _pendingBuildType = DNAInstanceCollection.DNABuildType.None;
    }

    private void OnCharacterUpdated(UMAData data)
    {
        _umaData = data != null ? data : _umaData;
        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        Dictionary<string, SourceSnapshot> retained = CaptureSources();
        UMAExpressionGroup group = ResolveGroup();
        UMAExpressionSet legacy =
            group == null ? ResolveLegacyExpressionSet() : null;
        if (group != _resolvedGroup ||
            legacy != _resolvedLegacyExpressionSet)
        {
            BindGroup(group);
            RestoreSources(retained);
        }
        _isBuilding = false;
        _bindingsValid = false;
        RefreshBindings();
        EvaluateValues(0f, true);
        _immediateDirty = true;
        ApplyImmediateEffects();
        if (_buildChangedWhileBuilding)
        {
            _buildPending = true;
            _buildRequestTime = Time.unscaledTime;
        }
        else
        {
            _buildPending = false;
            _pendingBuildType = DNAInstanceCollection.DNABuildType.None;
        }
    }

    public DNAInstanceCollection.DNABuildType AfterRecipeGenerated(
        DynamicCharacterAvatar avatar) => ApplyBuildPhase(
            avatar != null ? avatar : _umaData,
            ExpressionEffectPhase.BuildAfterRecipe);
    public DNAInstanceCollection.DNABuildType PreApply(UMAData data) =>
        ApplyBuildPhase(data, ExpressionEffectPhase.BuildPreApply);
    public DNAInstanceCollection.DNABuildType Apply(UMAData data) =>
        ApplyBuildPhase(data, ExpressionEffectPhase.BuildApply);
    public DNAInstanceCollection.DNABuildType PostApply(UMAData data) =>
        ApplyBuildPhase(data, ExpressionEffectPhase.BuildPostApply);

    private DNAInstanceCollection.DNABuildType ApplyBuildPhase(UMAData data,
        ExpressionEffectPhase phase)
    {
        DNAInstanceCollection.DNABuildType flags =
            DNAInstanceCollection.DNABuildType.None;
        if (data == null) return flags;
        if (_buildSnapshot.Count == 0) CaptureBuildSnapshot();
        for (int i = 0; i < _buildSnapshot.Count; i++)
        {
            BuildValue build = _buildSnapshot[i];
            if (build.dna == null || build.dna.effects == null) continue;
            for (int e = 0; e < build.dna.effects.Count; e++)
            {
                DNAEffect effect = build.dna.effects[e];
                if (effect == null || !effect.enabled ||
                    (effect.ExpressionPhases & phase) == 0) continue;
                flags |= effect.AreaEffect;
                switch (phase)
                {
                    case ExpressionEffectPhase.BuildAfterRecipe:
                        effect.AfterRecipeGenerated(data, build.dna,
                            build.value);
                        break;
                    case ExpressionEffectPhase.BuildPreApply:
                        effect.PreApply(data, build.dna, build.value);
                        break;
                    case ExpressionEffectPhase.BuildApply:
                        effect.Apply(data, build.dna, build.value);
                        break;
                    case ExpressionEffectPhase.BuildPostApply:
                        effect.PostApply(data, build.dna, build.value);
                        break;
                }
            }
        }
        return flags;
    }

    private static bool HasBuildPhase(ExpressionEffectPhase phases)
    {
        const ExpressionEffectPhase build =
            ExpressionEffectPhase.BuildAfterRecipe |
            ExpressionEffectPhase.BuildPreApply |
            ExpressionEffectPhase.BuildApply |
            ExpressionEffectPhase.BuildPostApply;
        return (phases & build) != 0;
    }

    private static bool IsValidSource(ExpressionSource source) =>
        (int)source >= 0 && (int)source < SourceCount;

    private Dictionary<string, SourceSnapshot> CaptureSources()
    {
        Dictionary<string, SourceSnapshot> result =
            new Dictionary<string, SourceSnapshot>(
                StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _runtimeExpressions.Count; i++)
        {
            RuntimeExpression runtime = _runtimeExpressions[i];
            SourceSnapshot snapshot = new SourceSnapshot();
            Array.Copy(runtime.sourceValues, snapshot.values, SourceCount);
            Array.Copy(runtime.sourceActive, snapshot.active, SourceCount);
            result[runtime.definition.id] = snapshot;
        }
        return result;
    }

    private void RestoreSources(
        Dictionary<string, SourceSnapshot> snapshots)
    {
        if (snapshots == null) return;
        for (int i = 0; i < _runtimeExpressions.Count; i++)
        {
            RuntimeExpression runtime = _runtimeExpressions[i];
            if (!snapshots.TryGetValue(runtime.definition.id,
                out SourceSnapshot snapshot)) continue;
            Array.Copy(snapshot.values, runtime.sourceValues, SourceCount);
            Array.Copy(snapshot.active, runtime.sourceActive, SourceCount);
            runtime.target = ResolveSources(runtime);
            runtime.effective = runtime.target;
        }
    }

    public float GetBlinkAmount()
    {
        if (!_isBlinking) return 0f;
        float p = Mathf.Clamp01(
            (Time.time - _blinkStartTime) / BlinkDuration);
        return BlinkCurve != null ? Mathf.Clamp01(BlinkCurve.Evaluate(p))
            : Mathf.Sin(p * Mathf.PI);
    }

    public Vector2 GetSaccadeOffset() => _saccade;

#if UNITY_EDITOR
    public void EditorSimulateOnce()
    {
        if (Application.isPlaying) return;
        EvaluateExpressionsNow();
        RestoreControlledBones();
        ApplyRigEffects();
        UnityEditor.SceneView.RepaintAll();
    }

    private void OnDrawGizmos()
    {
        if (!EnableLookAt || LookAtTarget == null) return;
        Gizmos.color = _ikActive ? Color.green : Color.yellow;
        Gizmos.DrawLine(transform.position, LookAtTarget.position);
    }
#endif
}
