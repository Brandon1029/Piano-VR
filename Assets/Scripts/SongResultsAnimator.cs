using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controla el panel animado de "Resultados de la Canción" y la sincronización visual de
/// récords y estrellas (0 a 4 ★) en el menú de 'ModoLibreMenu'.
///
/// Características:
///   • Permanece oculto durante la partida y aparece automáticamente al finalizar una canción.
///   • Estética Dark Smoked Glass (#0A0A0CD9 / #141418CC / #FFFFFF / #A1A1AA).
///   • Animaciones por corrutina con Time.unscaledDeltaTime (sin paquetes externos):
///       1. Fade-in con CanvasGroup + escalado elástico de entrada.
///       2. Contador numérico progresivo de 0.0% al porcentaje real alcanzado.
///       3. Aparición secuencial de cada estrella ganada (intervalo ~0.25s y golpe de escala 1.8x -> 1.0x).
///   • Botones interactivos "Reintentar" y "Volver al Menú" compatibles con raycast de manos + pellizco.
///   • Sincroniza estrellas y mejor récord en la ficha de detalles y en cada tarjeta 'SongItemPrefab'
///     de la lista, ejecutando un pulso animado al romper récord.
/// </summary>
public class SongResultsAnimator : MonoBehaviour
{
    public static SongResultsAnimator Instance { get; private set; }

    [Header("Referencias Opcionales (se auto-detectan en ModoLibreMenu)")]
    public GameObject panelResultados;
    public CanvasGroup panelCanvasGroup;
    public RectTransform tarjetaResultados;
    public TMP_Text txtTituloCancion;
    public TMP_Text txtBadgeNuevoRecord;
    public TMP_Text txtPorcentajeAnimado;
    public TMP_Text txtDetalleNotas;
    public Image[] estrellasResultado = new Image[4];
    public Button btnReintentar;
    public Button btnVolverMenu;

    [Header("Parámetros de Animación")]
    [Tooltip("Duración del fade-in y escalado elástico al abrir el panel (segundos).")]
    public float duracionEntradaPanel = 0.32f;

    [Tooltip("Duración del conteo progresivo de 0.0% al porcentaje final (segundos).")]
    public float duracionConteoPorcentaje = 0.95f;

    [Tooltip("Intervalo en segundos entre la aparición de cada estrella ganada (~0.25s).")]
    public float intervaloEntreEstrellas = 0.25f;

    [Tooltip("Duración del golpe/rebote de escala (1.8x -> 1.0x) de cada estrella.")]
    public float duracionGolpeEstrella = 0.28f;

    [Header("Colores de Estrellas")]
    public Color colorEstrellaGanada   = new Color(1f, 0.88f, 0.28f, 1f);
    public Color colorEstrellaNoGanada = new Color(0.32f, 0.32f, 0.36f, 0.42f);

    private SongSelectionMenu songMenu;
    private NoteSpawner noteSpawner;
    private ScoreManager scoreManager;
    private PinchMenuToggle pinchMenuToggle;
    private Canvas songCanvas;
    private Transform songListContainer;
    private TMP_Text txtRecordMenu;
    private Image[] estrellasMenu;
    private Sprite spriteEstrellaLlena;
    private Sprite spriteEstrellaVacia;
    private Sprite spriteRedondeadoUI;

    private SongData ultimaCancionJugada;
    private Coroutine rutinaAnimacionResultados;
    private Coroutine rutinaPulsoRecord;
    private int ultimoConteoHijosLista = -1;
    private bool pendientePulsoNuevoRecord = false;
    private string cancionConNuevoRecord = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInicializarEnModoLibreMenu()
    {
        VerificarEscena(SceneManager.GetActiveScene());
    }

    private static void VerificarEscena(Scene scene)
    {
        if (scene.name != "ModoLibreMenu") return;

        if (Instance == null)
        {
            var existente = FindFirstObjectByType<SongResultsAnimator>();
            if (existente != null)
            {
                Instance = existente;
            }
            else
            {
                GameObject go = new GameObject("[SongResultsAnimator]");
                Instance = go.AddComponent<SongResultsAnimator>();
            }
        }
    }

