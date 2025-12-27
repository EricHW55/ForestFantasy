using UnityEngine;

public class ImpWander : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Terrain 컴포넌트를 직접 넣어도 되고, 비우면 자동으로 찾습니다.")]
    public Terrain terrain;

    [Tooltip("Map 같은 루트 오브젝트를 넣으면 자식에서 Terrain을 찾아 사용합니다.")]
    public GameObject terrainRoot;

    [Tooltip("비워두면 GetComponentInChildren<Animator>()로 자동으로 찾음")]
    public Animator animator;

    [Header("Player (optional)")]
    public Transform playerTarget;
    public string playerTag = "Player";

    [Header("Wander")]
    public float moveSpeed = 2.0f;
    public float turnSpeed = 360f;
    public float wanderRadius = 10f;
    public float arriveDist = 0.8f;
    public float repathInterval = 3.0f;

    [Header("Chase/Attack")]
    [Tooltip("플레이어 인식 반경(늘려달라 해서 기본값 상향)")]
    public float detectRadius = 18f;

    [Tooltip("추격 시 이동 속도(늘려달라 해서 기본값 상향)")]
    public float chaseSpeed = 6.0f;

    [Tooltip("공격 시작 거리(너무 딱 붙지 않게 조금 넉넉히)")]
    public float attackRadius = 2.6f;

    [Tooltip("공격 간격(초)")]
    public float attackCooldown = 1.2f;

    [Tooltip("공격할 때도 플레이어를 바라보게")]
    public bool faceTargetWhenAttacking = true;

    [Header("Ground")]
    public float yOffset = 0.02f;

    [Header("Animator Params")]
    public string speedParam = "Speed";      // 0=idle, 1=walk, 2=run
    public string attack1Trigger = "Attack1";
    public string attack2Trigger = "Attack2";
    public string attackFallbackTrigger = "Attack"; // 없으면 무시

    private Vector3 _target;
    private float _nextRepathTime;

    private float _nextAttackTime;
    private int _attackFlip; // 0/1 번갈아

    private enum State { Wander, Chase, Attack }
    private State _state = State.Wander;

    private bool _hasSpeed;
    private bool _hasAtk1;
    private bool _hasAtk2;
    private bool _hasAtkFallback;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();

        CacheAnimatorParams();

        // Player 자동 (태그 기반)
        if (playerTarget == null && !string.IsNullOrEmpty(playerTag))
        {
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go != null) playerTarget = go.transform;
        }

        ResolveTerrain();
        PickNewTarget();
        _nextRepathTime = Time.time + repathInterval;
        _nextAttackTime = Time.time;
    }

    void Update()
    {
        ResolveTerrain();
        if (terrain == null) return;

        UpdateState();

        switch (_state)
        {
            case State.Attack:
                DoAttack();
                break;

            case State.Chase:
                _target = playerTarget != null ? playerTarget.position : _target;
                DoMove(chaseSpeed, speedValue: 2f, stopAtAttackRadius: true);
                break;

            default:
                // Wander
                if (Time.time >= _nextRepathTime)
                {
                    _nextRepathTime = Time.time + repathInterval;
                    PickNewTarget();
                }
                DoMove(moveSpeed, speedValue: 1f, stopAtAttackRadius: false);
                break;
        }

        SnapToTerrain();
    }

    void UpdateState()
    {
        if (playerTarget == null)
        {
            _state = State.Wander;
            return;
        }

        float d = Vector3.Distance(Flat(transform.position), Flat(playerTarget.position));

        if (d <= attackRadius) _state = State.Attack;
        else if (d <= detectRadius) _state = State.Chase;
        else _state = State.Wander;
    }

    void DoAttack()
    {
        // 공격 중엔 이동 멈춤 + 애니 speed 0
        SetAnimSpeed(0f);

        if (playerTarget != null && faceTargetWhenAttacking)
        {
            Vector3 to = playerTarget.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }
        }

        // ⭐ 핵심: 트리거를 매 프레임 쏘지 말고 쿨타임마다 한 번만!
        if (Time.time < _nextAttackTime) return;
        _nextAttackTime = Time.time + attackCooldown;

        // Attack1/Attack2 있으면 번갈아 or 랜덤
        if (_hasAtk1 && _hasAtk2)
        {
            _attackFlip ^= 1;
            if (_attackFlip == 0) animator.SetTrigger(attack1Trigger);
            else animator.SetTrigger(attack2Trigger);
        }
        else if (_hasAtk1)
        {
            animator.SetTrigger(attack1Trigger);
        }
        else if (_hasAtk2)
        {
            animator.SetTrigger(attack2Trigger);
        }
        else if (_hasAtkFallback)
        {
            animator.SetTrigger(attackFallbackTrigger);
        }
        // 없으면 그냥 멈춰있기만 함(Animator 설정이 아직 덜 된 상태)
    }

    void DoMove(float curSpeed, float speedValue, bool stopAtAttackRadius)
    {
        Vector3 pos = transform.position;

        // (공격 거리에서) 너무 딱 붙는 걸 막기 위해, Chase일 땐 attackRadius 안으로 더 파고들지 않게
        if (stopAtAttackRadius && playerTarget != null)
        {
            float d = Vector3.Distance(Flat(pos), Flat(playerTarget.position));
            if (d <= attackRadius)
            {
                SetAnimSpeed(0f);
                return;
            }
        }

        Vector3 to = _target - pos;
        to.y = 0f;
        float dist = to.magnitude;

        if (dist > arriveDist)
        {
            Vector3 dir = to / Mathf.Max(dist, 0.0001f);

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            transform.position += transform.forward * (curSpeed * Time.deltaTime);
            SetAnimSpeed(speedValue);
        }
        else
        {
            SetAnimSpeed(0f);
        }
    }

    void SetAnimSpeed(float v)
    {
        if (animator != null && _hasSpeed)
            animator.SetFloat(speedParam, v);
    }

    void CacheAnimatorParams()
    {
        _hasSpeed = _hasAtk1 = _hasAtk2 = _hasAtkFallback = false;
        if (animator == null) return;

        var ps = animator.parameters;
        foreach (var p in ps)
        {
            if (p.type == AnimatorControllerParameterType.Float && p.name == speedParam) _hasSpeed = true;
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == attack1Trigger) _hasAtk1 = true;
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == attack2Trigger) _hasAtk2 = true;
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == attackFallbackTrigger) _hasAtkFallback = true;
        }
    }

    void ResolveTerrain()
    {
        if (terrain != null) return;

        // terrainRoot가 있으면 그 안에서 찾기
        if (terrainRoot != null)
        {
            terrain = terrainRoot.GetComponent<Terrain>();
            if (terrain == null) terrain = terrainRoot.GetComponentInChildren<Terrain>();
            if (terrain != null) return;
        }

        // activeTerrain들 중 현재 위치에 맞는 Terrain 찾기
        if (Terrain.activeTerrain != null)
        {
            terrain = FindTerrainAt(transform.position);
        }
    }

    Terrain FindTerrainAt(Vector3 worldPos)
    {
        var terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0) return null;

        foreach (var t in terrains)
        {
            var tp = t.transform.position;
            var size = t.terrainData.size;
            bool inside =
                worldPos.x >= tp.x && worldPos.x <= tp.x + size.x &&
                worldPos.z >= tp.z && worldPos.z <= tp.z + size.z;
            if (inside) return t;
        }
        return Terrain.activeTerrain;
    }

    void SnapToTerrain()
    {
        var t = FindTerrainAt(transform.position);
        if (t != null) terrain = t;
        if (terrain == null) return;

        float y = terrain.SampleHeight(transform.position) + terrain.transform.position.y + yOffset;
        transform.position = new Vector3(transform.position.x, y, transform.position.z);
    }

    void PickNewTarget()
    {
        Vector2 rnd = Random.insideUnitCircle * wanderRadius;
        Vector3 center = transform.position;
        _target = new Vector3(center.x + rnd.x, center.y, center.z + rnd.y);

        var t = FindTerrainAt(_target);
        if (t != null)
        {
            float y = t.SampleHeight(_target) + t.transform.position.y;
            _target.y = y;
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
