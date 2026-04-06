using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateHitProcessor : MonoBehaviour
{
    [SerializeField] private bool debugLog;

    UltimateTargetBinder.BoundTarget _target;
    IUltimateTarget _targetUltimateTarget;
    IHealth _targetHealth;
    UltimateSequenceData _data;
    bool _dashHitApplied;
    bool _finalExplosionApplied;
    bool _bufferFlushed;
    int _accumulatedDamage;
    int _dashSlashDamage;
    int _multiSlashBaseDamage;
    int _finalExplosionDamage;
    int _executionBonusDamage;
    float _executionHealthThresholdNormalized;

    public int AccumulatedDamage => _accumulatedDamage;
    public bool FinalExplosionApplied => _finalExplosionApplied;

    public void BeginSequence(UltimateTargetBinder.BoundTarget target, UltimateSequenceData data)
    {
        _target = target;
        _targetUltimateTarget = target != null ? target.UltimateTarget : null;
        _targetHealth = target != null ? target.Health : null;
        _data = data;
        _dashHitApplied = false;
        _finalExplosionApplied = false;
        _bufferFlushed = false;
        _accumulatedDamage = 0;
        if (data != null)
        {
            _dashSlashDamage = data.Damage.dashSlashDamage;
            _multiSlashBaseDamage = data.Damage.multiSlashBaseDamage;
            _finalExplosionDamage = data.Damage.finalExplosionDamage;
            _executionBonusDamage = data.Damage.executionBonusDamage;
            _executionHealthThresholdNormalized = Mathf.Clamp01(data.Damage.executionHealthThresholdNormalized);
        }
        else
        {
            _dashSlashDamage = 0;
            _multiSlashBaseDamage = 0;
            _finalExplosionDamage = 0;
            _executionBonusDamage = 0;
            _executionHealthThresholdNormalized = 0f;
        }
    }

    public void EndSequence()
    {
        _target = null;
        _targetUltimateTarget = null;
        _targetHealth = null;
        _data = null;
        _dashHitApplied = false;
        _finalExplosionApplied = false;
        _bufferFlushed = false;
        _accumulatedDamage = 0;
        _dashSlashDamage = 0;
        _multiSlashBaseDamage = 0;
        _finalExplosionDamage = 0;
        _executionBonusDamage = 0;
        _executionHealthThresholdNormalized = 0f;
    }

    public void ApplyDashSlashHit()
    {
        if (_dashHitApplied || _data == null)
            return;

        _dashHitApplied = true;
        BufferDamage(_dashSlashDamage);

        if (debugLog)
            Debug.Log($"[UltimateHit] buffer dash damage={_dashSlashDamage} total={_accumulatedDamage}", this);
    }

    public void ApplyMultiSlashHit(UltimateSequenceData.SlashStepData step, int slashIndex)
    {
        if (_data == null)
            return;

        int damage = Mathf.RoundToInt(_multiSlashBaseDamage * Mathf.Max(0.1f, step.damageMultiplier));
        BufferDamage(damage);

        if (debugLog)
            Debug.Log($"[UltimateHit] buffer slash-{slashIndex} damage={damage} total={_accumulatedDamage}", this);
    }

    public void ApplyFinalExplosionHit()
    {
        if (_finalExplosionApplied || _data == null)
            return;

        _finalExplosionApplied = true;
        int damage = _finalExplosionDamage;
        if (ShouldApplyExecutionBonus())
            damage += _executionBonusDamage;
        BufferDamage(damage);

        if (debugLog)
            Debug.Log($"[UltimateHit] buffer final damage={damage} total={_accumulatedDamage}", this);
    }

    public bool FlushBufferedDamage()
    {
        if (_bufferFlushed || _accumulatedDamage <= 0 || _targetUltimateTarget == null)
            return false;

        _bufferFlushed = true;
        _targetUltimateTarget.ApplyUltimateDamage(_accumulatedDamage);

        if (debugLog)
            Debug.Log($"[UltimateHit] flush total={_accumulatedDamage}", this);

        return true;
    }

    bool ShouldApplyExecutionBonus()
    {
        if (_targetHealth == null || _targetHealth.IsDead)
            return false;
        if (_targetHealth.MaxHP <= 0)
            return false;

        float normalized = (float)_targetHealth.CurrentHP / _targetHealth.MaxHP;
        return normalized <= _executionHealthThresholdNormalized;
    }

    void BufferDamage(int damage)
    {
        if (damage <= 0 || _targetUltimateTarget == null)
            return;

        _accumulatedDamage += damage;
    }
}
