using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Toque de una punta de dedo sobre las teclas del piano.
///
/// • Agrupa los nodos por dedo físico completo (PianoKey.ObtenerIdDedoFisico) para que
///   'tip' y 'distal' del mismo dedo jamás activen teclas distintas ni vecinas a la vez.
/// • Si hay un HandColliders activo en la escena, este componente cede el control para
///   evitar doble detección.
/// • Sin timers ni cooldowns: se libera solo cuando el dedo sale del volumen de la tecla
///   (PianoKey.DedoDentroDeVolumen, compensado en Y por pressDepth). No usa OnTriggerExit.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class FingerPianoTouch : MonoBehaviour
{
    [Header("Ajustes de Toque Único")]
    [Tooltip("Radio de la yema del dedo (metros). Se ajusta a 0.009 m en Awake.")]
    public float radioYema = 0.009f;

    [Tooltip("Multiplicador del radio para considerar que el dedo salió de la tecla.")]
    public float multiplicadorSalida = 1.6f;

    [Tooltip("Si hay un HandColliders activo en la escena, este componente no dispara teclas.")]
    public bool cederAHandColliders = true;

    // Registro estático por dedo físico completo ("L_Index", "R_Thumb", etc.): 1 dedo físico = máx. 1 tecla
    private static readonly Dictionary<string, FingerPianoTouch> dedosFisicosActivos = new Dictionary<string, FingerPianoTouch>();
    private static readonly Collider[] bufferColliders = new Collider[16];

    private string   idDedoFisico = string.Empty;
    private PianoKey teclaActiva  = null;
    private bool     controladoPorHandColliders = false;

    private void Awake()
    {
        // Forzar radio preciso de yema (9 mm) para unificar todas las instancias en escena
        radioYema = 0.009f;
        idDedoFisico = PianoKey.ObtenerIdDedoFisico(transform);

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
        if (teclaActiva != null) return;

        // Bloqueo estricto por dedo físico completo (impide que tip y distal o teclas vecinas disparen a la vez)
        if (dedosFisicosActivos.TryGetValue(idDedoFisico, out FingerPianoTouch dueno) && dueno != null)
            return;

        // Priorizar únicamente la tecla más cercana a la punta del dedo
        PianoKey keyCercana = BuscarTeclaMasCercana(other);
        if (keyCercana != null && keyCercana.RegistrarToqueDedo(transform))
        {
            teclaActiva = keyCercana;
            dedosFisicosActivos[idDedoFisico] = this;
        }
    }

    private PianoKey BuscarTeclaMasCercana(Collider fallbackCollider)
    {
        int n = Physics.OverlapSphereNonAlloc(transform.position, radioYema, bufferColliders,
                                              ~0, QueryTriggerInteraction.Collide);

        PianoKey keyCercana     = null;
        float    distSuperficie = float.MaxValue;
        float    distCentroSqr  = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            Collider col = bufferColliders[i];
            if (col == null) continue;

            PianoKey k = col.GetComponentInParent<PianoKey>();
            if (k == null) continue;

            float dSup = Vector3.Distance(transform.position, col.ClosestPoint(transform.position));
            float dCen = (transform.position - col.bounds.center).sqrMagnitude;

            if (dSup < distSuperficie - 0.0005f ||
                (Mathf.Abs(dSup - distSuperficie) <= 0.0005f && dCen < distCentroSqr))
            {
                distSuperficie = dSup;
                distCentroSqr  = dCen;
                keyCercana     = k;
            }
        }

        if (keyCercana == null && fallbackCollider != null)
            keyCercana = fallbackCollider.GetComponentInParent<PianoKey>();

        return keyCercana;
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

        if (!string.IsNullOrEmpty(idDedoFisico) &&
            dedosFisicosActivos.TryGetValue(idDedoFisico, out FingerPianoTouch dueno) && dueno == this)
        {
            dedosFisicosActivos.Remove(idDedoFisico);
        }
    }

    private void OnDisable()
    {
        LiberarTeclaActual();
    }
}