    private void Awake()
    {
        if (SceneManager.GetActiveScene().name != "ModoLibreMenu")
        {
            enabled = false;
            return;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name != "ModoLibreMenu") return;
        StartCoroutine(InicializarConMenu());
    }

    private IEnumerator InicializarConMenu()
    {
        // Esperar un frame para que SongSelectionMenu termine su Awake/Start
        yield return null;

        VincularReferenciasDeEscena();
        AsegurarPanelResultadosEnCanvas();
        OcultarPanelInmediato();
        SincronizarTarjetasDeLista();

        if (XRUIInteractionEnabler.Instance != null)
        {
            XRUIInteractionEnabler.Instance.ConfigurarEscenaActual();
        }
    }

    private void VincularReferenciasDeEscena()
    {
        songMenu        = SongSelectionMenu.Instance != null ? SongSelectionMenu.Instance : FindFirstObjectByType<SongSelectionMenu>();
        noteSpawner     = FindFirstObjectByType<NoteSpawner>();
        scoreManager    = ScoreManager.Instance != null ? ScoreManager.Instance : FindFirstObjectByType<ScoreManager>();
        pinchMenuToggle = FindFirstObjectByType<PinchMenuToggle>();

        if (songMenu != null)
        {
            songCanvas = songMenu.GetComponentInParent<Canvas>();

            // Localizar elementos reales dentro del SongSelectionCanvas de ModoLibreMenu
            Transform bg = songMenu.transform.Find("Background");
            if (bg != null)
            {
                Transform contentArea = bg.Find("SongListPanel/ContentArea");
                if (contentArea != null)
                {
                    songListContainer = contentArea.Find("SongScrollView/Viewport/Content");
                    Transform details = contentArea.Find("SongDetailsPanel");
                    if (details != null)
                    {
                        Transform recTf = details.Find("RecordText");
                        if (recTf != null) txtRecordMenu = recTf.GetComponent<TMP_Text>();

                        Transform starsTf = details.Find("StarsContainer");
                        if (starsTf != null)
                        {
                            List<Image> imgs = new List<Image>();
                            for (int i = 0; i < starsTf.childCount; i++)
                            {
                                Image img = starsTf.GetChild(i).GetComponent<Image>();
                                if (img != null) imgs.Add(img);
                            }
                            estrellasMenu = imgs.ToArray();
                        }

                        // Interceptar clic en Btn_Play para ocultar el panel de resultados si se inicia otra canción
                        Transform playTf = details.Find("Btn_Play");
                        if (playTf != null && playTf.TryGetComponent<Button>(out Button btnPlay))
                        {
                            btnPlay.onClick.AddListener(OnCancionIniciadaDesdeMenu);
                        }
                    }
                }
            }
        }

        // Capturar sprites de estrella (llena y vacía) y UISprite redondeado desde los elementos existentes
        if (estrellasMenu != null && estrellasMenu.Length > 0 && estrellasMenu[0] != null)
        {
            if (spriteEstrellaVacia == null) spriteEstrellaVacia = estrellasMenu[0].sprite;
        }

        Image[] todasImgs = songMenu != null ? songMenu.GetComponentsInChildren<Image>(true) : FindObjectsByType<Image>(FindObjectsSortMode.None);
        foreach (Image im in todasImgs)
        {
            if (im != null && im.sprite != null && im.type == Image.Type.Sliced && spriteRedondeadoUI == null)
            {
                spriteRedondeadoUI = im.sprite;
                break;
            }
        }
    }

    private void OnCancionIniciadaDesdeMenu()
    {
        if (noteSpawner != null && noteSpawner.song != null)
        {
            ultimaCancionJugada = noteSpawner.song;
        }
        if (panelResultados != null && panelResultados.activeSelf)
        {
            OcultarPanelInmediato();
        }
    }

