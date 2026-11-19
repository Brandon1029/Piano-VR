using UnityEngine;

/// <summary>
/// Toque de una punta de dedo sobre las teclas del piano.
///
/// • Identificador del dedo = este Transform (el mismo que usa HandColliders), por lo que
///   aunque ambos sistemas vieran el mismo dedo, PianoKey nunca dispara dos veces.
/// • Si hay un HandColliders activo en la escena, este componente NO dispara
///   (evita doble detección); HandColliders es el controlador del rig.
/// • Sin timers ni cooldowns: se libera solo cuando el dedo sale del volumen de la tecla
///   (PianoKey.DedoDentroDeVolumen, compensado en Y por pressDepth). No usa OnTriggerExit.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class FingerPianoTouch : MonoBehaviour
{
    [Header("Ajustes de Toque Único")]
    [Tooltip("Radio de la yema del dedo (mínimo 4 mm)")]
    public float radioYema = 0.004f;

    [Tooltip("Multiplicador del radio para considerar que el dedo salió de la tecla.")]
    public float multiplicadorSalida = 1.6f;

    [Tooltip("Si hay un HandColliders activo en la escena, este componente no dispara teclas.")]
    public bool cederAHandColliders = true;

    private PianoKey teclaActiva = null;
    private bool controladoPorHandColliders = false;

    private void Awake()
    {
        if (radioYema < 0.004f) radioYema = 0.004f;

        SphereCollider col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = radioYema;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Start()
    {
        if (cederAHandColliders)
        {
            HandColliders hc = FindAnyObjectByType<HandColliders>();
            controladoPorHandColliders = hc != null && hc.isActiveAndEnabled;
            if (controladoPorHandColliders)
                Debug.Log($"[FingerPianoTouch] '{name}': HandColliders controla el rig, disparo propio desactivado.");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (controladoPorHandColliders) return;
        if (teclaActiva != null) return; // 1 dedo = máximo 1 tecla

        PianoKey key = other.GetComponentInParent<PianoKey>();
        if (key != null && key.RegistrarToqueDedo(transform))
        {
            teclaActiva = key;
        }
    }

    private void FixedUpdate()
    {
        if (teclaActiva == null) return;

        // Bloqueado mientras siga dentro del volumen compensado; solo se libera al salir.
        if (!teclaActiva.DedoDentroDeVolumen(transform.position, radioYema * multiplicadorSalida))
        {
            LiberarTeclaActual();
        }
    }

    public void LiberarTeclaActual()
    {
        if (teclaActiva != null)
        {
            teclaActiva.LiberarToqueDedo(transform);
            teclaActiva = null;
        }
    }

    private void OnDisable()
    {
        LiberarTeclaActual();
    }
}
