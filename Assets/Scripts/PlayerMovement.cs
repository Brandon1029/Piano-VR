using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    public Transform cameraTransform; // referencia a la cámara
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        float moveHorizontal = Input.GetAxis("Horizontal"); // A/D
        float moveVertical = Input.GetAxis("Vertical");     // W/S

        // Dirección relativa a la cámara
        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;

        // Ignorar inclinación vertical de la cámara
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        // Movimiento según cámara
        Vector3 movement = (forward * moveVertical + right * moveHorizontal).normalized;

        rb.linearVelocity = movement * speed;
    }}
