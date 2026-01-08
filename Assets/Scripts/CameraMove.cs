using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FollowPlayer : MonoBehaviour
{
    [Header("Target")]
    public Transform player;

    [Tooltip("headAnchor를 못 찾았을 때 fallback으로 쓰는 오프셋(플레이어 루트 기준)")]
    public Vector3 headOffset = new Vector3(0f, 1.7f, 0f);

    [Header("View")]
    public bool thirdPerson = true;
    public KeyCode toggleKey = KeyCode.V;

    public float distance = 4f;
    public float thirdPersonHeight = 1.2f;

    [Header("Look")]
    public float mouseSensitivityY = 2.0f;
    public float minPitch = -50f;
    public float maxPitch = 50f;

    [Header("Smoothing")]
    public float smoothTime = 0.06f;

    [Header("First Person Anchor")]
    [Tooltip("비워두면 자동으로 Humanoid Head bone을 찾아서 사용")]
    public Transform headAnchor;

    [Tooltip("Head 기준으로 미세 오프셋(눈 높이/살짝 앞). Z를 0.05~0.12 주면 몸통/머리 클리핑이 크게 줄어듦")]
    public Vector3 headLocalOffset = new Vector3(0f, 0.02f, 0.08f);

    [Header("First Person Rendering (Camera-only)")]
    public bool hideBodyInFirstPerson = true;
    public string bodyLayerName = "PlayerBody";

    private float pitch;
    private Vector3 smoothVel;

    private Camera cam;
    private int bodyLayer;
    private int bodyBit;

    private Animator playerAnimator;
    private Transform cachedPlayer; // 플레이어 바뀌는 경우 대비

    void Start()
    {
        cam = GetComponent<Camera>();

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

        // Layer bit 준비
        bodyLayer = LayerMask.NameToLayer(bodyLayerName);
        if (hideBodyInFirstPerson && bodyLayer < 0)
        {
            Debug.LogWarning($"[FollowPlayer] Layer '{bodyLayerName}'를 못 찾았어. Layers에 추가해줘.");
        }
        bodyBit = (bodyLayer >= 0) ? (1 << bodyLayer) : 0;

        CachePlayerRefs(force: true);
        ApplyCullingMask();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // (플레이어가 런타임에 바뀌는 경우 대비)
        CachePlayerRefs(force: false);

        if (Input.GetKeyDown(toggleKey))
        {
            thirdPerson = !thirdPerson;
            ApplyCullingMask();
        }

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
            desiredPos = player.position
                       + Vector3.up * thirdPersonHeight
                       + (rot * new Vector3(0f, 0f, -distance));
        }
        else
        {
            // ✅ 1인칭: headAnchor 우선, 없으면 headOffset fallback
            if (headAnchor != null)
            {
                // headLocalOffset은 headAnchor의 로컬(전방 포함) 기준으로 적용
                desiredPos = headAnchor.position + headAnchor.TransformDirection(headLocalOffset);
            }
            else
            {
                desiredPos = player.position + headOffset;
            }
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref smoothVel, smoothTime);
        transform.rotation = rot;
    }

    /// <summary>
    /// headAnchor 자동 찾기(Head bone) + 플레이어 변경 감지
    /// </summary>
    private void CachePlayerRefs(bool force)
    {
        if (!force && cachedPlayer == player) return;

        cachedPlayer = player;

        // Animator 캐싱
        playerAnimator = (player != null) ? player.GetComponentInChildren<Animator>() : null;

        // headAnchor가 비어있으면 Humanoid Head bone 자동 설정
        if (headAnchor == null && playerAnimator != null && playerAnimator.isHuman)
        {
            Transform head = playerAnimator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
                headAnchor = head;
        }
    }

    private void ApplyCullingMask()
    {
        if (!hideBodyInFirstPerson || cam == null || bodyLayer < 0) return;

        if (thirdPerson)
            cam.cullingMask |= bodyBit;        // 3인칭: 보이게
        else
            cam.cullingMask &= ~bodyBit;       // 1인칭: 카메라에서만 숨김
    }
}
