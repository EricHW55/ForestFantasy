using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class OneEyeAI : MonoBehaviour
{
    [Header("Refs")]
    public NavMeshAgent agent;
    public Animator animator;
    public Transform playerTarget;
    public string playerTag = "Player";

    [Header("Movement Mode")]
    public bool useNavMesh = true;
    public LayerMask groundMask = ~0;
    public float groundCheckDistance = 5f;

    [Header("Vision (NEW)")]
    public bool useFieldOfView = true;
    [Range(1f, 179f)] public float viewAngle = 120f;
    public float eyeHeight = 1.6f;
    public float targetHeight = 1.2f;
    public bool requireLineOfSight = false;
    public LayerMask visionBlockers = ~0;
    public float forgetTargetAfter = 1.2f;

    [Header("Wander")]
    public float wanderRadius = 10f;
    public float repathInterval = 3f;
    public float arriveDist = 0.6f;
    public float wanderSpeed = 1.8f;
    public float wanderAcceleration = 6f;

    [Header("Wander Turning")]
    [Range(30f, 180f)] public float wanderConeAngle = 120f;
    public float wanderTurnSpeed = 160f;

    [Header("Wander Pause / Actions")]
    public Vector2 pauseEverySeconds = new Vector2(2.0f, 5.0f);
    public Vector2 pauseDurationSeconds = new Vector2(0.6f, 1.8f);
    [Range(0f, 1f)] public float action1ChanceOnPause = 0.45f;
    [Range(0f, 1f)] public float action2ChanceOnPause = 0.05f;
    public bool action2OnDetect = true;
    [Range(0f, 1f)] public float action2ChanceOnDetect = 0.7f;

    [Header("Detect / Chase")]
    public float detectRadius = 20f;
    public float loseRadius = 28f;
    public float chaseSpeed = 5.5f;
    public float chaseAcceleration = 14f;
    public float chaseTurnSpeed = 360f;

    [Header("Sprint (NEW)")]
    public bool enableSprint = true;
    public float sprintDelay = 0.6f;
    public float sprintSpeed = 8.0f;
    public float sprintMaxSpeedRamp = 6.0f;

    [Header("Attack")]
    public float attackStopRadius = 2.3f;
    public float attackStartRadius = 2.7f;
    public float attackHitRadius = 3.0f;
    public int attackDamage = 10;
    public float attackCooldown = 0.0f;
    public float attackHitDelay = 0.25f;

    [Header("Damage Reaction")]
    public float damageCooldown = 0.15f;

    [Header("Ground (optional)")]
    public float yOffset = 0.02f;

    [Header("Animator Params")]
    public string speedParam = "Speed";
    public string isChasingParam = "IsChasing";
    public string action1Trigger = "Action1";
    public string action2Trigger = "Action2";
    public string attackTrigger = "Attack";
    public string damageTrigger = "Damage";

    [Header("Animator Speed (NEW)")]
    public bool driveAnimatorSpeed = true;
    public float walkAnimSpeed = 1.15f;
    public float runAnimSpeed = 1.75f;
    public float sprintAnimSpeed = 2.1f;
    public float attackAnimSpeed = 1.0f;
    public float actionAnimSpeed = 1.0f;
    public float animSpeedDamp = 8f;

    [Header("Lock")]
    public bool forceDisableRootMotion = true;
    public bool useClipLengthForLock = true;
    public float lockExtraBuffer = 0.02f;

    public string action1ClipNameContains = "Action01";
    public string action2ClipNameContains = "Action02";
    public string attackClipNameContains = "Attack";
    public string damageClipNameContains = "Damage";

    enum State { WanderMove, WanderPause, Chase, Attack, Damaged }
    State _state = State.WanderMove;

    Vector3 _wanderTarget;
    float _nextRepathTime;

    float _curSpeed;
    float _targetSpeed;

    float _nextPauseAt;
    float _pauseUntil;
    bool _didDetectAction2;

    float _nextAttackReady;
    float _attackHitAt;
    float _lockUntil;
    bool _damageApplied;

    float _nextDamageReady;

    bool _skipMoveThisFrame;

    float _lastSeenTime = -999f;

    float _chaseStartedAt = -999f;
    float _chaseMaxSpeed;

    void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();

        if (useNavMesh && agent)
        {
            agent.updateRotation = false;
            agent.angularSpeed = chaseTurnSpeed;
            if (animator && forceDisableRootMotion) 
                animator.applyRootMotion = false;
            agent.enabled = false;
        }
        else if (agent)
        {
            agent.enabled = false;
        }

        if (animator && forceDisableRootMotion)
            animator.applyRootMotion = false;

        _chaseMaxSpeed = chaseSpeed;

        PickNewWanderTarget();
        ScheduleNextPause();
    }

    void Start()
    {
        ResolvePlayerTarget();
        
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
                Debug.LogWarning($"[OneEyeAI] {name}이(가) NavMesh 위에 없습니다!");
            }
        }
    }

    bool IsBusyAnim()
    {
        if (!animator) return false;

        var cur = animator.GetCurrentAnimatorStateInfo(0);
        if (cur.IsTag("Action") || cur.IsTag("Attack") || cur.IsTag("Damage"))
            return true;

        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            if (next.IsTag("Action") || next.IsTag("Attack") || next.IsTag("Damage"))
                return true;
        }

        return false;
    }

    void Update()
    {
        ResolvePlayerTarget();

        if (Time.time < _lockUntil)
        {
            _curSpeed = 0f;
            if (useNavMesh && agent && agent.enabled)
                agent.velocity = Vector3.zero;
                
            if (_state == State.Attack) TickAttack();

            if (!useNavMesh || !agent || !agent.enabled)
                StickToGround();
            UpdateAnimatorParams();
            return;
        }

        if (IsBusyAnim())
        {
            _curSpeed = 0f;
            if (useNavMesh && agent && agent.enabled)
                agent.velocity = Vector3.zero;
                
            if (_state == State.Attack) TickAttack();

            if (!useNavMesh || !agent || !agent.enabled)
                StickToGround();
            UpdateAnimatorParams();
            return;
        }

        UpdateStateMachine();

        if (_skipMoveThisFrame)
        {
            _skipMoveThisFrame = false;
            if (!useNavMesh || !agent || !agent.enabled)
                StickToGround();
            UpdateAnimatorParams();
            return;
        }

        switch (_state)
        {
            case State.WanderMove:  DoWanderMove(); break;
            case State.WanderPause: DoWanderPause(); break;
            case State.Chase:       DoChase(); break;
            case State.Attack:      StartAttack(); break;
            case State.Damaged:     break;
        }

        if (!useNavMesh || !agent || !agent.enabled)
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

    bool CanSeePlayer()
    {
        if (!playerTarget) return false;

        float d = FlatDistance(transform.position, playerTarget.position);
        if (d > detectRadius) return false;

        if (!useFieldOfView) return true;

        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 to = Vector3.ProjectOnPlane(playerTarget.position - transform.position, Vector3.up).normalized;
        if (to.sqrMagnitude < 0.0001f) return true;

        float ang = Vector3.Angle(fwd, to);
        if (ang > viewAngle * 0.5f) return false;

        if (requireLineOfSight && !HasLineOfSightToPlayer())
            return false;

        return true;
    }

    bool HasLineOfSightToPlayer()
    {
        if (!playerTarget) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 target = playerTarget.position + Vector3.up * targetHeight;

        Vector3 dir = target - origin;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;

        dir /= dist;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, visionBlockers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == playerTarget || hit.transform.IsChildOf(playerTarget))
                return true;

            return false;
        }

        return true;
    }

    void EnterChaseIfNeeded(State prev)
    {
        if (prev == State.Chase) return;

        _chaseStartedAt = Time.time;
        _chaseMaxSpeed = chaseSpeed;
    }

    void ExitChaseIfNeeded(State next)
    {
        if (next == State.Chase) return;

        _chaseMaxSpeed = chaseSpeed;
        _chaseStartedAt = -999f;
    }

    void UpdateStateMachine()
    {
        if (!playerTarget)
        {
            _didDetectAction2 = false;
            if (_state != State.WanderPause)
            {
                ExitChaseIfNeeded(State.WanderMove);
                _state = State.WanderMove;
            }
            return;
        }

        float d = FlatDistance(transform.position, playerTarget.position);
        bool canSee = CanSeePlayer();
        if (canSee) _lastSeenTime = Time.time;

        if (d <= attackStartRadius && Time.time >= _nextAttackReady)
        {
            ExitChaseIfNeeded(State.Attack);
            _state = State.Attack;
            return;
        }

        if (_state == State.Chase)
        {
            if (d > loseRadius)
            {
                _didDetectAction2 = false;
                ExitChaseIfNeeded(State.WanderMove);
                _state = State.WanderMove;
                return;
            }

            if (useFieldOfView && (Time.time - _lastSeenTime) > forgetTargetAfter)
            {
                _didDetectAction2 = false;
                ExitChaseIfNeeded(State.WanderMove);
                _state = State.WanderMove;
                return;
            }

            return;
        }

        if (canSee)
        {
            State prev = _state;

            if (action2OnDetect && !_didDetectAction2 && Random.value <= action2ChanceOnDetect)
            {
                _didDetectAction2 = true;

                StopNow();
                FireTrigger(action2Trigger);
                LockFor(GetLockSeconds(action2ClipNameContains, 1.2f));
                _skipMoveThisFrame = true;

                EnterChaseIfNeeded(prev);
                _state = State.Chase;
                return;
            }

            EnterChaseIfNeeded(prev);
            _state = State.Chase;
            return;
        }

        if (_state != State.WanderPause)
        {
            ExitChaseIfNeeded(State.WanderMove);
            _state = State.WanderMove;
        }
    }

    void DoWanderMove()
    {
        if (Time.time >= _nextRepathTime)
        {
            _nextRepathTime = Time.time + repathInterval;
            PickNewWanderTarget();
        }

        if (IsDirectionOutsideWanderCone(_wanderTarget))
            PickNewWanderTarget();

        if (Time.time >= _nextPauseAt)
        {
            _state = State.WanderPause;
            _pauseUntil = Time.time + Random.Range(pauseDurationSeconds.x, pauseDurationSeconds.y);

            StopNow();

            float r = Random.value;
            if (r <= action2ChanceOnPause)
            {
                FireTrigger(action2Trigger);
                LockFor(GetLockSeconds(action2ClipNameContains, 1.2f));
                _skipMoveThisFrame = true;
            }
            else if (r <= action2ChanceOnPause + action1ChanceOnPause)
            {
                FireTrigger(action1Trigger);
                LockFor(GetLockSeconds(action1ClipNameContains, 1.0f));
                _skipMoveThisFrame = true;
            }

            return;
        }

        MoveTowards(_wanderTarget, wanderSpeed, wanderAcceleration, arriveDist, wanderTurnSpeed);
    }

    bool IsDirectionOutsideWanderCone(Vector3 targetPos)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 to = Vector3.ProjectOnPlane(targetPos - transform.position, Vector3.up).normalized;
        if (to.sqrMagnitude < 0.0001f) return false;

        float ang = Vector3.Angle(fwd, to);
        return ang > (wanderConeAngle * 0.5f);
    }

    void DoWanderPause()
    {
        _curSpeed = 0f;
        if (useNavMesh && agent && agent.enabled)
            agent.velocity = Vector3.zero;

        if (Time.time >= _pauseUntil)
        {
            ScheduleNextPause();
            _state = State.WanderMove;
            PickNewWanderTarget();
        }
    }

    void DoChase()
    {
        if (!playerTarget) { _state = State.WanderMove; return; }

        float d = FlatDistance(transform.position, playerTarget.position);

        if (d <= attackStartRadius && Time.time >= _nextAttackReady)
        {
            StartAttack();
            return;
        }

        float desiredMax = chaseSpeed;
        if (enableSprint)
        {
            float t = Time.time - _chaseStartedAt;
            if (t >= sprintDelay)
                desiredMax = Mathf.Max(chaseSpeed, sprintSpeed);
        }

        _chaseMaxSpeed = Mathf.MoveTowards(_chaseMaxSpeed, desiredMax, sprintMaxSpeedRamp * Time.deltaTime);

        if (d <= attackStopRadius)
        {
            StopNow();
            if (useNavMesh && agent && agent.enabled)
                agent.velocity = Vector3.zero;
            FaceTarget(playerTarget.position, chaseTurnSpeed);
            return;
        }

        MoveTowards(playerTarget.position, _chaseMaxSpeed, chaseAcceleration, attackStopRadius, chaseTurnSpeed);
    }

    void StartAttack()
    {
        _state = State.Attack;

        StopNow();
        if (useNavMesh && agent && agent.enabled)
            agent.velocity = Vector3.zero;
            
        _damageApplied = false;

        FireTrigger(attackTrigger);

        _attackHitAt = Time.time + attackHitDelay;

        float lockSeconds = GetLockSeconds(attackClipNameContains, 0.65f);
        LockFor(lockSeconds);

        _nextAttackReady = Time.time + lockSeconds + Mathf.Max(0f, attackCooldown);
        _skipMoveThisFrame = true;
    }

    void TickAttack()
    {
        if (!playerTarget) return;

        FaceTarget(playerTarget.position, chaseTurnSpeed);

        if (!_damageApplied && Time.time >= _attackHitAt)
        {
            _damageApplied = true;
            TryApplyDamage();
        }
    }

    void TryApplyDamage()
    {
        if (!playerTarget) return;

        float d = FlatDistance(transform.position, playerTarget.position);
        if (d > attackHitRadius) return;

        var dmg = playerTarget.GetComponentInParent<IDamageable>();
        if (dmg != null) { dmg.TakeDamage(attackDamage); return; }

        var hp = playerTarget.GetComponentInParent<Health>();
        if (hp != null) hp.TakeDamage(attackDamage);
    }

    public void NotifyDamaged()
    {
        if (!animator) return;
        if (Time.time < _nextDamageReady) return;

        _nextDamageReady = Time.time + damageCooldown;

        animator.ResetTrigger(attackTrigger);
        animator.ResetTrigger(action1Trigger);
        animator.ResetTrigger(action2Trigger);

        StopNow();
        if (useNavMesh && agent && agent.enabled)
            agent.velocity = Vector3.zero;
            
        FireTrigger(damageTrigger);

        _state = State.Damaged;
        LockFor(GetLockSeconds(damageClipNameContains, 0.45f));
        _skipMoveThisFrame = true;
    }

    void StopNow() 
    { 
        _curSpeed = 0f;
        _targetSpeed = 0f;
    }

    void MoveTowards(Vector3 worldTarget, float maxSpeed, float accel, float stopDistance, float turnSpeedDeg)
    {
        _targetSpeed = maxSpeed;

        if (_curSpeed < _targetSpeed)
            _curSpeed = Mathf.Min(_curSpeed + accel * Time.deltaTime, _targetSpeed);
        else if (_curSpeed > _targetSpeed)
            _curSpeed = Mathf.Max(_curSpeed - accel * Time.deltaTime, _targetSpeed);

        if (useNavMesh && agent && agent.enabled)
        {
            agent.speed = _curSpeed;
            agent.SetDestination(worldTarget);

            Vector3 dir = agent.desiredVelocity;
            if (dir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDeg * Time.deltaTime);
            }
        }
        else
        {
            Vector3 pos = transform.position;
            Vector3 to = worldTarget - pos;
            to.y = 0f;

            float dist = to.magnitude;

            if (dist <= stopDistance)
            {
                _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, accel * Time.deltaTime);
                return;
            }

            Vector3 dir = to / Mathf.Max(dist, 0.0001f);

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDeg * Time.deltaTime);
            }

            transform.position += transform.forward * (_curSpeed * Time.deltaTime);
        }
    }

    void FaceTarget(Vector3 worldPos, float turnSpeedDeg)
    {
        Vector3 to = worldPos - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(to.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDeg * Time.deltaTime);
    }

    void PickNewWanderTarget()
    {
        float half = wanderConeAngle * 0.5f;
        float yaw = Random.Range(-half, half);
        Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * transform.forward;
        dir = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;

        float dist = Random.Range(wanderRadius * 0.3f, wanderRadius);
        Vector3 candidate = transform.position + dir * dist;
        
        if (useNavMesh && NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            _wanderTarget = hit.position;
        }
        else
        {
            _wanderTarget = candidate;
        }
    }

    void ScheduleNextPause()
    {
        _nextPauseAt = Time.time + Random.Range(pauseEverySeconds.x, pauseEverySeconds.y);
    }

    void LockFor(float seconds)
    {
        _lockUntil = Time.time + seconds;
    }

    float GetLockSeconds(string clipNameContains, float fallbackSeconds)
    {
        float baseLen = fallbackSeconds;

        if (useClipLengthForLock && animator && animator.runtimeAnimatorController)
        {
            var clips = animator.runtimeAnimatorController.animationClips;
            float best = -1f;

            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (!c) continue;
                if (string.IsNullOrEmpty(clipNameContains)) continue;
                if (!c.name.Contains(clipNameContains)) continue;

                if (c.length > best) best = c.length;
            }

            if (best > 0f) baseLen = best;
        }

        return baseLen + lockExtraBuffer;
    }

    void FireTrigger(string trig)
    {
        if (!animator || string.IsNullOrEmpty(trig)) return;
        animator.SetTrigger(trig);
    }

    void UpdateAnimatorParams()
    {
        if (!animator) return;

        bool chasing = (_state == State.Chase);
        animator.SetBool(isChasingParam, chasing);

        float denom = Mathf.Max(0.001f, (chasing ? _chaseMaxSpeed : wanderSpeed));
        float speed01 = Mathf.Clamp01(_curSpeed / denom);
        animator.SetFloat(speedParam, speed01);

        if (!driveAnimatorSpeed) return;

        float targetAnim = 1f;

        if (_state == State.Attack) targetAnim = attackAnimSpeed;
        else if (_state == State.Damaged) targetAnim = 1f;
        else
        {
            bool moving = _curSpeed > 0.05f;

            if (!moving)
            {
                targetAnim = 1f;
            }
            else if (!chasing)
            {
                targetAnim = Mathf.Lerp(1.0f, walkAnimSpeed, speed01);
            }
            else
            {
                float sprint01 = 0f;
                if (enableSprint)
                {
                    float maxDen = Mathf.Max(0.001f, sprintSpeed - chaseSpeed);
                    sprint01 = Mathf.Clamp01((_chaseMaxSpeed - chaseSpeed) / maxDen);
                }

                float runToSprint = Mathf.Lerp(runAnimSpeed, sprintAnimSpeed, sprint01);
                targetAnim = Mathf.Lerp(walkAnimSpeed, runToSprint, speed01);
            }
        }

        float k = 1f - Mathf.Exp(-animSpeedDamp * Time.deltaTime);
        animator.speed = Mathf.Lerp(animator.speed, targetAnim, k);
    }

    void StickToGround()
    {
        Vector3 pos = transform.position;
        Vector3 origin = pos + Vector3.up * 50f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            pos.y = hit.point.y + yOffset;
            transform.position = pos;
        }
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position + Vector3.up * 0.05f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p, detectRadius);

        Gizmos.color = new Color(1f, 0.55f, 0f, 1f);
        Gizmos.DrawWireSphere(p, loseRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(p, attackStartRadius);

        Gizmos.color = new Color(1f, 0f, 1f, 1f);
        Gizmos.DrawWireSphere(p, attackStopRadius);

        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawWireSphere(p, attackHitRadius);

        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        Gizmos.DrawWireSphere(p, wanderRadius);

        if (useFieldOfView)
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            float half = viewAngle * 0.5f;
            Vector3 left = Quaternion.AngleAxis(-half, Vector3.up) * transform.forward;
            Vector3 right = Quaternion.AngleAxis(+half, Vector3.up) * transform.forward;

            Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
            Gizmos.DrawLine(eye, eye + left.normalized * detectRadius);
            Gizmos.DrawLine(eye, eye + right.normalized * detectRadius);
        }
    }
}