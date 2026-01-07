using UnityEngine;
using UnityEngine.Events;

public interface IDamageable
{
    void TakeDamage(int amount, GameObject source = null);
}

public class Health : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public int maxHP = 100;
    public int currentHP;

    [Header("Death")]
    public bool disableOnDeath = true;
    public bool destroyOnDeath = false;
    public float destroyDelay = 0f;

    [Header("Invincibility (optional)")]
    public bool useInvincibility = false;
    public float invincibilityDuration = 0.5f;
    private float _nextDamageTime;

    [Header("Events (optional)")]
    public UnityEvent<int, int> onHPChanged; // (current, max)
    public UnityEvent<int, GameObject> onDamaged; // (damage, source) - AI 피격 반응용
    public UnityEvent onDeath;

    void Awake()
    {
        // 초기 체력 설정
        if (currentHP <= 0) currentHP = maxHP;
        currentHP = Mathf.Clamp(currentHP, 0, maxHP);
    }

    public void TakeDamage(int amount, GameObject source = null)
    {
        // 데미지가 0 이하면 무시
        if (amount <= 0) return;

        // 이미 사망했으면 무시
        if (currentHP <= 0) return;

        // 무적 시간 체크
        if (useInvincibility && Time.time < _nextDamageTime) return;

        // 체력 감소
        int actualDamage = Mathf.Min(amount, currentHP);
        currentHP = Mathf.Max(0, currentHP - amount);

        // 무적 시간 설정
        if (useInvincibility)
            _nextDamageTime = Time.time + invincibilityDuration;

        // 이벤트 발동
        onHPChanged?.Invoke(currentHP, maxHP);
        onDamaged?.Invoke(actualDamage, source);

        Debug.Log($"{name} took {actualDamage} dmg from {(source ? source.name : "unknown")} -> {currentHP}/{maxHP}");

        // 사망 체크
        if (currentHP == 0)
            Die();
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        if (currentHP <= 0) return;

        int oldHP = currentHP;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        
        if (currentHP != oldHP)
            onHPChanged?.Invoke(currentHP, maxHP);

        Debug.Log($"{name} healed {currentHP - oldHP} HP -> {currentHP}/{maxHP}");
    }

    public void SetHP(int newHP)
    {
        currentHP = Mathf.Clamp(newHP, 0, maxHP);
        onHPChanged?.Invoke(currentHP, maxHP);
    }

    public bool IsDead() => currentHP <= 0;
    public bool IsAlive() => currentHP > 0;
    public float GetHealthPercent() => maxHP > 0 ? (float)currentHP / maxHP : 0f;

    void Die()
    {
        Debug.Log($"{name} died.");
        onDeath?.Invoke();

        if (destroyOnDeath)
        {
            if (destroyDelay > 0f)
                Destroy(gameObject, destroyDelay);
            else
                Destroy(gameObject);
        }
        else if (disableOnDeath)
        {
            gameObject.SetActive(false);
        }
    }
}