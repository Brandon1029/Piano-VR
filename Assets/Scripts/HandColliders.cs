using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Detección táctil de las puntas de los dedos (Hand Tracking) sobre las teclas del piano.
///
/// Comportamiento idéntico a un clic de ratón (Down -> true, Up -> false), SIN cooldowns:
///   • Agrupa nodos por dedo físico completo (mano + dedo, ej. L_Index, R_Thumb) mediante
///     PianoKey.ObtenerIdDedoFisico, de modo que 'tip' y 'distal' del mismo dedo jamás
///     actúen como dos dedos distintos.
///   • Exclusividad estricta 1 dedo = 1 tecla: mientras un dedo físico tenga una tecla
///     activa, bloquea por completo cualquier intento de activar teclas vecinas hasta que
///     se retire del volumen de la tecla actual.
///   • Radio de entrada preciso de yema (0.009 m forzado en Start) priorizando únicamente
///     la tecla más cercana a la punta del dedo.
/// </summary>
public class HandColliders : MonoBehaviour
{
    [Header("Configuración de Detección")]
    [Tooltip("Radio de la esfera de detección en cada punta de dedo (metros). Se ajusta a 0.009 m en Start.")]
    public float detectionRadius = 0.009f;

    [Tooltip("Multiplicador del radio para considerar que el dedo salió de la tecla.")]
    public float multiplicadorSalida = 1.6f;

    [Tooltip("Máscara de capas de las teclas del piano.")]
    public LayerMask teclasMask;

    [Header("Protección de Inicio")]
    [Tooltip("Tiempo en segundos tras cargar la escena durante el cual se ignoran toques automáticos.")]
    public float startupGracePeriod = 0.5f;

    private struct EstadoDedo
    {
        public Transform nodo;
        public PianoKey  tecla;
    }

    // Estado por dedo físico completo (clave = "L_Index", "R_Middle", etc.)
    private readonly Dictionary<string, EstadoDedo> dedosActivos = new Dictionary<string, EstadoDedo>();

    // Buffers reutilizables (sin asignaciones por frame)
    private readonly List<Transform>              bufferTransforms = new List<Transform>();
    private readonly List<string>                 bufferLiberar    = new List<string>();
    private readonly Dictionary<string, Transform> puntaPorDedo    = new Dictionary<string, Transform>();
    private readonly Collider[]                   bufferColliders  = new Collider[16];

    void Start()
    {
        // Forzar radio preciso de yema (9 mm) para sobrescribir valores antiguos serializados en escena
        detectionRadius = 0.009f;

        // Si teclasMask no está configurada, usar capa 6 (Teclas) y capa 0 (Default)
        if (teclasMask.value == 0) teclasMask = (1 << 6) | (1 << 0);

        Debug.Log($"[HandColliders] Inicializado en '{gameObject.name}' con radio={detectionRadius}m, mask={teclasMask.value}");
    }

