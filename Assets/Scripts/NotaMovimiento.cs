using UnityEngine;
using TMPro;

/// <summary>
/// Controla el desplazamiento visual y temporal de cada barra de nota.
/// Soporta dos modos:
///   1. Pista Frontal (Highway estilo Guitar Hero / PianoVision, activo en ModoLibreMenu):
///      La barra se desliza a velocidad constante (distanciaPista / tiempoAnticipacion) a lo largo
///      de la pista inclinada hacia el borde trasero de su tecla correspondiente, con largo
///      proporcional a la duración de la nota (duracionNota * velocidad) y diferenciación visual
///      entre teclas blancas y negras/sostenidas (#).
///      Mantiene transform.position sincronizado con la ventana de impacto de PianoKey.EvaluarAciertoNota()
///      y PianoKey.ActualizarColoresGlobales() sin modificar PianoKey.cs.
///   2. Caída vertical clásica (fallback utilizado por las notas invisibles de audio en ModoMusica).
/// </summary>
public class NotaMovimiento : MonoBehaviour
{
    public GameObject explosionPrefab;
    public float velocidadMaxima = 1.5f;

    [HideInInspector] public PianoKey targetKey;

    [Header("Curva de Brillo Guía en Tecla")]
    [Tooltip("Curvatura del brillo: 2 = cuadrática, 2.5 = progresiva al acercarse a la línea de impacto.")]
    public float curvaGlow = 2.2f;

    [Tooltip("Compensación de altura en modo vertical clásico.")]
    public float offsetImpactoVisual = 0.15f;

    // --- Estado modo vertical clásico (ModoMusica) ---
    private float targetY;
    private float spawnY;
    private bool targetSet = false;
    private Rigidbody rb;

    // --- Estado modo Pista Frontal (Highway en ModoLibreMenu) ---
    private bool modoPistaFrontal = false;
    private Vector3 puntoImpactoTecla;
    private Vector3 direccionHaciaOrigenPista; // unitario desde la tecla hacia el fondo de la pista (+Y/+Z inclinado)
    private Quaternion rotacionPista;
    private float distanciaTotalPista = 2.2f;
    private float velocidadPista = 0.88f;
    private float tiempoImpactoExacto = 0f;
    private float duracionNotaSeg = 0.45f;
    private float largoBarraTotal = 0.40f;
    private float anchoBarra = 0.018f;
    private float altoBarra = 0.014f;
    private bool impactoIniciado = false;

    private Transform contenedorVisualTransform;
    private Transform barraVisualTransform;
    private Renderer barraRenderer;
    private MaterialPropertyBlock propBlock;
    private TMP_Text etiquetaNotaTexto;

