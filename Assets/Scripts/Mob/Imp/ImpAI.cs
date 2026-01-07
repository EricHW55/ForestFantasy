using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class ImpAI : MonoBehaviour
{
    [Header("Refs")]
    public NavMeshAgent agent;
    public Terrain terrain;
    public GameObject terrainRoot;
    public Animator animator;
    public Transform playerTarget;
    public string playerTag = "Player";

    [Header("Movement Mode")]
    public bool useNavMesh = true;
    public float groundCheckDistance = 5f;

    [Header("Wander")]
    public float wanderRadius = 10f;
    public float repathInterval = 3f;
    public float arriveDist = 0.6f;
    public float wanderSpeed = 2.0f;
    public float wanderAcceleration = 6f;

    [Header("Detect / Chase")]
    public float detectRadius = 25f;
    public float loseRadius = 35f;
    public float chaseSpeed = 6.5f;
    public float chaseAcceleration = 14f;
    public float turnSpeed = 360f;

    [Header("Attack (distance)")]
    public float attackStopRadius = 2.4f;
    public float attackStartRadius = 2.9f;
    public float attackHitRadius = 3.2f;
    public int attackDamage = 10;
    public float attackCooldown = 1.2f;
    public float attackHitDelay = 0.25f;
    public float attackRecoverTime = 0.35f;

    [Header("Ground")]
    public LayerMask groundMask = ~0;
    public float yOffset = 0.02f;

    [Header("Animator Params")]
    public string speedParam = "Speed";
    public string isChasingParam = "IsChasing";
    public string attack1Trigger = "Attack1";
    public string attack2Trigger = "Attack2";

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

    bool _attackInProgress;
    bool _damageApplied;
    float _attackHitTime;
    float _attackEndTime;
    float _nextAttackReadyTime;
    int _attackFlip;

    bool _hasSpeed, _hasChasing, _hasAtk1, _hasAtk2;

    void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        
        if (useNavMesh && agent)
        {
            agent.updateRotation = false;
            agent.angularSpeed = turnSpeed;
            if (animator) animator.applyRootMotion = false;
            agent.enabled = false;
        }
        else if (agent)
        {
            agent.enabled = false;
        }
        
        ResolveTerrain();
        CacheAnimatorParams();
        PickNewWanderTarget();
    }

    void Start()
    {
        if (useNavMesh && agent)
        {
            Invoke(nameof(EnableAgent), 0.5f);
        }
    }

    void EnableAgent()
    {
        if (agent && useNavMesh)
        {
            agent.enabled = true;
            
            if (!agent.isOnNavMesh)
            {
                Debug.LogWarning($"[ImpWander] {name}이(가) NavMesh 위에 없습니다!");
            }
        }
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

        if (_state == State.Attack) return;

        if (d <= attackStartRadius && Time.time >= _nextAttackReadyTime)
        {
            _state = State.Attack;
            StartAttackTimers();
            FireAttackTrigger();
            return;
        }

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

        if (d <= attackStopRadius)
        {
            _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, chaseAcceleration * Time.deltaTime);
            
            if (useNavMesh && agent && agent.enabled)
                agent.velocity = Vector3.zero;
                
            FaceTarget(playerTarget.position);
            return;
        }

        MoveTowards(playerTarget.position, chaseSpeed, chaseAcceleration, stopAtDistance: attackStopRadius);
    }

    void DoAttack()
    {
        _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, (wanderAcceleration + chaseAcceleration) * Time.deltaTime);

        if (useNavMesh && agent && agent.enabled)
            agent.velocity = Vector3.zero;

        if (!playerTarget)
        {
            EndAttackIfNeeded();
            _state = State.Wander;
            return;
        }

        FaceTarget(playerTarget.position);

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

        // IDamageable 인터페이스 체크 (우선순위 1)
        var dmg = playerTarget.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(attackDamage, gameObject);
            return;
        }

        // Health 컴포넌트 체크 (우선순위 2)
        var hp = playerTarget.GetComponentInParent<Health>();
        if (hp != null)
        {
            hp.TakeDamage(attackDamage, gameObject);
            return;
        }

        Debug.LogWarning($"{name}: Target {playerTarget.name} has no IDamageable or Health component!");
    }
    
    void EndAttackIfNeeded()
    {
        _attackInProgress = false;
        _damageApplied = false;

        if (forceCrossFadeToIdleAfterAttack && animator && !string.IsNullOrEmpty(idleStateName))
            animator.CrossFade(idleStateName, 0.05f);
    }

    void MoveTowards(Vector3 worldTarget, float maxSpeed, float accel, float stopAtDistance)
    {
        if (useNavMesh && agent && agent.enabled)
        {
            // NavMesh 방식
            float dist = Vector3.Distance(transform.position, worldTarget);
            
            if (dist <= stopAtDistance)
            {
                _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, accel * Time.deltaTime);
                agent.velocity = Vector3.zero;
                return;
            }

            _curSpeed = Mathf.MoveTowards(_curSpeed, maxSpeed, accel * Time.deltaTime);
            agent.speed = _curSpeed;
            agent.SetDestination(worldTarget);

            Vector3 dir = agent.desiredVelocity;
            if (dir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }
        }
        else
        {
            // 수동 방식
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

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            _curSpeed = Mathf.MoveTowards(_curSpeed, maxSpeed, accel * Time.deltaTime);
            transform.position += transform.forward * (_curSpeed * Time.deltaTime);
        }
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
        Vector3 candidate = transform.position + new Vector3(r.x, 0f, r.y);
        
        if (useNavMesh && NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            _wanderTarget = hit.position;
        }
        else
        {
            _wanderTarget = candidate;
        }
    }

    void UpdateAnimatorParams()
    {
        if (!animator) return;

        bool moving = (_state != State.Attack) && (_curSpeed > 0.05f);
        bool chasing = (_state == State.Chase);

        if (_hasSpeed)
        {
            float denom = Mathf.Max(0.001f, chaseSpeed);
            float speed01 = moving ? Mathf.Clamp01(_curSpeed / denom) : 0f;
            animator.SetFloat(speedParam, speed01);
        }

        if (_hasChasing) animator.SetBool(isChasingParam, chasing);

        if (driveAnimatorSpeed)
        {
            if (_state == State.Attack) animator.speed = attackAnimSpeed;
            else if (moving)
            {
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
        // NavMesh 사용 시 지면 추적 불필요
        if (useNavMesh && agent && agent.enabled) return;

        Vector3 pos = transform.position;
        Vector3 origin = pos + Vector3.up * 50f;
        
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            pos.y = hit.point.y + yOffset;
            transform.position = pos;
            return;
        }

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