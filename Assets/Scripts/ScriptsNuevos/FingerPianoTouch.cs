using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class FingerPianoTouch : MonoBehaviour
{
    [Header("Ajustes de Toque Único")]
    [Tooltip("Radio de la yema del dedo (4 mm)")]
    public float radioYema = 0.004f;

    [Tooltip("Cuánto debe subir el dedo (en metros) por encima del punto donde tocó la tecla para poder volver a tocar")]
    public float alturaMinimaParaRearmar = 0.018f; // 1.8 cm hacia arriba

    private PianoKey teclaActiva = null;
    private float yAlTocar = 0f;
    private bool bloqueadoHastaLevantar = false;

    private void Awake()
    {
        SphereCollider col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = radioYema;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Update()
    {
        if (!bloqueadoHastaLevantar) return;

        // El dedo permanece BLOQUEADO (aunque la tecla baje y suba)
        // hasta que levantes el dedo 1.8 cm por encima de donde hiciste el toque.
        if (transform.position.y >= yAlTocar + alturaMinimaParaRearmar)
        {
            LiberarTeclaActual();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Si ya tocó una tecla y no has levantado el dedo, ignora TODO (ni repite ni toca vecinas)
        if (bloqueadoHastaLevantar || teclaActiva != null) return;

        PianoKey key = other.GetComponentInParent<PianoKey>();
        if (key != null)
        {
            teclaActiva = key;
            yAlTocar = transform.position.y;
            bloqueadoHastaLevantar = true;
            teclaActiva.RegistrarToqueDedo(this);
        }
    }

    // NO usamos OnTriggerExit para desbloquear, porque al hundirse la tecla
    // dispara OnTriggerExit falsos aunque no hayas movido el dedo.

    public void LiberarTeclaActual()
    {
        if (teclaActiva != null)
        {
            teclaActiva.LiberarToqueDedo(this);
            teclaActiva = null;
        }
        bloqueadoHastaLevantar = false;
    }

    private void OnDisable()
    {
        LiberarTeclaActual();
    }
}