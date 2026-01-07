using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class RakeAI : MonoBehaviour
{
    private enum State
    {
        WanderMove,
        WanderWait,
        Chase,
        BattleIdle,      // 공격 타이밍 기다리는 상태
        AttackApproach,  // 타이밍 됐을 때 달려가서 공격 거리까지 접근
        Attack,
        Hit,
        Stun,
        Dead
    }

    [Header("Target")]
    public Transform target;
    public string playerTag = "Player";

    [Header("Home / Wander")]
    public float wanderRadius = 6f;
    public float wanderWaitMin = 0.8f;
    public float wanderWaitMax = 2.0f;
    public float wanderArriveDistance = 0.6f;

    [Header("Vision (Detect)")]
    public float detectRadius = 14f;
    public float loseRadius = 22f;
    [Range(0f, 180f)] public float fovAngle = 120f;
    public bool useFOV = true;

    public bool requireLineOfSight = false;
    public LayerMask visionBlockers = ~0;
    public float eyeHeight = 1.2f;
    public float targetHeight = 1.2f;

    [Header("Combat Ranges (Hysteresis)")]
    [Tooltip("이 거리 안으로 들어오면 BattleIdle로 '진입' (작게)")]
    public float battleEnterRadius = 7.5f;

    [Tooltip("BattleIdle 상태에서 이 거리까지는 '유지' (진입보다 크게)")]
    public float battleKeepRadius = 9.0f;

    [Tooltip("이 거리 안에서 멈춰서 공격 시작")]
    public float attackStopRadius = 2.0f;

    [Tooltip("실제 데미지 판정 거리(Stop보다 약간 크게 추천)")]
    public float attackHitRadius = 3.2f;

    [Header("Attack Timing Loop")]
    public Vector2 battleIdleWaitRange = new Vector2(0.6f, 1.6f); // 다음 공격까지 랜덤 대기
    public float approachGiveUpMultiplier = 1.25f; // 접근 중 (battleKeep*mult) 넘어가면 추격 복귀

    [Header("Movement Speeds")]
    public float wanderSpeed = 2.0f;
    public float wanderAcceleration = 3.0f;

    public float chaseSpeed = 4.5f;
    public float chaseAcceleration = 8.0f;
    public float repathInterval = 0.2f;

    public float approachSpeed = 7.0f;
    public float approachAcceleration = 18.0f;

    [Header("Rotation")]
    public float turnSpeedDeg = 480f;
    [Tooltip("모델 forward 축이 +Z가 아니면 90 / -90 / 180 같은 값으로 보정")]
    public float modelYawOffset = 0f;

    [Header("Head Look IK (Optional)")]
    public bool useHeadLookIK = true;
    [Range(0f, 1f)] public float lookWeight = 0.75f;
    public float lookLerpSpeed = 10f;
    public float lookAtHeight = 1.2f;
    private float lookWeightCurrent;

    [Header("Combat Damage")]
    public int attackDamage = 15;
    public float attackHitDelay = 0.3f;
    public float attackCooldownExtra = 0.4f;

    [Header("Animator Params")]
    public string pSpeed = "Speed";     // BlendTree 0(Idle)-0.5(Walk)-1(Run)
    public string pBattle = "Battle";   // battleidle 진입용
    public string pAttackID = "AttackID";
    public string tAttack = "DoAttack";
    public string tHit = "Hit";
    public string tStun = "Sturn";
    public string tDie = "Die";

    [Header("BlendTree Mapping")]
    public float walkSpeedRef = -1f; // Speed=0.5 기준(기본 wanderSpeed)
    public float runSpeedRef = -1f;  // Speed=1.0 기준(기본 chaseSpeed)

    [Header("Speed Smoothing")]
    public float speedSmoothUp = 0.12f;
    public float speedSmoothDown = 0.18f;

    [Header("Animation Lock")]
    public bool useClipLengthForLock = true;
    public float lockExtraBuffer = 0.05f;
    public string attack1ClipContains = "attack1";
    public string attack2ClipContains = "attack2";
    public string hitClipContains = "hit";
    public string stunClipContains = "sturn";

    // ===== internals =====
    private NavMeshAgent agent;
    private Animator anim;
    private State state;

    private Vector3 homePos;

    private float nextRepathTime;
    private float wanderWaitUntil;

    private float lockUntil;
    private float nextAttackReady;
    private float nextAttackRollAt;

    private float attackHitAt;
    private bool damageApplied;

    private float speedParamCurrent;
    private float speedParamVel;

    private int hSpeed, hBattle, hAttackID, hAttack, hHit, hStun, hDie;

    private bool agentReady => agent != null && agent.enabled && agent.isOnNavMesh;

    private float nextTargetRefreshTime;

    private void OnValidate()
    {
        detectRadius = Mathf.Max(0.1f, detectRadius);
        loseRadius = Mathf.Max(detectRadius + 0.1f, loseRadius);

        battleEnterRadius = Mathf.Max(0.1f, battleEnterRadius);
        battleKeepRadius = Mathf.Max(battleEnterRadius + 0.25f, battleKeepRadius);

        attackStopRadius = Mathf.Max(0.1f, attackStopRadius);
        attackHitRadius = Mathf.Max(attackStopRadius + 0.1f, attackHitRadius);

        wanderRadius = Mathf.Max(0.1f, wanderRadius);
        wanderArriveDistance = Mathf.Max(0.05f, wanderArriveDistance);

        wanderSpeed = Mathf.Max(0.1f, wanderSpeed);
        chaseSpeed = Mathf.Max(0.1f, chaseSpeed);
        approachSpeed = Mathf.Max(0.1f, approachSpeed);

        repathInterval = Mathf.Max(0.02f, repathInterval);
        battleIdleWaitRange.x = Mathf.Max(0.05f, battleIdleWaitRange.x);
        battleIdleWaitRange.y = Mathf.Max(battleIdleWaitRange.x + 0.05f, battleIdleWaitRange.y);
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponent<Animator>();

        if (agent)
        {
            agent.updateRotation = false;
            agent.autoBraking = true;
            if (anim) anim.applyRootMotion = false;
        }

        homePos = transform.position;

        hSpeed = Animator.StringToHash(pSpeed);
        hBattle = Animator.StringToHash(pBattle);
        hAttackID = Animator.StringToHash(pAttackID);
        hAttack = Animator.StringToHash(tAttack);
        hHit = Animator.StringToHash(tHit);
        hStun = Animator.StringToHash(tStun);
        hDie = Animator.StringToHash(tDie);
    }

    private void Start()
    {
        AcquireTargetIfMissing(true);
        EnterWanderMove();
    }

    private void Update()
    {
        if (state == State.Dead) return;

        AcquireTargetIfMissing(false);

        // Lock 중: 이동 금지 + 시선
        if (Time.time < lockUntil)
        {
            HardStop();
            if (target) FaceWorldPos(target.position);

            if (state == State.Attack) TickAttack();
            UpdateAnimatorSpeed();
            return;
        }

        float dist = target ? FlatDistance(transform.position, target.position) : float.MaxValue;

        // 전역 감지
        bool seesPlayer = CanDetectTarget();

        if ((state == State.WanderMove || state == State.WanderWait) && seesPlayer)
            EnterChase();

        if ((state == State.Chase || state == State.BattleIdle || state == State.AttackApproach) && !TargetValidForCombat())
            EnterWanderMove();

        switch (state)
        {
            case State.WanderMove: UpdateWanderMove(); break;
            case State.WanderWait: UpdateWanderWait(); break;
            case State.Chase: UpdateChase(dist); break;
            case State.BattleIdle: UpdateBattleIdle(dist); break;
            case State.AttackApproach: UpdateAttackApproach(dist); break;

            case State.Attack:
                // lock이 풀린 프레임이면 battleIdle 복귀(다시 타이밍 기다리기)
                EnterBattleIdle(resetTimer: true);
                break;

            case State.Hit:
            case State.Stun:
                if (TargetValidForCombat()) EnterChase();
                else EnterWanderMove();
                break;
        }

        UpdateAnimatorSpeed();
    }

    // ===================== Detection =====================

    private void AcquireTargetIfMissing(bool force)
    {
        if (target != null) return;

        if (!force && Time.time < nextTargetRefreshTime) return;
        nextTargetRefreshTime = Time.time + 1.0f;

        var go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) target = go.transform;
    }

    private bool TargetValidForCombat()
    {
        if (target == null) return false;
        float d = FlatDistance(transform.position, target.position);
        return d <= loseRadius;
    }

    private bool CanDetectTarget()
    {
        if (target == null) return false;

        float d = FlatDistance(transform.position, target.position);
        if (d > detectRadius) return false;

        Vector3 to = target.position - transform.position;
        to.y = 0f;

        if (useFOV)
        {
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;

            float ang = Vector3.Angle(fwd.normalized, to.normalized);
            if (ang > fovAngle * 0.5f) return false;
        }

        if (requireLineOfSight && !HasLineOfSight())
            return false;

        return true;
    }

    private bool HasLineOfSight()
    {
        if (target == null) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 dest = target.position + Vector3.up * targetHeight;

        Vector3 dir = dest - origin;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;
        dir /= dist;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, visionBlockers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == target || hit.transform.IsChildOf(target)) return true;
            return false;
        }

        return true;
    }

    // ===================== Wander =====================

    private void EnterWanderMove()
    {
        state = State.WanderMove;
        anim.SetBool(hBattle, false);

        ConfigureAgent(wanderSpeed, wanderAcceleration, 0.2f);
        SetRandomWanderDestination();
    }

    private void UpdateWanderMove()
    {
        if (!agentReady) return;

        FaceMoveDirection();

        if (!agent.pathPending && agent.remainingDistance <= wanderArriveDistance)
        {
            state = State.WanderWait;
            SoftStopToCurrentPos();
            wanderWaitUntil = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
        }
    }

    private void UpdateWanderWait()
    {
        SoftStopToCurrentPos();

        if (Time.time >= wanderWaitUntil)
            EnterWanderMove();
    }

    private void SetRandomWanderDestination()
    {
        if (!agentReady) return;

        for (int i = 0; i < 20; i++)
        {
            Vector3 rand = homePos + Random.insideUnitSphere * wanderRadius;
            rand.y = homePos.y;

            if (NavMesh.SamplePosition(rand, out var hit, 2.0f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }

        agent.SetDestination(homePos);
    }

    // ===================== Chase =====================

    private void EnterChase()
    {
        state = State.Chase;
        anim.SetBool(hBattle, false);

        ConfigureAgent(chaseSpeed, chaseAcceleration, battleEnterRadius);
        nextRepathTime = 0f;
    }

    private void UpdateChase(float dist)
    {
        if (!agentReady) return;

        FaceMoveDirection();

        if (target != null && Time.time >= nextRepathTime)
        {
            agent.SetDestination(target.position);
            nextRepathTime = Time.time + repathInterval;
        }

        // ✅ 진입은 더 작은 battleEnterRadius로
        if (target != null && dist <= battleEnterRadius)
        {
            EnterBattleIdle(resetTimer: true);
            return;
        }
    }

    // ===================== Battle Idle =====================

    private void EnterBattleIdle(bool resetTimer)
    {
        state = State.BattleIdle;
        anim.SetBool(hBattle, true);

        SoftStopToCurrentPos();
        if (target) FaceWorldPos(target.position);

        if (resetTimer)
        {
            float t = Random.Range(battleIdleWaitRange.x, battleIdleWaitRange.y);
            nextAttackRollAt = Time.time + t;
            nextAttackReady = Mathf.Max(nextAttackReady, Time.time);
        }

        anim.CrossFade("Base Layer.battleidle", 0.06f);
    }

    private void UpdateBattleIdle(float dist)
    {
        if (!agentReady) return;

        if (target) FaceWorldPos(target.position);

        // ✅ 유지는 더 큰 battleKeepRadius로
        if (target == null || dist > battleKeepRadius)
        {
            EnterChase();
            return;
        }

        if (Time.time < nextAttackRollAt) return;
        if (Time.time < nextAttackReady) return;

        EnterAttackApproach();
    }

    // ===================== Attack Approach =====================

    private void EnterAttackApproach()
    {
        state = State.AttackApproach;
        anim.SetBool(hBattle, false);

        ConfigureAgent(approachSpeed, approachAcceleration, attackStopRadius);

        if (target)
            agent.SetDestination(target.position);
    }

    private void UpdateAttackApproach(float dist)
    {
        if (!agentReady) return;

        FaceMoveDirection();

        if (target)
            agent.SetDestination(target.position);

        // 접근 중 너무 멀어지면 추적으로 복귀(keep 기준)
        if (target == null || dist > battleKeepRadius * approachGiveUpMultiplier)
        {
            EnterChase();
            return;
        }

        if (target != null && dist <= attackStopRadius)
        {
            StartAttack();
        }
    }

    // ===================== Attack =====================

    private void StartAttack()
    {
        if (target == null) { EnterChase(); return; }

        state = State.Attack;

        anim.SetBool(hBattle, true);
        anim.CrossFade("Base Layer.battleidle", 0.04f);

        HardStop();
        if (target) FaceWorldPos(target.position);

        damageApplied = false;

        int id = Random.value < 0.5f ? 1 : 2;
        anim.SetInteger(hAttackID, id);
        anim.ResetTrigger(hAttack);
        anim.SetTrigger(hAttack);

        attackHitAt = Time.time + attackHitDelay;

        string clipContains = (id == 1) ? attack1ClipContains : attack2ClipContains;
        float lockTime = GetLockSeconds(clipContains, 0.8f);

        lockUntil = Time.time + lockTime;
        nextAttackReady = Time.time + lockTime + attackCooldownExtra;
    }

    private void TickAttack()
    {
        if (target) FaceWorldPos(target.position);

        if (!damageApplied && Time.time >= attackHitAt)
        {
            damageApplied = true;
            TryApplyDamage();
        }
    }

    private void TryApplyDamage()
    {
        if (target == null) return;

        float d = FlatDistance(transform.position, target.position);
        if (d > attackHitRadius) return;

        var dmg = target.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(attackDamage, gameObject);
            return;
        }

        var hp = target.GetComponentInParent<Health>();
        if (hp != null)
        {
            hp.TakeDamage(attackDamage, gameObject);
            return;
        }

        Debug.LogWarning($"{name}: Target {target.name} has no IDamageable or Health component!");
    }

    // ===================== Damage Reaction (Optional hooks) =====================

    public void NotifyDamaged()
    {
        if (state == State.Dead) return;

        state = State.Hit;
        HardStop();

        anim.SetBool(hBattle, true);
        anim.SetTrigger(hHit);

        float lockTime = GetLockSeconds(hitClipContains, 0.5f);
        lockUntil = Time.time + lockTime;
    }

    public void TriggerStun()
    {
        if (state == State.Dead) return;

        state = State.Stun;
        HardStop();

        anim.SetBool(hBattle, true);
        anim.SetTrigger(hStun);

        float lockTime = GetLockSeconds(stunClipContains, 1.0f);
        lockUntil = Time.time + lockTime;
    }

    public void TriggerDie()
    {
        if (state == State.Dead) return;

        state = State.Dead;
        HardStop();

        anim.SetBool(hBattle, false);
        anim.SetTrigger(hDie);
    }

    // ===================== Movement helpers =====================

    private void ConfigureAgent(float speed, float accel, float stoppingDistance)
    {
        if (!agentReady) return;
        agent.isStopped = false;
        agent.speed = speed;
        agent.acceleration = accel;
        agent.stoppingDistance = stoppingDistance;
    }

    private void HardStop()
    {
        if (!agentReady) return;
        agent.isStopped = true;
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    private void SoftStopToCurrentPos()
    {
        if (!agentReady) return;

        agent.isStopped = false;
        agent.stoppingDistance = 0f;
        agent.SetDestination(transform.position);

        if (agent.velocity.sqrMagnitude < 0.02f * 0.02f)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    // velocity → desiredVelocity → steeringTarget 순 회전(스트레이프 방지)
    private void FaceMoveDirection()
    {
        if (!agentReady) return;

        Vector3 dir = agent.velocity;

        if (dir.sqrMagnitude < 0.0004f) dir = agent.desiredVelocity;
        if (dir.sqrMagnitude < 0.0004f && agent.hasPath)
            dir = agent.steeringTarget - transform.position;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0004f) return;

        Quaternion rot = Quaternion.LookRotation(dir.normalized);
        rot *= Quaternion.Euler(0f, modelYawOffset, 0f);

        transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, turnSpeedDeg * Time.deltaTime);
    }

    private void FaceWorldPos(Vector3 worldPos)
    {
        Vector3 dir = worldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion rot = Quaternion.LookRotation(dir.normalized);
        rot *= Quaternion.Euler(0f, modelYawOffset, 0f);

        transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, turnSpeedDeg * Time.deltaTime);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ===================== Animator Speed =====================

    private void UpdateAnimatorSpeed()
    {
        if (!anim) return;

        float targetParam = 0f;

        bool locomotion =
            state == State.WanderMove ||
            state == State.Chase ||
            state == State.AttackApproach;

        if (locomotion && agentReady)
        {
            float v = agent.velocity.magnitude;

            float walkRef = (walkSpeedRef > 0f) ? walkSpeedRef : wanderSpeed;
            float runRef = (runSpeedRef > 0f) ? runSpeedRef : chaseSpeed;

            targetParam = VelocityToBlendParam(v, walkRef, runRef);
        }
        else
        {
            targetParam = 0f;
        }

        float smooth = (targetParam > speedParamCurrent) ? speedSmoothUp : speedSmoothDown;
        speedParamCurrent = Mathf.SmoothDamp(speedParamCurrent, targetParam, ref speedParamVel, Mathf.Max(0.0001f, smooth));
        speedParamCurrent = Mathf.Clamp01(speedParamCurrent);

        anim.SetFloat(hSpeed, speedParamCurrent);
    }

    private float VelocityToBlendParam(float v, float walkRef, float runRef)
    {
        v = Mathf.Max(0f, v);
        walkRef = Mathf.Max(0.01f, walkRef);
        runRef = Mathf.Max(walkRef + 0.01f, runRef);

        if (v <= walkRef)
            return Mathf.InverseLerp(0f, walkRef, v) * 0.5f;

        float t = Mathf.InverseLerp(walkRef, runRef, Mathf.Min(v, runRef));
        return 0.5f + t * 0.5f;
    }

    private float GetLockSeconds(string clipNameContains, float fallbackSeconds)
    {
        float baseLen = fallbackSeconds;

        if (useClipLengthForLock && anim && anim.runtimeAnimatorController)
        {
            var clips = anim.runtimeAnimatorController.animationClips;
            float best = -1f;

            foreach (var c in clips)
            {
                if (!c) continue;
                if (string.IsNullOrEmpty(clipNameContains)) continue;
                if (!c.name.Contains(clipNameContains)) continue;
                if (c.length > best) best = c.length;
            }

            if (best > 0f) baseLen = best;
        }

        return baseLen + lockExtraBuffer;
    }

    // ===================== Head Look IK =====================

    private void OnAnimatorIK(int layerIndex)
    {
        if (!useHeadLookIK || !anim) return;

        bool shouldLook =
            target != null &&
            (state == State.Chase || state == State.BattleIdle || state == State.AttackApproach || state == State.Attack);

        float targetW = shouldLook ? lookWeight : 0f;
        lookWeightCurrent = Mathf.Lerp(lookWeightCurrent, targetW, Time.deltaTime * lookLerpSpeed);

        anim.SetLookAtWeight(lookWeightCurrent, 0.2f, 0.9f, 1.0f, 0.55f);

        if (target != null)
            anim.SetLookAtPosition(target.position + Vector3.up * lookAtHeight);
    }

    // ===================== Gizmos =====================

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position + Vector3.up * 0.05f;

        // Wander radius
        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Gizmos.DrawWireSphere(homePos == Vector3.zero ? transform.position : homePos, wanderRadius);

        // Detect / Lose
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p, detectRadius);

        Gizmos.color = new Color(1f, 0.55f, 0f, 1f);
        Gizmos.DrawWireSphere(p, loseRadius);

        // Battle enter/keep (히스테리시스)
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(p, battleEnterRadius);

        Gizmos.color = new Color(0.1f, 0.7f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(p, battleKeepRadius);

        // Attack stop/hit
        Gizmos.color = new Color(1f, 0f, 1f, 1f);
        Gizmos.DrawWireSphere(p, attackStopRadius);

        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawWireSphere(p, attackHitRadius);

        // FOV
        if (useFOV)
        {
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            float half = fovAngle * 0.5f;
            Vector3 left = Quaternion.Euler(0f, -half, 0f) * fwd;
            Vector3 right = Quaternion.Euler(0f, half, 0f) * fwd;

            Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
            Gizmos.DrawLine(p, p + left * detectRadius);
            Gizmos.DrawLine(p, p + right * detectRadius);
        }
    }
#endif
}