    private void Update()
    {
        if (SceneManager.GetActiveScene().name != "ModoLibreMenu") return;

        // Registrar automáticamente cuál es la canción activa en NoteSpawner mientras suena
        if (noteSpawner != null && noteSpawner.song != null)
        {
            ultimaCancionJugada = noteSpawner.song;
        }

        // Detectar si SongSelectionMenu regeneró los ítems de la lista (al cambiar dificultad)
        if (songListContainer != null && songListContainer.childCount != ultimoConteoHijosLista)
        {
            ultimoConteoHijosLista = songListContainer.childCount;
            SincronizarTarjetasDeLista();
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        // Atajo de prueba rápida en Play Mode (Tecla F8): simula el final de la canción activa
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            SimularFinalDeCancionParaPrueba();
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.F8))
        {
            SimularFinalDeCancionParaPrueba();
        }
#endif
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construcción del Panel "Resultados de la Canción" (Dark Smoked Glass)
    // ─────────────────────────────────────────────────────────────────────────

    private void AsegurarPanelResultadosEnCanvas()
    {
        if (songMenu == null) return;

        Transform parentPanel = songMenu.transform.Find("Background");
        if (parentPanel == null) parentPanel = songMenu.transform;

        // Si ya existe en la escena, vincular sus referencias
        if (panelResultados == null)
        {
            Transform existente = parentPanel.Find("Panel_ResultadosCancion");
            if (existente != null)
            {
                panelResultados = existente.gameObject;
            }
        }

        TMP_FontAsset fuente = ObtenerFuenteTMP();

        if (panelResultados == null)
        {
            panelResultados = new GameObject("Panel_ResultadosCancion");
            panelResultados.transform.SetParent(parentPanel, false);
        }

        RectTransform rootRt = panelResultados.GetComponent<RectTransform>();
        if (rootRt == null) rootRt = panelResultados.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.anchoredPosition = Vector2.zero;
        rootRt.sizeDelta = Vector2.zero;

        // Fondo oscurecedor translúcido sobre el menú
        Image overlayImg = panelResultados.GetComponent<Image>();
        if (overlayImg == null) overlayImg = panelResultados.AddComponent<Image>();
        overlayImg.color = new Color(0.02f, 0.02f, 0.03f, 0.82f);
        if (spriteRedondeadoUI != null)
        {
            overlayImg.sprite = spriteRedondeadoUI;
            overlayImg.type = Image.Type.Sliced;
        }

        panelCanvasGroup = panelResultados.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null) panelCanvasGroup = panelResultados.AddComponent<CanvasGroup>();

        // Tarjeta central Dark Smoked Glass con borde sutil (#FFFFFF26)
        Transform borderTf = panelResultados.transform.Find("CardBorder");
        GameObject borderGO = borderTf != null ? borderTf.gameObject : CrearHijoUI("CardBorder", rootRt, new Vector2(720f, 470f), Vector2.zero);
        Image borderImg = borderGO.GetComponent<Image>() ?? borderGO.AddComponent<Image>();
        borderImg.color = new Color(1f, 1f, 1f, 0.15f); // #FFFFFF26
        if (spriteRedondeadoUI != null) { borderImg.sprite = spriteRedondeadoUI; borderImg.type = Image.Type.Sliced; }
        tarjetaResultados = borderGO.GetComponent<RectTransform>();

        Transform cardTf = borderGO.transform.Find("CardBody");
        GameObject cardGO = cardTf != null ? cardTf.gameObject : CrearHijoUI("CardBody", tarjetaResultados, new Vector2(714f, 464f), Vector2.zero);
        Image cardImg = cardGO.GetComponent<Image>() ?? cardGO.AddComponent<Image>();
        cardImg.color = new Color(0.0392f, 0.0392f, 0.0471f, 0.96f); // #0A0A0CF5 Dark Smoked Glass
        if (spriteRedondeadoUI != null) { cardImg.sprite = spriteRedondeadoUI; cardImg.type = Image.Type.Sliced; }
        RectTransform cardRt = cardGO.GetComponent<RectTransform>();

