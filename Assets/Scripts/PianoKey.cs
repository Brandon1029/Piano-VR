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

    [Header("Key Glow")]
    [Tooltip("Color del brillo al acercarse la nota")]
    [ColorUsage(true, true)]
    public Color glowColor = new Color(0f, 0.7f, 1f, 1f); // Cian/Azul suave HDR
    [Tooltip("Intensidad máxima de emisión")]
    public float maxGlowIntensity = 2.0f;

    AudioSource audioSource;
    Rigidbody   rb;
    Vector3     restLocalPosition;
    Vector3     pressedLocalPosition;
    bool        isPressed = false;

    // Componentes para el brillo
    Renderer keyRenderer;
    MaterialPropertyBlock propBlock;
    static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

    static Dictionary<string, string> notaES = new Dictionary<string, string>()
    {
        {"C", "Do"}, {"D", "Re"}, {"E", "Mi"},
        {"F", "Fa"}, {"G", "Sol"}, {"A", "La"}, {"B", "Si"}
    };

    void Awake()
    {
        keyRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();
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

    // 1. Extraer la primera letra válida de nota (A, B, C, D, E, F, G)
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

    // 2. Comprobar si es sostenida (#)
    bool sostenido = nombre.Contains("#");

    // 3. Extraer la octava deseada
    int octava = 4;
    for (int i = nombre.Length - 1; i >= 0; i--)
    {
        if (char.IsDigit(nombre[i]))
        {
            octava = int.Parse(nombre[i].ToString());
            break;
        }
    }

    // Factor multiplicador según la distancia con la octava 4 (base)
    // Octava 3 = 0.5f | Octava 4 = 1.0f | Octava 5 = 2.0f
    float factorOctava = Mathf.Pow(2f, octava - 4);
    float factorSostenido = sostenido ? 1.0595f : 1.0f;

    // 4. Intentar cargar el archivo exacto (ej. Sol#3 o Sol3)
    string nombreExacto = notaES[notaEN] + (sostenido ? "#" : "") + octava;
    AudioClip clip = Resources.Load<AudioClip>(nombreExacto);

    if (clip != null)
    {
        audioSource.clip = clip;
        audioSource.pitch = 1f;
        return;
    }

    // 5. Si no existe, intentar cargar la nota base con la misma octava (ej. Sol3)
    string nombreBaseMismaOctava = notaES[notaEN] + octava;
    clip = Resources.Load<AudioClip>(nombreBaseMismaOctava);

    if (clip != null)
    {
        audioSource.clip = clip;
        audioSource.pitch = factorSostenido;
        return;
    }

    // 6. Respaldo universal: usar el sample de la Octava 4 y modular el pitch
    string nombreBaseOctava4 = notaES[notaEN] + "4";
    clip = Resources.Load<AudioClip>(nombreBaseOctava4);

    if (clip != null)
    {
        audioSource.clip = clip;
        audioSource.pitch = factorOctava * factorSostenido;
    }
    else
    {
        Debug.LogWarning($"[PianoKey] No se encontró ningún sample base para: {nombreBaseOctava4}");
    }
}
    // Control de brillo según la cercanía de la nota (0 = apagado, 1 = brillo total)
    public void SetGlowProgress(float progress)
    {
        if (keyRenderer == null) return;

        progress = Mathf.Clamp01(progress);
        Color currentEmission = glowColor * (progress * maxGlowIntensity);

        keyRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(EmissionColorProp, currentEmission);
        keyRenderer.SetPropertyBlock(propBlock);
    }

    public void OnPokeEnter()
    {
        isPressed = true;
        audioSource.Stop();
        audioSource.Play();
        SetGlowProgress(0f); // Apaga el brillo al ser tocada
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