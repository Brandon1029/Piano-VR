using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controlador del menú desplegable/ocultable para 'ModoLibreMenu' y 'ModoMusica'.
///
/// Formas de abrir/ocultar el menú:
///   1. Hand Tracking (Meta Quest): Girar la palma de la mano izquierda abierta hacia
///      el rostro y mantenerla ~0.5 segundos (solo funciona con la mano elevada por encima
///      del teclado para evitar disparos accidentales al tocar el piano).
///   2. Mandos XR (Meta Quest): Botón Menú o botón Y (SecondaryButton) del mando izquierdo.
///   3. PC / Editor: Tecla 'M'.
///
/// Incluye animación suave de escala y opacidad (CanvasGroup). Al ocultarse desactiva
/// raycasts e interacción para que jamás interfiera con las teclas del piano.
/// </summary>
public class PinchMenuToggle : MonoBehaviour
{
    private static PinchMenuToggle instance;

    [Header("Referencias")]
    [Tooltip("Canvas principal del menú a mostrar/ocultar. Si está vacío se busca automáticamente.")]
    public GameObject menuCanvas;

    [Header("Gesto de Mano (Palma Izquierda hacia el Rostro)")]
    [Tooltip("Tiempo en segundos sosteniendo la palma izquierda mirando al rostro para alternar el menú.")]
    public float tiempoSostenerGesto = 0.5f;

    [Tooltip("Producto punto mínimo entre la normal de la palma y la dirección al rostro (0.68 ≈ cono de 47°).")]
    [Range(0.4f, 0.95f)]
    public float umbralAlineacionPalma = 0.68f;

    [Tooltip("Distancia máxima (metros) entre la palma izquierda y el rostro para reconocer el gesto.")]
    public float distanciaMaximaAlRostro = 0.75f;

    [Header("Animación de Aparición / Ocultamiento")]
    [Tooltip("Duración en segundos de la transición suave de escala y opacidad.")]
    public float duracionAnimacion = 0.22f;

    [Tooltip("Estado inicial del menú al cargar la escena.")]
    public bool visibleAlIniciar = true;

    private XRHandSubsystem handSubsystem;
    private XROrigin xrOrigin;
    private CanvasGroup canvasGroup;
    private Vector3 escalaBase = Vector3.one;
    private bool escalaBaseCapturada = false;

    private bool menuVisible = true;
    private float tiempoGestoPalma = 0f;
    private bool esperandoBajarPalma = false;
    private bool botonMandoAnterior = false;
    private Coroutine animacionActual;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInicializar()
    {
        if (instance == null)
        {
            var existente = FindFirstObjectByType<PinchMenuToggle>();
            if (existente != null)
            {
                instance = existente;
            }
            else
            {
                GameObject runner = new GameObject("[PinchMenuToggle]");
                instance = runner.AddComponent<PinchMenuToggle>();
                DontDestroyOnLoad(runner);
            }
        }

        instance.ConfigurarEnEscenaActual();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ConfigurarEnEscenaActual();
    }

    private void Start()
    {
        if (instance == null) instance = this;
        ConfigurarEnEscenaActual();
    }

    public void ConfigurarEnEscenaActual()
    {
        string escena = SceneManager.GetActiveScene().name;
        bool esEscenaConMenuJuego = escena == "ModoLibreMenu" || escena == "ModoMusica";

        xrOrigin = FindFirstObjectByType<XROrigin>();
        ObtenerHandSubsystem();

        tiempoGestoPalma = 0f;
        esperandoBajarPalma = false;
        botonMandoAnterior = false;

        if (!esEscenaConMenuJuego && menuCanvas == null)
        {
            return;
        }

        if (menuCanvas == null)
        {
            // Priorizar el SongSelectionCanvas activo en escena (el que sobrevivió al singleton)
            if (SongSelectionMenu.Instance != null)
            {
                menuCanvas = SongSelectionMenu.Instance.gameObject;
            }
            else
            {
                menuCanvas = GameObject.Find("SongSelectionCanvas") ?? GameObject.Find("CanvasMenus");
            }
        }

        if (menuCanvas != null)
        {
            escalaBase = menuCanvas.transform.localScale;
            if (escalaBase.sqrMagnitude < 0.0000001f)
            {
                escalaBase = new Vector3(0.001f, 0.001f, 0.001f);
            }
            escalaBaseCapturada = true;

            canvasGroup = menuCanvas.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = menuCanvas.AddComponent<CanvasGroup>();
            }

            menuVisible = visibleAlIniciar;
            AplicarEstadoInmediato(menuVisible);
            Debug.Log($"[PinchMenuToggle] Vinculado a '{menuCanvas.name}' en escena '{escena}'.");
        }
    }

    private void ObtenerHandSubsystem()
    {
        try
        {
            if (XRGeneralSettings.Instance != null &&
                XRGeneralSettings.Instance.Manager != null &&
                XRGeneralSettings.Instance.Manager.activeLoader != null)
            {
                handSubsystem = XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PinchMenuToggle] Sin XRHandSubsystem activo (normal en Editor PC): {ex.Message}");
        }
    }

    private void Update()
    {
        if (menuCanvas == null)
        {
            // Reintentar vinculación si SongSelectionMenu inicializó después
            if (SongSelectionMenu.Instance != null)
            {
                ConfigurarEnEscenaActual();
            }
            if (menuCanvas == null) return;
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        // Soporte de prueba en PC con tecla 'M'
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            AlternarMenu("PC Teclado 'M'");
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.M))
        {
            AlternarMenu("PC Teclado 'M'");
        }
