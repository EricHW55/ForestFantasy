using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerMove : MonoBehaviour
{
    [Header("Move")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.6f;
    public float slowMultiplier = 0.6f;

    [Range(0.3f, 1f)] public float backwardSpeedMultiplier = 0.7f;
    public bool allowSprintBackward = false;

    [Header("Input Smoothing")]
    public float inputSmoothTimeMove = 0.06f;
    public float inputSmoothTimeStop = 0.10f;
    public float inputSmoothMaxSpeed = 20f;

    [Header("Jump / Gravity")]
    [Tooltip("점프 최고 높이(미터)")]
    public float jumpHeight = 2.4f;          // ✅ 더 높게

    [Tooltip("기본 중력(음수)")]
    public float gravity = -16f;             // ✅ 너무 빨리 떨어지면 절댓값 낮추기(더 둥실)

    [Tooltip("상승 중 중력 배율")]
    public float gravityUpMultiplier = 1.0f;

    [Tooltip("하강 중 중력 배율(낙하가 빠르면 0.6~0.85 추천)")]
    public float gravityDownMultiplier = 0.70f; // ✅ 하강 더 천천히

    [Tooltip("최대 낙하 속도 제한")]
    public float maxFallSpeed = 30f;

    [Header("Variable Jump (선택, 체공/점프감 개선)")]
    public bool variableJump = true;

    [Tooltip("점프키를 누르고 있을 때 상승 중력 배율(낮을수록 더 높이/더 오래 뜸)")]
    public float jumpHoldGravityMul = 0.75f;

    [Tooltip("점프키를 빨리 떼면 상승 중력 배율(높을수록 짧게 점프)")]
    public float jumpCutGravityMul = 2.0f;

    [Header("Jump Assist")]
    public float coyoteTime = 0.12f;
    public float jumpBuffer = 0.12f;

    [Tooltip("지면에 붙는 보정(너무 크면 요철에서 하강속도가 커짐)")]
    public float groundStickY = -0.6f; // ✅ 덜 내려꽂히게

    [Header("Ground Probe")]
    public float groundProbeDistance = 0.25f;
    public float groundProbeRadius = 0.2f;
    public float groundProbeUp = 0.06f;
    public float slopeLimitExtra = 5f;
    public LayerMask groundMask = ~0;

    [Header("Anti-Bumpy Fall (Fall 엄격화)")]
    [Tooltip("stepOffset + extra 만큼 아래에 바닥이 있으면 '거의 바닥'으로 취급")]
    public float nearGroundExtra = 0.08f;

    [Tooltip("Fall로 인정하기 전 공중에 떠있어야 하는 최소 시간(초)")]
    public float fallMinAirTime = 0.18f;

    [Tooltip("이 값보다 더 빠르게 내려갈 때만 Fall로 인정")]
    public float fallEnterYVel = -2.2f;

    [Header("Look")]
    public float mouseSensitivity = 2.0f;

    [Header("Animator Params")]
    public string paramSpeed = "Speed";
    public string paramMoveX = "MoveX";
    public string paramMoveY = "MoveY";

    public string paramGrounded = "Grounded";
    public string paramYVel = "YVel";
    public string paramInJump = "InJump";

    public string triggerJump = "Jump";
    public string triggerLand = "Land";

    [Header("Jump Animation Sync (원샷 점프 클립용)")]
    public bool syncJumpAnimToAirtime = true;
    public float jumpClipSeconds = 0.90f;
    public string paramJumpAnimMul = "JumpAnimMul";

    [Header("Animator Threshold Values")]
    public float walkSpeedParam = 0.5f;
    public float runSpeedParam = 1.0f;

    [Header("Animator Smoothing")]
    public float speedSmoothUp = 0.10f;
    public float speedSmoothDown = 0.16f;

    private CharacterController controller;
    private Animator anim;

    private Vector3 velocity;
    private float yaw;

    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;

    private float speedParamCurrent;
    private float speedParamVel;

    private int hSpeed, hMoveX, hMoveY;
    private int hGrounded, hYVel, hInJump, hJumpTrig, hLandTrig, hJumpAnimMul;

    private bool hasGrounded, hasYVel, hasInJump, hasJumpTrig, hasLandTrig, hasJumpAnimMul;

    private Vector2 smoothedInput;
    private Vector2 smoothedInputVel;

    private bool inJump;
    private float airTimePhys;      // 실제로 공중이었던 시간(착지 판단용)
    private float ungroundedTime;   // 애니 Fall 엄격화용

    void Start()
    {
        controller = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();

        hSpeed = Animator.StringToHash(paramSpeed);
        hMoveX = Animator.StringToHash(paramMoveX);
        hMoveY = Animator.StringToHash(paramMoveY);

        hGrounded = Animator.StringToHash(paramGrounded);
        hYVel = Animator.StringToHash(paramYVel);
        hInJump = Animator.StringToHash(paramInJump);

        hJumpTrig = Animator.StringToHash(triggerJump);
        hLandTrig = Animator.StringToHash(triggerLand);
        hJumpAnimMul = Animator.StringToHash(paramJumpAnimMul);

        CacheAnimatorParamFlags();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        bool groundedNow = controller.isGrounded || ProbeGrounded(out _);
        if (groundedNow) lastGroundedTime = Time.time;

        inJump = false;
        airTimePhys = 0f;
        ungroundedTime = 0f;
    }

    void Update()
    {
        // --- yaw (Mouse X) ---
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // --- raw input ---
        float xRaw = Input.GetAxisRaw("Horizontal");
        float yRaw = Input.GetAxisRaw("Vertical");

        Vector2 rawInput = new Vector2(xRaw, yRaw);
        if (rawInput.sqrMagnitude > 1f) rawInput.Normalize();

        float smoothTime = (rawInput.sqrMagnitude < 0.0001f) ? inputSmoothTimeStop : inputSmoothTimeMove;
        smoothedInput = Vector2.SmoothDamp(
            smoothedInput, rawInput, ref smoothedInputVel,
            Mathf.Max(0.0001f, smoothTime), inputSmoothMaxSpeed, Time.deltaTime
        );
        if (smoothedInput.sqrMagnitude > 1f) smoothedInput = smoothedInput.normalized;

        // --- sprint/slow ---
        bool sprintKey = Input.GetKey(KeyCode.LeftShift);
        bool slowKey = Input.GetKey(KeyCode.LeftControl);

        float backward01 = Mathf.Clamp01(-smoothedInput.y);
        float backwardMul = Mathf.Lerp(1f, backwardSpeedMultiplier, backward01);

        bool sprint = sprintKey;
        if (!allowSprintBackward && backward01 > 0.2f) sprint = false;

        float speed = moveSpeed;
        if (sprint) speed *= sprintMultiplier;
        else if (slowKey) speed *= slowMultiplier;
        speed *= backwardMul;

        Vector3 moveDir = (transform.right * smoothedInput.x + transform.forward * smoothedInput.y);

        // --- jump buffer ---
        if (Input.GetKeyDown(KeyCode.Space))
            lastJumpPressedTime = Time.time;

        // --- grounded (physics 기준) ---
        bool groundedPhys = controller.isGrounded || ProbeGrounded(out _);
        if (groundedPhys) lastGroundedTime = Time.time;

        // 공중 시간(실제 착지 판단)
        bool justLandedPhys = false;
        if (!groundedPhys)
        {
            airTimePhys += Time.deltaTime;
        }
        else
        {
            if (airTimePhys > 0.05f) justLandedPhys = true;
            airTimePhys = 0f;
        }

        // --- jump (coyote + buffer) ---
        bool canCoyote = (Time.time - lastGroundedTime) <= coyoteTime;
        bool hasBufferedJump = (Time.time - lastJumpPressedTime) <= jumpBuffer;

        bool jumpFiredThisFrame = false;

        if (canCoyote && hasBufferedJump)
        {
            float gUp = Mathf.Abs(gravity) * gravityUpMultiplier;
            velocity.y = Mathf.Sqrt(jumpHeight * 2f * gUp);

            lastJumpPressedTime = -999f;
            lastGroundedTime = -999f;

            inJump = true;
            jumpFiredThisFrame = true;

            // Jump 애니 속도 싱크
            if (syncJumpAnimToAirtime && hasJumpAnimMul && jumpClipSeconds > 0.05f)
            {
                float gDown = Mathf.Abs(gravity) * gravityDownMultiplier;
                float timeUp = velocity.y / gUp;
                float timeDown = Mathf.Sqrt((2f * jumpHeight) / gDown);
                float airtime = timeUp + timeDown;

                float mul = Mathf.Clamp(jumpClipSeconds / Mathf.Max(0.05f, airtime), 0.6f, 1.4f);
                anim.SetFloat(hJumpAnimMul, mul);
            }

            if (hasJumpTrig) anim.SetTrigger(hJumpTrig);
        }

        // --- stick to ground ---
        if (groundedPhys && velocity.y < 0f)
            velocity.y = groundStickY;

        // --- gravity (상승/하강 다르게 + variable jump) ---
        float gravMul;
        if (velocity.y > 0f)
        {
            // 상승
            if (variableJump)
            {
                bool holdingJump = Input.GetKey(KeyCode.Space);
                gravMul = gravityUpMultiplier * (holdingJump ? jumpHoldGravityMul : jumpCutGravityMul);
            }
            else
            {
                gravMul = gravityUpMultiplier;
            }
        }
        else
        {
            // 하강
            gravMul = gravityDownMultiplier;
        }

        velocity.y += (gravity * gravMul) * Time.deltaTime;

        if (velocity.y < -maxFallSpeed)
            velocity.y = -maxFallSpeed;

        // Move
        Vector3 displacement = (moveDir * speed) + new Vector3(0f, velocity.y, 0f);
        controller.Move(displacement * Time.deltaTime);

        // --- near-ground (요철/경사에서 Fall 방지) ---
        bool nearGround = ProbeNearGround(out _);

        // 애니용 grounded는 "진짜 grounded" 또는 "stepOffset 근처의 거의 바닥"이면 true
        bool groundedAnim = groundedPhys || nearGround;

        // Fall 엄격화용 ungrounded 시간
        if (groundedAnim) ungroundedTime = 0f;
        else ungroundedTime += Time.deltaTime;

        // ✅ 진짜 Fall로 인정하는 조건 (엄격)
        bool isFalling = (!groundedAnim)
                         && (ungroundedTime >= fallMinAirTime)
                         && (velocity.y <= fallEnterYVel);

        // 착지 처리
        if (justLandedPhys)
        {
            inJump = false;

            if (hasLandTrig) anim.SetTrigger(hLandTrig);
            if (hasJumpAnimMul) anim.SetFloat(hJumpAnimMul, 1f);
        }

        // Fall 애니가 요철에서 안 뜨게: "진짜 Fall"일 때만 yVel을 음수로 보냄
        float yVelForAnim = isFalling ? velocity.y : 0f;

        UpdateAnimator(smoothedInput, sprint, groundedAnim, yVelForAnim, inJump);

        // 점프 직후에는 잠깐이라도 grounded로 보이면 안 되니(전환 안정), 이 프레임만 보정
        if (jumpFiredThisFrame && hasGrounded) anim.SetBool(hGrounded, false);
    }

    private void UpdateAnimator(Vector2 input, bool sprint, bool groundedAnim, float yVelForAnim, bool inJumpFlag)
    {
        if (!anim) return;

        anim.SetFloat(hMoveX, input.x);
        anim.SetFloat(hMoveY, input.y);

        bool hasMove = input.sqrMagnitude > 0.02f * 0.02f;
        float targetSpeedParam = (!groundedAnim) ? 0f :
            (!hasMove ? 0f : (sprint ? runSpeedParam : walkSpeedParam));

        float smooth = (targetSpeedParam > speedParamCurrent) ? speedSmoothUp : speedSmoothDown;
        speedParamCurrent = Mathf.SmoothDamp(speedParamCurrent, targetSpeedParam, ref speedParamVel, Mathf.Max(0.0001f, smooth));
        speedParamCurrent = Mathf.Clamp01(speedParamCurrent);
        anim.SetFloat(hSpeed, speedParamCurrent);

        if (hasGrounded) anim.SetBool(hGrounded, groundedAnim);
        if (hasYVel) anim.SetFloat(hYVel, yVelForAnim);
        if (hasInJump) anim.SetBool(hInJump, inJumpFlag);
    }

    private void CacheAnimatorParamFlags()
    {
        if (anim == null) return;

        hasGrounded = HasParam(paramGrounded, AnimatorControllerParameterType.Bool);
        hasYVel = HasParam(paramYVel, AnimatorControllerParameterType.Float);
        hasInJump = HasParam(paramInJump, AnimatorControllerParameterType.Bool);
        hasJumpTrig = HasParam(triggerJump, AnimatorControllerParameterType.Trigger);
        hasLandTrig = HasParam(triggerLand, AnimatorControllerParameterType.Trigger);
        hasJumpAnimMul = HasParam(paramJumpAnimMul, AnimatorControllerParameterType.Float);
    }

    private bool HasParam(string name, AnimatorControllerParameterType type)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var p in anim.parameters)
            if (p.name == name && p.type == type) return true;
        return false;
    }

    bool ProbeGrounded(out RaycastHit hit)
    {
        Vector3 feet = transform.position + controller.center
                     + Vector3.down * (controller.height * 0.5f - controller.radius);

        Vector3 origin = feet + Vector3.up * groundProbeUp;
        float radius = Mathf.Max(0.05f, Mathf.Min(groundProbeRadius, controller.radius * 0.95f));
        float dist = groundProbeDistance + groundProbeUp;

        if (Physics.SphereCast(origin, radius, Vector3.down, out hit, dist, groundMask, QueryTriggerInteraction.Ignore))
        {
            float angle = Vector3.Angle(hit.normal, Vector3.up);
            return angle <= controller.slopeLimit + slopeLimitExtra;
        }
        return false;
    }

    // ✅ stepOffset 근처 아래에 땅이 있으면 near-ground로 인정(요철/경사에서 fall 방지)
    bool ProbeNearGround(out RaycastHit hit)
    {
        Vector3 feet = transform.position + controller.center
                     + Vector3.down * (controller.height * 0.5f - controller.radius);

        Vector3 origin = feet + Vector3.up * groundProbeUp;
        float radius = Mathf.Max(0.05f, Mathf.Min(groundProbeRadius, controller.radius * 0.95f));

        float nearDist = Mathf.Max(0.05f, controller.stepOffset + nearGroundExtra) + groundProbeUp;

        if (Physics.SphereCast(origin, radius, Vector3.down, out hit, nearDist, groundMask, QueryTriggerInteraction.Ignore))
        {
            float angle = Vector3.Angle(hit.normal, Vector3.up);
            return angle <= controller.slopeLimit + slopeLimitExtra;
        }
        return false;
    }
}