        // Encabezado
        CrearOActualizarTexto(cardRt, "TxtHeader", fuente, "RESULTADOS DE LA CANCIÓN",
            18f, FontStyles.Bold, new Color(0.631f, 0.631f, 0.667f, 1f),
            TextAlignmentOptions.Center, new Vector2(640f, 28f), new Vector2(0f, 192f));

        txtTituloCancion = CrearOActualizarTexto(cardRt, "TxtTituloCancion", fuente, "Pieza Musical",
            32f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, new Vector2(650f, 44f), new Vector2(0f, 152f));

        txtBadgeNuevoRecord = CrearOActualizarTexto(cardRt, "TxtBadgeNuevoRecord", fuente, "★ ¡NUEVO RÉCORD PERSONAL! ★",
            19f, FontStyles.Bold, new Color(1f, 0.88f, 0.28f, 1f),
            TextAlignmentOptions.Center, new Vector2(520f, 30f), new Vector2(0f, 112f));

        // Contador grande de porcentaje
        txtPorcentajeAnimado = CrearOActualizarTexto(cardRt, "TxtPorcentaje", fuente, "0.0%",
            58f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, new Vector2(420f, 74f), new Vector2(0f, 52f));

        txtDetalleNotas = CrearOActualizarTexto(cardRt, "TxtDetalleNotas", fuente, "Notas acertadas: 0 / 0",
            19f, FontStyles.Normal, new Color(0.631f, 0.631f, 0.667f, 1f),
            TextAlignmentOptions.Center, new Vector2(620f, 32f), new Vector2(0f, -2f));

        // Contenedor de las 4 Estrellas
        Transform rowStarsTf = cardRt.Find("RowEstrellas");
        GameObject rowStarsGO = rowStarsTf != null ? rowStarsTf.gameObject : CrearHijoUI("RowEstrellas", cardRt, new Vector2(420f, 76f), new Vector2(0f, -68f));
        RectTransform rowStarsRt = rowStarsGO.GetComponent<RectTransform>();

        estrellasResultado = new Image[4];
        float startX = -126f;
        float stepX  = 84f;
        for (int i = 0; i < 4; i++)
        {
            string sName = $"Star_Result_{i + 1}";
            Transform stTf = rowStarsRt.Find(sName);
            GameObject stGO = stTf != null ? stTf.gameObject : CrearHijoUI(sName, rowStarsRt, new Vector2(66f, 66f), new Vector2(startX + i * stepX, 0f));
            Image stImg = stGO.GetComponent<Image>() ?? stGO.AddComponent<Image>();
            if (spriteEstrellaVacia != null) stImg.sprite = spriteEstrellaVacia;
            stImg.preserveAspect = true;
            stImg.color = colorEstrellaNoGanada;
            estrellasResultado[i] = stImg;
        }

        // Botones interactivos Dark Smoked Glass: "Reintentar" y "Volver al Menú"
        btnReintentar = CrearOActualizarBotonSmokedGlass(cardRt, "Btn_Reintentar", fuente,
            "↻  Reintentar", new Vector2(275f, 58f), new Vector2(-152f, -168f));
        btnReintentar.onClick.RemoveAllListeners();
        btnReintentar.onClick.AddListener(ReintentarCancion);

        btnVolverMenu = CrearOActualizarBotonSmokedGlass(cardRt, "Btn_VolverMenu", fuente,
            "←  Volver al Menú", new Vector2(275f, 58f), new Vector2(152f, -168f));
        btnVolverMenu.onClick.RemoveAllListeners();
        btnVolverMenu.onClick.AddListener(VolverAlMenu);

