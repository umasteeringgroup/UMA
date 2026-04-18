using UMA;
using UnityEngine;
using System.Collections.Generic;
using UMA.CharacterSystem;

public class DynamicExpressionPlayer : MonoBehaviour
{
    [Header("Eye Movement Settings")]
    public bool EnableSaccades = true;
    public float SaccadeIntervalMin = 0.5f;
    public float SaccadeIntervalMax = 1.5f;
    public float SaccadeMaxOffsetDeg = 6f;
    public float SaccadeVerticalBias = 0.35f;

    [Header("Blink Settings")]
    public bool EnableBlinking = true;
    public float BlinkIntervalMin = 3.0f;
    public float BlinkIntervalMax = 7.0f;
    public float BlinkDuration = 0.15f;
    public AnimationCurve BlinkCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.05f, 1f),
        new Keyframe(0.35f, 0f),
        new Keyframe(1f, 0f)
    );

    [Header("LookAt Settings")]
    public bool EnableLookAt = true;
    public Transform LookAtTarget;
    public float LookAtMaxDistance = 25f;
    public float LookAtMinDistance = 0.5f;
    public float EyeMaxAngle = 30f;
    public float HeadAssistStartAngle = 15f;
    public float HeadAssistFullAngle = 35f;
    public float GazeWeight = 0.75f;
    public float HeadWeight = 0.5f;
    public float BodyWeight = 0.1f;
    public float EyesWeight = 1.0f;
    public float ClampVerticalAngle = 20f;

    [Header("Processing")]
    public float processDistance = 30f;

    [Header("Expressions")]
    public List<DynamicExpression> Expressions = new List<DynamicExpression>();

    // Optional expression name mapping for blink influence
    private const string LeftEyeOpenExpr = "leftEyeOpen_Close";
    private const string RightEyeOpenExpr = "rightEyeOpen_Close";

    // Internal state
    private Animator _animator;
    private Camera _mainCam;
    private float _nextBlinkTime;
    private float _blinkStartTime;
    private bool _isBlinking;
    private float _nextSaccadeTime;
    private float _saccadeStartTime;
    private float _saccadeDuration;
    private Vector2 _eyeJitter;        // current jitter (normalized -1..1 range)
    private Vector2 _eyeJitterTarget;  // target jitter for current saccade
    private Vector2 _eyeJitterPrev;    // previous jitter start
    private bool _initialized;
    private Transform _headTransform;  // cached head (if available)
    private bool _hasHead;
    private Transform _leftEyeTransform;
    private Transform _rightEyeTransform;
    private bool _hasEyes;
    private bool _lookAtEngaged; // true when current frame LookAt passes gating
    private DynamicCharacterAvatar _dca;
    private UMAData _umaData;
    private readonly Dictionary<string, float> _lastValues = new Dictionary<string, float>(32);

    // One-time log flags
    private bool _logInitMissing;
    private bool _logAnimatorMissing;
    private bool _logDistanceTooFar;
    private bool _logBlinkSkipped;
    private bool _logSaccadeSkipped;
    private bool _logLookAtNoTarget;
    private bool _logLookAtTooNear;
    private bool _logLookAtTooFar;
    private bool _logLookAtBehind;
    private bool _logLookAtNoEyes;

    // Scratch vars (avoid per-frame allocations)
    private Vector3 _lookDir;
    private Vector3 _headForward;
    private Vector3 _toTarget;
    private Vector3 _ikLookPos;
    private float _dynamicHeadWeight;
    private float _dynamicBodyWeight;

    void Awake()
    {
        Initialize();
    }

    void OnEnable()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;
        _mainCam = Camera.main;
        ScheduleNextBlink();
        ScheduleNextSaccade();
        _dca = GetComponent<DynamicCharacterAvatar>();
        if (_dca != null)
        {
            _umaData = _dca.umaData;
        }
        _initialized = true;
    }

    private void CacheTransforms()
    {
        if (_headTransform != null)
        {
            return; // already cached
        }
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>();
        }
        if (_dca == null)
        {
            _dca = GetComponentInChildren<DynamicCharacterAvatar>();
        }
        if (_umaData == null && _dca != null)
        {
            _umaData = _dca.umaData;
        }

        if (_animator != null)
        {
            if (_animator.avatar != null)
            {
                if (_animator.avatar.isHuman)
                {
                    _headTransform = _animator.GetBoneTransform(HumanBodyBones.Head);
                    _hasHead = _headTransform != null;
                    _leftEyeTransform = _animator.GetBoneTransform(HumanBodyBones.LeftEye);
                    _rightEyeTransform = _animator.GetBoneTransform(HumanBodyBones.RightEye);
                    _hasEyes = (_leftEyeTransform != null && _rightEyeTransform != null);
                }
            }
        }
    }

    void Update()
    {
        DoUpdate();
    }

    void LateUpdate()
    {
        DoLateUpdate();
    }

    private bool ShouldProcess()
    {
        // Evaluate and log reasons once
        if (!_initialized)
        {
            LogOnce(ref _logInitMissing, "[DynamicExpressionPlayer] Skipping processing: not initialized.");
            return false;
        }
        if (_animator == null)
        {
            LogOnce(ref _logAnimatorMissing, "[DynamicExpressionPlayer] Skipping processing: Animator not found.");
            return false;
        }
        if (_mainCam != null)
        {
            float sq = (_mainCam.transform.position - transform.position).sqrMagnitude;
            if (sq > processDistance * processDistance)
            {
                LogOnce(ref _logDistanceTooFar, "[DynamicExpressionPlayer] Skipping processing: beyond processDistance.");
                return false;
            }
        }
        if (_headTransform == null)
        {
            return false;
        }
        return true;
    }

    private void DoUpdate()
    {
        CacheTransforms();
        bool canProcess = ShouldProcess();
        if (!canProcess)
        {
            // Log feature-specific skips once if enabled
            if (EnableBlinking) LogOnce(ref _logBlinkSkipped, "[DynamicExpressionPlayer] Blink update skipped: processing disabled.");
            if (EnableSaccades) LogOnce(ref _logSaccadeSkipped, "[DynamicExpressionPlayer] Saccade update skipped: processing disabled.");
            if (EnableLookAt) LogOnce(ref _logLookAtNoTarget, "[DynamicExpressionPlayer] LookAt update skipped: processing disabled.");
            return;
        }

        if (EnableBlinking)
        {
            UpdateBlinking();
        }
        if (EnableSaccades)
        {
            UpdateSaccades();
        }
        if (EnableLookAt)
        {
            UpdateLookAtComputation();
        }

        // Process dynamic expressions and trigger minimal rebuilds
        if (_umaData != null && _dca != null && Expressions != null && Expressions.Count > 0)
        {
            bool textureDirty = false;
            bool meshDirty = false;
            // Apply effects only when value changed
            //bool skeletonEditing = false; // guard Begin/EndSkeletonUpdate
            for (int i = 0; i < Expressions.Count; i++)
            {
                var expr = Expressions[i];
                if (expr == null) { continue; }
                float val = expr.ExpressionValue != null ? expr.ExpressionValue.Value : 0f;
                float prev;
                if (_lastValues.TryGetValue(expr.Name, out prev) && Mathf.Approximately(prev, val))
                {
                    continue; // no change
                }
                _lastValues[expr.Name] = val;

                var dna = expr.ExpressionDNA;
                if (dna == null || dna.effects == null || dna.effects.Count == 0) { continue; }

                // Bones need to be restored to base pose for Expressions, to avoid accumulation without rebuilding character completely.
                dna.Restore(_umaData, val);
                // Invoke DNA effects and aggregate required rebuild via AreaEffect flags
                dna.PreApply(_umaData, val);
                for (int ei = 0; ei < dna.effects.Count; ei++)
                {
                    var effect = dna.effects[ei];
                    if (effect == null) { continue; }
                    // Classify by AreaEffect
                    var area = effect.AreaEffect;
                    // If this is a Rig effect and we haven't prepared the skeleton yet, do so now
                    //if (!skeletonEditing && (area & UMA.DNAInstanceCollection.DNABuildType.Rig) != 0)
                    //{
                        //if (_umaData != null && _umaData.skeleton != null)
                        //{
                            //_umaData.skeleton.RestoreAll();
                            //skeletonEditing = true;
                        //}
                    //}
                    // Apply the effect after any required skeleton preparation
                    effect.Apply(_umaData, dna, val);
                    if ((area & UMA.DNAInstanceCollection.DNABuildType.Texture) != 0)
                    {
                        textureDirty = true;
                    }
                    if ((area & UMA.DNAInstanceCollection.DNABuildType.SharedColors) != 0)
                    {
                        textureDirty = true;
                    }
                    if ((area & UMA.DNAInstanceCollection.DNABuildType.Mesh) != 0)
                    {
                        meshDirty = true;
                    }
                    // Rig and BlendShape are immediate applications (handled by Apply) and do not trigger rebuilds here
                }
                dna.PostApply(_umaData, val);
            }

            // Do not call EndSkeletonUpdate here; avoid updating the saved baseline so poses do not accumulate

            if (textureDirty || meshDirty)
            {
                _dca.ForceUpdate(false, textureDirty, meshDirty);
            }
        }
    }

    private void DoLateUpdate()
    {
        // Reserve for future late expression evaluation (if needed).
    }

    #region Blinking
    private void ScheduleNextBlink()
    {
        _nextBlinkTime = Time.time + Random.Range(BlinkIntervalMin, BlinkIntervalMax);
    }

    private void UpdateBlinking()
    {
        if (_headTransform == null)
        {
            return; // no head, skip
        }
        if (!_isBlinking)
        {
            if (Time.time >= _nextBlinkTime)
            {
                _isBlinking = true;
                _blinkStartTime = Time.time;
            }
        }

        if (_isBlinking)
        {
            float t = (Time.time - _blinkStartTime) / BlinkDuration;
            if (t >= 1f)
            {
                _isBlinking = false;
                ScheduleNextBlink();
                SetBlinkExpression(0f);
            }
            else
            {
                float blinkValue = BlinkCurve.Evaluate(t);
                SetBlinkExpression(-blinkValue);
            }
        }
    }

    private void SetBlinkExpression(float value)
    {
        // If expression list contains eye open/close channels, set them.
        // Otherwise do nothing (internal only).
        for (int i = 0; i < Expressions.Count; i++)
        {
            var expr = Expressions[i];
            if (expr == null) continue;
            if (expr.Name == LeftEyeOpenExpr || expr.Name == RightEyeOpenExpr)
            {
                expr.ExpressionValue.Value = value;
            }
        }
    }
    #endregion

    #region Saccades
    private void ScheduleNextSaccade()
    {
        _nextSaccadeTime = Time.time + Random.Range(SaccadeIntervalMin, SaccadeIntervalMax);
    }

    private void BeginSaccade()
    {
        _saccadeStartTime = Time.time;
        _saccadeDuration = Random.Range(0.04f, 0.12f);
        _eyeJitterPrev = _eyeJitter;

        // Random direction: horizontal weighted more than vertical for natural look
        float horiz = Random.Range(-1f, 1f);
        float vert = Random.Range(-1f, 1f) * SaccadeVerticalBias;
        Vector2 raw = new Vector2(horiz, vert);
        if (raw.sqrMagnitude > 1f) raw.Normalize();

        // Convert to degrees offset, clamp by SaccadeMaxOffsetDeg
        _eyeJitterTarget = raw * (SaccadeMaxOffsetDeg / EyeMaxAngle); // normalized offset relative to max eye angle
    }

    private void UpdateSaccades()
    {
        if (_headTransform == null)
        {
            return; // no head, skip
        }

        if (Time.time >= _nextSaccadeTime && !_isBlinking)
        {
            BeginSaccade();
            ScheduleNextSaccade();
        }

        if (_saccadeDuration > 0f)
        {
            float t = Mathf.Clamp01((Time.time - _saccadeStartTime) / _saccadeDuration);
            // Ease: fast accel + decel
            float shaped = 1f - Mathf.Pow(1f - t, 3f);
            _eyeJitter = Vector2.Lerp(_eyeJitterPrev, _eyeJitterTarget, shaped);
        }
    }
    #endregion

    #region LookAt
    private void UpdateLookAtComputation()
    {

        _lookAtEngaged = false; // reset
        if (!EnableLookAt)
        {
            return;
        }
        if (LookAtTarget == null)
        {
            LogOnce(ref _logLookAtNoTarget, "[DynamicExpressionPlayer] LookAt not engaged: LookAtTarget is null.");
            _ikLookPos = transform.position + transform.forward * 2f;
            return;
        }

        _toTarget = LookAtTarget.position - transform.position;
        float dist = _toTarget.magnitude;
        if (dist < LookAtMinDistance)
        {
            LogOnce(ref _logLookAtTooNear, "[DynamicExpressionPlayer] LookAt not engaged: target closer than MinDistance.");
            _ikLookPos = transform.position + transform.forward * 2f;
            return;
        }
        if (dist > LookAtMaxDistance)
        {
            LogOnce(ref _logLookAtTooFar, "[DynamicExpressionPlayer] LookAt not engaged: target beyond MaxDistance.");
            _ikLookPos = transform.position + transform.forward * 2f;
            return;
        }

        _lookDir = _toTarget.normalized;
        // Ignore targets behind character
        float forwardDot = Vector3.Dot(transform.forward, _lookDir);
        if (forwardDot < 0.05f)
        {
            LogOnce(ref _logLookAtBehind, "[DynamicExpressionPlayer] LookAt not engaged: target behind character.");
            _ikLookPos = transform.position + transform.forward * 2f;
            return;
        }

        if (!_hasEyes)
        {
            LogOnce(ref _logLookAtNoEyes, "[DynamicExpressionPlayer] LookAt engaged without eye bones: using head only.");
        }

        // Apply eye jitter (localized small offsets)
        // Convert jitter (normalized relative to max angle) into an angular offset in local space
        Vector3 right = transform.right;
        Vector3 up = transform.up;
        float horizAngle = _eyeJitter.x * EyeMaxAngle * Mathf.Deg2Rad;
        float vertAngle = _eyeJitter.y * ClampVerticalAngle * Mathf.Deg2Rad;

        // Rotate forward vector by small angles
        Vector3 modifiedDir = (Quaternion.AngleAxis(horizAngle * Mathf.Rad2Deg, up) *
                               Quaternion.AngleAxis(-vertAngle * Mathf.Rad2Deg, right)) * transform.forward;

        // Blend towards actual target direction for natural gaze acquisition
        modifiedDir = Vector3.Slerp(modifiedDir, _lookDir, 0.6f).normalized;
        _ikLookPos = transform.position + modifiedDir * Mathf.Max(2f, dist);

        // Head assist (adjust IK head weight if target angle large)
        if (_hasHead)
        {
            _headForward = _headTransform.forward;
            float headAngle = Vector3.Angle(_headForward, _lookDir);
            // Remap angle to 0..1 head assist weight factor
            float assist = 0f;
            if (headAngle > HeadAssistStartAngle)
            {
                assist = Mathf.InverseLerp(HeadAssistStartAngle, HeadAssistFullAngle, headAngle);
            }
            // Adjust body/head weights dynamically (eyes weight kept constant for now)
            _dynamicHeadWeight = Mathf.Lerp(0f, HeadWeight, assist);
            _dynamicBodyWeight = Mathf.Lerp(0f, BodyWeight, assist * 0.5f);
        }
        else
        {
            _dynamicHeadWeight = HeadWeight;
            _dynamicBodyWeight = BodyWeight;
        }
        _lookAtEngaged = true; // all gating passed
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!EnableLookAt || !ShouldProcess()) return;
        if (_animator == null) return;

        _animator.SetLookAtPosition(_ikLookPos);
        _animator.SetLookAtWeight(
            GazeWeight,
            _dynamicBodyWeight,
            _dynamicHeadWeight,
            EyesWeight,
            0.5f
        );
    }
    #endregion

    #region Expression Utility
    // Optional: If you want to expose saccade & blink to expressions later
    public float GetBlinkAmount()
    {
        if (!_isBlinking) return 0f;
        float t = (Time.time - _blinkStartTime) / BlinkDuration;
        return BlinkCurve.Evaluate(Mathf.Clamp01(t));
    }

    public Vector2 GetSaccadeOffset()
    {
        return _eyeJitter;
    }
    #endregion

    private static void LogOnce(ref bool flag, string message)
    {
        if (flag) return;
        flag = true;
        if (Debug.isDebugBuild)
        {
            Debug.Log(message);
        }
    }

