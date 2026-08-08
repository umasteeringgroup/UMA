using UMA;
using UMA.PoseTools;
using UnityEngine;

/// <summary>
/// Compatibility surface for animation clips and scripts authored against the
/// legacy 51 ExpressionPlayer fields. It forwards values to stable expression
/// IDs and never applies the old pose set.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class DynamicExpressionLegacyAdapter : ExpressionPlayer
{
    public DynamicExpressionPlayer target;
    [Tooltip("When enabled, every legacy channel is continuously owned by the Animation source.")]
    public bool forwardEveryFrame = true;
    [Tooltip("Compatibility event using legacy signed values and channel IDs.")]
    public UMAExpressionEvent ExpressionChanged;

    private readonly float[] _lastValues = new float[PoseCount];
    private readonly int[] _indices = new int[PoseCount];
    private bool _hasForwarded;
    private bool _indicesResolved;
    private bool _subscribed;
    private DynamicExpressionPlayer _boundTarget;
    private UMAData _umaData;

    private void Awake()
    {
        if (target == null) target = GetComponent<DynamicExpressionPlayer>();
    }

    private void OnEnable()
    {
        EnsureTargetBinding();
        ForwardValues(true);
    }

    private void Update()
    {
        if (forwardEveryFrame) ForwardValues(false);
    }

    public void ForwardValues(bool force = true)
    {
        EnsureTargetBinding();
        if (_boundTarget == null) return;
        float[] values = Values;
        _boundTarget.BeginExpressionBatch();
        try
        {
            int count = Mathf.Min(PoseCount, values.Length);
            for (int i = 0; i < count; i++)
            {
                float value = Mathf.Clamp(values[i], -1f, 1f);
                if (!force && _hasForwarded &&
                    Mathf.Approximately(_lastValues[i], value)) continue;
                _lastValues[i] = value;
                if (_indices[i] < 0) continue;
                _boundTarget.SetExpression(_indices[i],
                    value * 0.5f + 0.5f,
                    ExpressionSource.Animation);
            }
        }
        finally
        {
            _boundTarget.EndExpressionBatch();
        }
        _hasForwarded = true;
    }

    private void OnDisable()
    {
        if (_boundTarget != null)
        {
            Unsubscribe();
            _boundTarget.ResetAllExpressions(ExpressionSource.Animation);
        }
        _hasForwarded = false;
    }

    private void OnGroupRebound(UMAExpressionGroup group)
    {
        _indicesResolved = false;
        ResolveIndices();
        _hasForwarded = false;
    }

    private void EnsureTargetBinding()
    {
        if (target == null) target = GetComponent<DynamicExpressionPlayer>();
        if (_boundTarget != target)
        {
            if (_boundTarget != null)
            {
                Unsubscribe();
                _boundTarget.ResetAllExpressions(
                    ExpressionSource.Animation);
            }
            _boundTarget = target;
            _umaData = _boundTarget != null
                ? _boundTarget.GetComponent<UMAData>() : null;
            _indicesResolved = false;
            _hasForwarded = false;
        }
        if (_boundTarget == null) return;
        if (isActiveAndEnabled && !_subscribed)
        {
            _boundTarget.GroupReboundAction += OnGroupRebound;
            _boundTarget.ExpressionChangedAction += OnExpressionChanged;
            _subscribed = true;
        }
        if (!_indicesResolved) ResolveIndices();
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _boundTarget == null) return;
        _boundTarget.GroupReboundAction -= OnGroupRebound;
        _boundTarget.ExpressionChangedAction -= OnExpressionChanged;
        _subscribed = false;
    }

    private void OnExpressionChanged(
        DynamicExpressionPlayer.ExpressionChange change)
    {
        if (ExpressionChanged == null) return;
        for (int i = 0; i < PoseCount; i++)
        {
            if (!string.Equals(PoseNames[i], change.id,
                System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            ExpressionChanged.Invoke(_umaData, PoseNames[i],
                change.value * 2f - 1f);
            return;
        }
    }

    private void ResolveIndices()
    {
        for (int i = 0; i < PoseCount; i++)
            _indices[i] = _boundTarget != null &&
                _boundTarget.TryGetExpressionIndex(PoseNames[i], out int index)
                    ? index : -1;
        _indicesResolved = true;
    }
}
