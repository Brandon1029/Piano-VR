using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Provee detección de proximidad y contacto físico para dedos (Hand Tracking) y mandos de Meta Quest.
/// Permite que las teclas se activen al rozar o tocar suavemente sin necesidad de presionar botones.
/// </summary>
public class HandColliders : MonoBehaviour
{
    [Header("Configuración de Detección")]
    [Tooltip("Radio de la esfera de detección en cada punta de dedo / mando.")]
    public float detectionRadius = 0.02f;

    [Tooltip("Tiempo mínimo entre activaciones consecutivas con el mismo dedo/mando.")]
    public float tiempoMinEntreNotas = 0.08f;

    [Tooltip("Máscara de capas de las teclas del piano.")]
    public LayerMask teclasMask;

    [Header("Protección de Inicio")]
    [Tooltip("Tiempo en segundos tras cargar la escena durante el cual se ignoran toques automáticos.")]
    public float startupGracePeriod = 0.5f;

    private readonly Dictionary<string, PianoKey> teclaActual = new Dictionary<string, PianoKey>();
    private readonly Dictionary<string, float> ultimoToque = new Dictionary<string, float>();
    private readonly Dictionary<PianoKey, string> teclaPresionadaPor = new Dictionary<PianoKey, string>();

    void Start()
    {
        if (detectionRadius < 0.015f)
        {
            detectionRadius = 0.02f;
        }

        // Si teclasMask no está configurada, usar capa 6 (Teclas) y capa 0 (Default)
        if (teclasMask.value == 0)
        {
            teclasMask = (1 << 6) | (1 << 0);
        }

        Debug.Log($"[HandColliders] Inicializado en '{gameObject.name}' con radio={detectionRadius}m, mask={teclasMask.value}");
    }

    void Update()
    {
        // Prevención de activación automática al iniciar la escena
        if (Time.timeSinceLevelLoad < startupGracePeriod)
            return;

        Transform[] todos = GetComponentsInChildren<Transform>(true);

        foreach (Transform t in todos)
        {
            if (t == null) continue;
            string n = t.name.ToLower();

            // Detectar puntas de dedos (manos virtuales) o interactores/puntas de mandos VR
            bool esPunta = (n.Contains("index")  && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("middle") && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("ring")   && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("pinky")  && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("little") && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("thumb")  && (n.Contains("tip") || n.Contains("distal"))) ||
                           n.Contains("poke")    || n.Contains("attach") || n.Contains("controller");

            // Si el nombre no coincide, verificar si tiene tags de mano o dedo
            if (!esPunta)
            {
                string tagStr = t.gameObject.tag;
                if (tagStr == "PlayerFinger" || tagStr == "PlayerHand" || tagStr == "Hand")
                {
                    esPunta = true;
                }
            }

            if (!esPunta) continue;

            // Detectar teclas en el radio de contacto
            Collider[] cols = Physics.OverlapSphere(t.position, detectionRadius, teclasMask);
            if (cols.Length == 0)
            {
                cols = Physics.OverlapSphere(t.position + Vector3.down * 0.005f, detectionRadius, teclasMask);
            }

            // Buscar la tecla más cercana
            PianoKey keyDetectada = null;
            float distanciaMinima = float.MaxValue;
            foreach (var col in cols)
            {
                if (col == null) continue;

                PianoKey k = col.GetComponent<PianoKey>();
                if (k == null)
                {
                    k = col.GetComponentInParent<PianoKey>();
                }

                if (k != null)
                {
                    float dist = Vector3.Distance(t.position, col.ClosestPoint(t.position));
                    if (dist < distanciaMinima)
                    {
                        distanciaMinima = dist;
                        keyDetectada = k;
                    }
                }
            }

            teclaActual.TryGetValue(t.name, out PianoKey keyAnterior);
            ultimoToque.TryGetValue(t.name, out float ultimoToqueTime);

            // Salió de la tecla anterior
            if (keyDetectada != keyAnterior)
            {
                if (keyAnterior != null)
                {
                    if (teclaPresionadaPor.TryGetValue(keyAnterior, out string dedoQuePresiona) && dedoQuePresiona == t.name)
                    {
                        keyAnterior.OnPokeExit();
                        teclaPresionadaPor.Remove(keyAnterior);
                    }
                }
                teclaActual[t.name] = keyDetectada;
            }

            // Activación por contacto físico/roce directo (sin exigir presionar botones ni alta velocidad)
            if (keyDetectada != null &&
                Time.time - ultimoToqueTime > tiempoMinEntreNotas &&
                !teclaPresionadaPor.ContainsKey(keyDetectada))
            {
                Debug.Log($"[HandColliders] Tecla activada por roce/toque: '{keyDetectada.nombreNotaCompleto}' por '{t.name}'");
                keyDetectada.OnPokeEnter();
                ultimoToque[t.name] = Time.time;
                teclaPresionadaPor[keyDetectada] = t.name;
            }
        }
    }
}
