using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class PianoKey : MonoBehaviour
{
    [Header("Física")]
    public float pressDepth  = 0.008f;
    public float pressSpeed  = 20f;
    public float returnSpeed = 8f;

    [Header("Colores de Turno")]
    [Tooltip("Color de la tecla que debe tocarse ya")]
    [ColorUsage(true, true)]
    public Color colorTurnoActual = new Color(1f, 0.92f, 0.016f, 1f); // Amarillo neón

    [Tooltip("Color de las teclas que tienen notas en camino")]
    [ColorUsage(true, true)]
    public Color colorEnEspera = new Color(0f, 0.6f, 1f, 1f);        // Azul cian

    [Tooltip("Intensidad máxima del brillo HDR")]
    public float maxGlowIntensity = 2.5f;

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

    static Dictionary<string, string> notaES = new Dictionary<string, string>()
    {
        {"C", "Do"}, {"D", "Re"}, {"E", "Mi"},
        {"F", "Fa"}, {"G", "Sol"}, {"A", "La"}, {"B", "Si"}
    };

    void Awake()
    {
        keyRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();
        if (!todasLasTeclas.Contains(this))
            todasLasTeclas.Add(this);
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
        ConfigurarNota();
    }

    void ConfigurarNota()
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

        int octava = 4;
        for (int i = nombre.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(nombre[i]))
            {
                octava = int.Parse(nombre[i].ToString());
                break;
            }
        }

        string nombreArchivo = notaES[notaEN] + (sostenido ? "#" : "") + octava;
        AudioClip clip = Resources.Load<AudioClip>(nombreArchivo);

        if (clip != null)
        {
            audioSource.clip  = clip;
            audioSource.pitch = 1f;
            return;
        }

        string nombreBase = notaES[notaEN] + octava;
        AudioClip clipBase = Resources.Load<AudioClip>(nombreBase);

        if (clipBase != null)
        {
            audioSource.clip  = clipBase;
            audioSource.pitch = sostenido ? 1.0595f : 1f;
            return;
        }

        string nombreRespaldoOctava4 = notaES[notaEN] + "4";
        AudioClip clipOctava4 = Resources.Load<AudioClip>(nombreRespaldoOctava4);

        if (clipOctava4 != null)
        {
            audioSource.clip = clipOctava4;
            float multiplicadorOctava = (octava == 3) ? 0.5f : 1f;
            audioSource.pitch = multiplicadorOctava * (sostenido ? 1.0595f : 1f);
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

        // Apagar teclas por defecto
        foreach (var key in todasLasTeclas)
        {
            key.ApplyEmission(Color.black, 0f);
        }

        if (notasEnEscena.Count == 0) return;

        // 1. Para cada tecla, conservar únicamente la nota más baja que vaya hacia ella
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
                // Si ya había una nota hacia esta tecla, nos quedamos con la que esté más abajo
                if (yActual < notaMasBajaPorTecla[nota.targetKey].transform.position.y)
                {
                    notaMasBajaPorTecla[nota.targetKey] = nota;
                }
            }
        }

        // 2. Aplicar color e intensidad gradual a cada tecla activa
        foreach (var par in notaMasBajaPorTecla)
        {
            PianoKey key = par.Key;
            NotaMovimiento nota = par.Value;

            // Tolerancia de 0.08m para acordes que caen a la par
            bool esTurnoActual = Mathf.Abs(nota.transform.position.y - menorYGlobal) <= 0.08f;
            Color baseColor = esTurnoActual ? key.colorTurnoActual : key.colorEnEspera;

            // Calcula el porcentaje gradual de caída (0 = arriba, 1 = abajo)
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
        audioSource.Stop();
        audioSource.Play();
        ApplyEmission(Color.black, 0f);
    }

    public void OnPokeExit()
    {
        isPressed = false;
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