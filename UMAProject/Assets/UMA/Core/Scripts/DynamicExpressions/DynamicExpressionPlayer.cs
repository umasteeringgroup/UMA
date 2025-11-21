using UMA;
using UnityEngine;
using System.Collections.Generic;

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
        _animator = GetComponentInChildren<Animator>();
        _mainCam = Camera.main;
        if (_animator != null)
        {
            // Try to fetch head transform (human rig)
            _headTransform = _animator.GetBoneTransform(HumanBodyBones.Head);
            _hasHead = _headTransform != null;
            CacheEyeTransforms();
        }
        ScheduleNextBlink();
        ScheduleNextSaccade();
        _initialized = true;
    }

    private void CacheEyeTransforms()
    {
        _leftEyeTransform = _animator.GetBoneTransform(HumanBodyBones.LeftEye);
        _rightEyeTransform = _animator.GetBoneTransform(HumanBodyBones.RightEye);
        _hasEyes = (_leftEyeTransform != null && _rightEyeTransform != null);
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
        if (!_initialized) return false;
        if (_animator == null) return false;
        if (_mainCam != null)
        {
            float sq = (_mainCam.transform.position - transform.position).sqrMagnitude;
            if (sq > processDistance * processDistance) return false;
        }
        return true;
    }

    private void DoUpdate()
    {
        if (!ShouldProcess()) return;

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
        _lookAtEngaged = false; // reset, will set true if all conditions satisfied
        if (LookAtTarget == null)
        {
            _ikLookPos = transform.position + transform.forward * 2f;
            return;
        }

        _toTarget = LookAtTarget.position - transform.position;
        float dist = _toTarget.magnitude;
        if (dist < LookAtMinDistance || dist > LookAtMaxDistance)
        {
            _ikLookPos = transform.position + transform.forward * 2f;
            return;
        }

        _lookDir = _toTarget.normalized;
        // Ignore targets behind character
        float forwardDot = Vector3.Dot(transform.forward, _lookDir);
        if (forwardDot < 0.05f)
        {
            _ikLookPos = transform.position + transform.forward * 2f;
            return;
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

#if UNITY_EDITOR
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
        DoUpdate();      // simulate Update phase
        DoLateUpdate();  // simulate LateUpdate phase
        UnityEditor.SceneView.RepaintAll(); // refresh gizmos & scene view
    }
#endif
}