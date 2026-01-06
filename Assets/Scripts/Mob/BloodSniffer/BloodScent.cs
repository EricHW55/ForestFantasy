using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 피냄새 데이터
/// </summary>
public class BloodScent
{
    public Vector3 position;
    public float intensity;
    public float createdTime;
    public float lifetime;

    public BloodScent(Vector3 pos, float intensity = 1f, float lifetime = 60f)
    {
        this.position = pos;
        this.intensity = intensity;
        this.createdTime = Time.time;
        this.lifetime = lifetime;
    }

    public bool IsExpired()
    {
        return Time.time - createdTime >= lifetime;
    }

    public float GetCurrentIntensity()
    {
        float elapsed = Time.time - createdTime;
        float t = elapsed / lifetime;
        return intensity * (1f - t);
    }
}

/// <summary>
/// 전역 피냄새 관리 시스템
/// </summary>
public class BloodScentManager : MonoBehaviour
{
    private static BloodScentManager _instance;
    public static BloodScentManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("BloodScentManager");
                _instance = go.AddComponent<BloodScentManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private List<BloodScent> scents = new List<BloodScent>();

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        // 만료된 냄새 제거
        scents.RemoveAll(s => s.IsExpired());
    }

    /// <summary>
    /// 피냄새 추가 (몹 사망, 고기 먹을 때 호출)
    /// </summary>
    public static void AddBloodScent(Vector3 position, float intensity = 1f, float lifetime = 60f)
    {
        Instance.scents.Add(new BloodScent(position, intensity, lifetime));
    }

    /// <summary>
    /// 가장 가까운 피냄새 찾기
    /// </summary>
    public static BloodScent GetNearestScent(Vector3 fromPosition, float minIntensity = 0.1f)
    {
        BloodScent nearest = null;
        float minDist = float.MaxValue;

        foreach (var scent in Instance.scents)
        {
            if (scent.GetCurrentIntensity() < minIntensity)
                continue;

            float dist = Vector3.Distance(fromPosition, scent.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = scent;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 모든 유효한 피냄새 목록
    /// </summary>
    public static List<BloodScent> GetAllScents(float minIntensity = 0.1f)
    {
        List<BloodScent> valid = new List<BloodScent>();
        foreach (var scent in Instance.scents)
        {
            if (scent.GetCurrentIntensity() >= minIntensity)
                valid.Add(scent);
        }
        return valid;
    }

    void OnDrawGizmos()
    {
        if (scents == null) return;

        foreach (var scent in scents)
        {
            float intensity = scent.GetCurrentIntensity();
            Gizmos.color = new Color(1f, 0f, 0f, intensity);
            Gizmos.DrawWireSphere(scent.position + Vector3.up * 0.5f, 1f);
        }
    }
}