        if (XRUIInteractionEnabler.Instance != null)
        {
            XRUIInteractionEnabler.Instance.MejorarControlesEnJerarquia(panelResultados.transform);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Disparo y Corrutinas de Animación de Resultados (Time.unscaledDeltaTime)
    // ─────────────────────────────────────────────────────────────────────────

    public void MostrarResultadosFinales()
    {
        if (SceneManager.GetActiveScene().name != "ModoLibreMenu") return;

        if (scoreManager == null) scoreManager = ScoreManager.Instance;
        if (panelResultados == null) AsegurarPanelResultadosEnCanvas();
        if (panelResultados == null) return;

        // Asegurar que el Canvas principal del menú esté visible frente al jugador
        if (pinchMenuToggle != null)
        {
            pinchMenuToggle.MostrarMenu(true, "Fin de Canción - Resultados");
        }
        else if (songMenu != null && !songMenu.gameObject.activeSelf)
        {
            songMenu.gameObject.SetActive(true);
        }

        string nombreCancion = (scoreManager != null && !string.IsNullOrEmpty(scoreManager.IdCancionActual))
            ? scoreManager.IdCancionActual
            : (ultimaCancionJugada != null ? ultimaCancionJugada.songName : "Canción Completada");

        int acertadas = scoreManager != null ? scoreManager.NotasAcertadas : 0;
        int totales   = scoreManager != null ? scoreManager.TotalNotasCancion : 1;
        float precision = scoreManager != null ? scoreManager.ObtenerPorcentajePrecision() : 0f;
        int estrellas   = scoreManager != null ? scoreManager.CalcularEstrellas(precision) : 0;
        bool nuevoRec   = scoreManager != null && scoreManager.UltimoFueNuevoRecord;
        float mejorHist = ScoreManager.GetBestAccuracy(nombreCancion);

        pendientePulsoNuevoRecord = nuevoRec || estrellas > 0;
        cancionConNuevoRecord = nombreCancion;

        // Actualizar también en segundo plano las tarjetas de la lista y la ficha del menú
        if (songMenu != null)
        {
            songMenu.ActualizarRécordUI();
        }
        SincronizarTarjetasDeLista();

        if (rutinaAnimacionResultados != null)
        {
            StopCoroutine(rutinaAnimacionResultados);
        }
        rutinaAnimacionResultados = StartCoroutine(RutinaAnimarResultados(nombreCancion, acertadas, totales, precision, estrellas, mejorHist, nuevoRec));
    }

    private IEnumerator RutinaAnimarResultados(string nombreCancion, int acertadas, int totales, float precisionFinal, int estrellasGanadas, float mejorHistorico, bool esNuevoRecord)
    {
        panelResultados.SetActive(true);
        panelResultados.transform.SetAsLastSibling();

        txtTituloCancion.text = nombreCancion;
        txtBadgeNuevoRecord.text = esNuevoRecord
            ? "★  ¡NUEVO RÉCORD PERSONAL!  ★"
            : (estrellasGanadas >= 3 ? "¡EXCELENTE INTERPRETACIÓN!" : "CANCIÓN FINALIZADA");
        txtBadgeNuevoRecord.color = esNuevoRecord
            ? new Color(1f, 0.88f, 0.28f, 1f)
            : new Color(0.22f, 0.74f, 0.97f, 1f);

        txtPorcentajeAnimado.text = "0.0%";
        txtDetalleNotas.text = $"Notas acertadas: {acertadas} / {totales}   •   Mejor Récord: {Mathf.Max(precisionFinal, mejorHistorico):F1}%";

        // Inicializar las 4 estrellas en estado gris tenue (no obtenidas)
        for (int i = 0; i < estrellasResultado.Length; i++)
        {
            if (estrellasResultado[i] == null) continue;
            if (spriteEstrellaVacia != null) estrellasResultado[i].sprite = spriteEstrellaVacia;
            estrellasResultado[i].color = colorEstrellaNoGanada;
            estrellasResultado[i].transform.localScale = Vector3.one;
        }

        // 1. Entrada suave del panel (Fade-in con CanvasGroup + ligero escalado elástico)
        panelCanvasGroup.alpha = 0f;
        panelCanvasGroup.interactable = true;
        panelCanvasGroup.blocksRaycasts = true;
        tarjetaResultados.localScale = Vector3.one * 0.78f;

        float t = 0f;
        float durIn = Mathf.Max(0.1f, duracionEntradaPanel);
        while (t < durIn)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / durIn);
            panelCanvasGroup.alpha = u;

            // Curva elástica suave: 0.78 -> 1.04 -> 1.00
            float s = u < 0.75f
                ? Mathf.Lerp(0.78f, 1.045f, u / 0.75f)
                : Mathf.Lerp(1.045f, 1.0f, (u - 0.75f) / 0.25f);
            tarjetaResultados.localScale = Vector3.one * s;
            yield return null;
        }
        panelCanvasGroup.alpha = 1f;
        tarjetaResultados.localScale = Vector3.one;

