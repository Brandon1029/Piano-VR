using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using PianoVR;

/// <summary>
/// Controlador del tutorial interactivo guiado por pasos para la escena 'ModoTutorial'.
/// Crea y gestiona un panel flotante en World Space frente al piano (vinculado a Camera.main
/// y compatible con el raycast de manos + gesto de pellizco de XRUIInteractionEnabler):
///   • Paso 1: Cómo presionar las teclas (movimiento firme + prueba en Do central / C4).
///   • Paso 2: Invocar el menú con gesto de palma izquierda y elegir dificultad Fácil (Principiante).
///   • Paso 3: Recorrido por el Menú de Ajustes explicando cada control real de la escena.
///   • Paso 4: Práctica rápida de 3 notas consecutivas (Do - Re - Mi) y salida a ModoMusica o MainMenu_VR.
/// </summary>
[DefaultExecutionOrder(500)]
public class TutorialPianoController : MonoBehaviour
{
    public static TutorialPianoController Instance { get; private set; }

    public enum PasoTutorial
    {
        Paso1_TocarTecla = 1,
        Paso2_AbrirMenuYDificultadFacil = 2,
        Paso3_RecorridoAjustes = 3,
        Paso4_PracticaDoReMi = 4,
        Completado = 5
    }

    [Header("Estado del Tutorial")]
    public PasoTutorial pasoActual = PasoTutorial.Paso1_TocarTecla;

    [Header("Posicionamiento del Panel Flotante (World Space)")]
    [Tooltip("Posición centrada frente al piano (Pasos 1, 4 y Completado).")]
    public Vector3 posicionCentroPiano = new Vector3(25.773f, 1.50f, 2.62f);

    [Tooltip("rotación Euler cuando el panel está centrado frente al piano.")]
    public Vector3 rotacionCentroPiano = new Vector3(8f, 0f, 0f);

    [Tooltip("Posición lateral izquierda cuando el menú de canciones/ajustes está abierto (Pasos 2 y 3).")]
    public Vector3 posicionLateralMenu = new Vector3(24.98f, 1.52f, 2.48f);

    [Tooltip("Rotación Euler cuando el panel está a la izquierda del menú.")]
    public Vector3 rotacionLateralMenu = new Vector3(6f, -24f, 0f);

    [Header("Colores de Guía en Teclado")]
    [ColorUsage(true, true)]
    public Color colorTeclaGuia = new Color(0.15f, 0.95f, 1f, 1f);

    // Referencias de sistemas de la escena
    private NoteSpawner noteSpawner;
    private AutoPlayManager autoPlayManager;
    private PinchMenuToggle pinchMenuToggle;
    private SongSelectionMenu songSelectionMenu;
    private UIConfiguracionManager uiConfigManager;
    private GameObject songSelectionCanvasObj;

    // UI del Panel Flotante del Tutorial
    private Canvas tutorialCanvas;
    private RectTransform canvasRect;
    private TMP_Text txtPasoBadge;
    private TMP_Text txtTitulo;
    private TMP_Text txtCuerpo;
    private TMP_Text txtEstado;
    private Image barraProgresoFill;
    private Button btnAccionPrimaria;
    private TMP_Text txtBtnPrimario;
    private Button btnIrModoMusica;
    private Button btnVolverMainMenu;

    // Indicador 3D flotante sobre la tecla objetivo
    private GameObject indicadorTeclaObj;
    private TMP_Text txtIndicadorTecla;

    // Estado de seguimiento de teclas y pasos
    private PianoKey teclaObjetivoActual;
    private readonly string[] secuenciaPaso4 = new string[] { "C4", "D4", "E4" };
    private readonly string[] nombresPaso4   = new string[] { "Do (C4)", "Re (D4)", "Mi (E4)" };
    private int indiceNotaPaso4 = 0;
    private bool esperandoSoltarTecla = false;
    private bool transicionandoPaso = false;
    private bool dificultadFacilSeleccionada = false;
    private bool listenersMenuConectados = false;

    private Vector3 posObjetivoCanvas;
    private Quaternion rotObjetivoCanvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInicializarEnModoTutorial()
    {
        VerificarYCrearEnEscena(SceneManager.GetActiveScene());
    }