#endif
#endif

        EvaluarBotonMandoXR();
        EvaluarGestoPalmaIzquierda();
    }

    /// <summary>
    /// Alterna el menú al pulsar el botón Menú o el botón Y (SecondaryButton) del mando izquierdo XR.
    /// </summary>
    private void EvaluarBotonMandoXR()
    {
        UnityEngine.XR.InputDevice leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!leftController.isValid)
        {
            botonMandoAnterior = false;
            return;
        }

        bool menuBtn = false;
        bool secondaryBtn = false;
        leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out menuBtn);
        leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out secondaryBtn);

        bool pulsado = menuBtn || secondaryBtn;
        if (pulsado && !botonMandoAnterior)
        {
            AlternarMenu(menuBtn ? "Mando Izquierdo (Botón Menú)" : "Mando Izquierdo (Botón Y)");
        }
        botonMandoAnterior = pulsado;
    }

    /// <summary>
    /// Detecta si el usuario gira la palma de la mano izquierda abierta hacia su rostro
    /// durante ~0.5 segundos (con la mano levantada por encima del piano).
    /// </summary>
    private void EvaluarGestoPalmaIzquierda()
    {
        if (handSubsystem == null)
        {
            ObtenerHandSubsystem();
            if (handSubsystem == null) return;
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        try
        {
            XRHand leftHand = handSubsystem.leftHand;
            if (!leftHand.isTracked)
            {
                tiempoGestoPalma = 0f;
                esperandoBajarPalma = false;
                return;
            }

            if (!leftHand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palmPoseLocal))
            {
                tiempoGestoPalma = 0f;
                return;
            }

            // Convertir la pose de XR Origin local a espacio mundial
            Transform originTf = xrOrigin != null && xrOrigin.Origin != null ? xrOrigin.Origin.transform : null;
            Vector3 palmWorldPos = originTf != null ? originTf.TransformPoint(palmPoseLocal.position) : palmPoseLocal.position;
            Quaternion palmWorldRot = originTf != null ? originTf.rotation * palmPoseLocal.rotation : palmPoseLocal.rotation;

            // Verificar que la mano esté elevada (por encima del piano) y cerca del rostro
            float distancia = Vector3.Distance(palmWorldPos, cam.transform.position);
            bool alturaValida = palmWorldPos.y > (cam.transform.position.y - 0.38f);

            // En OpenXR / XR Hands, -Up de la articulación Palm apunta hacia afuera de la palma
            Vector3 normalPalma = -(palmWorldRot * Vector3.up);
            Vector3 dirAlRostro = (cam.transform.position - palmWorldPos).normalized;
            float alineacion = Vector3.Dot(normalPalma, dirAlRostro);

            // Verificar que la mano esté abierta (dedo medio extendido respecto a la palma)
            bool manoAbierta = true;
            if (leftHand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out Pose middlePoseLocal))
            {
                manoAbierta = Vector3.Distance(middlePoseLocal.position, palmPoseLocal.position) > 0.075f;
            }

            bool gestoActivo = alturaValida && distancia <= distanciaMaximaAlRostro &&
                               manoAbierta && alineacion >= umbralAlineacionPalma;

            if (esperandoBajarPalma)
            {
                // Re-armar el gesto cuando el usuario gire o baje la palma
                if (alineacion < 0.45f || !alturaValida)
                {
                    esperandoBajarPalma = false;
                    tiempoGestoPalma = 0f;
                }
                return;
            }

            if (gestoActivo)
            {
                tiempoGestoPalma += Time.unscaledDeltaTime;
                if (tiempoGestoPalma >= tiempoSostenerGesto)
                {
                    tiempoGestoPalma = 0f;
                    esperandoBajarPalma = true;
                    AlternarMenu("Gesto Palma Izquierda hacia el Rostro");
                }
            }
            else
            {
                tiempoGestoPalma = Mathf.Max(0f, tiempoGestoPalma - Time.unscaledDeltaTime * 2f);
            }
        }
        catch { }
    }

    public void AlternarMenu(string origen = "Manual")
    {
        MostrarMenu(!menuVisible, origen);
    }

    public void MostrarMenu(bool mostrar, string origen = "Manual")
    {
        if (menuCanvas == null) return;

        menuVisible = mostrar;

        if (animacionActual != null)
        {
            StopCoroutine(animacionActual);
        }
        animacionActual = StartCoroutine(AnimarVisibilidad(menuVisible));

        Debug.Log($"[PinchMenuToggle] Menú '{menuCanvas.name}' -> {(menuVisible ? "DESPLEGADO" : "OCULTO")} (Origen: {origen})");
    }

    private void AplicarEstadoInmediato(bool visible)
    {
        if (menuCanvas == null) return;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        if (escalaBaseCapturada)
        {
            menuCanvas.transform.localScale = visible ? escalaBase : escalaBase * 0.85f;
        }

        menuCanvas.SetActive(visible);
    }

    private IEnumerator AnimarVisibilidad(bool mostrar)
    {
        if (mostrar)
        {
            menuCanvas.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }
        else if (canvasGroup != null)
        {
            // Bloquear clics de inmediato al iniciar el ocultamiento
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        float alphaInicio = canvasGroup != null ? canvasGroup.alpha : (mostrar ? 0f : 1f);
        float alphaFin    = mostrar ? 1f : 0f;

        Vector3 escalaInicio = menuCanvas.transform.localScale;
        Vector3 escalaFin    = mostrar ? escalaBase : escalaBase * 0.85f;

        float duracion = Mathf.Max(0.05f, duracionAnimacion);
        float elapsed = 0f;

        while (elapsed < duracion)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duracion);
            float suave = t * t * (3f - 2f * t); // Smoothstep

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(alphaInicio, alphaFin, suave);
            }
            menuCanvas.transform.localScale = Vector3.Lerp(escalaInicio, escalaFin, suave);

            yield return null;
        }

        AplicarEstadoInmediato(mostrar);
        animacionActual = null;
    }
}
