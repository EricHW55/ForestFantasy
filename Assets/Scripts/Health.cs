using UnityEngine;
using UnityEngine.Events;

public interface IDamageable
{
    void TakeDamage(int amount, GameObject source = null);
}

public class Health : MonoBehaviour, IDamageable
{
    public int maxHP = 100;
    public int currentHP;

    [Header("Death")]
    public bool disableOnDeath = true;
    public bool destroyOnDeath = false;

    [Header("Events (optional)")]
    public UnityEvent<int, int> onHPChanged; // (current, max)
    public UnityEvent onDeath;

    void Awake()
    {
        if (currentHP <= 0) currentHP = maxHP;
        currentHP = Mathf.Clamp(currentHP, 0, maxHP);
    }

    public void TakeDamage(int amount, GameObject source = null)
    {
        if (amount <= 0) return;
        if (currentHP <= 0) return;

        currentHP = Mathf.Max(0, currentHP - amount);
        onHPChanged?.Invoke(currentHP, maxHP);

        Debug.Log($"{name} took {amount} dmg from {(source ? source.name : "unknown")} -> {currentHP}/{maxHP}");

        if (currentHP == 0)
            Die();
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        if (currentHP <= 0) return;

        currentHP = Mathf.Min(maxHP, currentHP + amount);
        onHPChanged?.Invoke(currentHP, maxHP);
    }

    void Die()
    {
        Debug.Log($"{name} died.");
        onDeath?.Invoke();

        if (destroyOnDeath) Destroy(gameObject);
        else if (disableOnDeath) gameObject.SetActive(false);
    }
}
