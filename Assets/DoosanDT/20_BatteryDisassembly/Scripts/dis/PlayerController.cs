using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float climbSpeed = 3f;
    public float gravity = -9.81f;
    public float jumpHeight = 2f;

    [Header("Look Settings")]
    public float mouseSensitivity = 100f;
    public Transform playerCamera;
    public float maxLookAngle = 90f;

    [Header("Interaction Ray")]
    public float rayLength = 50f;
    public Color rayColor = Color.green;
    public float rayWidth = 0.02f;
    public Vector3 rayOffset = new Vector3(0, 0, 0);

    private CharacterController controller;
    private float verticalRotation;
    private Vector3 velocity;
    private LineRenderer interactionRay;
    private bool isGrounded;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;

        // 创建射线可视化
        CreateInteractionRay();
    }

    private void CreateInteractionRay()
    {
        GameObject rayObj = new GameObject("InteractionRay");
        rayObj.transform.SetParent(playerCamera);
        rayObj.transform.localPosition = rayOffset;
        rayObj.transform.localRotation = Quaternion.identity;

        interactionRay = rayObj.AddComponent<LineRenderer>();
        interactionRay.startWidth = rayWidth;
        interactionRay.endWidth = rayWidth;
        interactionRay.material = new Material(Shader.Find("Unlit/Color"))
        {
            color = rayColor
        };
        interactionRay.positionCount = 2;
        interactionRay.useWorldSpace = true;
    }

    private void Update()
    {
        HandleGroundCheck();
        HandleMovement();
        HandleLookRotation();
        UpdateInteractionRay();
    }

    private void HandleGroundCheck()
    {
        // 检测角色是否在地面上
        isGrounded = Physics.CheckSphere(
            transform.position + controller.center,
            controller.radius - 0.1f,
            LayerMask.GetMask("Default")
        );
    }

    private void HandleMovement()
    {
        // 在地面时重置下落速度
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // 获取输入
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // 上升/下降控制
        float verticalMove = 0f;
        if (Input.GetKey(KeyCode.Q)) verticalMove = climbSpeed;
        if (Input.GetKey(KeyCode.E)) verticalMove = -climbSpeed;

        // 跳跃控制（仅在地面时有效）
        if (isGrounded && Input.GetKeyDown(KeyCode.Space))
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // 移动方向计算
        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * moveSpeed * Time.deltaTime);

        // 垂直移动
        velocity.y += gravity * Time.deltaTime;
        controller.Move(new Vector3(0, verticalMove, 0) * Time.deltaTime);
        controller.Move(velocity * Time.deltaTime);
    }

    private void HandleLookRotation()
    {
        // 鼠标输入
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // 垂直视角旋转（上下看）
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -maxLookAngle, maxLookAngle);
        playerCamera.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);

        // 水平视角旋转（左右转）
        transform.Rotate(Vector3.up * mouseX);
    }

    private void UpdateInteractionRay()
    {
        // 更新射线位置（从摄像机位置发出）
        Vector3 rayStart = playerCamera.position;

        // 射线方向（摄像机前方）
        Vector3 rayDirection = playerCamera.forward;

        // 计算射线终点
        Vector3 rayEnd = rayStart + rayDirection * rayLength;

        // 射线碰撞检测
        RaycastHit hit;
        if (Physics.Raycast(rayStart, rayDirection, out hit, rayLength))
        {
            rayEnd = hit.point;
        }

        // 更新视觉射线（添加偏移）
        Vector3 offsetStart = rayStart + playerCamera.TransformDirection(rayOffset);
        interactionRay.SetPosition(0, offsetStart);
        interactionRay.SetPosition(1, rayEnd);

        // 调试信息（在控制台显示射线方向）
        Debug.DrawRay(rayStart, rayDirection * rayLength, rayColor);
    }
}