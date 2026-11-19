using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Detección táctil de las puntas de los dedos (Hand Tracking) sobre las teclas del piano.
///
/// Comportamiento idéntico a un clic de ratón (Down -> true, Up -> false), SIN cooldowns:
///   • Identificador único de cada dedo = su Transform (nunca t.name).
///   • Un dedo con tecla activa NO busca otras teclas (1 dedo = máx. 1 tecla).
///   • El dedo permanece bloqueado mientras siga dentro de un volumen de salida ampliado
///     (detectionRadius * 1.6) evaluado en su posición y compensado hacia abajo por el
///     hundimiento de la tecla (pressDepth). Solo al salir por completo se libera.
/// </summary>
public class HandColliders : MonoBehaviour
{
    [Header("Configuración de Detección")]
    [Tooltip("Radio de la esfera de detección en cada punta de dedo (metros).")]
    public float detectionRadius = 0.02f;

    [Tooltip("Multiplicador del radio para considerar que el dedo salió de la tecla.")]
    public float multiplicadorSalida = 1.6f;

    [Tooltip("Máscara de capas de las teclas del piano.")]
    public LayerMask teclasMask;

    [Header("Protección de Inicio")]
    [Tooltip("Tiempo en segundos tras cargar la escena durante el cual se ignoran toques automáticos.")]
    public float startupGracePeriod = 0.5f;

    // Único estado: qué tecla tiene presionada cada dedo (clave = instancia Transform)
    private readonly Dictionary<Transform, PianoKey> teclaActivaPorDedo = new Dictionary<Transform, PianoKey>();

    // Buffers reutilizables (sin asignaciones por frame)
    private readonly List<Transform> bufferTransforms = new List<Transform>();
    private readonly List<Transform> bufferLiberar    = new List<Transform>();
    private readonly Collider[]      bufferColliders  = new Collider[16];

    void Start()
    {
        if (detectionRadius < 0.005f) detectionRadius = 0.02f;

        // Si teclasMask no está configurada, usar capa 6 (Teclas) y capa 0 (Default)
        if (teclasMask.value == 0) teclasMask = (1 << 6) | (1 << 0);

        Debug.Log($"[HandColliders] Inicializado en '{gameObject.name}' con radio={detectionRadius}m, mask={teclasMask.value}");
    }

    void FixedUpdate()
    {
        if (Time.timeSinceLevelLoad < startupGracePeriod) return;

        float radioSalida = detectionRadius * multiplicadorSalida;

        // ── 1. Dedos que YA tienen tecla: solo comprobar si siguen dentro ──────
        bufferLiberar.Clear();
        foreach (var kv in teclaActivaPorDedo)
        {
            Transform dedo  = kv.Key;
            PianoKey  tecla = kv.Value;

            // Dedo destruido / oculto (tracking perdido) o tecla destruida -> liberar
            if (dedo == null || !dedo.gameObject.activeInHierarchy || tecla == null)
            {
                bufferLiberar.Add(dedo);
                continue;
            }

            if (!tecla.DedoDentroDeVolumen(dedo.position, radioSalida))
                bufferLiberar.Add(dedo);
            // Mientras siga dentro: bloqueado, no se hace nada (no vuelve a sonar).
        }

        foreach (Transform dedo in bufferLiberar)
        {
            if (teclaActivaPorDedo.TryGetValue(dedo, out PianoKey tecla))
            {
                if (tecla != null)
                {
                    tecla.LiberarToqueDedo(dedo);
                    Debug.Log($"[HandColliders] UP '{tecla.nombreNotaCompleto}' (dedo salió del volumen)");
                }
                teclaActivaPorDedo.Remove(dedo);
            }
        }

        // ── 2. Dedos libres: buscar UNA sola tecla dentro de detectionRadius ───
        bufferTransforms.Clear();
        GetComponentsInChildren(false, bufferTransforms);

        foreach (Transform dedo in bufferTransforms)
        {
            if (dedo == null) continue;
            if (teclaActivaPorDedo.ContainsKey(dedo)) continue; // ya bloqueado con su tecla
            if (!EsPuntaDeDedo(dedo)) continue;

            int n = Physics.OverlapSphereNonAlloc(dedo.position, detectionRadius, bufferColliders,
                                                  teclasMask, QueryTriggerInteraction.Collide);

            PianoKey keyCercana = null;
            float    distMin    = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider col = bufferColliders[i];
                if (col == null) continue;

                PianoKey k = col.GetComponentInParent<PianoKey>();
                if (k == null) continue;

                float d = Vector3.Distance(dedo.position, col.ClosestPoint(dedo.position));
                if (d < distMin) { distMin = d; keyCercana = k; }
            }

            if (keyCercana != null && keyCercana.RegistrarToqueDedo(dedo))
            {
                teclaActivaPorDedo[dedo] = keyCercana;
                Debug.Log($"[HandColliders] DOWN '{keyCercana.nombreNotaCompleto}' por '{dedo.name}'");
            }
        }
    }

    /// <summary>
    /// Solo las PUNTAS (tip) de los dedos actúan como identificador de dedo.
    /// Se excluyen huesos distales/poke/attach para que un mismo dedo físico no
    /// aparezca como varios "dedos" distintos, y se excluyen mandos/rayos/teleport.
    /// </summary>
    private static bool EsPuntaDeDedo(Transform t)
    {
        string n = t.name.ToLower();

        if (n.Contains("controller") || n.Contains("mando") ||
            n.Contains("ray")        || n.Contains("teleport"))
            return false;

        if (n.Contains("tip") &&
            (n.Contains("index") || n.Contains("middle") || n.Contains("ring") ||
             n.Contains("pinky") || n.Contains("little") || n.Contains("thumb")))
            return true;

        string tagStr = t.gameObject.tag;
        return tagStr == "PlayerFinger";
    }

    void OnDisable()
    {
        foreach (var kv in teclaActivaPorDedo)
            if (kv.Value != null) kv.Value.LiberarToqueDedo(kv.Key);
        teclaActivaPorDedo.Clear();
    }
}
