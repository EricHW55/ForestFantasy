using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowPlayer : MonoBehaviour {
    public GameObject player;

    private bool view = false; // 3인칭 시점

    private Vector3 cameraDir; // 3인칭 카메라 위치
    private float cameraLen = 3f; // 3인칭 카메라 거리 
    private float mincameraLen;
    private float mouseSpeed = PlayerMove.mouseSpeed;
    private float mouseX = 0.0f; // 좌 우 회전
    private float mouseY = 0.0f; // 위 아래 회전


    private void mouseRotate() {
        mouseX += Input.GetAxis("Mouse X") * mouseSpeed * Time.deltaTime;
        mouseY += Input.GetAxis("Mouse Y") * mouseSpeed * Time.deltaTime;
        // mouseY = Mathf.Clamp(mouseY, -50f, 30f);
        mouseY = Mathf.Clamp(mouseY, -50.0f, 50.0f);

        this.transform.localEulerAngles = new Vector3(-mouseY, mouseX, 0);
    }


    // Start is called before the first frame update
    void Start() {
        player = GameObject.FindWithTag("Player"); // Player 태그 찾기
    }

    // Update is called once per frame
    void Update() {
        if (Input.GetKeyDown(KeyCode.V)) view = !view;

        // transform.position = player.transform.position;
        // transform.forward = player.transform.forward;

        mouseRotate();

        if (view)  {
            cameraDir = new Vector3(
                player.transform.position.x + cameraLen * Mathf.Sin(-mouseX * Mathf.Deg2Rad), 
                player.transform.position.y + cameraLen, 
                player.transform.position.z - cameraLen * Mathf.Cos(-mouseX * Mathf.Deg2Rad)
                );

            /*
            Ray ray = new Ray(transform.position, -transform.forward);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, cameraLen)) {
                cameraDir = hit.point;
            }
            */

            transform.position = cameraDir; // 3인칭 시점
        }
        else {
            transform.position = new Vector3(player.transform.position.x, player.transform.position.y + 1.8f, player.transform.position.z); // 1인칭 시점
        }
    }
}
