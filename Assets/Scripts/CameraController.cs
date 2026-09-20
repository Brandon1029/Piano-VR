using UnityEngine;

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
        Cursor.lockState = CursorLockMode.Locked; // bloquea el cursor en pantalla
        Cursor.visible = false;
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        rotY += mouseX * sensitivity;
        rotX -= mouseY * sensitivity;
        rotX = Mathf.Clamp(rotX, -clampAngle, clampAngle);

        Quaternion localRotation = Quaternion.Euler(rotX, rotY, 0.0f);
        transform.rotation = localRotation;

        // Mantener la cámara en la posición del jugador
        transform.position = player.position;
    }
}
