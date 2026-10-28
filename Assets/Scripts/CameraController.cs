using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CameraController : MonoBehaviour
{
    public Transform player;   // referencia al jugador (la cápsula)
    public float sensitivity = 2f;
    public float clampAngle = 80f;

    private float rotY; // rotación vertical
    private float rotX; // rotación horizontal

    void Start()
    {
        Vector3 rot = transform.localRotation.eulerAngles;
        rotY = rot.y;
        rotX = rot.x;

        // Solo bloquear el cursor si no estamos en VR
        if (!UnityEngine.XR.XRSettings.isDeviceActive)
        {
            Cursor.lockState = CursorLockMode.None; // Mantener cursor libre para probar botones y piano en PC
            Cursor.visible = true;
        }
    }

    void Update()
    {
        float mouseX = 0f;
        float mouseY = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.rightButton.isPressed) // Solo rotar si mantiene clic derecho
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * 0.1f;
            mouseX = delta.x;
            mouseY = delta.y;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(1))
        {
            mouseX = Input.GetAxis("Mouse X");
            mouseY = Input.GetAxis("Mouse Y");
        }
#endif

        if (Mathf.Abs(mouseX) > 0.001f || Mathf.Abs(mouseY) > 0.001f)
        {
            rotY += mouseX * sensitivity;
            rotX -= mouseY * sensitivity;
            rotX = Mathf.Clamp(rotX, -clampAngle, clampAngle);

            Quaternion localRotation = Quaternion.Euler(rotX, rotY, 0.0f);
            transform.rotation = localRotation;
        }

        // Mantener la cámara en la posición del jugador
        if (player != null)
        {
            transform.position = player.position;
        }
    }
}