        // 2. Contador numérico animado desde 0.0% hasta el porcentaje real alcanzado
        t = 0f;
        float durCount = Mathf.Max(0.2f, duracionConteoPorcentaje);
        while (t < durCount)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / durCount);
            float easeOut = 1f - Mathf.Pow(1f - u, 3f);
            float valorActual = Mathf.Lerp(0f, precisionFinal, easeOut);
            txtPorcentajeAnimado.text = $"{valorActual:F1}%";
            yield return null;
        }
        txtPorcentajeAnimado.text = $"{precisionFinal:F1}%";

        // 3. Aparición secuencial de cada estrella ganada (~0.25s de intervalo + rebote 1.8x -> 1.0x)
        int maxEstrellas = Mathf.Clamp(estrellasGanadas, 0, estrellasResultado.Length);
        for (int i = 0; i < maxEstrellas; i++)
        {
            Image starImg = estrellasResultado[i];
            if (starImg != null)
            {
                StartCoroutine(RutinaGolpeEstrella(starImg));
            }
            yield return new WaitForSecondsRealtime(intervaloEntreEstrellas);
        }

        rutinaAnimacionResultados = null;
    }

    private IEnumerator RutinaGolpeEstrella(Image starImg)
    {
        if (starImg == null) yield break;

        if (spriteEstrellaLlena != null)
        {
            starImg.sprite = spriteEstrellaLlena;
        }
        starImg.color = colorEstrellaGanada;

        Transform tf = starImg.transform;
        float t = 0f;
        float dur = Mathf.Max(0.1f, duracionGolpeEstrella);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            // Golpe de escala 1.8x -> 0.93x -> 1.0x
            float escala = u < 0.7f
                ? Mathf.Lerp(1.8f, 0.93f, u / 0.7f)
                : Mathf.Lerp(0.93f, 1.0f, (u - 0.7f) / 0.3f);
            tf.localScale = Vector3.one * escala;
            yield return null;
        }

        tf.localScale = Vector3.one;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Acciones de Botones: Reintentar y Volver al Menú + Pulso de Récord
    // ─────────────────────────────────────────────────────────────────────────

    public void ReintentarCancion()
    {
        OcultarPanelInmediato();

        SongData cancion = ultimaCancionJugada != null
            ? ultimaCancionJugada
            : (noteSpawner != null ? noteSpawner.song : null);

        if (cancion != null && noteSpawner != null)
        {
            Debug.Log($"[SongResultsAnimator] Reintentando canción '{cancion.songName}'...");
            noteSpawner.StartSong(cancion);
        }
    }

    public void VolverAlMenu()
    {
        if (rutinaAnimacionResultados != null)
        {
            StopCoroutine(rutinaAnimacionResultados);
            rutinaAnimacionResultados = null;
        }

        OcultarPanelInmediato();

        if (songMenu != null)
        {
            songMenu.ActualizarRécordUI();
        }
        SincronizarTarjetasDeLista();

        if (pendientePulsoNuevoRecord)
        {
            pendientePulsoNuevoRecord = false;
            if (rutinaPulsoRecord != null) StopCoroutine(rutinaPulsoRecord);
            rutinaPulsoRecord = StartCoroutine(RutinaPulsoActualizacionMenu(cancionConNuevoRecord));
        }
    }

    private void OcultarPanelInmediato()
    {
        if (panelResultados != null)
        {
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
            }
            panelResultados.SetActive(false);
        }
    }

    /// <summary>
    /// Sincroniza en cada tarjeta 'SongItemPrefab' de la lista de canciones las estrellas (0 a 4 ★)
    /// y el mejor porcentaje guardados en PlayerPrefs.
    /// </summary>
    public void SincronizarTarjetasDeLista()
    {
        if (songListContainer == null) return;

        TMP_FontAsset fuente = ObtenerFuenteTMP();

        for (int i = 0; i < songListContainer.childCount; i++)
        {
            Transform itemTf = songListContainer.GetChild(i);
            if (itemTf == null) continue;

            // El primer TMP_Text del SongItemPrefab contiene el nombre de la canción
            TMP_Text txtNombre = itemTf.GetComponentInChildren<TMP_Text>();
            if (txtNombre == null) continue;

            string nombreCancion = txtNombre.text.Trim();
            if (string.IsNullOrEmpty(nombreCancion)) continue;

            // Ajustar margen derecho del título para dejar espacio limpio a las estrellas de la tarjeta
            txtNombre.margin = new Vector4(18f, 0f, 125f, 0f);

            float mejorAcc = ScoreManager.GetBestAccuracy(nombreCancion);
            int mejoresStars = ScoreManager.GetBestStars(nombreCancion);

            Transform badgeTf = itemTf.Find("BadgeEstrellasRecord");
            TMP_Text txtBadge;
            if (badgeTf == null)
            {
                RectTransform itemRt = itemTf.GetComponent<RectTransform>();
                GameObject badgeGO = CrearHijoUI("BadgeEstrellasRecord", itemRt, new Vector2(120f, 44f), new Vector2(-12f, 0f));
                RectTransform bRt = badgeGO.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(1f, 0.5f);
                bRt.anchorMax = new Vector2(1f, 0.5f);
                bRt.pivot     = new Vector2(1f, 0.5f);
                bRt.anchoredPosition = new Vector2(-14f, 0f);

                txtBadge = badgeGO.AddComponent<TextMeshProUGUI>();
                if (fuente != null) txtBadge.font = fuente;
                txtBadge.fontSize = 16.5f;
                txtBadge.fontStyle = FontStyles.Bold;
                txtBadge.alignment = TextAlignmentOptions.MidlineRight;
                txtBadge.raycastTarget = false;
            }
            else
            {
                txtBadge = badgeTf.GetComponent<TMP_Text>();
            }

            if (txtBadge != null)
            {
                if (mejorAcc > 0f || mejoresStars > 0)
                {
                    string llenas = new string('★', Mathf.Clamp(mejoresStars, 0, 4));
                    string vacias = new string('☆', Mathf.Clamp(4 - mejoresStars, 0, 4));
                    txtBadge.text = $"<color=#FFE047>{llenas}</color><color=#686873>{vacias}</color> <color=#A1A1AA>{mejorAcc:F0}%</color>";
                }
                else
                {
                    txtBadge.text = "<color=#52525B>☆☆☆☆</color>";
                }
            }
        }
    }

    private IEnumerator RutinaPulsoActualizacionMenu(string nombreCancion)
    {
        List<Transform> objetivosPulso = new List<Transform>();

        if (txtRecordMenu != null)
            objetivosPulso.Add(txtRecordMenu.transform);

        if (estrellasMenu != null)
        {
            for (int i = 0; i < estrellasMenu.Length; i++)
            {
                if (estrellasMenu[i] != null) objetivosPulso.Add(estrellasMenu[i].transform);
            }
        }

        if (songListContainer != null && !string.IsNullOrEmpty(nombreCancion))
        {
            for (int i = 0; i < songListContainer.childCount; i++)
            {
                Transform item = songListContainer.GetChild(i);
                TMP_Text t = item != null ? item.GetComponentInChildren<TMP_Text>() : null;
                if (t != null && t.text.Trim() == nombreCancion)
                {
                    Transform badge = item.Find("BadgeEstrellasRecord");
                    if (badge != null) objetivosPulso.Add(badge);
                }
            }
        }

        float tElapsed = 0f;
        float dur = 0.42f;
        while (tElapsed < dur)
        {
            tElapsed += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(tElapsed / dur);
            float s = 1f + 0.26f * Mathf.Sin(u * Mathf.PI);

            for (int i = 0; i < objetivosPulso.Count; i++)
            {
                if (objetivosPulso[i] != null)
                    objetivosPulso[i].localScale = Vector3.one * s;
            }
            yield return null;
        }

        for (int i = 0; i < objetivosPulso.Count; i++)
        {
            if (objetivosPulso[i] != null)
                objetivosPulso[i].localScale = Vector3.one;
        }

        rutinaPulsoRecord = null;
    }

    [ContextMenu("Probar Animación de Resultados (Simular Fin de Canción)")]
    public void SimularFinalDeCancionParaPrueba()
    {
        if (scoreManager == null) scoreManager = ScoreManager.Instance;
        string nombre = ultimaCancionJugada != null ? ultimaCancionJugada.songName : "Nocturne Op. 9 No. 2";

        if (scoreManager != null)
        {
            if (scoreManager.TotalNotasCancion <= 1 && scoreManager.NotasAcertadas == 0)
            {
                scoreManager.IniciarSesion(nombre, 20);
                for (int i = 0; i < 18; i++) scoreManager.RegistrarAcierto(); // 90% -> 3★
            }
            scoreManager.GuardarResultado();
        }

        if (songMenu != null)
        {
            songMenu.OnSongEnded();
        }

        MostrarResultadosFinales();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers de Construcción UI Dark Smoked Glass
    // ─────────────────────────────────────────────────────────────────────────

    private static TMP_FontAsset ObtenerFuenteTMP()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        TMP_Text existente = FindFirstObjectByType<TMP_Text>();
        return existente != null ? existente.font : null;
    }

    private static GameObject CrearHijoUI(string nombre, RectTransform padre, Vector2 size, Vector2 anchoredPos)
    {
        GameObject go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        return go;
    }

    private static TMP_Text CrearOActualizarTexto(RectTransform padre, string nombre, TMP_FontAsset fuente,
        string texto, float tamano, FontStyles estilo, Color color, TextAlignmentOptions alineacion,
        Vector2 size, Vector2 anchoredPos)
    {
        Transform existente = padre.Find(nombre);
        GameObject go = existente != null ? existente.gameObject : CrearHijoUI(nombre, padre, size, anchoredPos);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
        if (fuente != null) tmp.font = fuente;
        tmp.text = texto;
        tmp.fontSize = tamano;
        tmp.fontStyle = estilo;
        tmp.color = color;
        tmp.alignment = alineacion;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private Button CrearOActualizarBotonSmokedGlass(RectTransform padre, string nombre, TMP_FontAsset fuente,
        string etiqueta, Vector2 size, Vector2 anchoredPos)
    {
        Transform existente = padre.Find(nombre);
        GameObject go = existente != null ? existente.gameObject : CrearHijoUI(nombre, padre, size, anchoredPos);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Image img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        img.color = new Color(0.30f, 0.30f, 0.34f, 0.96f);
        if (spriteRedondeadoUI != null)
        {
            img.sprite = spriteRedondeadoUI;
            img.type = Image.Type.Sliced;
        }

        Button btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock cb = btn.colors;
        cb.normalColor      = new Color(0.26f, 0.26f, 0.28f, 0.84f); // #141418CC
        cb.highlightedColor = new Color(0.51f, 0.51f, 0.55f, 0.98f); // #27272AF2
        cb.pressedColor     = new Color(0.22f, 0.74f, 0.97f, 0.90f); // #38BDF8E6
        cb.selectedColor    = new Color(0.12f, 0.16f, 0.23f, 0.90f); // #1E293BE6
        cb.disabledColor    = new Color(0.08f, 0.08f, 0.09f, 0.40f);
        cb.fadeDuration     = 0.08f;
        btn.colors = cb;

        CrearOActualizarTexto(rt, "Text (TMP)", fuente, etiqueta, 22f, FontStyles.Bold,
            Color.white, TextAlignmentOptions.Midline, size - new Vector2(16f, 8f), Vector2.zero);

        return btn;
    }
}
