using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class BloodSnifferAI : MonoBehaviour
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
    public float yOffset = 0.02f;

    [Header("Crawl Wander")]
    public float crawlWanderRadius = 10f;
    public float crawlRepathInterval = 3f;
    public float crawlArriveDist = 0.6f;
    public float crawlSpeed = 1.5f;
    public float crawlAcceleration = 4f;
    public float crawlTurnSpeed = 120f;
    [Range(30f, 180f)] public float crawlConeAngle = 120f;

    [Header("Crawl Speed Smoothing")]
    // ✅ "멈출 때" 감속 속도(이 값이 작을수록 천천히 멈춤)
    public float crawlDeceleration = 6f;
    // ✅ 애니 BlendTree Speed 파라미터 댐핑(작을수록 부드럽게)
    public float speedParamDampTime = 0.12f;

    [Header("Crawl Pause / Sniff")]
    public Vector2 pauseEverySeconds = new Vector2(3f, 7f);
    public Vector2 pauseDurationSeconds = new Vector2(0.8f, 2f);
    [Range(0f, 1f)] public float sniffChanceOnPause = 0.3f;

    [Header("Blood Scent Detection")]
    public float scentCheckInterval = 2f;
    public float scentDetectRadius = 50f;
    public float minScentIntensity = 0.2f;
    public float scentArriveDistance = 2f;
    public float crawlToScentSpeed = 2.5f;
    public float crawlToScentMaxSpeed = 4.0f;
    public float crawlSpeedRampRate = 2f;

    [Header("Player Detection")]
    public float detectRadius = 15f;
    public float loseRadius = 22f;
    public bool requireLineOfSight = false;
    public LayerMask visionBlockers = ~0;
    public float eyeHeight = 1.0f;
    public float targetHeight = 1.2f;

    [Header("Stand Chase")]
    public float standChaseSpeed = 4.5f;
    public float standChaseMaxSpeed = 7.0f;
    public float standChaseAcceleration = 12f;
    public float standChaseTurnSpeed = 300f;
    public float standSpeedRampRate = 4f;
    public float roarDelay = 1.2f;

    [Header("Stand Stop Smoothing")]
    public float standChaseDeceleration = 18f;

    [Header("Attack")]
    public float attackStopRadius = 1.8f;
    public float attackStartRadius = 2.2f;
    public float attackHitRadius = 4.0f;
    public int punchDamage = 15;
    public int biteDamage = 25;
    [Range(0f, 1f)] public float biteChance = 0.4f;
    public float attackCooldown = 0.5f;
    public float attackHitDelay = 0.3f;

    [Header("Attack - Combo Clip Slicing")]
    public bool sliceComboClips = true;
    [Range(0.05f, 1f)] public float punchClipFraction = 0.25f;     // 4연타 중 1타
    [Range(0.05f, 1f)] public float biteClipFraction = 0.3333f;    // 3연타 중 1타
    public float minAttackWindow = 0.18f;

    [Header("Attack - Cancel Option")]
    public bool cancelAttackIfTargetFar = true;
    public float cancelCheckGrace = 0.05f;
    public float cancelDistanceMultiplier = 1.6f;
    public float cancelCooldown = 0.15f;

    [Header("Meat Detection")]
    public string meatTag = "Meat";
    public float meatDetectRadius = 5f;
    public float meatArriveDistance = 1f;

    [Header("Damage Reaction")]
    public float damageCooldown = 0.2f;

    [Header("Animator Params")]
    public string stanceParam = "Stance";
    public string speedParam = "Speed";
    public string standWalkStyleParam = "StandWalkStyle";
    public string sniffTrigger = "Sniff";
    public string eatTrigger = "Eat";
    public string roarTrigger = "Roar";
    public string punchTrigger = "Punch";
    public string biteTrigger = "Bite";
    public string damageTrigger = "Damage";

    [Header("Animator Speed")]
    public bool driveAnimatorSpeed = true;
    public float crawlAnimSpeed = 1.0f;
    public float crawlRunAnimSpeed = 1.8f;
    public float standWalkAnimSpeed = 1.2f;
    public float standRunAnimSpeed = 2.0f;
    public float actionAnimSpeed = 1.0f;
    public float animSpeedDamp = 8f;

    [Header("Lock Settings")]
    public bool useClipLengthForLock = true;
    public float lockExtraBuffer = 0.05f;
    public string sniffClipContains = "Sniff";
    public string eatClipContains = "Eating";
    public string roarClipContains = "Roar";
    public string punchClipContains = "Punch";
    public string biteClipContains = "Bite";
    public string damageClipContains = "Damage";

    [Header("Force Exit Action (State Names)")]
    public string crouchLocomotionState = "BT_CrouchLocomotion";
    public string standLocomotionState = "BT_StandLocomotion_Walk1";
    public float locomotionCrossFade = 0.05f;

    enum State
    {
        CrawlWander,
        CrawlPause,
        CrawlToScent,
        StandChase,
        Attack,
        Eating,
        Damaged
    }
    State _state = State.CrawlWander;

    Vector3 _wanderTarget;
    float _nextRepathTime;
    float _curSpeed;
    float _targetSpeed;
    float _crawlToScentSpeed;
    float _standChaseSpeed;

    float _nextPauseAt;
    float _pauseUntil;

    BloodScent _currentScent;
    float _nextScentCheckTime;

    Transform _currentMeat;

    bool _hasRoared;
    float _roarCompleteTime;

    float _nextAttackReady;
    float _attackHitAt;
    bool _damageApplied;
    bool _isAttackingWithBite;

    float _lockUntil;
    float _nextDamageReady;
    bool _skipMoveThisFrame;

    float _attackEndAt;
    float _attackCanCancelAfter;
    float _ignoreBusyUntil;

    void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();

        if (useNavMesh && agent)
        {
            agent.updateRotation = false;
            agent.angularSpeed = standChaseTurnSpeed;
            if (animator) animator.applyRootMotion = false;
            agent.enabled = false;
        }

        _crawlToScentSpeed = crawlToScentSpeed;
        _standChaseSpeed = standChaseSpeed;

        PickNewWanderTarget();
        ScheduleNextPause();
    }

    void Start()
    {
        ResolvePlayerTarget();

        if (useNavMesh && agent)
            Invoke(nameof(EnableAgent), 0.5f);

        SetStance(2); // Crawl
    }

    void EnableAgent()
    {
        if (agent && useNavMesh)
        {
            agent.enabled = true;
            if (!agent.isOnNavMesh)
                Debug.LogWarning($"[BloodSnifferAI] {name}가 NavMesh 위에 없습니다!");
        }
    }

    bool IsBusyAnim()
    {
        if (!animator) return false;
        if (Time.time < _ignoreBusyUntil) return false;

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

        // Lock 중: ✅ 배회/기어다닐 때는 speed를 0으로 박지 말고 감속
        if (Time.time < _lockUntil)
        {
            SoftStopByState();
            if (_state == State.Attack) TickAttack();

            StickToGroundIfNeeded();
            UpdateAnimatorParams();
            return;
        }

        // BusyAnim 중: ✅ 배회/기어다닐 때는 speed를 0으로 박지 말고 감속
        if (IsBusyAnim())
        {
            SoftStopByState();
            if (_state == State.Attack) TickAttack();

            StickToGroundIfNeeded();
            UpdateAnimatorParams();
            return;
        }

        if (_state == State.Attack)
        {
            _state = State.StandChase;
            _curSpeed = 0f;
            _targetSpeed = 0f;
        }

        if (_state == State.Damaged)
        {
            if (animator && animator.GetInteger(stanceParam) == 1) _state = State.StandChase;
            else _state = State.CrawlWander;
        }

        UpdateStateMachine();

        if (_skipMoveThisFrame)
        {
            _skipMoveThisFrame = false;
            StickToGroundIfNeeded();
            UpdateAnimatorParams();
            return;
        }

        switch (_state)
        {
            case State.CrawlWander:  DoCrawlWander(); break;
            case State.CrawlPause:   DoCrawlPause(); break;
            case State.CrawlToScent: DoCrawlToScent(); break;
            case State.StandChase:   DoStandChase(); break;
            case State.Attack:       DoAttack(); break;
            case State.Eating:       DoEating(); break;
            case State.Damaged:      break;
        }

        StickToGroundIfNeeded();
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
        if (playerTarget && CanSeePlayer())
        {
            if (_state != State.StandChase)
            {
                EnterStandChase();
                return;
            }
            return;
        }

        if (_state == State.StandChase)
        {
            float d = playerTarget ? FlatDistance(transform.position, playerTarget.position) : float.MaxValue;
            if (d > loseRadius || !playerTarget)
            {
                ExitStandChase();
                _state = State.CrawlWander;
                return;
            }
        }

        if (_state == State.CrawlWander || _state == State.CrawlPause || _state == State.CrawlToScent)
        {
            Transform meat = FindNearestMeat();
            if (meat != null)
            {
                _currentMeat = meat;
                _state = State.Eating;
                return;
            }
        }

        if (_state == State.CrawlWander || _state == State.CrawlPause)
        {
            if (Time.time >= _nextScentCheckTime)
            {
                _nextScentCheckTime = Time.time + scentCheckInterval;

                BloodScent scent = BloodScentManager.GetNearestScent(transform.position, minScentIntensity);
                if (scent != null)
                {
                    float dist = Vector3.Distance(transform.position, scent.position);
                    if (dist <= scentDetectRadius)
                    {
                        _currentScent = scent;

                        // ✅ "StopNow()" 삭제: 감속 + 소프트 정지
                        RequestStopSoft(IsStandStance() ? standChaseDeceleration : crawlDeceleration);

                        FireTrigger(sniffTrigger);
                        LockFor(GetLockSeconds(sniffClipContains, 1.0f));
                        _skipMoveThisFrame = true;

                        _state = State.CrawlToScent;
                        _crawlToScentSpeed = crawlToScentSpeed;
                        return;
                    }
                }
            }
        }

        if (_state == State.CrawlToScent)
        {
            if (_currentScent == null || _currentScent.IsExpired())
            {
                _currentScent = null;
                _state = State.CrawlWander;
                return;
            }

            float dist = Vector3.Distance(transform.position, _currentScent.position);
            if (dist <= scentArriveDistance)
            {
                _currentScent = null;
                _state = State.CrawlWander;
                PickNewWanderTarget();
                return;
            }
        }
    }

    bool CanSeePlayer()
    {
        if (!playerTarget) return false;

        float d = FlatDistance(transform.position, playerTarget.position);
        if (d > detectRadius) return false;

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

    Transform FindNearestMeat()
    {
        if (string.IsNullOrEmpty(meatTag)) return null;

        GameObject[] meats = GameObject.FindGameObjectsWithTag(meatTag);
        Transform nearest = null;
        float minDist = meatDetectRadius;

        foreach (var meat in meats)
        {
            float d = Vector3.Distance(transform.position, meat.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = meat.transform;
            }
        }
        return nearest;
    }

    void EnterStandChase()
    {
        SetStance(1);
        _hasRoared = false;
        _standChaseSpeed = standChaseSpeed;

        // 포효는 “딱 멈춰서” 하는 게 자연스러워서 하드 스톱 유지
        StopNowHard();
        StopAgentHard();

        FireTrigger(roarTrigger);
        LockFor(GetLockSeconds(roarClipContains, roarDelay));
        _roarCompleteTime = Time.time + roarDelay;
        _skipMoveThisFrame = true;

        _state = State.StandChase;
    }

    void ExitStandChase()
    {
        SetStance(2);
        _hasRoared = false;
    }

    void DoCrawlWander()
    {
        if (Time.time >= _nextRepathTime)
        {
            _nextRepathTime = Time.time + crawlRepathInterval;
            PickNewWanderTarget();
        }

        if (IsDirectionOutsideWanderCone(_wanderTarget))
            PickNewWanderTarget();

        if (Time.time >= _nextPauseAt)
        {
            _state = State.CrawlPause;
            _pauseUntil = Time.time + Random.Range(pauseDurationSeconds.x, pauseDurationSeconds.y);

            // ✅ 여기서 _curSpeed=0 박지 말기
            RequestStopSoft(crawlDeceleration);

            if (Random.value <= sniffChanceOnPause)
            {
                FireTrigger(sniffTrigger);
                LockFor(GetLockSeconds(sniffClipContains, 1.0f));
                _skipMoveThisFrame = true;
            }
            return;
        }

        // ✅ 가속/감속 분리 적용
        MoveTowards(_wanderTarget, crawlSpeed, crawlAcceleration, crawlDeceleration, crawlArriveDist, crawlTurnSpeed);
    }

    bool IsDirectionOutsideWanderCone(Vector3 targetPos)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 to = Vector3.ProjectOnPlane(targetPos - transform.position, Vector3.up).normalized;
        if (to.sqrMagnitude < 0.0001f) return false;

        float ang = Vector3.Angle(fwd, to);
        return ang > (crawlConeAngle * 0.5f);
    }

    void DoCrawlPause()
    {
        // ✅ Pause 동안에도 부드럽게 감속해서 0으로 수렴
        RequestStopSoft(crawlDeceleration);

        if (Time.time >= _pauseUntil)
        {
            ScheduleNextPause();
            _state = State.CrawlWander;
            PickNewWanderTarget();
        }
    }

    void DoCrawlToScent()
    {
        if (_currentScent == null)
        {
            _state = State.CrawlWander;
            return;
        }

        _crawlToScentSpeed = Mathf.MoveTowards(_crawlToScentSpeed, crawlToScentMaxSpeed, crawlSpeedRampRate * Time.deltaTime);
        MoveTowards(_currentScent.position, _crawlToScentSpeed, crawlAcceleration, crawlDeceleration, scentArriveDistance, crawlTurnSpeed);
    }

    void DoStandChase()
    {
        if (!playerTarget)
        {
            ExitStandChase();
            _state = State.CrawlWander;
            return;
        }

        if (!_hasRoared)
        {
            if (Time.time >= _roarCompleteTime) _hasRoared = true;
            else
            {
                StopNowHard();
                StopAgentHard();
                FaceTarget(playerTarget.position, standChaseTurnSpeed);
                return;
            }
        }

        float d = FlatDistance(transform.position, playerTarget.position);

        if (d <= attackStartRadius && Time.time >= _nextAttackReady)
        {
            StartAttack();
            return;
        }

        _standChaseSpeed = Mathf.MoveTowards(_standChaseSpeed, standChaseMaxSpeed, standSpeedRampRate * Time.deltaTime);

        if (d <= attackStopRadius)
        {
            // ✅ stand 정지도 뚝 끊기면 어색하면 아래 한 줄로 부드럽게
            RequestStopSoft(standChaseDeceleration);
            FaceTarget(playerTarget.position, standChaseTurnSpeed);
            return;
        }

        MoveTowards(playerTarget.position, _standChaseSpeed, standChaseAcceleration, standChaseDeceleration, attackStopRadius, standChaseTurnSpeed);
    }

    void StartAttack()
    {
        _state = State.Attack;

        StopNowHard();
        StopAgentHard();

        _damageApplied = false;

        _isAttackingWithBite = Random.value <= biteChance;
        string trigger = _isAttackingWithBite ? biteTrigger : punchTrigger;
        string clipContains = _isAttackingWithBite ? biteClipContains : punchClipContains;

        FireTrigger(trigger);

        _attackHitAt = Time.time + attackHitDelay;

        float fullLen = GetClipLength(clipContains, 0.8f);
        float fraction = sliceComboClips ? (_isAttackingWithBite ? biteClipFraction : punchClipFraction) : 1f;

        float window = fullLen * Mathf.Clamp01(fraction);
        window = Mathf.Max(window, minAttackWindow);
        window = Mathf.Max(window, attackHitDelay + 0.05f);

        _attackEndAt = Time.time + window;
        _attackCanCancelAfter = Time.time + cancelCheckGrace;

        LockFor(window + lockExtraBuffer);
        _nextAttackReady = Time.time + window + attackCooldown;

        _skipMoveThisFrame = true;
    }

    void DoAttack()
    {
        _curSpeed = 0f;
        _targetSpeed = 0f;
        StopAgentHard();

        if (playerTarget)
            FaceTarget(playerTarget.position, standChaseTurnSpeed);
    }

    void TickAttack()
    {
        if (cancelAttackIfTargetFar && playerTarget && Time.time >= _attackCanCancelAfter)
        {
            float d = FlatDistance(transform.position, playerTarget.position);
            float cancelDist = attackStartRadius * Mathf.Max(1.0f, cancelDistanceMultiplier);

            if (d > cancelDist)
            {
                CancelAttackToChase();
                return;
            }
        }

        if (playerTarget)
            FaceTarget(playerTarget.position, standChaseTurnSpeed);

        if (!_damageApplied && Time.time >= _attackHitAt)
        {
            _damageApplied = true;
            TryApplyDamage();
        }

        if (sliceComboClips && Time.time >= _attackEndAt)
        {
            EndAttackToChase();
            return;
        }
    }

    void CancelAttackToChase()
    {
        ResetAttackTriggers();
        ForceLocomotionNow();
        _state = State.StandChase;

        _nextAttackReady = Mathf.Max(_nextAttackReady, Time.time + cancelCooldown);

        _lockUntil = 0f;
        _curSpeed = 0f;
        _targetSpeed = 0f;
        _ignoreBusyUntil = Time.time + 0.2f;
        _skipMoveThisFrame = true;
    }

    void EndAttackToChase()
    {
        ResetAttackTriggers();
        ForceLocomotionNow();
        _state = State.StandChase;

        _lockUntil = 0f;
        _curSpeed = 0f;
        _targetSpeed = 0f;
        _ignoreBusyUntil = Time.time + 0.2f;
        _skipMoveThisFrame = true;
    }

    void ResetAttackTriggers()
    {
        if (!animator) return;
        if (!string.IsNullOrEmpty(punchTrigger)) animator.ResetTrigger(punchTrigger);
        if (!string.IsNullOrEmpty(biteTrigger)) animator.ResetTrigger(biteTrigger);
    }

    void ForceLocomotionNow()
    {
        if (!animator) return;

        int stance = animator.GetInteger(stanceParam);
        string stateName = (stance == 1) ? standLocomotionState : crouchLocomotionState;
        if (string.IsNullOrEmpty(stateName)) return;

        animator.CrossFadeInFixedTime(stateName, locomotionCrossFade, 0);
    }

    void TryApplyDamage()
    {
        if (!playerTarget) return;

        float d = FlatDistance(transform.position, playerTarget.position);
        if (d > attackHitRadius) return;

        int damage = _isAttackingWithBite ? biteDamage : punchDamage;

        // IDamageable 인터페이스 체크 (우선순위 1)
        var dmg = playerTarget.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(damage, gameObject);
            return;
        }

        // Health 컴포넌트 체크 (우선순위 2)
        var hp = playerTarget.GetComponentInParent<Health>();
        if (hp != null)
        {
            hp.TakeDamage(damage, gameObject);
            return;
        }

        Debug.LogWarning($"{name}: Target {playerTarget.name} has no IDamageable or Health component!");
    }

    void DoEating()
    {
        if (_currentMeat == null)
        {
            _state = State.CrawlWander;
            return;
        }

        float d = Vector3.Distance(transform.position, _currentMeat.position);
        if (d <= meatArriveDistance)
        {
            StopNowHard();
            StopAgentHard();
            FaceTarget(_currentMeat.position, crawlTurnSpeed);

            FireTrigger(eatTrigger);
            LockFor(GetLockSeconds(eatClipContains, 1.5f));
            _skipMoveThisFrame = true;

            BloodScentManager.AddBloodScent(_currentMeat.position, 1f, 60f);

            Destroy(_currentMeat.gameObject);
            _currentMeat = null;

            _state = State.CrawlWander;
            return;
        }

        MoveTowards(_currentMeat.position, crawlSpeed, crawlAcceleration, crawlDeceleration, meatArriveDistance, crawlTurnSpeed);
    }

    public void NotifyDamaged()
    {
        if (!animator) return;
        if (Time.time < _nextDamageReady) return;

        _nextDamageReady = Time.time + damageCooldown;

        animator.ResetTrigger(sniffTrigger);
        animator.ResetTrigger(eatTrigger);
        animator.ResetTrigger(roarTrigger);
        animator.ResetTrigger(punchTrigger);
        animator.ResetTrigger(biteTrigger);

        StopNowHard();
        StopAgentHard();

        FireTrigger(damageTrigger);

        _state = State.Damaged;
        LockFor(GetLockSeconds(damageClipContains, 0.5f));
        _skipMoveThisFrame = true;
    }

    // ✅ 즉시 정지(공격/피격/포효 같은 “딱 멈추는” 액션용)
    void StopNowHard()
    {
        _curSpeed = 0f;
        _targetSpeed = 0f;
    }

    // ✅ 부드러운 정지(배회/정지 전환용)
    void RequestStopSoft(float decel)
    {
        _targetSpeed = 0f;
        _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, Mathf.Max(0.01f, decel) * Time.deltaTime);

        if (useNavMesh && agent && agent.enabled)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.speed = _curSpeed;
                // "현재 위치로" 목적지를 잡아두면, 기존 경로로 밀고 가는 문제 없이 자연스럽게 멈춤
                agent.SetDestination(transform.position);
            }
            else
            {
                StopAgentHard();
            }
        }
    }

    void SoftStopByState()
    {
        bool hard = (_state == State.Attack || _state == State.Damaged || _state == State.Eating);
        if (hard)
        {
            StopNowHard();
            StopAgentHard();
            return;
        }

        // Crawl이면 crawlDecel, Stand면 standDecel
        float decel = IsStandStance() ? standChaseDeceleration : crawlDeceleration;
        RequestStopSoft(decel);
    }

    bool IsStandStance()
    {
        if (!animator) return false;
        return animator.GetInteger(stanceParam) == 1;
    }

    void StopAgentHard()
    {
        if (!useNavMesh || !agent || !agent.enabled) return;
        agent.isStopped = true;
        agent.ResetPath();
    }

    void ResumeAgent()
    {
        if (!useNavMesh || !agent || !agent.enabled) return;
        agent.isStopped = false;
    }

    // ✅ accel/decEL 분리
    void MoveTowards(Vector3 worldTarget, float maxSpeed, float accel, float decel, float stopDistance, float turnSpeedDeg)
    {
        _targetSpeed = maxSpeed;

        float rate = (_curSpeed < _targetSpeed) ? accel : decel;
        _curSpeed = Mathf.MoveTowards(_curSpeed, _targetSpeed, Mathf.Max(0.01f, rate) * Time.deltaTime);

        if (useNavMesh && agent && agent.enabled)
        {
            ResumeAgent();
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
                _targetSpeed = 0f;
                _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, Mathf.Max(0.01f, decel) * Time.deltaTime);
                return;
            }

            Vector3 dir = to / Mathf.Max(dist, 0.0001f);

            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDeg * Time.deltaTime);

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
        float half = crawlConeAngle * 0.5f;
        float yaw = Random.Range(-half, half);
        Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * transform.forward;
        dir = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;

        float dist = Random.Range(crawlWanderRadius * 0.3f, crawlWanderRadius);
        Vector3 candidate = transform.position + dir * dist;

        if (useNavMesh && NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            _wanderTarget = hit.position;
        else
            _wanderTarget = candidate;
    }

    void ScheduleNextPause()
    {
        _nextPauseAt = Time.time + Random.Range(pauseEverySeconds.x, pauseEverySeconds.y);
    }

    void SetStance(int stance)
    {
        if (!animator) return;

        animator.SetInteger(stanceParam, stance);
        if (stance == 1)
            animator.SetInteger(standWalkStyleParam, 2);
    }

    void LockFor(float seconds)
    {
        _lockUntil = Time.time + seconds;
    }

    float GetClipLength(string clipNameContains, float fallbackSeconds)
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

        return baseLen;
    }

    float GetLockSeconds(string clipNameContains, float fallbackSeconds)
    {
        return GetClipLength(clipNameContains, fallbackSeconds) + lockExtraBuffer;
    }

    void FireTrigger(string trig)
    {
        if (!animator || string.IsNullOrEmpty(trig)) return;
        animator.SetTrigger(trig);
    }

    void UpdateAnimatorParams()
    {
        if (!animator) return;

        float maxSpeed = (_state == State.StandChase) ? _standChaseSpeed :
                         (_state == State.CrawlToScent) ? _crawlToScentSpeed : crawlSpeed;

        float denom = Mathf.Max(0.001f, maxSpeed);
        float target01 = Mathf.Clamp01(_curSpeed / denom);

        // ✅ 핵심: Speed 파라미터를 댐핑으로 부드럽게
        animator.SetFloat(speedParam, target01, speedParamDampTime, Time.deltaTime);

        if (!driveAnimatorSpeed) return;

        float targetAnim = 1f;
        if (_state == State.Attack || _state == State.Eating || _state == State.Damaged)
        {
            targetAnim = actionAnimSpeed;
        }
        else if (_state == State.CrawlToScent)
        {
            float t = (_crawlToScentSpeed - crawlToScentSpeed) /
                      Mathf.Max(0.001f, crawlToScentMaxSpeed - crawlToScentSpeed);
            targetAnim = Mathf.Lerp(crawlAnimSpeed, crawlRunAnimSpeed, t);
        }
        else if (_state == State.StandChase)
        {
            if (_hasRoared)
            {
                float t = (_standChaseSpeed - standChaseSpeed) /
                          Mathf.Max(0.001f, standChaseMaxSpeed - standChaseSpeed);
                float runSpeed = Mathf.Lerp(standWalkAnimSpeed, standRunAnimSpeed, t);
                targetAnim = Mathf.Lerp(1f, runSpeed, target01);
            }
            else targetAnim = 1f;
        }

        float k = 1f - Mathf.Exp(-animSpeedDamp * Time.deltaTime);
        animator.speed = Mathf.Lerp(animator.speed, targetAnim, k);
    }

    void StickToGroundIfNeeded()
    {
        if (useNavMesh && agent && agent.enabled) return;

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
        Gizmos.DrawWireSphere(p, crawlWanderRadius);

        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawWireSphere(p, scentDetectRadius);

        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(p, meatDetectRadius);

        if (_currentScent != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, _currentScent.position);
        }
    }
}
