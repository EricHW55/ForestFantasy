using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class GoblinAI : MonoBehaviour
{
    private enum State { Wander, WanderPause, CrouchApproach, Chase, Flee, Locked }

    [Header("Refs")]
    public NavMeshAgent agent;
    public Animator animator;
    public GoblinGroup group;
    public Transform player;
    public string playerTag = "Player";
    public Transform playerLookTransform;
    
    [Header("Movement Mode")]
    public bool useNavMesh = true;
    public LayerMask groundMask = ~0;
    public float groundCheckDistance = 5f;

    [Header("Vision")]
    public float viewRange = 14f;
    [Range(0f, 180f)] public float viewAngle = 110f;
    public float chaseKeepRange = 22f;
    public float senseRange = 10f;
    public LayerMask lineOfSightMask = ~0;
    [Range(0f, 180f)] public float playerLookFov = 70f;
    public float eyeHeight = 1.3f;

    [Header("Movement")]
    public float walkSpeed = 1.6f;
    public float runSpeed = 3.2f;
    public float sprintSpeed = 4.6f;
    public float crouchSpeed = 1.1f;
    public float turnSpeed = 240f;
    public float arriveDistance = 0.6f;
    public float acceleration = 3.0f;
    public float deceleration = 5.0f;

    [Header("Wander")]
    public float wanderRadius = 10f;
    public Vector2 wanderPauseSeconds = new Vector2(1.2f, 2.5f);
    [Range(30f, 180f)] public float wanderForwardCone = 140f;

    [Header("Actions / Dialogue")]
    public Vector2 action1Interval = new Vector2(4f, 8f);
    public Vector2 action1SpeedRange = new Vector2(1.5f, 2.5f);
    [Range(0f, 1f)] public float action2ChanceOnPause = 0.01f;
    [Range(0f, 1f)] public float dialogueChanceOnPause = 0.4f;
    public float dialogueDistance = 3f;
    public float dialogueTurnSpeed = 360f;

    [Header("Combat")]
    public float attackRange = 2.2f;
    public float attackCooldown = 0.5f;
    [Range(0f, 1f)] public float firstAttackRunChance = 0.35f;
    [Range(0f, 1f)] public float jumpBackChance = 0.4f;
    public float jumpBackDistance = 2.5f;
    public float jumpBackHeight = 0.3f;

    [Header("Flee")]
    public bool enableFlee = true;
    public float fleeTurnRunDist = 2.5f;
    public float fleeSafeDist = 12f;
    [Range(0f, 1f)] public float currentThreat = 0f;
    public float fleeThreatLevel = 0.85f;

    [Header("Animation Lock")]
    public string action1ClipName = "Action";
    public string action2ClipName = "Taunt";
    public string dialogueClipName = "Dialogue";
    public string attackClipName = "Attack";
    public string hitClipName = "Hit";
    public float lockBuffer = 0.1f;

    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashIsCrouch = Animator.StringToHash("IsCrouch");
    private static readonly int HashIsFlee = Animator.StringToHash("IsFlee");
    private static readonly int HashDoAction = Animator.StringToHash("DoAction");
    private static readonly int HashActionId = Animator.StringToHash("ActionId");
    private static readonly int HashDoDialogue = Animator.StringToHash("DoDialogue");
    private static readonly int HashDialogueId = Animator.StringToHash("DialogueId");
    private static readonly int HashDoAttack = Animator.StringToHash("DoAttack");
    private static readonly int HashAttackId = Animator.StringToHash("AttackId");
    private static readonly int HashDoHit = Animator.StringToHash("DoHit");
    private static readonly int HashHitId = Animator.StringToHash("HitId");

    private State _state = State.Wander;
    private bool _hasTarget;
    private bool _firstAttackThisEngage = true;
    
    private Vector3 _wanderTarget;
    private float _wanderPauseUntil;
    private float _nextActionTime;
    
    private float _lockUntil;
    private float _nextAttackTime;
    
    private Transform _dialoguePartner;
    private Vector3 _jumpBackStart;
    private Vector3 _jumpBackTarget;
    private float _jumpBackStartTime;
    private float _jumpBackDuration = 0.6f;
    private bool _isJumpingBack;
    private float _currentAction1Speed = 1.0f;
    
    private float _currentSpeed = 0f;
    private float _targetSpeed = 0f;
    
    private void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        
        if (useNavMesh && agent)
        {
            agent.updateRotation = false;
            agent.angularSpeed = turnSpeed;
            if (animator) animator.applyRootMotion = false;
            
            // 초기화 시 비활성화 (NavMesh 배치 전)
            agent.enabled = false;
        }
        else if (agent)
        {
            // NavMesh 사용 안 할 경우 비활성화
            agent.enabled = false;
        }
        
        if (!group) group = GetComponentInParent<GoblinGroup>();
        if (group) group.Register(this);
        
        ScheduleNextAction();
        DecideNewWanderTarget();
    }
    
    private void Start()
    {
        if (!player)
        {
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go) player = go.transform;
        }
        if (!playerLookTransform && Camera.main) 
            playerLookTransform = Camera.main.transform;
        
        // NavMesh에 배치 후 활성화
        if (useNavMesh && agent)
        {
            // 0.5초 후 활성화 (NavMesh 베이크 대기)
            Invoke(nameof(EnableAgent), 0.5f);
        }
    }
    
    private void EnableAgent()
    {
        if (agent && useNavMesh)
        {
            agent.enabled = true;
            
            // NavMesh에 제대로 배치되었는지 확인
            if (!agent.isOnNavMesh)
            {
                Debug.LogWarning($"[GoblinAI] {name}이(가) NavMesh 위에 없습니다!");
            }
        }
    }

    private void Update()
    {
        if (!agent || !animator) return;
        if (!player)
        {
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go) player = go.transform;
        }

        // JumpBack 처리
        if (_isJumpingBack)
        {
            UpdateJumpBack();
            return;
        }

        // Lock 체크
        if (Time.time < _lockUntil)
        {
            agent.velocity = Vector3.zero;
            UpdateAnimatorSpeed(0f);
            
            // Dialogue 중에는 상대방을 바라봄
            if (_dialoguePartner != null)
            {
                FaceToTarget(_dialoguePartner.position, dialogueTurnSpeed);
            }
            return;
        }

        // Busy 애니메이션 체크
        if (IsBusyAnim())
        {
            agent.velocity = Vector3.zero;
            UpdateAnimatorSpeed(0f);
            
            // Dialogue 중에는 상대방을 바라봄
            if (_dialoguePartner != null)
            {
                FaceToTarget(_dialoguePartner.position, dialogueTurnSpeed);
            }
            return;
        }

        // Lock에서 벗어났으면 상태 복귀
        if (_state == State.Locked)
        {
            _dialoguePartner = null;
            animator.speed = 1.0f; // 애니메이션 속도 복구
            if (_hasTarget) _state = State.Chase;
            else
            {
                _state = State.WanderPause;
                BeginWanderPause(Random.Range(0.5f, 1.0f));
            }
        }

        // Flee 체크
        if (enableFlee && _hasTarget && currentThreat >= fleeThreatLevel)
        {
            _state = State.Flee;
        }

        // Targeting
        UpdateTargeting();

        // State 처리
        switch (_state)
        {
            case State.Wander: UpdateWander(); break;
            case State.WanderPause: UpdateWanderPause(); break;
            case State.CrouchApproach: UpdateCrouchApproach(); break;
            case State.Chase: UpdateChase(); break;
            case State.Flee: UpdateFlee(); break;
        }
    }

    private bool IsBusyAnim()
    {
        if (!animator) return false;
        var cur = animator.GetCurrentAnimatorStateInfo(0);
        if (cur.IsTag("Action") || cur.IsTag("Attack") || cur.IsTag("Hit") || cur.IsTag("Dialogue"))
            return true;
        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            if (next.IsTag("Action") || next.IsTag("Attack") || next.IsTag("Hit") || next.IsTag("Dialogue"))
                return true;
        }
        return false;
    }

    // ===== Targeting =====
    private void UpdateTargeting()
    {
        if (!player)
        {
            _hasTarget = false;
            return;
        }

        float dist = Vector3.Distance(transform.position, player.position);

        if (!_hasTarget)
        {
            if (CanSeePlayer())
            {
                AcquireTarget();
                return;
            }

            // 감지 범위 내 + 플레이어가 안 볼 때 은신 접근
            if (dist <= senseRange && !IsPlayerLookingAtMe())
            {
                if (Random.value < 0.3f)
                {
                    _state = State.CrouchApproach;
                    animator.SetBool(HashIsCrouch, true);
                }
            }
            return;
        }

        if (dist > chaseKeepRange)
        {
            LoseTarget();
        }
    }

    private bool CanSeePlayer()
    {
        if (!player) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 toPlayer = (player.position + Vector3.up * 1.0f) - origin;
        float dist = toPlayer.magnitude;

        if (dist > viewRange) return false;

        float angle = Vector3.Angle(transform.forward, toPlayer.normalized);
        if (angle > viewAngle * 0.5f) return false;

        if (Physics.Raycast(origin, toPlayer.normalized, out RaycastHit hit, dist, lineOfSightMask))
            return hit.transform == player || hit.transform.IsChildOf(player);

        return true;
    }

    private bool IsPlayerLookingAtMe()
    {
        if (!playerLookTransform) return false;

        Vector3 toMe = (transform.position + Vector3.up * 1.2f) - playerLookTransform.position;
        float angle = Vector3.Angle(playerLookTransform.forward, toMe.normalized);
        return angle <= playerLookFov * 0.5f;
    }

    private void AcquireTarget()
    {
        _hasTarget = true;
        _firstAttackThisEngage = true;
        
        // 그룹 알람 (첫 발견자만 Action2)
        if (group) group.RaiseAlarm(this, player);
        
        _state = State.Chase;
    }

    private void LoseTarget()
    {
        _hasTarget = false;
        _firstAttackThisEngage = true;
        animator.SetBool(HashIsCrouch, false);
        animator.SetBool(HashIsFlee, false);
        
        _state = State.WanderPause;
        BeginWanderPause(Random.Range(wanderPauseSeconds.x, wanderPauseSeconds.y));
        DecideNewWanderTarget();
    }

    // ===== Wander =====
    private void UpdateWander()
    {
        animator.SetBool(HashIsCrouch, false);
        animator.SetBool(HashIsFlee, false);

        // 그룹 복귀
        if (group)
        {
            Vector3 center = group.GetCenter();
            float maxDist = group.groupMaxRadius;
            if (Vector3.Distance(transform.position, center) > maxDist)
            {
                MoveTo(center, walkSpeed);
                return;
            }
        }

        // 목표 도착
        float distToTarget = Vector3.Distance(transform.position, _wanderTarget);
        if (distToTarget <= arriveDistance)
        {
            BeginWanderPause(Random.Range(wanderPauseSeconds.x, wanderPauseSeconds.y));
            return;
        }

        // Action1 주기
        if (Time.time >= _nextActionTime)
        {
            ScheduleNextAction();
            BeginWanderPause(Random.Range(wanderPauseSeconds.x, wanderPauseSeconds.y));
            return;
        }

        MoveTo(_wanderTarget, walkSpeed);
    }

    private void UpdateWanderPause()
    {
        agent.velocity = Vector3.zero;
        UpdateAnimatorSpeed(0f);

        if (Time.time >= _wanderPauseUntil)
        {
            // 정지 끝나면 행동 결정
            float roll = Random.value;
            
            // Dialogue 시도 (가장 높은 우선순위)
            if (roll < dialogueChanceOnPause && group)
            {
                if (TryStartDialogue()) return;
            }
            
            // Action2 (도발)
            if (roll < dialogueChanceOnPause + action2ChanceOnPause)
            {
                StartAction(2);
                return;
            }
            
            // Action1 (주변 살피기)
            if (Time.time >= _nextActionTime)
            {
                ScheduleNextAction();
                StartAction(1);
                return;
            }

            // 다시 걷기
            _state = State.Wander;
            DecideNewWanderTarget();
        }
    }

    private void BeginWanderPause(float seconds)
    {
        _state = State.WanderPause;
        _wanderPauseUntil = Time.time + seconds;
        agent.velocity = Vector3.zero;
        _currentSpeed = 0f; // 정지 시 속도 초기화
    }

    private void DecideNewWanderTarget()
    {
        Vector3 center = group ? group.GetCenter() : transform.position;
        float radius = wanderRadius;

        for (int i = 0; i < 10; i++)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(r.x, 0f, r.y);

            Vector3 dir = candidate - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1f) continue;

            float angle = Vector3.Angle(transform.forward, dir.normalized);
            if (angle <= wanderForwardCone * 0.5f || i == 9)
            {
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    _wanderTarget = hit.position;
                    return;
                }
            }
        }

        _wanderTarget = transform.position + transform.forward * 3f;
    }

    // ===== Crouch =====
    private void UpdateCrouchApproach()
    {
        if (!player || IsPlayerLookingAtMe() || CanSeePlayer())
        {
            animator.SetBool(HashIsCrouch, false);
            if (player) AcquireTarget();
            else
            {
                _state = State.Wander;
                DecideNewWanderTarget();
            }
            return;
        }

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= attackRange)
        {
            animator.SetBool(HashIsCrouch, false);
            AcquireTarget();
            TryAttack(forceRunAttack: true);
            return;
        }

        MoveTo(player.position, crouchSpeed);
    }

    // ===== Chase / Combat =====
    private void UpdateChase()
    {
        if (!player)
        {
            LoseTarget();
            return;
        }

        animator.SetBool(HashIsCrouch, false);
        animator.SetBool(HashIsFlee, false);

        float dist = Vector3.Distance(transform.position, player.position);

        // 공격 범위
        if (dist <= attackRange && Time.time >= _nextAttackTime)
        {
            TryAttack(forceRunAttack: false);
            return;
        }

        // 추격
        float speed = (dist > 10f) ? sprintSpeed : runSpeed;
        MoveTo(player.position, speed);
    }

    private void TryAttack(bool forceRunAttack)
    {
        if (!player) return;

        agent.velocity = Vector3.zero;
        _currentSpeed = 0f; // 공격 시 속도 초기화

        int id = PickAttackId(forceRunAttack);
        
        animator.SetInteger(HashAttackId, id);
        animator.SetTrigger(HashDoAttack);

        _state = State.Locked;
        float lockTime = GetClipLength(attackClipName, 0.8f);
        _lockUntil = Time.time + lockTime + lockBuffer;

        _nextAttackTime = Time.time + lockTime + attackCooldown;

        // JumpBack 예약
        if (!forceRunAttack && Random.value < jumpBackChance)
        {
            Invoke(nameof(DelayedJumpBack), lockTime * 0.7f);
        }
    }

    private void DelayedJumpBack()
    {
        if (_state != State.Locked || !_hasTarget) return;
        
        animator.SetInteger(HashAttackId, 90);
        animator.SetTrigger(HashDoAttack);

        float lockTime = GetClipLength(attackClipName, 0.6f);
        _lockUntil = Time.time + lockTime + lockBuffer;
        
        // JumpBack 물리 시작
        StartJumpBack();
    }

    private void StartJumpBack()
    {
        _isJumpingBack = true;
        _jumpBackStart = transform.position;
        
        Vector3 backDir = -transform.forward;
        _jumpBackTarget = _jumpBackStart + backDir * jumpBackDistance;
        
        if (NavMesh.SamplePosition(_jumpBackTarget, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            _jumpBackTarget = hit.position;
        }
        
        _jumpBackStartTime = Time.time;
        _jumpBackDuration = 0.6f;
    }

    private void UpdateJumpBack()
    {
        float elapsed = Time.time - _jumpBackStartTime;
        float t = elapsed / _jumpBackDuration;
        
        if (t >= 1f)
        {
            transform.position = _jumpBackTarget;
            _isJumpingBack = false;
            return;
        }
        
        // 포물선 움직임
        Vector3 horizontalPos = Vector3.Lerp(_jumpBackStart, _jumpBackTarget, t);
        float height = Mathf.Sin(t * Mathf.PI) * jumpBackHeight;
        transform.position = horizontalPos + Vector3.up * height;
        
        agent.velocity = Vector3.zero;
        UpdateAnimatorSpeed(0f);
    }

    private int PickAttackId(bool forceRunAttack)
    {
        if (_firstAttackThisEngage || forceRunAttack)
        {
            _firstAttackThisEngage = false;
            if (Random.value < firstAttackRunChance) return 10;
        }
        return Random.Range(1, 7);
    }

    // ===== Flee =====
    private void UpdateFlee()
    {
        if (!player)
        {
            animator.SetBool(HashIsFlee, false);
            _state = State.Wander;
            DecideNewWanderTarget();
            return;
        }

        float dist = Vector3.Distance(transform.position, player.position);

        if (dist >= fleeSafeDist)
        {
            animator.SetBool(HashIsFlee, false);
            _state = State.Wander;
            DecideNewWanderTarget();
            return;
        }

        // 가까우면 뒤돌아서 도망
        if (dist <= fleeTurnRunDist)
        {
            animator.SetBool(HashIsFlee, false);
            Vector3 away = (transform.position - player.position).normalized;
            MoveTo(transform.position + away * 6f, runSpeed);
            return;
        }

        // 뒷걸음질
        animator.SetBool(HashIsFlee, true);
        agent.velocity = Vector3.zero;
        UpdateAnimatorSpeed(0f);
        FaceToTarget(player.position, turnSpeed);
    }

    // ===== Dialogue =====
    private bool TryStartDialogue()
    {
        if (!group) return false;
        if (_state == State.Locked || _state == State.Chase) return false;

        var others = group.GetAvailableMembers(this);
        GoblinAI closest = null;
        float closestDist = float.MaxValue;

        foreach (var other in others)
        {
            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d <= dialogueDistance && d < closestDist)
            {
                closest = other;
                closestDist = d;
            }
        }

        if (closest == null) return false;

        // 대화 시작
        int myId = Random.Range(1, 6);
        int otherId = Random.Range(1, 6);

        StartDialogue(myId, closest.transform);
        closest.StartDialogue(otherId, transform);

        return true;
    }

    public void StartDialogue(int id, Transform faceTo)
    {
        if (_state == State.Locked || _state == State.Chase) return;

        agent.velocity = Vector3.zero;
        _currentSpeed = 0f; // 대화 시 속도 초기화
        _dialoguePartner = faceTo;
        if (faceTo) FaceToTarget(faceTo.position, dialogueTurnSpeed);

        animator.SetInteger(HashDialogueId, id);
        animator.SetTrigger(HashDoDialogue);

        _state = State.Locked;
        float lockTime = GetClipLength(dialogueClipName, 1.5f);
        _lockUntil = Time.time + lockTime + lockBuffer;
    }

    // ===== Actions =====
    private void StartAction(int actionId)
    {
        agent.velocity = Vector3.zero;
        _currentSpeed = 0f; // 액션 시 속도 초기화

        animator.SetInteger(HashActionId, actionId);
        animator.SetTrigger(HashDoAction);

        // Action1일 때 랜덤 속도 적용
        if (actionId == 1)
        {
            _currentAction1Speed = Random.Range(action1SpeedRange.x, action1SpeedRange.y);
            animator.speed = _currentAction1Speed;
        }
        else
        {
            animator.speed = 1.0f;
        }

        _state = State.Locked;
        string clipName = actionId == 1 ? action1ClipName : action2ClipName;
        float lockTime = GetClipLength(clipName, 1.2f);
        
        // Action1의 경우 속도에 따라 lockTime 조정
        if (actionId == 1)
        {
            lockTime /= _currentAction1Speed;
        }
        
        _lockUntil = Time.time + lockTime + lockBuffer;
    }

    private void ScheduleNextAction()
    {
        _nextActionTime = Time.time + Random.Range(action1Interval.x, action1Interval.y);
    }

    // ===== Hit =====
    public void PlayHit(int hitId)
    {
        if (!animator) return;

        agent.velocity = Vector3.zero;

        animator.SetInteger(HashHitId, hitId);
        animator.SetTrigger(HashDoHit);

        _state = State.Locked;
        float lockTime = GetClipLength(hitClipName, 0.5f);
        _lockUntil = Time.time + lockTime + lockBuffer;
    }

    // ===== 그룹 알람 =====
    public void OnGroupAlarm(Transform target, bool isLeader)
    {
        if (!target) return;

        player = target;
        _hasTarget = true;
        _firstAttackThisEngage = true;

        // 리더만 도발
        if (isLeader && _state != State.Locked)
        {
            StartAction(2);
            return;
        }

        _state = State.Chase;
    }

    // ===== Movement =====
    private void MoveTo(Vector3 target, float speed)
    {
        _targetSpeed = speed;
        
        // 가속/감속 처리
        if (_currentSpeed < _targetSpeed)
        {
            _currentSpeed = Mathf.Min(_currentSpeed + acceleration * Time.deltaTime, _targetSpeed);
        }
        else if (_currentSpeed > _targetSpeed)
        {
            _currentSpeed = Mathf.Max(_currentSpeed - deceleration * Time.deltaTime, _targetSpeed);
        }
        
        if (useNavMesh && agent && agent.enabled)
        {
            // NavMesh 방식
            agent.speed = _currentSpeed;
            agent.SetDestination(target);
            
            Vector3 dir = agent.desiredVelocity;
            if (dir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            float vel = agent.velocity.magnitude;
            UpdateAnimatorSpeed(vel / Mathf.Max(0.01f, sprintSpeed));
        }
        else
        {
            // 수동 방식 (Imp/OneEye 스타일)
            Vector3 pos = transform.position;
            Vector3 to = target - pos;
            to.y = 0f;
            
            float dist = to.magnitude;
            if (dist > arriveDistance)
            {
                Vector3 dir = to / Mathf.Max(dist, 0.0001f);
                
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                }
                
                transform.position += transform.forward * (_currentSpeed * Time.deltaTime);
                StickToGroundManual();
            }
            
            UpdateAnimatorSpeed(_currentSpeed / Mathf.Max(0.01f, sprintSpeed));
        }
    }
    
    private void StickToGroundManual()
    {
        Vector3 pos = transform.position;
        Vector3 origin = pos + Vector3.up * 5f;
        
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            pos.y = hit.point.y + 0.1f;
            transform.position = pos;
        }
    }

    private void FaceToTarget(Vector3 worldPos, float speed)
    {
        Vector3 dir = worldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, speed * Time.deltaTime);
    }

    private void UpdateAnimatorSpeed(float normalized)
    {
        if (!animator) return;
        animator.SetFloat(HashSpeed, Mathf.Clamp01(normalized), 0.1f, Time.deltaTime);
    }

    // ===== Clip Length =====
    private float GetClipLength(string clipName, float fallback)
    {
        if (!animator || !animator.runtimeAnimatorController) return fallback;

        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip && clip.name.Contains(clipName))
                return clip.length;
        }

        return fallback;
    }

    // ===== Gizmos =====
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, viewRange);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * transform.forward;

        Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.8f);
        Gizmos.DrawLine(transform.position + Vector3.up * 1.2f, transform.position + Vector3.up * 1.2f + left * viewRange);
        Gizmos.DrawLine(transform.position + Vector3.up * 1.2f, transform.position + Vector3.up * 1.2f + right * viewRange);
    }
}