using UnityEngine;

public class GoblinWeaponAttach : MonoBehaviour
{
    [Header("Weapon")]
    [SerializeField] private GameObject weaponPrefab;
    [SerializeField] private bool attachOnStart = true;

    [Header("Attach Target")]
    [Tooltip("손 뼈 이름 (권장: hand_r)")]
    [SerializeField] private string handBoneName = "hand_r";

    [Tooltip("손 뼈를 못 찾았을 때 보험(있으면)")]
    [SerializeField] private string fallbackHandBoneName = "ik_hand_r";

    [Tooltip("소켓 이름 (너가 만든 WeaponSocket_R)")]
    [SerializeField] private string socketName = "WeaponSocket_R";

    [Tooltip("소켓 밑에 Offset을 쓰고 싶으면 이름 지정(없으면 socket에 바로 붙임)")]
    [SerializeField] private string optionalOffsetName = ""; // 예: "WeaponOffset_R"

    [Header("Auto Create (optional)")]
    [SerializeField] private bool createSocketIfMissing = true;

    [Tooltip("소켓을 자동 생성했을 때 적용할 기본 로컬 값(너가 맞춘 값)")]
    [SerializeField] private bool applyDefaultSocketTransformIfCreated = true;

    // 너가 스샷에서 맞춘 값
    [SerializeField] private Vector3 defaultSocketLocalPos = new Vector3(0.0276f, -0.0134f, 0.0341f);
    [SerializeField] private Vector3 defaultSocketLocalEuler = new Vector3(76.303f, -354.688f, -358.2f);

    [Header("Weapon Local (Usually keep zero)")]
    [SerializeField] private Vector3 weaponLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 weaponLocalEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 weaponLocalScale = Vector3.one;

    [Header("Debug")]
    [SerializeField] private bool logWarnings = true;

    private GameObject _weaponInstance;

    void Start()
    {
        if (attachOnStart)
            AttachWeapon();
    }

    [ContextMenu("Attach Weapon Now")]
    public void AttachWeapon()
    {
        if (!weaponPrefab)
        {
            if (logWarnings) Debug.LogWarning($"[{nameof(GoblinWeaponAttach)}] weaponPrefab is null on {name}");
            return;
        }

        // 중복 장착 방지
        DetachWeapon();

        Transform hand = FindDeepChild(transform, handBoneName);
        if (!hand) hand = FindDeepChild(transform, fallbackHandBoneName);

        if (!hand)
        {
            if (logWarnings) Debug.LogWarning($"[{nameof(GoblinWeaponAttach)}] Hand bone not found ({handBoneName}/{fallbackHandBoneName}) on {name}");
            return;
        }

        // Socket 찾기/생성
        Transform socket = hand.Find(socketName);
        bool createdSocket = false;

        if (!socket && createSocketIfMissing)
        {
            var go = new GameObject(socketName);
            socket = go.transform;
            socket.SetParent(hand, false);   // local 0/0/0
            createdSocket = true;
        }

        if (!socket)
        {
            if (logWarnings) Debug.LogWarning($"[{nameof(GoblinWeaponAttach)}] Socket not found and not created: {socketName} on {name}");
            return;
        }

        // 소켓을 “자동 생성”했을 때만, 기본값을 적용 (프리팹에 이미 맞춰뒀으면 이 코드가 건드리지 않음)
        if (createdSocket && applyDefaultSocketTransformIfCreated)
        {
            socket.localPosition = defaultSocketLocalPos;
            socket.localRotation = Quaternion.Euler(defaultSocketLocalEuler);
        }

        // optional offset이 있으면 그쪽에 붙임
        Transform mount = socket;
        if (!string.IsNullOrEmpty(optionalOffsetName))
        {
            var offset = socket.Find(optionalOffsetName);
            if (offset) mount = offset;
        }

        // 무기 생성
        _weaponInstance = Instantiate(weaponPrefab, mount, false);

        // 무기는 보통 0,0,0 / identity 로 두고, 잡는 자세는 Socket에서 해결
        _weaponInstance.transform.localPosition = weaponLocalPosition;
        _weaponInstance.transform.localRotation = Quaternion.Euler(weaponLocalEulerAngles);
        _weaponInstance.transform.localScale = weaponLocalScale;
    }

    [ContextMenu("Detach Weapon Now")]
    public void DetachWeapon()
    {
        if (_weaponInstance)
        {
            if (Application.isPlaying) Destroy(_weaponInstance);
            else DestroyImmediate(_weaponInstance);
            _weaponInstance = null;
        }
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result) return result;
        }
        return null;
    }
}
