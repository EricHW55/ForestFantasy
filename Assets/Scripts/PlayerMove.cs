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
    public float gravity = -9.81f;

    [Header("Look")]
    public float mouseSensitivity = 2.0f;

    private CharacterController controller;
    private Vector3 velocity; // y는 중력/점프용
    private float yaw;

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

        controller.Move(move * speed * Time.deltaTime);

        // --- ground check & gravity ---
        if (controller.isGrounded && velocity.y < 0f)
            velocity.y = -2f; // 바닥에 붙게 살짝 음수

        // --- jump ---
        if (controller.isGrounded && Input.GetKeyDown(KeyCode.Space))
        {
            // v = sqrt(2gh)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}
