using UnityEngine;

public class ImpWander : MonoBehaviour
{
    [Header("Refs")]
    public Terrain terrain;              // 비워도 됨(자동 탐색)
    public GameObject terrainRoot;        // Map 같은 부모 오브젝트 넣고 싶으면 여기에
    public Animator animator;             // 비워도 GetComponent로 잡음
    public Transform playerTarget;        // 비워도 tag로 자동 탐색
    public string playerTag = "Player";

    [Header("Wander")]
    public float wanderRadius = 10f;
    public float repathInterval = 3f;
    public float arriveDist = 0.6f;
    public float wanderSpeed = 2.0f;
    public float wanderAcceleration = 6f;

    [Header("Detect / Chase")]
    public float detectRadius = 25f;      // 인식 사거리 (늘림)
    public float loseRadius = 35f;        // 추격 유지 사거리(히스테리시스)
    public float chaseSpeed = 6.5f;       // 추격 속도(늘림)
    public float chaseAcceleration = 14f; // 가속도(늘림)
    public float turnSpeed = 360f;

    [Header("Attack (distance)")]
    public float attackStopRadius = 2.4f;   // 여기선 더 가까이 안 감(정지 거리)
    public float attackStartRadius = 2.9f;  // 이 안이면 공격 시작
    public float attackHitRadius = 3.2f;    // 데미지 판정 거리(조금 더 여유)
    public int attackDamage = 10;           // ✅ 없어서 에러났던 변수
    public float attackCooldown = 1.2f;
    public float attackHitDelay = 0.25f;    // 공격 모션 중 “맞는 타이밍”
    public float attackRecoverTime = 0.35f; // 후딜

    [Header("Ground")]
    public LayerMask groundMask = ~0;
    public float yOffset = 0.02f;

    [Header("Animator Params")]
    public string speedParam = "Speed";         // float (0~1 권장)
    public string isChasingParam = "IsChasing"; // bool
    public string attack1Trigger = "Attack1";   // trigger
    public string attack2Trigger = "Attack2";   // trigger

    [Header("Animator Speed")]
    public bool driveAnimatorSpeed = true;
    public float walkAnimSpeed = 1.15f;
    public float runAnimSpeed = 1.85f;
    public float attackAnimSpeed = 1.15f;

    [Header("Failsafe")]
    public bool forceCrossFadeToIdleAfterAttack = true;
    public string idleStateName = "Idle";

    enum State { Wander, Chase, Attack }
    State _state = State.Wander;

    Vector3 _wanderTarget;
    float _nextRepathTime;

    float _curSpeed;

    // attack runtime
    bool _attackInProgress;
    bool _damageApplied;
    float _attackHitTime;
    float _attackEndTime;
    float _nextAttackReadyTime;
    int _attackFlip;

