using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMove : MonoBehaviour
{
    [Header("Move")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.6f;
    public float slowMultiplier = 0.6f;

    [Header("Jump / Gravity")]
    public float jumpHeight = 1.2f;
    public float gravity = -35f;

    [Header("Jump Assist")]
    public float coyoteTime = 0.12f;      // 바닥에서 떨어진 직후도 점프 허용
    public float jumpBuffer = 0.12f;      // 점프 입력을 잠깐 저장
    public float groundStickY = -2.0f;      // 바닥에 붙는 힘
    public float groundProbeDistance = 0.25f; // 접지 보조 체크 거리
    public float groundProbeRadius = 0.2f;    // 접지 보조 체크 반경
    public LayerMask groundMask = ~0;     // 필요하면 Ground 레이어로 제한

    [Header("Look")]
    public float mouseSensitivity = 2.0f;

    private CharacterController controller;
    private Vector3 velocity;
    private float yaw;

    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // --- yaw (Mouse X only) ---
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // --- move input ---
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        Vector3 move = transform.right * x + transform.forward * z;

        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift)) speed *= sprintMultiplier;
        else if (Input.GetKey(KeyCode.LeftControl)) speed *= slowMultiplier;

        // 점프 입력 버퍼
        if (Input.GetKeyDown(KeyCode.Space))
            lastJumpPressedTime = Time.time;

        // --- grounded (보조 체크 포함) ---
        bool grounded = controller.isGrounded || ProbeGrounded();
        if (grounded) lastGroundedTime = Time.time;

        // --- gravity / stick ---
        if (grounded && velocity.y < 0f)
            velocity.y = groundStickY;

        // --- jump (coyote + buffer) ---
        bool canCoyote = (Time.time - lastGroundedTime) <= coyoteTime;
        bool hasBufferedJump = (Time.time - lastJumpPressedTime) <= jumpBuffer;

        if (canCoyote && hasBufferedJump)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            lastJumpPressedTime = -999f; // 버퍼 소진
            lastGroundedTime = -999f;    // 코요테 소진(연속 점프 방지)
        }

        velocity.y += gravity * Time.deltaTime;

        // Move는 가능하면 한 번에
        Vector3 displacement = (move * speed) + new Vector3(0f, velocity.y, 0f);
        controller.Move(displacement * Time.deltaTime);
    }

    // controller.isGrounded가 흔들릴 때 보조로 바닥 체크
    bool ProbeGrounded()
    {
        Vector3 origin = transform.position + controller.center;
        float castDist = (controller.height * 0.5f) + groundProbeDistance;

        // 아래로 구체 캐스트(경사/요철에서 안정적)
        return Physics.SphereCast(
            origin,
            groundProbeRadius,
            Vector3.down,
            out _,
            castDist,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }
}
