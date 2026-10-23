using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class PianoKey : MonoBehaviour
{
    [Header("Física")]
    public float pressDepth  = 0.008f;
    public float pressSpeed  = 20f;
    public float returnSpeed = 8f;

    [Header("Configuración de Impacto")]
    [Tooltip("Distancia vertical máxima (en metros) entre la nota y la tecla para considerar un acierto.")]
    public float rangoToleranciaHit = 0.08f;

    [Header("Colores de Turno")]
    [Tooltip("Color de la tecla que debe tocarse ya")]
    [ColorUsage(true, true)]
    public Color colorTurnoActual = new Color(1f, 0.92f, 0.016f, 1f); // Amarillo neón

    [Tooltip("Color de las teclas que tienen notas en camino")]
    [ColorUsage(true, true)]
    public Color colorEnEspera = new Color(0f, 0.6f, 1f, 1f);        // Azul cian

    [Tooltip("Intensidad máxima del brillo HDR")]
    public float maxGlowIntensity = 2.5f;

    [Header("Datos de la Nota Dinámica")]
    public string notaNombreBase = "";     // Ej: "C", "C#", "D"
    public int offsetOctavaRelativa = 0;    // 0 para la 1ra octava física, 1 para la 2da, etc.
    public int octavaCalculada = 4;        // Octava resultante activa
    public string nombreNotaCompleto = ""; // Ej: "C4", "A#3"

    [Header("Etiqueta Visual de Octava")]
    public TMPro.TMP_Text textoEtiquetaOctava;

    [Header("Pop-up Visual de Nota")]
    public GameObject prefabPopUpNota;
    public bool mostrarPopUpNota = true;

    AudioSource audioSource;
    Rigidbody   rb;
    Vector3     restLocalPosition;
    Vector3     pressedLocalPosition;
    public bool isPressed = false;

    Renderer keyRenderer;
    MaterialPropertyBlock propBlock;
    static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

    public static List<PianoKey> todasLasTeclas = new List<PianoKey>();
    public static List<NotaMovimiento> notasEnEscena = new List<NotaMovimiento>();

    // Diccionario original para validación de notas base
    static Dictionary<string, string> notaES = new Dictionary<string, string>()
    {
        {"C", "C"}, {"D", "D"}, {"E", "E"},
        {"F", "F"}, {"G", "G"}, {"A", "A"}, {"B", "B"}
    };

    void Awake()
    {
        keyRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();
        if (!todasLasTeclas.Contains(this))
            todasLasTeclas.Add(this);

        ExtraerNotaYOctavaInicial();
    }

    void OnDestroy()
    {
        todasLasTeclas.Remove(this);
    }

    void Start()
    {
        audioSource          = GetComponent<AudioSource>();
        rb                   = GetComponent<Rigidbody>();
        restLocalPosition    = transform.localPosition;
        pressedLocalPosition = restLocalPosition - new Vector3(0, pressDepth, 0);

        int octavaBase = (PianoManager.Instance != null) ? PianoManager.Instance.octavaBaseActual : 3;
        ReconfigurarOctava(octavaBase);
    }

    private void ExtraerNotaYOctavaInicial()
    {
        string nombre = gameObject.name.Trim();
        string notaEN = "";

        for (int i = 0; i < nombre.Length; i++)
        {
            string c = nombre[i].ToString().ToUpper();
            if (notaES.ContainsKey(c))
            {
                notaEN = c;
                break;
            }
        }

        if (string.IsNullOrEmpty(notaEN)) return;

        bool sostenido = nombre.Contains("#");
        notaNombreBase = notaEN + (sostenido ? "#" : "");

        int octavaEnNombre = 3;
        for (int i = nombre.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(nombre[i]))
            {
                octavaEnNombre = int.Parse(nombre[i].ToString());
                break;
            }
        }

        offsetOctavaRelativa = Mathf.Max(0, octavaEnNombre - 3);
    }

    public void ReconfigurarOctava(int octavaBaseGlobal)
    {
        if (string.IsNullOrEmpty(notaNombreBase)) return;

        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        octavaCalculada = octavaBaseGlobal + offsetOctavaRelativa;
        nombreNotaCompleto = notaNombreBase + octavaCalculada;

        ConfigurarNota();

        // Actualiza la etiqueta si es una tecla C (Do)
        if (notaNombreBase == "C" && textoEtiquetaOctava != null)
        {
            textoEtiquetaOctava.text = nombreNotaCompleto;
        }
    }

    void ConfigurarNota()
    {
        bool sostenido = notaNombreBase.Contains("#");
        string notaLimpia = notaNombreBase.Replace("#", "");

        if (!notaES.ContainsKey(notaLimpia)) return;

        // 1. Si es sostenido (#), obtiene su equivalente en bemol (b) (ej: C#3 -> Db3)
        string nombreNotaParaArchivo = notaES[notaLimpia];
        if (sostenido)
        {
            nombreNotaParaArchivo = ObtenerEquivalenteBemol(notaLimpia);
        }

        string nombreArchivoExacto = nombreNotaParaArchivo + octavaCalculada;
        AudioClip clipExacto = Resources.Load<AudioClip>(nombreArchivoExacto);

        if (clipExacto != null)
        {
            audioSource.clip  = clipExacto;
            audioSource.pitch = 1f; // Sin alteración de pitch
            return;
        }

        // 2. Respaldo: Si no existe el archivo alterado, usa la nota base y modifica el pitch (1.0595)
        string nombreBase = notaES[notaLimpia] + octavaCalculada;
        AudioClip clipBase = Resources.Load<AudioClip>(nombreBase);

        if (clipBase != null)
        {
            audioSource.clip  = clipBase;
            audioSource.pitch = sostenido ? 1.0595f : 1f;
            return;
        }

        // 3. Respaldo por octava 4 si la octava calculada no existe
        string nombreRespaldoOctava4 = notaES[notaLimpia] + "4";
        AudioClip clipOctava4 = Resources.Load<AudioClip>(nombreRespaldoOctava4);

        if (clipOctava4 != null)
        {
            audioSource.clip = clipOctava4;
            float multiplicadorOctava = Mathf.Pow(2f, octavaCalculada - 4);
            audioSource.pitch = multiplicadorOctava * (sostenido ? 1.0595f : 1f);
        }
    }

    private string ObtenerEquivalenteBemol(string notaBase)
    {
        switch (notaBase)
        {
            case "C": return "Db";
            case "D": return "Eb";
            case "F": return "Gb";
            case "G": return "Ab";
            case "A": return "Bb";
            default: return notaBase;
        }
    }

    void LateUpdate()
    {
        if (todasLasTeclas.Count > 0 && this == todasLasTeclas[0])
        {
            ActualizarColoresGlobales();
        }
    }

    private static void ActualizarColoresGlobales()
    {
        notasEnEscena.RemoveAll(n => n == null);

        foreach (var key in todasLasTeclas)
        {
            key.ApplyEmission(Color.black, 0f);
        }

        if (notasEnEscena.Count == 0) return;

        Dictionary<PianoKey, NotaMovimiento> notaMasBajaPorTecla = new Dictionary<PianoKey, NotaMovimiento>();
        float menorYGlobal = float.MaxValue;

        foreach (var nota in notasEnEscena)
        {
            if (nota.targetKey == null || nota.targetKey.isPressed) continue;

            float yActual = nota.transform.position.y;

            if (yActual < menorYGlobal)
                menorYGlobal = yActual;

            if (!notaMasBajaPorTecla.ContainsKey(nota.targetKey))
            {
                notaMasBajaPorTecla[nota.targetKey] = nota;
            }
            else
            {
                if (yActual < notaMasBajaPorTecla[nota.targetKey].transform.position.y)
                {
                    notaMasBajaPorTecla[nota.targetKey] = nota;
                }
            }
        }

        foreach (var par in notaMasBajaPorTecla)
        {
            PianoKey key = par.Key;
            NotaMovimiento nota = par.Value;

            bool esTurnoActual = Mathf.Abs(nota.transform.position.y - menorYGlobal) <= 0.08f;
            Color baseColor = esTurnoActual ? key.colorTurnoActual : key.colorEnEspera;

            float progreso = nota.ObtenerProgreso();

            key.ApplyEmission(baseColor, progreso);
        }
    }

    public void ApplyEmission(Color color, float progress)
    {
        if (keyRenderer == null) return;

        progress = Mathf.Clamp01(progress);
        Color finalEmission = color * (progress * maxGlowIntensity);

        keyRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(EmissionColorProp, finalEmission);
        keyRenderer.SetPropertyBlock(propBlock);
    }

    public void OnPokeEnter()
    {
        isPressed = true;

        if (audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        audioSource.time = 0f;
        audioSource.Play();

        // Notifica interacción para silenciar la música ambiental
        if (AmbientAudioManager.Instance != null)
        {
            AmbientAudioManager.Instance.NotificarTeclaTocada();
        }

        // Instancia el Pop-up flotante en 3D
        if (mostrarPopUpNota && prefabPopUpNota != null)
        {
            Vector3 posicionSpawn = transform.position + new Vector3(0, 0.08f, 0);
            GameObject popUpObj = Instantiate(prefabPopUpNota, posicionSpawn, Quaternion.identity);
            EfectoPopUpNota popupScript = popUpObj.GetComponent<EfectoPopUpNota>();
            if (popupScript != null)
            {
                popupScript.Inicializar(nombreNotaCompleto);
            }
        }

        ApplyEmission(Color.black, 0f);

        EvaluarAciertoNota();
    }

    public void OnPokeExit()
    {
        isPressed = false;
    }

    private void EvaluarAciertoNota()
{
    NotaMovimiento notaCercana = null;
    float menorDistanciaY = float.MaxValue;

    // Recorre las notas activas en pantalla
    foreach (var nota in notasEnEscena)
    {
        if (nota != null && nota.targetKey == this)
        {
            float yImpactoIdeal = transform.position.y;
            float diferenciaDistancia = Mathf.Abs(nota.transform.position.y - yImpactoIdeal);

            // Evalúa si la nota está dentro del margen de tolerancia vertical
            if (diferenciaDistancia <= rangoToleranciaHit && diferenciaDistancia < menorDistanciaY)
            {
                menorDistanciaY = diferenciaDistancia;
                notaCercana = nota;
            }
        }
    }

    if (notaCercana != null)
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.RegistrarAcierto();
            Debug.Log($"[PianoKey] ¡Acierto registrado en {nombreNotaCompleto}! Precisión actual: {ScoreManager.Instance.ObtenerPorcentajePrecision():F1}%");
        }

        // Remover de la lista global y destruir la nota acertada
        notasEnEscena.Remove(notaCercana);
        Destroy(notaCercana.gameObject);
    }
    else
    {
        Debug.LogWarning($"[PianoKey] Se presionó {nombreNotaCompleto} pero no había ninguna nota dentro del rango (Tolerancia: {rangoToleranciaHit}m).");
    }
}

    void FixedUpdate()
    {
        Vector3 targetLocal = isPressed ? pressedLocalPosition : restLocalPosition;
        float   speed       = isPressed ? pressSpeed : returnSpeed;
        Vector3 targetWorld = transform.parent != null
            ? transform.parent.TransformPoint(targetLocal)
            : targetLocal;

        rb.MovePosition(Vector3.Lerp(transform.position, targetWorld, Time.fixedDeltaTime * speed));
    }

#if UNITY_EDITOR
    void OnMouseDown() { OnPokeEnter(); }
    void OnMouseUp()   { OnPokeExit(); }
#endif
}