    void FixedUpdate()
    {
        if (Time.timeSinceLevelLoad < startupGracePeriod) return;

        float radioSalida = detectionRadius * multiplicadorSalida;

        // ── 1. Dedos físicos que YA tienen tecla: comprobar solo si siguen dentro ─
        // Ningún nodo de estos dedos puede tocar teclas vecinas hasta liberarse.
        bufferLiberar.Clear();
        foreach (var kv in dedosActivos)
        {
            string    fingerId = kv.Key;
            Transform dedo     = kv.Value.nodo;
            PianoKey  tecla    = kv.Value.tecla;

            if (dedo == null || !dedo.gameObject.activeInHierarchy || tecla == null)
            {
                bufferLiberar.Add(fingerId);
                continue;
            }

            if (!tecla.DedoDentroDeVolumen(dedo.position, radioSalida))
                bufferLiberar.Add(fingerId);
        }

        foreach (string fingerId in bufferLiberar)
        {
            if (dedosActivos.TryGetValue(fingerId, out EstadoDedo estado))
            {
                if (estado.tecla != null)
                {
                    estado.tecla.LiberarToqueDedo(estado.nodo);
                    Debug.Log($"[HandColliders] UP '{estado.tecla.nombreNotaCompleto}' dedo='{fingerId}'");
                }
                dedosActivos.Remove(fingerId);
            }
        }

        // ── 2. Agrupar nodos libres por dedo físico (1 sola punta por dedo) ───────
        bufferTransforms.Clear();
        GetComponentsInChildren(false, bufferTransforms);
        puntaPorDedo.Clear();

        foreach (Transform t in bufferTransforms)
        {
            if (t == null) continue;
            if (!EsNodoDeDedoValido(t, out bool esTip)) continue;

            string fingerId = PianoKey.ObtenerIdDedoFisico(t);

            // Si este dedo físico ya está presionando una tecla, bloquear todos sus nodos (tip, distal, etc.)
            if (dedosActivos.ContainsKey(fingerId)) continue;

            // Priorizar siempre el nodo 'tip' sobre 'distal' para que cada dedo físico tenga 1 solo punto
            if (!puntaPorDedo.TryGetValue(fingerId, out Transform existente) ||
                (esTip && !EsNombreTip(existente.name)))
            {
                puntaPorDedo[fingerId] = t;
            }
        }

        // ── 3. Dedos físicos libres: buscar SOLO la tecla más cercana ─────────────
        foreach (var kv in puntaPorDedo)
        {
            string    fingerId = kv.Key;
            Transform dedo     = kv.Value;

            int n = Physics.OverlapSphereNonAlloc(dedo.position, detectionRadius, bufferColliders,
                                                  teclasMask, QueryTriggerInteraction.Collide);

            PianoKey keyCercana     = null;
            float    distSuperficie = float.MaxValue;
            float    distCentroSqr  = float.MaxValue;

            for (int i = 0; i < n; i++)
            {
                Collider col = bufferColliders[i];
                if (col == null) continue;

                PianoKey k = col.GetComponentInParent<PianoKey>();
                if (k == null) continue;

                float dSup = Vector3.Distance(dedo.position, col.ClosestPoint(dedo.position));
                float dCen = (dedo.position - col.bounds.center).sqrMagnitude;

                // Priorizar la tecla más cercana en superficie; en empate (ej. dentro de 2 colliders), la de centro más cercano
                if (dSup < distSuperficie - 0.0005f ||
                    (Mathf.Abs(dSup - distSuperficie) <= 0.0005f && dCen < distCentroSqr))
                {
                    distSuperficie = dSup;
                    distCentroSqr  = dCen;
                    keyCercana     = k;
                }
            }

            if (keyCercana != null && keyCercana.RegistrarToqueDedo(dedo))
            {
                dedosActivos[fingerId] = new EstadoDedo { nodo = dedo, tecla = keyCercana };
                Debug.Log($"[HandColliders] DOWN '{keyCercana.nombreNotaCompleto}' dedo='{fingerId}' ({dedo.name})");
            }
        }
    }

    private static bool EsNombreTip(string nombre)
    {
        return nombre != null && nombre.ToLowerInvariant().Contains("tip");
    }

    /// <summary>
    /// Reconoce nodos de dedos (tip / distal / tag PlayerFinger) excluyendo mandos/rayos/teleport.
    /// El agrupamiento por fingerId garantiza que 'tip' y 'distal' del mismo dedo nunca actúen por separado.
    /// </summary>
    private static bool EsNodoDeDedoValido(Transform t, out bool esTip)
    {
        esTip = false;
        string n = t.name.ToLowerInvariant();

        if (n.Contains("controller") || n.Contains("mando") ||
            n.Contains("ray")        || n.Contains("teleport"))
            return false;

        bool esDedo = n.Contains("index") || n.Contains("middle") || n.Contains("ring") ||
                      n.Contains("pinky") || n.Contains("little") || n.Contains("thumb");

        if (esDedo && (n.Contains("tip") || n.Contains("distal")))
        {
            esTip = n.Contains("tip");
            return true;
        }

        if (t.gameObject.tag == "PlayerFinger")
        {
            esTip = n.Contains("tip");
            return true;
        }

        return false;
    }

    void OnDisable()
    {
        foreach (var kv in dedosActivos)
            if (kv.Value.tecla != null) kv.Value.tecla.LiberarToqueDedo(kv.Value.nodo);
        dedosActivos.Clear();
    }
}
