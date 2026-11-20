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

    [Tooltip("Producto punto mínimo entre la normal de la palma y la dirección al rostro (0.60 ≈ cono de 53°).")]
    [Range(0.4f, 0.95f)]
    public float umbralAlineacionPalma = 0.60f;

    [Tooltip("Distancia máxima (metros) entre la palma izquierda y el rostro para reconocer el gesto.")]
    public float distanciaMaximaAlRostro = 1.05f;

    [Tooltip("Margen vertical máximo (metros) por debajo de la cámara donde aún se reconoce el gesto de palma.")]
    public float margenAlturaBajoCamara = 0.60f;

    [Header("Animación de Aparición / Ocultamiento")]
    [Tooltip("Duración en segundos de la transición suave de escala y opacidad.")]
    public float duracionAnimacion = 0.22f;

    [Tooltip("Estado inicial del menú al cargar la escena.")]
    public bool visibleAlIniciar = true;

    private XRHandSubsystem handSubsystem;
    private XROrigin xrOrigin;
    private Camera camaraActiva;
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
        AsignarCamaraACanvasesWorldSpace();
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
        AsignarCamaraACanvasesWorldSpace();
    }

    /// <summary>
    /// Búsqueda robusta de la cámara activa: Camera.main -> xrOrigin.Camera ->
    /// primer Camera activo dentro del XR Origin -> FindFirstObjectByType&lt;Camera&gt;().
    /// </summary>
    private Camera ObtenerCamaraActiva()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
        {
            camaraActiva = Camera.main;
            return camaraActiva;
        }

        if (xrOrigin == null)
        {
            xrOrigin = FindFirstObjectByType<XROrigin>();
        }

        if (xrOrigin != null)
        {
            if (xrOrigin.Camera != null && xrOrigin.Camera.isActiveAndEnabled)
            {
                camaraActiva = xrOrigin.Camera;
                return camaraActiva;
            }

            Camera[] camsRig = xrOrigin.GetComponentsInChildren<Camera>(false);
            for (int i = 0; i < camsRig.Length; i++)
            {
                if (camsRig[i] != null && camsRig[i].isActiveAndEnabled)
                {
                    camaraActiva = camsRig[i];
                    return camaraActiva;
                }
            }
        }

        if (camaraActiva != null && camaraActiva.isActiveAndEnabled)
            return camaraActiva;

        Camera fallback = FindFirstObjectByType<Camera>();
        if (fallback != null && fallback.isActiveAndEnabled)
        {
            camaraActiva = fallback;
            return camaraActiva;
        }

        return null;
    }

    /// <summary>
    /// Devuelve el Transform real del espacio de tracking (CameraFloorOffsetObject / Camera Offset)
    /// para convertir poses de XRHandSubsystem a coordenadas mundiales inmunes a CameraYOffset.
    /// </summary>
    private Transform ObtenerTransformEspacioTracking(Camera cam)
    {
        if (xrOrigin == null)
        {
            xrOrigin = FindFirstObjectByType<XROrigin>();
        }

        if (xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null)
        {
            return xrOrigin.CameraFloorOffsetObject.transform;
        }

        if (cam != null && cam.transform.parent != null)
        {
            return cam.transform.parent;
        }

        return xrOrigin != null && xrOrigin.Origin != null ? xrOrigin.Origin.transform : null;
    }

    private void AsignarCamaraACanvasesWorldSpace()
    {
        Camera cam = ObtenerCamaraActiva();
        if (cam == null) return;

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c != null && c.renderMode == RenderMode.WorldSpace)
            {
                c.worldCamera = cam;
            }
        }
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

        Camera cam = ObtenerCamaraActiva();
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

            // Convertir la pose del espacio de tracking real (CameraFloorOffsetObject) a espacio mundial
            Transform trackingTf = ObtenerTransformEspacioTracking(cam);
            Vector3 palmWorldPos = trackingTf != null ? trackingTf.TransformPoint(palmPoseLocal.position) : palmPoseLocal.position;
            Quaternion palmWorldRot = trackingTf != null ? trackingTf.rotation * palmPoseLocal.rotation : palmPoseLocal.rotation;

            // Verificar que la mano esté en un rango cómodo frente al rostro (inmune al ajuste de altura de cámara)
            float distancia = Vector3.Distance(palmWorldPos, cam.transform.position);
            bool alturaValida = palmWorldPos.y > (cam.transform.position.y - margenAlturaBajoCamara);

            // En OpenXR / XR Hands, -Up de la articulación Palm apunta hacia afuera de la palma
            Vector3 normalPalma = -(palmWorldRot * Vector3.up);
            Vector3 dirAlRostro = (cam.transform.position - palmWorldPos).normalized;
            float alineacion = Vector3.Dot(normalPalma, dirAlRostro);

            // Verificar que la mano esté abierta (dedo medio extendido respecto a la palma)
            bool manoAbierta = true;
            if (leftHand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out Pose middlePoseLocal))
            {
                manoAbierta = Vector3.Distance(middlePoseLocal.position, palmPoseLocal.position) > 0.065f;
            }

            bool gestoActivo = alturaValida && distancia <= distanciaMaximaAlRostro &&
                               manoAbierta && alineacion >= umbralAlineacionPalma;

            if (esperandoBajarPalma)
            {
                // Re-armar el gesto cuando el usuario gire o baje la palma
                if (alineacion < 0.40f || !alturaValida)
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
