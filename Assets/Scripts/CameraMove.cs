using UnityEngine;

public class FollowPlayer : MonoBehaviour
{
    [Header("Target")]
    public Transform player;      // Inspector에 드래그 추천
    public Vector3 headOffset = new Vector3(0f, 1.7f, 0f);

    [Header("View")]
    public bool thirdPerson = true;
    public float distance = 4f;
    public float thirdPersonHeight = 1.2f;

    [Header("Look")]
    public float mouseSensitivityY = 2.0f;
    public float minPitch = -50f;
    public float maxPitch = 50f;

    [Header("Smoothing")]
    public float smoothTime = 0.06f;

    private float pitch;
    private Vector3 smoothVel;

    void Start()
    {
        if (player == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go != null) player = go.transform;
        }

        if (player == null)
        {
            Debug.LogError("[FollowPlayer] Player를 못 찾았어. Player 태그를 달거나 Inspector에 할당해줘.");
            enabled = false;
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.V))
            thirdPerson = !thirdPerson;

        // Mouse Y only -> pitch
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivityY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    void LateUpdate()
    {
        if (player == null) return;

        float yaw = player.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

        Vector3 desiredPos;
        if (thirdPerson)
        {
            // 플레이어 뒤쪽 + 위쪽
            desiredPos = player.position
                       + Vector3.up * thirdPersonHeight
                       + (rot * new Vector3(0f, 0f, -distance));
        }
        else
        {
            // 1인칭(머리 위치)
            desiredPos = player.position + headOffset;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref smoothVel, smoothTime);

        // 바라보는 방향
        if (thirdPerson)
            transform.rotation = rot;          // 3인칭: pitch+yaw 그대로
        else
            transform.rotation = rot;          // 1인칭도 동일 (필요하면 roll 0 유지)
    }
}
