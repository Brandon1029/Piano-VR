using UnityEngine;
using System.Collections.Generic;

public class HandColliders : MonoBehaviour
{
    public float detectionRadius = 0.015f;
    public float velocidadMinima = 0.15f;
    public float tiempoMinEntreNotas = 0.15f;
    public LayerMask teclasMask;

    Dictionary<string, PianoKey> teclaActual  = new Dictionary<string, PianoKey>();
    Dictionary<string, Vector3>  posAnterior  = new Dictionary<string, Vector3>();
    Dictionary<string, float>    ultimoToque  = new Dictionary<string, float>();

    // Teclas actualmente presionadas por cualquier dedo
    Dictionary<PianoKey, string> teclaPresionadaPor = new Dictionary<PianoKey, string>();

    void Update()
    {
        Transform[] todos = GetComponentsInChildren<Transform>(true);

        foreach (Transform t in todos)
        {
            string n = t.name.ToLower();

            bool esPunta = (n.Contains("index")  && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("middle")  && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("ring")    && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("pinky")   && (n.Contains("tip") || n.Contains("distal"))) ||
                           (n.Contains("thumb")   && (n.Contains("tip") || n.Contains("distal")));

            if (!esPunta) continue;

            // Velocidad del dedo
            posAnterior.TryGetValue(t.name, out Vector3 posVieja);
            float velocidadY = posVieja != Vector3.zero
                ? (t.position.y - posVieja.y) / Time.deltaTime
                : 0f;
            posAnterior[t.name] = t.position;

            // Detectar tecla
            Collider[] cols = Physics.OverlapSphere(t.position, detectionRadius, teclasMask);
            if (cols.Length == 0)
                cols = Physics.OverlapSphere(t.position + Vector3.down * 0.005f, detectionRadius, teclasMask);

            // Buscar la tecla más cercana
            PianoKey keyDetectada = null;
            float distanciaMinima = float.MaxValue;
            foreach (var col in cols)
            {
                PianoKey k = col.GetComponent<PianoKey>();
                if (k != null)
                {
                    float dist = Vector3.Distance(t.position, col.transform.position);
                    if (dist < distanciaMinima)
                    {
                        distanciaMinima = dist;
                        keyDetectada = k;
                    }
                }
            }

            teclaActual.TryGetValue(t.name, out PianoKey keyAnterior);
            ultimoToque.TryGetValue(t.name, out float ultimoToqueTime);

            // Salió de la tecla
            if (keyDetectada != keyAnterior)
            {
                if (keyAnterior != null)
                {
                    // Solo hacer OnPokeExit si este dedo era el que la presionaba
                    if (teclaPresionadaPor.TryGetValue(keyAnterior, out string dedoQuePresiona)
                        && dedoQuePresiona == t.name)
                    {
                        keyAnterior.OnPokeExit();
                        teclaPresionadaPor.Remove(keyAnterior);
                    }
                }
                teclaActual[t.name] = keyDetectada;
            }

            // Tocar tecla — cada dedo puede tocar su propia tecla
            if (keyDetectada != null &&
                velocidadY < -velocidadMinima &&
                Time.time - ultimoToqueTime > tiempoMinEntreNotas &&
                !teclaPresionadaPor.ContainsKey(keyDetectada)) // evita doble toque
            {
                keyDetectada.OnPokeEnter();
                ultimoToque[t.name] = Time.time;
                teclaPresionadaPor[keyDetectada] = t.name;
            }
        }
    }
}