using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Detección táctil precisa de las 5 puntas de los dedos (Hand Tracking) sobre las teclas del piano.
///
/// Reglas físicas (sin cooldowns):
///   1. Solo las 5 puntas ('ThumbTip', 'IndexTip', 'MiddleTip', 'RingTip', 'LittleTip'/'PinkyTip')
///      pueden activar teclas con radio de 0.008 m (8 mm); los colliders en falanges medias/proximales/
///      distales se desactivan para evitar roces accidentales.
///   2. Exclusividad estricta 1 dedo físico = 1 tecla a la vez hasta salir del volumen de salida.
///   3. Filtro de velocidad descendente (velY < -0.12 m/s y |velY| > velocidad lateral) +
///      recorrido muerto (pre-travel >= 0.006 m por debajo de la cara superior de la tecla).
///   4. Envía la velocidad vertical de impacto a PianoKey para modular el volumen de la nota.
/// </summary>
public class HandColliders : MonoBehaviour
{
    [Header("Configuración de Detección")]
    [Tooltip("Radio de la esfera de detección en cada punta de dedo (metros). Se ajusta a 0.008 m en Start.")]
    public float detectionRadius = 0.008f;

    [Tooltip("Multiplicador del radio para considerar que el dedo salió de la tecla.")]
    public float multiplicadorSalida = 1.6f;

    [Tooltip("Máscara de capas de las teclas del piano.")]
    public LayerMask teclasMask;

    [Header("Filtro de Velocidad y Recorrido Muerto (Pre-travel)")]
    [Tooltip("Velocidad vertical máxima (negativa = bajando, m/s) requerida para disparar una tecla.")]
    public float velocidadMinimaDescenso = -0.12f;

    [Tooltip("Penetración mínima (metros) por debajo de la cara superior del collider estático (6 mm).")]
    public float preTravelMinimo = 0.006f;

    [Header("Protección de Inicio")]
    [Tooltip("Tiempo en segundos tras cargar la escena durante el cual se ignoran toques automáticos.")]
    public float startupGracePeriod = 0.5f;

    private struct EstadoDedo
    {
        public Transform nodo;
        public PianoKey  tecla;
    }

    // Estado por dedo físico completo (clave = "L_Index", "R_Thumb", etc.)
    private readonly Dictionary<string, EstadoDedo> dedosActivos      = new Dictionary<string, EstadoDedo>();
    private readonly Dictionary<Transform, Vector3> posAnteriorPorTip = new Dictionary<Transform, Vector3>();
    private readonly HashSet<int>                   nodosAuditados    = new HashSet<int>();

    // Buffers reutilizables (sin asignaciones por frame)
    private readonly List<Transform>               bufferTransforms = new List<Transform>();
    private readonly List<string>                  bufferLiberar    = new List<string>();
    private readonly Dictionary<string, Transform> puntaPorDedo     = new Dictionary<string, Transform>();
    private readonly Collider[]                    bufferColliders  = new Collider[16];

    void Start()
    {
        // Forzar radio preciso de yema (8 mm) para sobrescribir valores antiguos serializados en escena
        detectionRadius = 0.008f;

        if (teclasMask.value == 0) teclasMask = (1 << 6) | (1 << 0);

        AuditarCollidersDeManos();
        Debug.Log($"[HandColliders] Inicializado en '{gameObject.name}' | radio={detectionRadius}m | velMin={velocidadMinimaDescenso}m/s | preTravel={preTravelMinimo}m");
    }