    // cached animator param existence
    bool _hasSpeed, _hasChasing, _hasAtk1, _hasAtk2;

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        ResolveTerrain();
        CacheAnimatorParams();
        PickNewWanderTarget();
    }

    void Update()
    {
        ResolvePlayerTarget();
        ResolveTerrain();

        UpdateStateMachine();

        if (_state == State.Wander) DoWander();
        else if (_state == State.Chase) DoChase();
        else DoAttack();

        StickToGround();
        UpdateAnimatorParams();
    }

    void ResolvePlayerTarget()
    {
        if (playerTarget) return;
        if (string.IsNullOrEmpty(playerTag)) return;

        var go = GameObject.FindGameObjectWithTag(playerTag);
        if (go) playerTarget = go.transform;
    }

    void UpdateStateMachine()
    {
        if (!playerTarget)
        {
            if (_state != State.Attack) _state = State.Wander;
            return;
        }

        float d = FlatDistance(transform.position, playerTarget.position);

        // 공격 중이면 DoAttack에서 관리
        if (_state == State.Attack) return;

        // 공격 시작 조건
        if (d <= attackStartRadius && Time.time >= _nextAttackReadyTime)
        {
            _state = State.Attack;
            StartAttackTimers();
            FireAttackTrigger();
            return;
        }

        // 추격/배회 스위치
        if (_state == State.Chase)
            _state = (d > loseRadius) ? State.Wander : State.Chase;
        else
            _state = (d <= detectRadius) ? State.Chase : State.Wander;
    }

    void DoWander()
    {
        if (Time.time >= _nextRepathTime)
        {
            _nextRepathTime = Time.time + repathInterval;
            PickNewWanderTarget();
        }

        MoveTowards(_wanderTarget, wanderSpeed, wanderAcceleration, stopAtDistance: arriveDist);
    }

    void DoChase()
    {
        if (!playerTarget) { _state = State.Wander; return; }

        float d = FlatDistance(transform.position, playerTarget.position);

        // 너무 붙으면 멈춰서 거리 유지(비비기 방지)
        if (d <= attackStopRadius)
        {
            _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, chaseAcceleration * Time.deltaTime);
            FaceTarget(playerTarget.position);
            return;
        }

        MoveTowards(playerTarget.position, chaseSpeed, chaseAcceleration, stopAtDistance: attackStopRadius);
    }

    void DoAttack()
    {
        // 공격 중엔 이동 정지
        _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, (wanderAcceleration + chaseAcceleration) * Time.deltaTime);

        if (!playerTarget)
        {
            EndAttackIfNeeded();
            _state = State.Wander;
            return;
        }

        FaceTarget(playerTarget.position);

        // 타이밍에 맞춰 1회 데미지
        if (_attackInProgress)
        {
            if (!_damageApplied && Time.time >= _attackHitTime)
            {
                _damageApplied = true;
                TryApplyDamage();
            }

            if (Time.time >= _attackEndTime)
            {
                EndAttackIfNeeded();
                // 공격 끝나면 다시 상황 판단
                _state = State.Chase;
            }
        }
    }

    void StartAttackTimers()
    {
        _attackInProgress = true;
        _damageApplied = false;

        _nextAttackReadyTime = Time.time + attackCooldown;
        _attackHitTime = Time.time + attackHitDelay;
        _attackEndTime = _attackHitTime + attackRecoverTime;
    }

    void FireAttackTrigger()
    {
        if (!animator) return;

        if (_hasAtk1 && _hasAtk2)
        {
            _attackFlip ^= 1;
            if (_attackFlip == 0) animator.SetTrigger(attack1Trigger);
            else animator.SetTrigger(attack2Trigger);
        }
        else if (_hasAtk1) animator.SetTrigger(attack1Trigger);
        else if (_hasAtk2) animator.SetTrigger(attack2Trigger);
    }

    void TryApplyDamage()
    {
        if (!playerTarget) return;

        float d = FlatDistance(transform.position, playerTarget.position);
        if (d > attackHitRadius) return;

        // IDamageable(=Health 포함) 우선
        var dmg = playerTarget.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(attackDamage);
            return;
        }

        // 혹시 인터페이스 안 쓸 때 대비
        var hp = playerTarget.GetComponentInParent<Health>();
        if (hp != null) hp.TakeDamage(attackDamage);
    }

    void EndAttackIfNeeded()
    {
        _attackInProgress = false;
        _damageApplied = false;

        // Animator에 Attack -> Idle 복귀가 없으면 “멈춤” 방지용 안전장치
        if (forceCrossFadeToIdleAfterAttack && animator && !string.IsNullOrEmpty(idleStateName))
            animator.CrossFade(idleStateName, 0.05f);
    }

    void MoveTowards(Vector3 worldTarget, float maxSpeed, float accel, float stopAtDistance)
    {
        Vector3 pos = transform.position;
        Vector3 to = worldTarget - pos;
        to.y = 0f;

        float dist = to.magnitude;

        if (dist <= stopAtDistance)
        {
            _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, accel * Time.deltaTime);
            return;
        }

        Vector3 dir = to / Mathf.Max(dist, 0.0001f);

        // 회전
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        // 가속도
        _curSpeed = Mathf.MoveTowards(_curSpeed, maxSpeed, accel * Time.deltaTime);

        // 이동
        transform.position += transform.forward * (_curSpeed * Time.deltaTime);
    }

    void FaceTarget(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(to.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
    }

    void PickNewWanderTarget()
    {
        Vector2 r = Random.insideUnitCircle * wanderRadius;
        _wanderTarget = transform.position + new Vector3(r.x, 0f, r.y);
    }

    void UpdateAnimatorParams()
    {
        if (!animator) return;

        bool moving = (_state != State.Attack) && (_curSpeed > 0.05f);
        bool chasing = (_state == State.Chase);

        // Speed: 0~1 (이동 여부/속도 비례)
        if (_hasSpeed)
        {
            float denom = Mathf.Max(0.001f, chaseSpeed);
            float speed01 = moving ? Mathf.Clamp01(_curSpeed / denom) : 0f;
            animator.SetFloat(speedParam, speed01);
        }

        // IsChasing: Move <-> Move2_forward 분기용
        if (_hasChasing) animator.SetBool(isChasingParam, chasing);

        // 애니메이션 재생 속도(달리는 느낌)
        if (driveAnimatorSpeed)
        {
            if (_state == State.Attack) animator.speed = attackAnimSpeed;
            else if (moving)
            {
                // wander는 walk, chase는 run 쪽으로
                float t = Mathf.Clamp01(_curSpeed / Mathf.Max(0.001f, chaseSpeed));
                float target = chasing ? Mathf.Lerp(walkAnimSpeed, runAnimSpeed, t) : Mathf.Lerp(1.0f, walkAnimSpeed, t);
                animator.speed = target;
            }
            else animator.speed = 1f;
        }
    }

    void CacheAnimatorParams()
    {
        _hasSpeed = _hasChasing = _hasAtk1 = _hasAtk2 = false;
        if (!animator) return;

        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Float && p.name == speedParam) _hasSpeed = true;
            if (p.type == AnimatorControllerParameterType.Bool && p.name == isChasingParam) _hasChasing = true;
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == attack1Trigger) _hasAtk1 = true;
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == attack2Trigger) _hasAtk2 = true;
        }
    }

    void ResolveTerrain()
    {
        if (terrain) return;

        if (terrainRoot)
        {
            terrain = terrainRoot.GetComponent<Terrain>();
            if (!terrain) terrain = terrainRoot.GetComponentInChildren<Terrain>();
            if (terrain) return;
        }

        if (Terrain.activeTerrain) terrain = Terrain.activeTerrain;
    }

    void StickToGround()
    {
        Vector3 pos = transform.position;

        // Raycast 우선(지형이 Terrain이 아니어도 대응)
        Vector3 origin = pos + Vector3.up * 50f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
        {
            pos.y = hit.point.y + yOffset;
            transform.position = pos;
            return;
        }

        // fallback: Terrain
        if (terrain)
        {
            float y = terrain.SampleHeight(pos) + terrain.transform.position.y;
            pos.y = y + yOffset;
            transform.position = pos;
        }
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Color.orange는 Unity에 없어서 직접 만듦(이전 에러 원인)
        Color orange = new Color(1f, 0.5f, 0f, 1f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRadius);

        Gizmos.color = orange;
        Gizmos.DrawWireSphere(transform.position, loseRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackStartRadius);

        Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, attackHitRadius);
    }
#endif
}