    private static void VerificarYCrearEnEscena(Scene scene)
    {
        if (scene.name != "ModoTutorial") return;

        if (Instance == null)
        {
            var existente = FindFirstObjectByType<TutorialPianoController>();
            if (existente != null)
            {
                Instance = existente;
            }
            else
            {
                GameObject go = new GameObject("[TutorialPianoController]");
                Instance = go.AddComponent<TutorialPianoController>();
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        StartCoroutine(InicializarTutorial());
    }

    private IEnumerator InicializarTutorial()
    {
        // Esperar un frame para que PianoKey, SongSelectionMenu y XRUIInteractionEnabler terminen su Start()
        yield return null;

        LocalizarReferenciasEscena();
        DetenerReproduccionAutomatica();
        AjustarPosicionSongCanvasSiNecesario();
        ConstruirPanelTutorialWorldSpace();
        ConstruirIndicadorTecla3D();
        ConectarEventosMenuCanciones();

        // Registrar el nuevo Canvas en XRUIInteractionEnabler para que funcione con rayo de mano + pellizco
        if (XRUIInteractionEnabler.Instance != null)
        {
            XRUIInteractionEnabler.Instance.ConfigurarEscenaActual();
        }

        IrAPaso(PasoTutorial.Paso1_TocarTecla);
    }

    private void LocalizarReferenciasEscena()
    {
        noteSpawner       = FindFirstObjectByType<NoteSpawner>();
        autoPlayManager   = FindFirstObjectByType<AutoPlayManager>();
        pinchMenuToggle   = FindFirstObjectByType<PinchMenuToggle>();
        songSelectionMenu = SongSelectionMenu.Instance != null ? SongSelectionMenu.Instance : FindFirstObjectByType<SongSelectionMenu>();
        uiConfigManager   = UIConfiguracionManager.Instance != null ? UIConfiguracionManager.Instance : FindFirstObjectByType<UIConfiguracionManager>();

        if (songSelectionMenu != null)
        {
            songSelectionCanvasObj = songSelectionMenu.gameObject;
        }
        else
        {
            songSelectionCanvasObj = GameObject.Find("SongSelectionCanvas");
        }

        // Si el piano de la escena está en otra coordenada X/Z, alinear las posiciones del panel respecto al piano real
        GameObject synth = GameObject.Find("Synthesizer_GameReady 2") ?? GameObject.Find("Synthesizer_GameReady");
        if (synth != null)
        {
            Vector3 p = synth.transform.position;
            posicionCentroPiano = new Vector3(p.x, p.y + 1.00f, p.z + 0.12f);
            posicionLateralMenu = new Vector3(p.x - 0.82f, p.y + 1.02f, p.z - 0.04f);
        }
    }

    /// <summary>
    /// Desactiva cualquier inicio automático de canciones o autoplayer mientras corre el tutorial.
    /// </summary>
    private void DetenerReproduccionAutomatica()
    {
        if (noteSpawner != null)
        {
            noteSpawner.StopCurrentSong();
        }

        if (songSelectionMenu != null)
        {
            songSelectionMenu.DetenerModoCancion();
        }

        if (autoPlayManager != null)
        {
            autoPlayManager.isAutoPlayEnabled = false;
            autoPlayManager.enabled = false;
        }

        if (PianoManager.Instance != null)
        {
            PianoManager.Instance.autoplayerHabilitado = false;
        }

        PianoKey.notasEnEscena.Clear();
    }

    private void AjustarPosicionSongCanvasSiNecesario()
    {
        if (songSelectionCanvasObj == null) return;
        RectTransform rt = songSelectionCanvasObj.GetComponent<RectTransform>();
        if (rt != null && rt.anchoredPosition.y < 1.50f)
        {
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 1.62f);
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 2.75f);
            rt.localRotation = Quaternion.Euler(10f, 0f, 0f);
        }
    }

    private void ConectarEventosMenuCanciones()
    {
        if (listenersMenuConectados || songSelectionCanvasObj == null) return;

        Button[] botones = songSelectionCanvasObj.GetComponentsInChildren<Button>(true);
        foreach (Button btn in botones)
        {
            if (btn == null) continue;
            string n = btn.name.ToLowerInvariant();

            // Detectar selección de dificultad Fácil / Principiante
            if (n.Contains("principiante") || n.Contains("facil"))
            {
                btn.onClick.AddListener(OnDificultadFacilPulsada);
            }
            // Si el usuario pulsa Play durante el tutorial, evitar que arranque la partida completa y contarlo como selección en Paso 2
            else if (n.Contains("btn_play") || n == "play")
            {
                btn.onClick.AddListener(OnBotonPlayInterceptadoEnTutorial);
            }
        }

        listenersMenuConectados = true;
    }

    private void OnDificultadFacilPulsada()
    {
        dificultadFacilSeleccionada = true;
        Debug.Log("[TutorialPianoController] Dificultad Fácil (Principiante) seleccionada por el jugador.");

        if (pasoActual == PasoTutorial.Paso2_AbrirMenuYDificultadFacil && !transicionandoPaso)
        {
            txtEstado.text = "✓ Dificultad Fácil (Principiante) seleccionada. Avanzando...";
            txtEstado.color = new Color(0.35f, 1f, 0.60f, 1f);
            StartCoroutine(AvanzarPasoConRetardo(PasoTutorial.Paso3_RecorridoAjustes, 0.65f));
        }
    }

