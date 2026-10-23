using UnityEngine;

public class NotaMovimiento : MonoBehaviour
{
    public GameObject explosionPrefab;
    public float velocidadMaxima = 1.5f;

    [HideInInspector] public PianoKey targetKey;
    private float targetY;
    private float spawnY;
    private bool targetSet = false;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (!PianoKey.notasEnEscena.Contains(this))
            PianoKey.notasEnEscena.Add(this);
    }

    public void SetTarget(float y, float startY, PianoKey key)
    {
        targetY = y;
        spawnY = startY;
        targetKey = key;
        targetSet = true;
    }

    public void SetTarget(float y)
    {
        targetY = y;
        spawnY = transform.position.y;
        targetSet = true;
    }

    [Tooltip("Curvatura del brillo: 2 = cuadrática (sube al final), 3 = cúbica (más pronunciada)")]
    public float curvaGlow = 2.5f;

    [Tooltip("Compensación de altura por el tamaño del prefab de la nota")]
    public float offsetImpactoVisual = 0.15f;

    public float ObtenerProgreso()
    {
        if (!targetSet) return 0f;

        // Se ajusta el punto de llegada considerando el grosor de la nota
        float puntoFinalReal = targetY + offsetImpactoVisual;

        // Calcula el porcentaje lineal (0 = arriba, 1 = punto de impacto)
        float progresoLineal = Mathf.InverseLerp(spawnY, puntoFinalReal, transform.position.y);

        // Curva exponencial: retrasa el brillo para que el pico ocurra justo al impactar
        return Mathf.Pow(Mathf.Clamp01(progresoLineal), curvaGlow);
    }

    void FixedUpdate()
    {
        if (rb != null && rb.linearVelocity.y < -velocidadMaxima)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -velocidadMaxima, rb.linearVelocity.z);
    }

    void Update()
    {
        if (!targetSet) return;

        if (transform.position.y <= targetY)
        {
            Explotar();
            return;
        }

        if (transform.position.y < -20f)
        {
            Destroy(gameObject);
        }
    }

    void Explotar()
{
    PianoKey.notasEnEscena.Remove(this);

    // --- MODO AUTO-PLAY FIEL AL SONIDO ORIGINAL ---
    if (AutoPlayManager.Instance != null && AutoPlayManager.Instance.isAutoPlayEnabled)
    {
        if (targetKey != null)
        {
            // Obtiene la fuente de audio original de la tecla
            AudioSource originalSource = targetKey.GetComponent<AudioSource>();
            if (originalSource == null) originalSource = targetKey.GetComponentInChildren<AudioSource>();

            if (originalSource != null && originalSource.clip != null)
            {
                // Crea un objeto temporal para reproducir el audio de forma independiente (evita que se corten los acordes)
                GameObject tempAudioObj = new GameObject("TempAudio_" + targetKey.gameObject.name);
                tempAudioObj.transform.position = transform.position;

                AudioSource tempSource = tempAudioObj.AddComponent<AudioSource>();
                
                // Copia exactamente la configuración de la tecla original para que suene idéntico
                tempSource.clip = originalSource.clip;
                tempSource.outputAudioMixerGroup = originalSource.outputAudioMixerGroup;
                tempSource.spatialBlend = originalSource.spatialBlend; // Mantiene si es 2D o 3D igual al piano
                tempSource.volume = originalSource.volume;
                tempSource.pitch = originalSource.pitch;

                tempSource.Play();

                // Destruye el objeto temporal automáticamente cuando termine el clip de audio
                Destroy(tempAudioObj, originalSource.clip.length);
            }
        }
    }

    if (explosionPrefab != null)
        Instantiate(explosionPrefab, transform.position, Quaternion.identity);

    Destroy(gameObject);
}

    void OnDestroy()
    {
        PianoKey.notasEnEscena.Remove(this);
    }
}