using UnityEngine;

public class ImpWander : MonoBehaviour
{
    [Header("Refs")]
    public Terrain terrain; // 비워두면 자동으로 ActiveTerrain 사용
    public Animator animator; // 비워두면 자식에서 자동으로 찾음

    [Header("Move")]
    public float moveSpeed = 2.0f;
    public float turnSpeed = 360f;
    public float wanderRadius = 10f;
    public float arriveDist = 0.6f;
    public float repathInterval = 3.0f;

    [Header("Ground")]
    public float yOffset = 0.02f;

    private Vector3 _target;
    private float _nextRepathTime;

    void Awake()
    {
        if (terrain == null) terrain = Terrain.activeTerrain;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        PickNewTarget();
    }

    void Update()
    {
        if (terrain == null) return;

        // 주기적으로 목표 갱신(자연스럽게 헤매기)
        if (Time.time >= _nextRepathTime)
        {
            _nextRepathTime = Time.time + repathInterval;
            PickNewTarget();
        }

        Vector3 pos = transform.position;
        Vector3 to = _target - pos;
        to.y = 0f;

        float dist = to.magnitude;

        float speed01 = 0f;

        if (dist > arriveDist)
        {
            Vector3 dir = to / Mathf.Max(dist, 0.0001f);

            // 회전
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            // 전진
            Vector3 step = transform.forward * (moveSpeed * Time.deltaTime);
            transform.position += step;

            speed01 = 1f; // 애니메이션용
        }

        // 지형 위로 붙이기
        float y = terrain.SampleHeight(transform.position) + terrain.transform.position.y + yOffset;
        transform.position = new Vector3(transform.position.x, y, transform.position.z);

        // Animator 파라미터 (컨트롤러에서 Speed 사용한다고 가정)
        if (animator != null)
            animator.SetFloat("Speed", speed01);
    }

    void PickNewTarget()
    {
        Vector2 rnd = Random.insideUnitCircle * wanderRadius;
        Vector3 center = transform.position;
        _target = new Vector3(center.x + rnd.x, center.y, center.z + rnd.y);

        // 목표도 지형 위로
        if (terrain != null)
        {
            float y = terrain.SampleHeight(_target) + terrain.transform.position.y;
            _target.y = y;
        }
    }
}