#if UNITY_EDITOR
    [SerializeField] public string dnaCreationFolder = "Assets"; // destination for newly created DNA assets from bone poses
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying && !_initialized) Initialize();
        if (!enabled) return;
        if (!EnableLookAt || LookAtTarget == null || !_lookAtEngaged) return;
        if (!_hasEyes) return;
        // Draw from each eye to target
        Gizmos.color = Color.cyan;
        if (_leftEyeTransform != null)
        {
            Gizmos.DrawLine(_leftEyeTransform.position, LookAtTarget.position);
            //Gizmos.DrawSphere(_leftEyeTransform.position, 0.005f);
        }
        Gizmos.color = Color.magenta;
        if (_rightEyeTransform != null)
        {
            Gizmos.DrawLine(_rightEyeTransform.position, LookAtTarget.position);
           // Gizmos.DrawSphere(_rightEyeTransform.position, 0.005f);
        }
        //Gizmos.color = Color.yellow;
       // Gizmos.DrawSphere(LookAtTarget.position, 0.01f);
    }

    public void EditorSimulateOnce()
    {
        if (Application.isPlaying) return;
        Initialize();
        DoUpdate();      // simulate Update phase (includes logging)
        DoLateUpdate();  // simulate LateUpdate phase
        UnityEditor.SceneView.RepaintAll(); // refresh gizmos & scene view
    }
#endif
}