    private static readonly int BaseColorProp     = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProp         = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (!PianoKey.notasEnEscena.Contains(this))
            PianoKey.notasEnEscena.Add(this);
    }

    /// <summary>
    /// Inicializa la nota en modo Pista Frontal (Highway estilo Guitar Hero / PianoVision).
    /// </summary>
    public void ConfigurarEnPistaFrontal(
        PianoKey key,
        Vector3 puntoImpacto,
        Vector3 dirPistaHaciaFondo,
        Quaternion rotPista,
        float distPista,
        float tiempoAnticipacion,
        float timestampImpactoMundial,
        float duracionNota,
        bool esSostenido)
    {
        modoPistaFrontal = true;
        targetKey = key;
        puntoImpactoTecla = puntoImpacto;
        direccionHaciaOrigenPista = dirPistaHaciaFondo.normalized;
        rotacionPista = rotPista;
        distanciaTotalPista = Mathf.Max(0.5f, distPista);

        float anticipacion = Mathf.Max(0.25f, tiempoAnticipacion);
        velocidadPista = distanciaTotalPista / anticipacion;
        tiempoImpactoExacto = timestampImpactoMundial;
        duracionNotaSeg = Mathf.Clamp(duracionNota, 0.18f, 2.5f);
        largoBarraTotal = Mathf.Max(0.06f, duracionNotaSeg * velocidadPista);

        // Diferenciar visualmente teclas blancas vs negras/sostenidas (#)
        anchoBarra = esSostenido ? 0.0115f : 0.0185f;
        altoBarra  = esSostenido ? 0.0175f : 0.0135f;
        if (esSostenido)
        {
            puntoImpactoTecla += Vector3.up * 0.004f;
        }

        // Desactivar gravedad y colisiones físicas en la barra para que se deslice limpiamente por la pista
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        PrepararMallaVisualBarra(esSostenido, key != null ? key.nombreNotaCompleto : "");
        ActualizarPosicionEnPista();

        if (!PianoKey.notasEnEscena.Contains(this))
            PianoKey.notasEnEscena.Add(this);
    }

    private void PrepararMallaVisualBarra(bool esSostenido, string nombreNota)
    {
        propBlock = new MaterialPropertyBlock();

        MeshFilter rootFilter = GetComponent<MeshFilter>();
        MeshRenderer rootRend = GetComponent<MeshRenderer>();

        if (contenedorVisualTransform == null)
        {
            GameObject contGO = new GameObject("ContenedorVisualPista");
            contGO.layer = gameObject.layer;
            contenedorVisualTransform = contGO.transform;
            contenedorVisualTransform.SetParent(transform, false);
        }

        if (barraVisualTransform == null)
        {
            GameObject visualGO = new GameObject("BarraNotaVisual");
            visualGO.layer = gameObject.layer;
            barraVisualTransform = visualGO.transform;
            barraVisualTransform.SetParent(contenedorVisualTransform, false);

            if (rootFilter != null && rootRend != null)
            {
                MeshFilter mf = visualGO.AddComponent<MeshFilter>();
                mf.sharedMesh = rootFilter.sharedMesh;

                barraRenderer = visualGO.AddComponent<MeshRenderer>();
                barraRenderer.sharedMaterials = rootRend.sharedMaterials;
                barraRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                barraRenderer.receiveShadows = false;

                rootRend.enabled = false;
            }
        }

        // Color diferenciador: Cian brillante para teclas blancas, Ámbar/Dorado intenso para sostenidos (#)
        if (barraRenderer != null && barraRenderer.enabled)
        {
            Color colorBase = esSostenido
                ? new Color(1.00f, 0.48f, 0.14f, 0.96f)  // Ámbar cálido para negras/sostenidas
                : new Color(0.18f, 0.84f, 1.00f, 0.95f); // Cian cristal para blancas

            Color colorEmision = esSostenido
                ? new Color(1.00f, 0.42f, 0.08f, 1f) * 1.4f
                : new Color(0.12f, 0.75f, 1.00f, 1f) * 1.4f;

            barraRenderer.GetPropertyBlock(propBlock);
            propBlock.SetColor(BaseColorProp, colorBase);
            propBlock.SetColor(ColorProp, colorBase);
            propBlock.SetColor(EmissionColorProp, colorEmision);
            barraRenderer.SetPropertyBlock(propBlock);
        }

        // Conservar o añadir etiqueta con el nombre de la nota en el frente de la barra
        etiquetaNotaTexto = GetComponentInChildren<TMP_Text>();
        if (etiquetaNotaTexto == null && barraRenderer != null && barraRenderer.enabled && !string.IsNullOrEmpty(nombreNota))
        {
            GameObject lblGO = new GameObject("EtiquetaNota");
            lblGO.transform.SetParent(contenedorVisualTransform, false);
            lblGO.transform.localPosition = new Vector3(0f, altoBarra + 0.008f, 0.005f);
            lblGO.transform.localRotation = Quaternion.identity;
            lblGO.transform.localScale = Vector3.one * 0.012f;

            TextMeshPro tmp = lblGO.AddComponent<TextMeshPro>();
            tmp.text = nombreNota;
            tmp.fontSize = 22f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            etiquetaNotaTexto = tmp;
        }
        else if (etiquetaNotaTexto != null)
        {
            if (etiquetaNotaTexto.transform.parent != contenedorVisualTransform)
            {
                etiquetaNotaTexto.transform.SetParent(contenedorVisualTransform, false);
                etiquetaNotaTexto.transform.localPosition = new Vector3(0f, altoBarra + 0.008f, 0.005f);
                etiquetaNotaTexto.transform.localRotation = Quaternion.identity;
            }
            if (!string.IsNullOrEmpty(nombreNota))
            {
                etiquetaNotaTexto.text = nombreNota;
            }
        }
    }

    // --- Métodos del modo vertical clásico (usado en ModoMusica) ---
    public void SetTarget(float y, float startY, PianoKey key)
    {
        modoPistaFrontal = false;
        targetY = y;
        spawnY = startY;
        targetKey = key;
        targetSet = true;
    }

    public void SetTarget(float y)
    {
        modoPistaFrontal = false;
        targetY = y;
        spawnY = transform.position.y;
        targetSet = true;
    }

    public float ObtenerProgreso()
    {
        if (modoPistaFrontal)
        {
            float distFrente = (tiempoImpactoExacto - Time.time) * velocidadPista;
            float progLineal = 1f - Mathf.Clamp01(distFrente / Mathf.Max(0.1f, distanciaTotalPista));
            return Mathf.Pow(progLineal, curvaGlow);
        }

        if (!targetSet) return 0f;

        float puntoFinalReal = targetY + offsetImpactoVisual;
        float progresoLineal = Mathf.InverseLerp(spawnY, puntoFinalReal, transform.position.y);
        return Mathf.Pow(Mathf.Clamp01(progresoLineal), curvaGlow);
    }

    void FixedUpdate()
    {
        if (modoPistaFrontal) return;

        if (rb != null && !rb.isKinematic && rb.linearVelocity.y < -velocidadMaxima)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -velocidadMaxima, rb.linearVelocity.z);
    }

    void Update()
    {
        if (modoPistaFrontal)
        {
            ActualizarPosicionEnPista();
            return;
        }

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

    private void ActualizarPosicionEnPista()
    {
        float ahora = Time.time;

        // Distancia del borde frontal (inicio de nota) y borde trasero (fin de duración) a la línea de impacto
        float distFrente = (tiempoImpactoExacto - ahora) * velocidadPista;
        float distCola   = distFrente + largoBarraTotal;

        // En cuanto el frente llega a la línea de impacto (distFrente <= 0), disparar feedback/audio
        if (!impactoIniciado && distFrente <= 0f)
        {
            impactoIniciado = true;
            AlLlegarALineaDeImpacto();
        }

        // Si toda la duración de la barra ya terminó de cruzar la línea de impacto, destruir limpia
        if (distCola <= 0f)
        {
            FinalizarBarraEnPista();
            return;
        }

        // Recortar visualmente la barra contra la línea de impacto (0 m) y el horizonte de la pista (distanciaTotalPista)
        float frenteVisible = Mathf.Max(0f, distFrente);
        float colaVisible   = Mathf.Clamp(distCola, 0f, distanciaTotalPista);
        float largoVisible  = Mathf.Max(0.005f, colaVisible - frenteVisible);

        // Posición visual real del frente de la barra sobre la pista inclinada
        Vector3 posVisualFrente = puntoImpactoTecla + direccionHaciaOrigenPista * frenteVisible;

        // transform.position.y se mapea proporcionalmente a frenteVisible (0.45 m verticales por metro en pista)
        // para que PianoKey.EvaluarAciertoNota() (rangoToleranciaHit = 0.08m) evalúe una ventana precisa de ~17.5 cm
        // antes de la línea de impacto y 0.0m durante toda la duración de cruce de la barra.
        float yTecla = targetKey != null ? targetKey.transform.position.y : puntoImpactoTecla.y;
        float yEvaluacionImpacto = yTecla + frenteVisible * 0.45f;

        transform.position = new Vector3(posVisualFrente.x, yEvaluacionImpacto, posVisualFrente.z);
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        if (contenedorVisualTransform != null)
        {
            contenedorVisualTransform.position = posVisualFrente;
            contenedorVisualTransform.rotation = rotacionPista;
        }

        if (barraVisualTransform != null)
        {
            // El centro de la barra visual está desplazado la mitad del largoVisible hacia atrás en la pista (+Z local)
            barraVisualTransform.localRotation = Quaternion.identity;
            barraVisualTransform.localPosition = new Vector3(0f, altoBarra * 0.5f, largoVisible * 0.5f);
            barraVisualTransform.localScale    = new Vector3(anchoBarra, altoBarra, largoVisible);
        }
    }

    private void AlLlegarALineaDeImpacto()
    {
        // Iluminar brevemente la tecla guía correspondiente al llegar al borde
        if (targetKey != null && !targetKey.isPressed)
        {
            targetKey.ApplyEmission(targetKey.colorTurnoActual, 1f);
        }

        // Si el modo Autoplayer está activo, hacer sonar la tecla
        EjecutarAutoPlaySiActivo();

        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, puntoImpactoTecla, Quaternion.identity);
        }
    }

    private void FinalizarBarraEnPista()
    {
        PianoKey.notasEnEscena.Remove(this);
        Destroy(gameObject);
    }

    void Explotar()
    {
        PianoKey.notasEnEscena.Remove(this);
        EjecutarAutoPlaySiActivo();

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }

    private void EjecutarAutoPlaySiActivo()
    {
        if (AutoPlayManager.Instance != null &&
            AutoPlayManager.Instance.isActiveAndEnabled &&
            AutoPlayManager.Instance.isAutoPlayEnabled &&
            targetKey != null)
        {
            AudioSource originalSource = targetKey.GetComponent<AudioSource>();
            if (originalSource == null) originalSource = targetKey.GetComponentInChildren<AudioSource>();

            if (originalSource != null && originalSource.clip != null)
            {
                GameObject tempAudioObj = new GameObject("TempAudio_" + targetKey.gameObject.name);
                tempAudioObj.transform.position = transform.position;

                AudioSource tempSource = tempAudioObj.AddComponent<AudioSource>();
                tempSource.clip = originalSource.clip;
                tempSource.outputAudioMixerGroup = originalSource.outputAudioMixerGroup;
                tempSource.spatialBlend = originalSource.spatialBlend;
                tempSource.volume = originalSource.volume;
                tempSource.pitch = originalSource.pitch;
                tempSource.Play();

                Destroy(tempAudioObj, originalSource.clip.length);
            }
        }
    }

    void OnDestroy()
    {
        PianoKey.notasEnEscena.Remove(this);
    }
}
