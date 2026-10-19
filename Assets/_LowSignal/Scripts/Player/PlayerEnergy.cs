using UnityEngine;

/// <summary>
/// Shared energy pool. Abilities own their costs and consume energy from this component.
/// Continuous costs use Consume; abilities requiring their entire cost use TryConsume.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class PlayerEnergy : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float maxEnergy = 100f;
    [SerializeField, Range(0f, 1f)] private float startingEnergy01 = 1f;
    [Tooltip("Seconds since the last successful consumption before recharge begins.")]
    [SerializeField, Min(0f)] private float rechargeDelay = 5f;
    [Tooltip("Seconds to recharge from empty to full.")]
    [SerializeField, Min(0.1f)] private float fullRechargeSeconds = 30f;

    private float currentEnergy;
    private float lastConsumptionTime;
    private int lastConsumptionFrame = -1;

    public float MaxEnergy => Mathf.Max(0.01f, maxEnergy);
    public float CurrentEnergy => currentEnergy;
    public float Energy01 => Mathf.Clamp01(currentEnergy / MaxEnergy);
    public bool HasEnergy => currentEnergy > 0f;

    private void Awake()
    {
        currentEnergy = MaxEnergy * startingEnergy01;
        lastConsumptionTime = Time.time;
    }

    private void LateUpdate()
    {
        // All abilities may consume during Update. Never recharge in a spending frame,
        // including when the delay is zero, regardless of component execution order.
        if (lastConsumptionFrame == Time.frameCount)
            return;

        float rechargeStart = lastConsumptionTime + Mathf.Max(0f, rechargeDelay);
        float rechargeTime = Mathf.Min(Time.deltaTime, Mathf.Max(0f, Time.time - rechargeStart));
        Restore(MaxEnergy * rechargeTime / Mathf.Max(0.1f, fullRechargeSeconds));
    }

    /// <summary>
    /// Continuous drain in energy units. Consumes the remaining balance if needed.
    /// Returns false when empty. Zero cost succeeds without delaying recharge.
    /// </summary>
    public bool Consume(float amount)
    {
        if (!IsValidAmount(amount))
            return false;
        if (amount == 0f)
            return true;
        if (!HasEnergy)
            return false;

        currentEnergy = Mathf.Max(0f, currentEnergy - amount);
        lastConsumptionTime = Time.time;
        lastConsumptionFrame = Time.frameCount;
        return true;
    }

    /// <summary>Spends an ability's entire cost, or returns false without spending anything.</summary>
    public bool TryConsume(float amount)
    {
        return IsValidAmount(amount) && currentEnergy >= amount && Consume(amount);
    }

    /// <summary>Adds energy, clamped to capacity. Does not reset the recharge delay.</summary>
    public void Restore(float amount)
    {
        if (IsValidAmount(amount))
            currentEnergy = Mathf.Min(MaxEnergy, currentEnergy + amount);
    }

    private static bool IsValidAmount(float amount)
    {
        return amount >= 0f && !float.IsNaN(amount) && !float.IsInfinity(amount);
    }
}
