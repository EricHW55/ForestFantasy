using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMove : MonoBehaviour {
    private CharacterController controller; // 캐릭터 컨트롤러

    private Vector3 move; // 방향

    public const float normalSpeed = 5.0f; // 기본 캐릭터 속도
    public static float mouseSpeed = 1.3f * 100.0f; // 마우스 속도
    public float playerSpeed = normalSpeed; //5.0f; // 캐릭터 속도
    private float gravity = 9.81f; // 중력
    private float mouseX;
    private float jumpForce = 2.0f;




    private void mouseRotate() {
        mouseX += Input.GetAxis("Mouse X") * mouseSpeed * Time.deltaTime;
        this.transform.localEulerAngles = new Vector3(0, mouseX, 0);
    }


    private void characterMove() {
        if (controller.isGrounded) {
            move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
            move = controller.transform.TransformDirection(move);

            if (Input.GetKeyDown(KeyCode.Space)) { // 점프
                move.y = jumpForce;
            }
        }
        else {
            move.y -= gravity * Time.deltaTime; // 중력 적용
        }

        if (Input.GetKey(KeyCode.LeftShift)) playerSpeed = normalSpeed * 1.6f; // 달리기
        else if (Input.GetKey(KeyCode.LeftControl)) playerSpeed = normalSpeed * 0.6f; // 천천히 걷기
        else playerSpeed = normalSpeed;
        
        controller.Move(move * Time.deltaTime * playerSpeed);
    }



    // Start is called before the first frame update
    void Start() {
        controller = GetComponent<CharacterController>();
        move = Vector3.zero; // 벡터 초기화
    }

    // Update is called once per frame
    void Update(){
        mouseRotate();
        characterMove();
    }

}