    private void OnBotonPlayInterceptadoEnTutorial()
    {
        // Impedir que una canción completa arranque durante los pasos del tutorial
        DetenerReproduccionAutomatica();

        if (pasoActual == PasoTutorial.Paso2_AbrirMenuYDificultadFacil && !transicionandoPaso)
        {
            dificultadFacilSeleccionada = true;
            txtEstado.text = "✓ Canción de prueba seleccionada (reproducción pausada durante el tutorial).";
            txtEstado.color = new Color(0.35f, 1f, 0.60f, 1f);
            StartCoroutine(AvanzarPasoConRetardo(PasoTutorial.Paso3_RecorridoAjustes, 0.65f));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construcción del Panel Flotante en World Space
    // ─────────────────────────────────────────────────────────────────────────

    private void ConstruirPanelTutorialWorldSpace()
    {
        if (tutorialCanvas != null) return;

        TMP_FontAsset fuente = ObtenerFuenteTMP();

        GameObject canvasGO = new GameObject("TutorialFloatingCanvas");
        canvasGO.transform.SetParent(transform, false);

        tutorialCanvas = canvasGO.AddComponent<Canvas>();
        tutorialCanvas.renderMode = RenderMode.WorldSpace;
        tutorialCanvas.worldCamera = ObtenerCamaraActiva();
        tutorialCanvas.sortingOrder = 25;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.referencePixelsPerUnit = 100f;

        canvasGO.AddComponent<GraphicRaycaster>();

        canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(860f, 500f);
        canvasRect.localScale = new Vector3(0.001f, 0.001f, 0.001f);
        canvasRect.position = posicionCentroPiano;
        canvasRect.rotation = Quaternion.Euler(rotacionCentroPiano);

        posObjetivoCanvas = posicionCentroPiano;
        rotObjetivoCanvas = Quaternion.Euler(rotacionCentroPiano);

        // Fondo principal oscuro elegante (#111C2D) con borde sutil
        GameObject bordeGO = CrearElementoUI("Border", canvasRect, new Vector2(860f, 500f), Vector2.zero);
        Image imgBorde = bordeGO.AddComponent<Image>();
        imgBorde.color = new Color(0.20f, 0.72f, 0.95f, 0.85f);

        GameObject bgGO = CrearElementoUI("Background", bordeGO.GetComponent<RectTransform>(), new Vector2(852f, 492f), Vector2.zero);
        Image imgBg = bgGO.AddComponent<Image>();
        imgBg.color = new Color(0.07f, 0.11f, 0.18f, 0.96f);
        RectTransform bgRect = bgGO.GetComponent<RectTransform>();

        // Barra superior de encabezado
        GameObject headerGO = CrearElementoUI("HeaderBar", bgRect, new Vector2(852f, 64f), new Vector2(0f, 214f));
        Image imgHeader = headerGO.AddComponent<Image>();
        imgHeader.color = new Color(0.11f, 0.21f, 0.36f, 1f);

        txtPasoBadge = CrearTextoUI("TxtPasoBadge", headerGO.GetComponent<RectTransform>(), fuente,
            "TUTORIAL INTERACTIVO — PASO 1 DE 4", 20f, FontStyles.Bold,
            new Color(0.35f, 0.88f, 1f, 1f), TextAlignmentOptions.MidlineLeft,
            new Vector2(780f, 44f), new Vector2(0f, 0f));

        // Barra de progreso (debajo del header)
        GameObject progBgGO = CrearElementoUI("ProgressBg", bgRect, new Vector2(852f, 8f), new Vector2(0f, 178f));
        Image imgProgBg = progBgGO.AddComponent<Image>();
        imgProgBg.color = new Color(0.16f, 0.24f, 0.35f, 1f);

        GameObject progFillGO = CrearElementoUI("ProgressFill", progBgGO.GetComponent<RectTransform>(), new Vector2(852f, 8f), Vector2.zero);
        RectTransform fillRt = progFillGO.GetComponent<RectTransform>();
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.anchorMin = new Vector2(0f, 0.5f);
        fillRt.anchorMax = new Vector2(0f, 0.5f);
        fillRt.anchoredPosition = Vector2.zero;
        barraProgresoFill = progFillGO.AddComponent<Image>();
        barraProgresoFill.color = new Color(0.20f, 0.88f, 0.98f, 1f);

        // Título del paso
        txtTitulo = CrearTextoUI("TxtTitulo", bgRect, fuente,
            "Paso 1: Cómo presionar las teclas del piano", 27f, FontStyles.Bold,
            Color.white, TextAlignmentOptions.TopLeft,
            new Vector2(788f, 42f), new Vector2(0f, 140f));

        // Cuerpo explicativo
        txtCuerpo = CrearTextoUI("TxtCuerpo", bgRect, fuente,
            "", 20.5f, FontStyles.Normal,
            new Color(0.90f, 0.94f, 0.98f, 1f), TextAlignmentOptions.TopLeft,
            new Vector2(788f, 230f), new Vector2(0f, -6f));
        txtCuerpo.lineSpacing = 6f;

        // Caja de estado en vivo
        GameObject statusBoxGO = CrearElementoUI("StatusBox", bgRect, new Vector2(788f, 46f), new Vector2(0f, -142f));
        Image imgStatus = statusBoxGO.AddComponent<Image>();
        imgStatus.color = new Color(0.10f, 0.18f, 0.29f, 0.95f);

        txtEstado = CrearTextoUI("TxtEstado", statusBoxGO.GetComponent<RectTransform>(), fuente,
            "", 19f, FontStyles.Bold,
            new Color(1f, 0.90f, 0.35f, 1f), TextAlignmentOptions.Midline,
            new Vector2(760f, 40f), Vector2.zero);

        // Botones inferiores (compatibles con Raycast de manos + Pellizco)
        btnAccionPrimaria = CrearBotonUI("Btn_ContinuarTutorial", bgRect, fuente,
            "Siguiente ➔", new Vector2(290f, 58f), new Vector2(245f, -206f),
            new Color(0.16f, 0.55f, 0.86f, 1f), out txtBtnPrimario);
        btnAccionPrimaria.onClick.AddListener(OnBotonPrimarioPulsado);

        btnIrModoMusica = CrearBotonUI("Btn_IrModoMusica", bgRect, fuente,
            "Ir a Modo Música", new Vector2(320f, 58f), new Vector2(-180f, -206f),
            new Color(0.14f, 0.62f, 0.42f, 1f), out _);
        btnIrModoMusica.onClick.AddListener(IrAModoMusica);
        btnIrModoMusica.gameObject.SetActive(false);

        btnVolverMainMenu = CrearBotonUI("Btn_VolverMainMenu", bgRect, fuente,
            "Volver a Menú Principal", new Vector2(340f, 58f), new Vector2(180f, -206f),
            new Color(0.22f, 0.45f, 0.78f, 1f), out _);
        btnVolverMainMenu.onClick.AddListener(VolverAMainMenu);
        btnVolverMainMenu.gameObject.SetActive(false);
    }

    private void ConstruirIndicadorTecla3D()
    {
        if (indicadorTeclaObj != null) return;

        TMP_FontAsset fuente = ObtenerFuenteTMP();

        indicadorTeclaObj = new GameObject("IndicadorTeclaTutorial3D");
        indicadorTeclaObj.transform.SetParent(transform, false);

        Canvas c = indicadorTeclaObj.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.sortingOrder = 30;

        RectTransform rt = indicadorTeclaObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(220f, 70f);
        rt.localScale = new Vector3(0.00075f, 0.00075f, 0.00075f);

        GameObject pill = CrearElementoUI("PillBg", rt, new Vector2(220f, 70f), Vector2.zero);
        Image img = pill.AddComponent<Image>();
        img.color = new Color(0.08f, 0.16f, 0.28f, 0.94f);

        txtIndicadorTecla = CrearTextoUI("TxtNotaGuia", pill.GetComponent<RectTransform>(), fuente,
            "▼ Do (C4)", 28f, FontStyles.Bold,
            new Color(0.30f, 0.95f, 1f, 1f), TextAlignmentOptions.Midline,
            new Vector2(210f, 62f), Vector2.zero);

        indicadorTeclaObj.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lógica de los 4 Pasos del Tutorial
    // ─────────────────────────────────────────────────────────────────────────

    public void IrAPaso(PasoTutorial nuevoPaso)
    {
        pasoActual = nuevoPaso;
        transicionandoPaso = false;
        esperandoSoltarTecla = false;
        DetenerReproduccionAutomatica();

        switch (pasoActual)
        {
            case PasoTutorial.Paso1_TocarTecla:
                ConfigurarPaso1();
                break;

            case PasoTutorial.Paso2_AbrirMenuYDificultadFacil:
                ConfigurarPaso2();
                break;

            case PasoTutorial.Paso3_RecorridoAjustes:
                ConfigurarPaso3();
                break;

            case PasoTutorial.Paso4_PracticaDoReMi:
                ConfigurarPaso4();
                break;

            case PasoTutorial.Completado:
                ConfigurarTutorialCompletado();
                break;
        }
    }

    private void ConfigurarPaso1()
    {
        // Ocultar el menú de canciones en el Paso 1 para dejar libre la vista del piano
        MostrarMenuCanciones(false);

        posObjetivoCanvas = posicionCentroPiano;
        rotObjetivoCanvas = Quaternion.Euler(rotacionCentroPiano);

        ActualizarBarraProgreso(0.25f);
        txtPasoBadge.text = "TUTORIAL INTERACTIVO — PASO 1 DE 4";
        txtTitulo.text    = "Paso 1: Cómo presionar las teclas del piano";
        txtCuerpo.text    =
            "Para tocar una tecla, baja la punta de tu dedo con un movimiento firme y algo de velocidad " +
            "(si solo apoyas el dedo muy lento no sonará para evitar toques accidentales).\n\n" +
            "• Puedes posar tus dedos suavemente sobre el teclado y luego hundir el dedo (~3.5 mm) hacia abajo.\n" +
            "• Prueba ahora haciendo sonar la tecla iluminada <b>Do central (C4)</b>.";

        teclaObjetivoActual = BuscarTeclaPorNombre("C4");
        string nombreTecla = teclaObjetivoActual != null ? teclaObjetivoActual.nombreNotaCompleto : "C4";
        txtEstado.text  = $"⏳ Esperando que toques la tecla iluminada: Do central ({nombreTecla})...";
        txtEstado.color = new Color(1f, 0.90f, 0.35f, 1f);

        btnAccionPrimaria.gameObject.SetActive(false);
        btnIrModoMusica.gameObject.SetActive(false);
        btnVolverMainMenu.gameObject.SetActive(false);
    }

    private void ConfigurarPaso2()
    {
        teclaObjetivoActual = null;
        if (indicadorTeclaObj != null) indicadorTeclaObj.SetActive(false);

        // Colocar el panel del tutorial a la izquierda para no tapar el SongSelectionCanvas
        posObjetivoCanvas = posicionLateralMenu;
        rotObjetivoCanvas = Quaternion.Euler(rotacionLateralMenu);

        dificultadFacilSeleccionada = false;
        ConectarEventosMenuCanciones();

        ActualizarBarraProgreso(0.50f);
        txtPasoBadge.text = "TUTORIAL INTERACTIVO — PASO 2 DE 4";
        txtTitulo.text    = "Paso 2: Abrir el menú y elegir dificultad Fácil";
        txtCuerpo.text    =
            "1. <b>Abre el menú de canciones:</b> gira la <b>palma de tu mano izquierda</b> hacia tu rostro " +
            "durante 0.5 s (o pulsa el <b>botón Menú / Y</b> del mando izquierdo, o <b>M</b> en PC).\n\n" +
            "2. <b>Selecciona dificultad Fácil (Principiante):</b> apunta con el rayo visual de tu mano al botón " +
            "<b>Principiante (Fácil)</b> en la barra superior del menú y junta <b>índice + pulgar (pellizco)</b>.";

        bool menuAbierto = EstaMenuCancionesVisible();
        txtEstado.text = menuAbierto
            ? "⏳ Menú abierto. Apunta y haz pellizco en 'Principiante (Fácil)' o pulsa Siguiente."
            : "⏳ Gira tu palma izquierda hacia el rostro (o pulsa Menú/Y) para abrir el menú...";
        txtEstado.color = new Color(1f, 0.90f, 0.35f, 1f);

        txtBtnPrimario.text = "Siguiente ➔";
        btnAccionPrimaria.gameObject.SetActive(true);
        btnIrModoMusica.gameObject.SetActive(false);
        btnVolverMainMenu.gameObject.SetActive(false);
    }

    private void ConfigurarPaso3()
    {
        teclaObjetivoActual = null;
        if (indicadorTeclaObj != null) indicadorTeclaObj.SetActive(false);

        posObjetivoCanvas = posicionLateralMenu;
        rotObjetivoCanvas = Quaternion.Euler(rotacionLateralMenu);

        // Asegurar que el menú esté visible y abrir automáticamente la sección de Ajustes
        MostrarMenuCanciones(true);
        if (uiConfigManager != null)
        {
            uiConfigManager.AbrirConfiguracion();
        }

        string resumenControles = ConstruirDescripcionControlesAjustesReales();

        ActualizarBarraProgreso(0.75f);
        txtPasoBadge.text = "TUTORIAL INTERACTIVO — PASO 3 DE 4";
        txtTitulo.text    = "Paso 3: Controles del Menú de Ajustes";
        txtCuerpo.text    = resumenControles;

        txtEstado.text  = "💡 Puedes probar los controles de Ajustes con rayo + pellizco y luego pulsar 'Entendido / Continuar'.";
        txtEstado.color = new Color(0.45f, 0.92f, 1f, 1f);

        txtBtnPrimario.text = "Entendido / Continuar ➔";
        btnAccionPrimaria.gameObject.SetActive(true);
        btnIrModoMusica.gameObject.SetActive(false);
        btnVolverMainMenu.gameObject.SetActive(false);
    }

    /// <summary>
    /// Inspecciona el panel de Ajustes real de la escena 'ModoTutorial' y devuelve la tarjeta explicativa.
    /// </summary>
    private string ConstruirDescripcionControlesAjustesReales()
    {
        return
            "En el panel de <b>Ajustes</b> (a tu derecha) dispones de estos controles reales:\n" +
            "• <b>Rango de Octavas (Botones - / +):</b> Desplaza el teclado hacia notas más graves o agudas (C1 a C5).\n" +
            "• <b>Pop-ups de Notas (Toggle):</b> Muestra u oculta el nombre flotante 3D de cada nota al tocarla.\n" +
            "• <b>Música Ambiental (Toggle):</b> Activa o silencia el sonido ambiental de fondo del salón.\n" +
            "• <b>Modo Autoplayer (Toggle):</b> Reproduce automáticamente la canción elegida como demostración.\n" +
            "• <b>Volver / Menú:</b> <i>Btn_Volver</i> regresa al catálogo de canciones y <i>Btn_Menu</i> vuelve al Menú Principal.";
    }

    private void ConfigurarPaso4()
    {
        // Cerrar el menú de ajustes/canciones para dejar el teclado despejado
        if (uiConfigManager != null)
        {
            uiConfigManager.VolverACanciones();
        }
        MostrarMenuCanciones(false);

        posObjetivoCanvas = posicionCentroPiano;
        rotObjetivoCanvas = Quaternion.Euler(rotacionCentroPiano);

        indiceNotaPaso4 = 0;
        teclaObjetivoActual = BuscarTeclaPorNombre(secuenciaPaso4[indiceNotaPaso4]);
        esperandoSoltarTecla = teclaObjetivoActual != null && teclaObjetivoActual.isPressed;

        ActualizarBarraProgreso(0.90f);
        txtPasoBadge.text = "TUTORIAL INTERACTIVO — PASO 4 DE 4";
        txtTitulo.text    = "Paso 4: Práctica rápida (Do - Re - Mi)";
        txtCuerpo.text    =
            "¡Último paso! Toca en orden las <b>3 notas iluminadas consecutivas</b> con un movimiento " +
            "firme de la punta de tu dedo:\n\n" +
            "   <b>1. Do (C4)</b>   ➔   <b>2. Re (D4)</b>   ➔   <b>3. Mi (E4)</b>";

        ActualizarTextoEstadoPaso4();

        btnAccionPrimaria.gameObject.SetActive(false);
        btnIrModoMusica.gameObject.SetActive(false);
        btnVolverMainMenu.gameObject.SetActive(false);
    }

    private void ActualizarTextoEstadoPaso4()
    {
        txtEstado.text  = $"🎹 Progreso: {indiceNotaPaso4}/3 notas — Toca ahora la tecla iluminada: {nombresPaso4[indiceNotaPaso4]}";
        txtEstado.color = new Color(1f, 0.90f, 0.35f, 1f);
    }

    private void ConfigurarTutorialCompletado()
    {
        teclaObjetivoActual = null;
        if (indicadorTeclaObj != null) indicadorTeclaObj.SetActive(false);

        posObjetivoCanvas = posicionCentroPiano;
        rotObjetivoCanvas = Quaternion.Euler(rotacionCentroPiano);

        ActualizarBarraProgreso(1.0f);
        txtPasoBadge.text = "TUTORIAL COMPLETADO — ¡LISTO PARA TOCAR!";
        txtTitulo.text    = "¡Tutorial completado!";
        txtCuerpo.text    =
            "Ya dominas los controles esenciales de <b>Piano VR</b>:\n" +
            "• <b>Toque en el teclado:</b> descenso firme de la yema del dedo con pre-travel visual.\n" +
            "• <b>Menú desplegable:</b> girar la palma izquierda hacia el rostro (0.5 s) o botón Menú / Y.\n" +
            "• <b>Selección en paneles:</b> apuntar con el rayo de la mano y hacer <b>pellizco (índice + pulgar)</b>.\n\n" +
            "Elige a dónde quieres ir ahora apuntando y pellizcando uno de los botones inferiores:";

        txtEstado.text  = "✓ ¡Felicidades! Has completado las 3 notas de práctica (Do - Re - Mi).";
        txtEstado.color = new Color(0.35f, 1f, 0.60f, 1f);

        btnAccionPrimaria.gameObject.SetActive(false);
        btnIrModoMusica.gameObject.SetActive(true);
        btnVolverMainMenu.gameObject.SetActive(true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bucle de Actualización y Guía Visual en Teclas
    // ─────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        // Asegurar que la cámara del Canvas esté siempre vinculada a Camera.main / cámara XR activa
        if (tutorialCanvas != null && tutorialCanvas.worldCamera == null)
        {
            tutorialCanvas.worldCamera = ObtenerCamaraActiva();
        }

        // Animar suavemente la posición/rotación del panel del tutorial
        if (canvasRect != null)
        {
            float t = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
            canvasRect.position = Vector3.Lerp(canvasRect.position, posObjetivoCanvas, t);
            canvasRect.rotation = Quaternion.Slerp(canvasRect.rotation, rotObjetivoCanvas, t);
        }

        if (transicionandoPaso) return;

        switch (pasoActual)
        {
            case PasoTutorial.Paso1_TocarTecla:
                EvaluarProgresoPaso1();
                break;

            case PasoTutorial.Paso2_AbrirMenuYDificultadFacil:
                EvaluarProgresoPaso2();
                break;

            case PasoTutorial.Paso4_PracticaDoReMi:
                EvaluarProgresoPaso4();
                break;
        }
    }

    private void LateUpdate()
    {
        // Se ejecuta DESPUÉS de PianoKey.LateUpdate() gracias a [DefaultExecutionOrder(500)]
        // para iluminar la tecla guía actual sin modificar PianoKey.cs
        if (teclaObjetivoActual != null && !transicionandoPaso &&
            (pasoActual == PasoTutorial.Paso1_TocarTecla || pasoActual == PasoTutorial.Paso4_PracticaDoReMi))
        {
            float pulso = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5.5f);
            if (!teclaObjetivoActual.isPressed)
            {
                teclaObjetivoActual.ApplyEmission(colorTeclaGuia, pulso);
            }

            if (indicadorTeclaObj != null)
            {
                if (!indicadorTeclaObj.activeSelf) indicadorTeclaObj.SetActive(true);
                Vector3 posBase = teclaObjetivoActual.transform.position;
                float offsetFlotante = Mathf.Sin(Time.unscaledTime * 4f) * 0.005f;
                indicadorTeclaObj.transform.position = posBase + new Vector3(0f, 0.055f + offsetFlotante, 0.01f);

                Camera cam = ObtenerCamaraActiva();
                if (cam != null)
                {
                    Vector3 dir = indicadorTeclaObj.transform.position - cam.transform.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        indicadorTeclaObj.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    }
                }

                txtIndicadorTecla.text = pasoActual == PasoTutorial.Paso1_TocarTecla
                    ? $"▼ Do ({teclaObjetivoActual.nombreNotaCompleto})"
                    : $"▼ {indiceNotaPaso4 + 1}/3: {nombresPaso4[indiceNotaPaso4]}";
            }
        }
        else if (indicadorTeclaObj != null && indicadorTeclaObj.activeSelf)
        {
            indicadorTeclaObj.SetActive(false);
        }
    }

    private void EvaluarProgresoPaso1()
    {
        if (teclaObjetivoActual == null)
        {
            teclaObjetivoActual = BuscarTeclaPorNombre("C4");
            if (teclaObjetivoActual == null) return;
        }

        if (teclaObjetivoActual.isPressed)
        {
            txtEstado.text  = $"✓ ¡Muy bien! Hiciste sonar {teclaObjetivoActual.nombreNotaCompleto}. Avanzando al Paso 2...";
            txtEstado.color = new Color(0.35f, 1f, 0.60f, 1f);
            StartCoroutine(AvanzarPasoConRetardo(PasoTutorial.Paso2_AbrirMenuYDificultadFacil, 0.75f));
        }
    }

    private void EvaluarProgresoPaso2()
    {
        if (!listenersMenuConectados)
        {
            ConectarEventosMenuCanciones();
        }

        bool menuVisible = EstaMenuCancionesVisible();
        if (menuVisible && !dificultadFacilSeleccionada)
        {
            txtEstado.text  = "✓ ¡Menú abierto! Ahora apunta y haz pellizco en 'Principiante (Fácil)' (o pulsa Siguiente).";
            txtEstado.color = new Color(0.45f, 0.92f, 1f, 1f);
        }
        else if (menuVisible && dificultadFacilSeleccionada && !transicionandoPaso)
        {
            txtEstado.text  = "✓ Menú abierto y dificultad Fácil seleccionada. Avanzando al Paso 3...";
            txtEstado.color = new Color(0.35f, 1f, 0.60f, 1f);
            StartCoroutine(AvanzarPasoConRetardo(PasoTutorial.Paso3_RecorridoAjustes, 0.65f));
        }
    }

    private void EvaluarProgresoPaso4()
    {
        if (indiceNotaPaso4 >= secuenciaPaso4.Length) return;

        if (teclaObjetivoActual == null)
        {
            teclaObjetivoActual = BuscarTeclaPorNombre(secuenciaPaso4[indiceNotaPaso4]);
            if (teclaObjetivoActual == null) return;
        }

        if (esperandoSoltarTecla)
        {
            if (!teclaObjetivoActual.isPressed)
            {
                esperandoSoltarTecla = false;
            }
            return;
        }

        if (teclaObjetivoActual.isPressed)
        {
            indiceNotaPaso4++;
            if (indiceNotaPaso4 < secuenciaPaso4.Length)
            {
                teclaObjetivoActual = BuscarTeclaPorNombre(secuenciaPaso4[indiceNotaPaso4]);
                esperandoSoltarTecla = teclaObjetivoActual != null && teclaObjetivoActual.isPressed;
                ActualizarTextoEstadoPaso4();
            }
            else
            {
                txtEstado.text  = "✓ ¡Secuencia Do - Re - Mi completada!";
                txtEstado.color = new Color(0.35f, 1f, 0.60f, 1f);
                StartCoroutine(AvanzarPasoConRetardo(PasoTutorial.Completado, 0.55f));
            }
        }
    }

    private IEnumerator AvanzarPasoConRetardo(PasoTutorial siguientePaso, float segundos)
    {
        transicionandoPaso = true;
        yield return new WaitForSecondsRealtime(segundos);
        IrAPaso(siguientePaso);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Acciones de Botones del Panel Tutorial
    // ─────────────────────────────────────────────────────────────────────────

    private void OnBotonPrimarioPulsado()
    {
        if (transicionandoPaso) return;

        if (pasoActual == PasoTutorial.Paso2_AbrirMenuYDificultadFacil)
        {
            IrAPaso(PasoTutorial.Paso3_RecorridoAjustes);
        }
        else if (pasoActual == PasoTutorial.Paso3_RecorridoAjustes)
        {
            IrAPaso(PasoTutorial.Paso4_PracticaDoReMi);
        }
    }

    private void IrAModoMusica()
    {
        CargarEscenaConTransicion("ModoMusica");
    }

    private void VolverAMainMenu()
    {
        CargarEscenaConTransicion("MainMenu_VR");
    }

    private void CargarEscenaConTransicion(string nombreEscena)
    {
        Debug.Log($"[TutorialPianoController] Cargando escena '{nombreEscena}'...");
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TransitionToScene(nombreEscena);
        }
        else
        {
            SceneManager.LoadScene(nombreEscena);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Utilidades de Búsqueda y Creación UI
    // ─────────────────────────────────────────────────────────────────────────

    private PianoKey BuscarTeclaPorNombre(string notaObjetivo)
    {
        // 1. Buscar por nombreNotaCompleto (ej. "C4", "D4", "E4")
        foreach (var k in PianoKey.todasLasTeclas)
        {
            if (k != null && string.Equals(k.nombreNotaCompleto, notaObjetivo, System.StringComparison.OrdinalIgnoreCase))
            {
                return k;
            }
        }

        // 2. Buscar por nombre del GameObject
        foreach (var k in PianoKey.todasLasTeclas)
        {
            if (k != null && string.Equals(k.gameObject.name.Trim(), notaObjetivo, System.StringComparison.OrdinalIgnoreCase))
            {
                return k;
            }
        }

        // 3. Fallback: buscar la nota base en la octava central disponible
        string baseBuscada = notaObjetivo.Length > 0 ? notaObjetivo.Substring(0, 1).ToUpperInvariant() : "C";
        foreach (var k in PianoKey.todasLasTeclas)
        {
            if (k != null && k.notaNombreBase == baseBuscada && k.offsetOctavaRelativa == 1)
            {
                return k;
            }
        }

        return PianoKey.todasLasTeclas.Count > 0 ? PianoKey.todasLasTeclas[0] : null;
    }

    private bool EstaMenuCancionesVisible()
    {
        if (songSelectionCanvasObj == null || !songSelectionCanvasObj.activeInHierarchy)
            return false;

        CanvasGroup cg = songSelectionCanvasObj.GetComponent<CanvasGroup>();
        return cg == null || cg.alpha > 0.5f;
    }

    private void MostrarMenuCanciones(bool mostrar)
    {
        if (pinchMenuToggle != null)
        {
            pinchMenuToggle.MostrarMenu(mostrar, "TutorialPianoController");
        }
        else if (songSelectionCanvasObj != null)
        {
            songSelectionCanvasObj.SetActive(mostrar);
        }
    }

    private Camera ObtenerCamaraActiva()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;

        if (XRUIInteractionEnabler.Instance != null)
        {
            Camera c = XRUIInteractionEnabler.Instance.ObtenerCamaraActiva();
            if (c != null) return c;
        }

        return FindFirstObjectByType<Camera>();
    }

    private void ActualizarBarraProgreso(float norm)
    {
        if (barraProgresoFill != null)
        {
            RectTransform rt = barraProgresoFill.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(852f * Mathf.Clamp01(norm), 8f);
        }
    }

    private static TMP_FontAsset ObtenerFuenteTMP()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        TMP_Text existente = FindFirstObjectByType<TMP_Text>();
        return existente != null ? existente.font : null;
    }

    private static GameObject CrearElementoUI(string nombre, RectTransform padre, Vector2 size, Vector2 anchoredPos)
    {
        GameObject go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        return go;
    }

    private static TMP_Text CrearTextoUI(string nombre, RectTransform padre, TMP_FontAsset fuente,
        string texto, float tamano, FontStyles estilo, Color color, TextAlignmentOptions alineacion,
        Vector2 size, Vector2 anchoredPos)
    {
        GameObject go = CrearElementoUI(nombre, padre, size, anchoredPos);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (fuente != null) tmp.font = fuente;
        tmp.text = texto;
        tmp.fontSize = tamano;
        tmp.fontStyle = estilo;
        tmp.color = color;
        tmp.alignment = alineacion;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CrearBotonUI(string nombre, RectTransform padre, TMP_FontAsset fuente,
        string etiqueta, Vector2 size, Vector2 anchoredPos, Color colorFondo, out TMP_Text textoBoton)
    {
        GameObject go = CrearElementoUI(nombre, padre, size, anchoredPos);
        Image img = go.AddComponent<Image>();
        img.color = colorFondo;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        textoBoton = CrearTextoUI("Label", go.GetComponent<RectTransform>(), fuente,
            etiqueta, 21f, FontStyles.Bold, Color.white, TextAlignmentOptions.Midline,
            size - new Vector2(16f, 8f), Vector2.zero);

        return btn;
    }
}