    void FixedUpdate()
    {
        if (Time.timeSinceLevelLoad < startupGracePeriod) return;

        float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        float radioSalida = detectionRadius * multiplicadorSalida;

        // ── 1. Dedos físicos que YA tienen tecla: comprobar solo si siguen dentro ─
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

            // Mantener actualizada la posición anterior también mientras está presionada
            posAnteriorPorTip[dedo] = dedo.position;

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
                }
                dedosActivos.Remove(fingerId);
            }
        }

        // ── 2. Recopilar ÚNICAMENTE las 5 puntas activas (*Tip) y desactivar falanges ─
        bufferTransforms.Clear();
        GetComponentsInChildren(false, bufferTransforms);
        puntaPorDedo.Clear();

        for (int i = 0; i < bufferTransforms.Count; i++)
        {
            Transform t = bufferTransforms[i];
            if (t == null) continue;

            int id = t.gameObject.GetInstanceID();
            bool esTip = EsPuntaDeYemaValida(t);

            if (!nodosAuditados.Contains(id))
            {
                nodosAuditados.Add(id);
                ConfigurarODesactivarColliderHueso(t, esTip);
            }

            if (!esTip) continue;

            string fingerId = PianoKey.ObtenerIdDedoFisico(t);

            // Si este dedo físico ya está presionando una tecla, bloquear cualquier intento en vecinas
            if (dedosActivos.ContainsKey(fingerId)) continue;

            if (!puntaPorDedo.ContainsKey(fingerId))
            {
                puntaPorDedo[fingerId] = t;
            }
        }

        // ── 3. Dedos libres: exigir velocidad descendente + pre-travel (6 mm) ────
        foreach (var kv in puntaPorDedo)
        {
            string    fingerId = kv.Key;
            Transform dedo     = kv.Value;
            Vector3   posAct   = dedo.position;

            bool tienePrev = posAnteriorPorTip.TryGetValue(dedo, out Vector3 posPrev);
            posAnteriorPorTip[dedo] = posAct;

            if (!tienePrev) continue;

            Vector3 delta = posAct - posPrev;
            float velY = delta.y / dt;
            float velLateral = new Vector2(delta.x, delta.z).magnitude / dt;

            // Filtro 1: movimiento claramente descendente y mayor que el desplazamiento lateral
            if (velY >= velocidadMinimaDescenso || Mathf.Abs(velY) <= velLateral)
                continue;

            int n = Physics.OverlapSphereNonAlloc(posAct, detectionRadius, bufferColliders,
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

                // Filtro 2: recorrido muerto (penetración >= 6 mm bajo la cara superior del collider estático)
                if (!k.CumplePreTravel(posAct, preTravelMinimo)) continue;

                float dSup = Vector3.Distance(posAct, col.ClosestPoint(posAct));
                float dCen = (posAct - col.bounds.center).sqrMagnitude;

                if (dSup < distSuperficie - 0.0005f ||
                    (Mathf.Abs(dSup - distSuperficie) <= 0.0005f && dCen < distCentroSqr))
                {
                    distSuperficie = dSup;
                    distCentroSqr  = dCen;
                    keyCercana     = k;
                }
            }

            if (keyCercana != null && keyCercana.RegistrarToqueDedo(dedo, Mathf.Abs(velY)))
            {
                dedosActivos[fingerId] = new EstadoDedo { nodo = dedo, tecla = keyCercana };
                Debug.Log($"[HandColliders] DOWN '{keyCercana.nombreNotaCompleto}' dedo='{fingerId}' velY={velY:F2}m/s");
            }
        }
    }

    private void AuditarCollidersDeManos()
    {
        bufferTransforms.Clear();
        GetComponentsInChildren(true, bufferTransforms);

        for (int i = 0; i < bufferTransforms.Count; i++)
        {
            Transform t = bufferTransforms[i];
            if (t == null) continue;
            int id = t.gameObject.GetInstanceID();
            if (nodosAuditados.Contains(id)) continue;

            nodosAuditados.Add(id);
            ConfigurarODesactivarColliderHueso(t, EsPuntaDeYemaValida(t));
        }
    }

    private void ConfigurarODesactivarColliderHueso(Transform t, bool esTip)
    {
        if (esTip)
        {
            SphereCollider sc = t.GetComponent<SphereCollider>();
            if (sc != null)
            {
                sc.isTrigger = true;
                sc.radius = detectionRadius;
            }
            return;
        }

        // Desactivar colliders en falanges medias, proximales, distales, metacarpos o palma para evitar roces
        string n = t.name.ToLowerInvariant();
        if (n.Contains("controller") || n.Contains("mando") || n.Contains("ray") || n.Contains("teleport"))
            return;

        bool esFalangeNoPunta = n.Contains("proximal") || n.Contains("intermediate") ||
                                n.Contains("distal")   || n.Contains("metacarpal")   ||
                                n.Contains("knuckle")  || n.Contains("phalanx")      ||
                                n.Contains("palm")     || n.Contains("wrist");

        if (esFalangeNoPunta)
        {
            Collider[] cols = t.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && cols[i].enabled)
                {
                    cols[i].enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// Reconoce ÚNICAMENTE las puntas de los 5 dedos ('ThumbTip', 'IndexTip', 'MiddleTip',
    /// 'RingTip', 'LittleTip'/'PinkyTip'). Excluye falanges medias/proximales/distales y mandos.
    /// </summary>
    private static bool EsPuntaDeYemaValida(Transform t)
    {
        string n = t.name.ToLowerInvariant();

        if (n.Contains("controller") || n.Contains("mando") ||
            n.Contains("ray")        || n.Contains("teleport"))
            return false;

        if (!n.Contains("tip")) return false;

        return n.Contains("thumb")  || n.Contains("index")  || n.Contains("middle") ||
               n.Contains("ring")   || n.Contains("little") || n.Contains("pinky");
    }

    void OnDisable()
    {
        foreach (var kv in dedosActivos)
            if (kv.Value.tecla != null) kv.Value.tecla.LiberarToqueDedo(kv.Value.nodo);
        dedosActivos.Clear();
        posAnteriorPorTip.Clear();
    }
}
