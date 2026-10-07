using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class XRDesktopClearanceController : MonoBehaviour
{
    [SerializeField] private Camera validationCamera;
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float turnSpeed = 75f;
    [SerializeField] private float gravity = -9.81f;

    private CharacterController characterController;
    private float verticalSpeed;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        float turn = Input.GetAxisRaw("Horizontal");
        float forward = Input.GetAxisRaw("Vertical");
        transform.Rotate(0f, turn * turnSpeed * Time.deltaTime, 0f);

        Vector3 velocity = transform.forward * (forward * moveSpeed);
        if (characterController.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -1f;
        else
            verticalSpeed += gravity * Time.deltaTime;

        velocity.y = verticalSpeed;
        characterController.Move(velocity * Time.deltaTime);
    }

    public void SetValidationCamera(Camera cameraComponent)
    {
        validationCamera = cameraComponent;
    }
}
