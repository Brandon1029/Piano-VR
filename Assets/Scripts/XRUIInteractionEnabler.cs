using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Unity.XR.CoreUtils;
using PianoVR.MainMenu;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Controla la interacción ergonómica de UI en VR (Meta Quest Hand Tracking + mandos XR + PC):
///   1. Elimina el disparo ambiguo por proximidad física (desactiva UI Poke en manos y
///      colliders físicos en botones de UI).
///   2. Implementa detección explícita de Pellizco (IndexTip + ThumbTip) con histéresis
///      limpia (< 0.022 m para cerrar, > 0.035 m para re-armar).
///   3. Proyecta un rayo visual fino y una retícula sobre los Canvas World Space, resaltando
///      Button/Toggle/Slider en Hover y ejecutando clic o arrastre de Slider al pellizcar.
/// </summary>
public class XRUIInteractionEnabler : MonoBehaviour
{
    public static XRUIInteractionEnabler Instance { get; private set; }

    [Header("Ergonomía y Respuesta Visual UI")]
    [Tooltip("Tiempo de enfriamiento (debounce) en segundos para evitar dobles clics accidentales.")]
    public float debounceBotones = 0.25f;

    [Tooltip("Tamaño mínimo visual/táctil en unidades de Canvas para botones pequeños.")]
    public float tamanoMinimoTactil = 64f;

    [Tooltip("Margen extra (en unidades de Canvas) al evaluar el impacto del rayo sobre un control UI.")]
    public float margenHitCanvas = 10f;

    [Header("Detección Explícita de Pellizco (Hand Tracking)")]
    [Tooltip("Distancia máxima (m) entre IndexTip y ThumbTip para cerrar el pellizco (2.2 cm).")]
    public float distanciaCerrarPellizco = 0.022f;

    [Tooltip("Distancia mínima (m) entre IndexTip y ThumbTip para soltar y re-armar el pellizco (3.5 cm).")]
    public float distanciaAbrirPellizco = 0.035f;

    [Tooltip("Alcance máximo (m) del rayo de mano hacia paneles UI.")]
    public float alcanceMaximoRayoUI = 6.0f;

    [Header("Apariencia del Rayo y Retícula")]
    public Color colorRayoNormal    = new Color(0.85f, 0.93f, 1f, 0.55f);
    public Color colorRayoHover     = new Color(0.20f, 0.90f, 1f, 0.95f);
    public Color colorRayoPellizco  = new Color(0.30f, 1f, 0.55f, 1f);

    private XRHandSubsystem handSubsystem;
    private XROrigin xrOrigin;
    private Camera camaraActiva;
    private readonly List<Canvas> canvasesWorldSpace = new List<Canvas>();
    private readonly List<StatueMenuInteractable> bustosEnEscena = new List<StatueMenuInteractable>();
    private readonly RaycastHit[] bufferRaycast3D = new RaycastHit[24];

    private HandPinchPointer manoIzquierda;
    private HandPinchPointer manoDerecha;
    private Material materialRayo;
    private Material materialReticula;

    private class HandPinchPointer
    {
        public bool esDerecha;
        public bool estaPellizcando;
        public bool rayoInicializado;
        public Vector3 origenSuavizado;
        public Vector3 direccionSuavizada;

        public Transform fallbackIndexTip;
        public Transform fallbackThumbTip;

        public LineRenderer lineaRayo;
        public GameObject reticulaObj;
        public Renderer reticulaRenderer;
        public MaterialPropertyBlock reticulaProps = new MaterialPropertyBlock();

        public Selectable hoverActual;
        public StatueMenuInteractable bustoHoverActual;
        public Slider sliderArrastrado;
        public Canvas canvasSlider;
        public PointerEventData pointerData;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInicializarUIEnabler()
    {
        if (Instance == null)
        {
            GameObject runner = new GameObject("[XRUIInteractionEnabler]");
            Instance = runner.AddComponent<XRUIInteractionEnabler>();
            DontDestroyOnLoad(runner);
        }

        Instance.ConfigurarEscenaActual();
    }

    private void Start()
    {
        AsignarCamaraACanvasesWorldSpace();
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
        ConfigurarEscenaActual();
        StartCoroutine(ReconfigurarTrasCarga());
    }

    /// <summary>
    /// Re-vincula cámara, XR Origin, huesos de manos y Canvas un frame después de cargar
    /// la escena para cubrir objetos inicializados en Start() durante transiciones entre escenas.
    /// </summary>
    private IEnumerator ReconfigurarTrasCarga()
    {
        yield return null;
        ConfigurarEscenaActual();
    }

    public void ConfigurarEscenaActual()
    {
        xrOrigin = FindFirstObjectByType<XROrigin>();
        camaraActiva = ObtenerCamaraActiva();
        ObtenerHandSubsystem();

        ConfigurarEventSystem();
        DesactivarProximidadUIEnManos();
        ConfigurarCanvases();
        DetectarBustosEnEscena();
        ConfigurarCamara();
        AsegurarPunterosDeMano();
        LimpiarEstadoPunteros();

        Debug.Log($"[XRUIInteractionEnabler] Escena '{SceneManager.GetActiveScene().name}' configurada: " +
                  $"{canvasesWorldSpace.Count} Canvas WorldSpace, {bustosEnEscena.Count} bustos 3D, cámara='{(camaraActiva != null ? camaraActiva.name : "null")}', rayo+pellizco activo.");
    }

    private void DetectarBustosEnEscena()
    {
        bustosEnEscena.Clear();
        StatueMenuInteractable[] bustos = FindObjectsByType<StatueMenuInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bustos.Length; i++)
        {
            if (bustos[i] != null)
            {
                bustosEnEscena.Add(bustos[i]);
            }
        }
    }

    /// <summary>
    /// Búsqueda robusta de la cámara activa: Camera.main -> xrOrigin.Camera ->
    /// primer Camera activo dentro del XR Origin -> FindFirstObjectByType&lt;Camera&gt;().
    /// </summary>
    public Camera ObtenerCamaraActiva()
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
        catch { }
    }

    private void ConfigurarEventSystem()
    {
        EventSystem es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            es = esObj.AddComponent<EventSystem>();
        }

        var xruiModule = es.GetComponent<XRUIInputModule>();
        var inputSysModule = es.GetComponent<InputSystemUIInputModule>();

        if (xruiModule == null)
        {
            if (inputSysModule != null)
            {
                DestroyImmediate(inputSysModule);
            }

            xruiModule = es.gameObject.AddComponent<XRUIInputModule>();
        }

        if (xruiModule != null)
        {
            xruiModule.enableXRInput = true;
            xruiModule.enableMouseInput = true;
            xruiModule.enableTouchInput = true;
            xruiModule.enableGamepadInput = true;
            xruiModule.enableJoystickInput = true;
        }
    }

    /// <summary>
    /// Desactiva la interacción UI por proximidad/poke en las manos (XRPokeInteractor y
    /// NearFar/Ray de manos) para eliminar disparos ambiguos al acercar los dedos.
    /// Los mandos físicos conservan su raycast normal con gatillo.
    /// </summary>
    private void DesactivarProximidadUIEnManos()
    {
        XRPokeInteractor[] pokes = FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var poke in pokes)
        {
            if (poke == null) continue;
            poke.enableUIInteraction = false;
            if (PerteneceAManoYNoAMando(poke.transform))
            {
                poke.enabled = false;
            }
        }

        // En NearFarInteractor / XRRayInteractor pertenecientes a manos, ceder el control UI al sistema explícito de pellizco
        XRBaseInputInteractor[] inputInteractors = FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var interactor in inputInteractors)
        {
            if (interactor == null) continue;
            if (!PerteneceAManoYNoAMando(interactor.transform)) continue;

            if (interactor is NearFarInteractor nearFar)
            {
                nearFar.enableUIInteraction = false;
            }
            else if (interactor is XRRayInteractor rayInteractor)
            {
                rayInteractor.enableUIInteraction = false;
            }
        }
    }

    private static bool PerteneceAManoYNoAMando(Transform t)
    {
        bool tieneMano = false;
        Transform actual = t;
        int prof = 0;
        while (actual != null && prof < 8)
        {
            string n = actual.name.ToLowerInvariant();
            if (n.Contains("controller") || n.Contains("mando")) return false;
            if (n.Contains("hand") || n.Contains("mano") || n.Contains("poke")) tieneMano = true;
            actual = actual.parent;
            prof++;
        }
        return tieneMano;
    }

    private void ConfigurarCanvases()
    {
        canvasesWorldSpace.Clear();
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Camera mainCam = ObtenerCamaraActiva();

        foreach (Canvas canvas in canvases)
        {
            if (canvas == null) continue;

            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                canvasesWorldSpace.Add(canvas);

                if (mainCam != null)
                {
                    canvas.worldCamera = mainCam;
                }

                // Raycaster para mandos físicos XR (ignorar cara trasera y sin oclusión 3D por teclado/manos)
                var trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                if (trackedRaycaster == null)
                {
                    trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                }
                trackedRaycaster.ignoreReversedGraphics = true;
                trackedRaycaster.checkFor3DOcclusion = false;

                // Raycaster para ratón en PC Editor
                var graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
                if (graphicRaycaster == null)
                {
                    graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                }
                graphicRaycaster.ignoreReversedGraphics = true;

                MejorarControlesEnJerarquia(canvas.transform);
            }
        }
    }

    /// <summary>
    /// Configura tamaño cómodo, feedback visual claro (hover/click) y debounce en controles UI,
    /// eliminando colliders físicos en botones para evitar toques accidentales por proximidad.
    /// </summary>
    public void MejorarControlesEnJerarquia(Transform raiz)
    {
        if (raiz == null) return;

        Selectable[] selectables = raiz.GetComponentsInChildren<Selectable>(true);
        foreach (Selectable sel in selectables)
        {
            if (sel == null) continue;
            if (sel is Button || sel is Toggle || sel is Slider)
            {
                MejorarControlUI(sel);
            }
        }
    }

    public void MejorarControlUI(Selectable sel)
    {
        if (sel == null) return;

        // 1. Feedback de color claro en hover y click
        ColorBlock cb = sel.colors;
        cb.highlightedColor = new Color(0.75f, 0.95f, 1f, 1f);
        cb.pressedColor     = new Color(0.45f, 0.80f, 1f, 1f);
        cb.selectedColor    = new Color(0.85f, 0.96f, 1f, 1f);
        cb.colorMultiplier  = Mathf.Max(cb.colorMultiplier, 1.1f);
        cb.fadeDuration     = 0.08f;
        sel.colors = cb;

        // 2. Asegurar tamaño cómodo sin colliders físicos que disparen por proximidad
        RectTransform rt = sel.GetComponent<RectTransform>();
        if (rt != null)
        {
            if (sel is Button && rt.sizeDelta.x > 0f && rt.sizeDelta.y > 0f &&
                (rt.sizeDelta.x < tamanoMinimoTactil || rt.sizeDelta.y < tamanoMinimoTactil))
            {
                rt.sizeDelta = new Vector2(
                    Mathf.Max(rt.sizeDelta.x, tamanoMinimoTactil),
                    Mathf.Max(rt.sizeDelta.y, tamanoMinimoTactil)
                );
            }

            if (sel is Slider slider && slider.handleRect != null)
            {
                Vector2 hSize = slider.handleRect.sizeDelta;
                if (hSize.x < 44f || hSize.y < 44f)
                {
                    slider.handleRect.sizeDelta = new Vector2(Mathf.Max(hSize.x, 44f), Mathf.Max(hSize.y, 44f));
                }
            }
        }

        // Desactivar BoxCollider en el botón UI para eliminar cualquier colisión/proximidad física con los dedos
        BoxCollider col = sel.GetComponent<BoxCollider>();
        if (col != null)
        {
            col.enabled = false;
        }

        // 3. Feedback de escala (hover/click) y debounce de 0.25s
        var feedback = sel.GetComponent<VRUIControlFeedback>();
        if (feedback == null)
        {
            feedback = sel.gameObject.AddComponent<VRUIControlFeedback>();
        }
        feedback.Configurar(sel, debounceBotones);
    }

    private void ConfigurarCamara()
    {
        Camera mainCam = ObtenerCamaraActiva();
        if (mainCam != null && mainCam.GetComponent<PhysicsRaycaster>() == null)
        {
            mainCam.gameObject.AddComponent<PhysicsRaycaster>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sistema Explícito de Rayo Visual + Retícula + Clic por Pellizco
    // ─────────────────────────────────────────────────────────────────────────

    private void AsegurarPunterosDeMano()
    {
        if (materialRayo == null)
        {
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            materialRayo = new Material(sh);
        }
        if (materialReticula == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            materialReticula = new Material(sh);
        }

        if (manoIzquierda == null) manoIzquierda = CrearPunteroMano(false);
        if (manoDerecha   == null) manoDerecha   = CrearPunteroMano(true);

        BuscarHuesosFallback(manoIzquierda);
        BuscarHuesosFallback(manoDerecha);
    }

    private HandPinchPointer CrearPunteroMano(bool esDerecha)
    {
        string sufijo = esDerecha ? "Right" : "Left";
        var ptr = new HandPinchPointer { esDerecha = esDerecha };

        // Rayo visual fino
        GameObject rayObj = new GameObject($"[HandUIRay_{sufijo}]");
        rayObj.transform.SetParent(transform, false);
        LineRenderer lr = rayObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.startWidth = 0.0028f;
        lr.endWidth   = 0.0014f;
        lr.material   = materialRayo;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        ptr.lineaRayo = lr;

        // Punto de mira (retícula) sobre el Canvas
        GameObject retObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        retObj.name = $"[HandUIReticle_{sufijo}]";
        retObj.transform.SetParent(transform, false);
        retObj.transform.localScale = Vector3.one * 0.012f;
        Collider retCol = retObj.GetComponent<Collider>();
        if (retCol != null) Destroy(retCol);

        Renderer retRend = retObj.GetComponent<Renderer>();
        retRend.sharedMaterial = materialReticula;
        retRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        retRend.receiveShadows = false;
        retObj.SetActive(false);

        ptr.reticulaObj = retObj;
        ptr.reticulaRenderer = retRend;
        return ptr;
    }

    private void LimpiarEstadoPunteros()
    {
        ResetearPuntero(manoIzquierda);
        ResetearPuntero(manoDerecha);
    }

    private void ResetearPuntero(HandPinchPointer ptr)
    {
        if (ptr == null) return;
        LimpiarHover(ptr);
        LimpiarHoverBusto(ptr);
        ptr.sliderArrastrado = null;
        ptr.canvasSlider = null;
        ptr.estaPellizcando = false;
        ptr.rayoInicializado = false;
        if (ptr.lineaRayo != null) ptr.lineaRayo.enabled = false;
        if (ptr.reticulaObj != null) ptr.reticulaObj.SetActive(false);
    }

    private void BuscarHuesosFallback(HandPinchPointer ptr)
    {
        ptr.fallbackIndexTip = null;
        ptr.fallbackThumbTip = null;
        if (xrOrigin == null) return;

        Transform[] todos = xrOrigin.GetComponentsInChildren<Transform>(true);
        string ladoBuscado = ptr.esDerecha ? "R" : "L";

        foreach (Transform t in todos)
        {
            if (t == null) continue;
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("tip")) continue;
            if (!PerteneceALado(t, ladoBuscado)) continue;

            if (n.Contains("index") && ptr.fallbackIndexTip == null) ptr.fallbackIndexTip = t;
            if (n.Contains("thumb") && ptr.fallbackThumbTip == null) ptr.fallbackThumbTip = t;
        }
    }

    private static bool PerteneceALado(Transform t, string ladoBuscado)
    {
        Transform actual = t;
        int prof = 0;
        while (actual != null && prof < 8)
        {
            string n = actual.name.ToLowerInvariant();
            if (n.Contains("right") || n.StartsWith("r_") || n.Contains("_r_")) return ladoBuscado == "R";
            if (n.Contains("left")  || n.StartsWith("l_") || n.Contains("_l_")) return ladoBuscado == "L";
            actual = actual.parent;
            prof++;
        }
        return false;
    }

    private void Update()
    {
        if (canvasesWorldSpace.Count == 0 && bustosEnEscena.Count == 0) return;

        if (handSubsystem == null)
        {
            ObtenerHandSubsystem();
        }

        if (manoIzquierda != null) ActualizarPunteroMano(manoIzquierda);
        if (manoDerecha   != null) ActualizarPunteroMano(manoDerecha);
    }

    private void ActualizarPunteroMano(HandPinchPointer ptr)
    {
        Camera cam = ObtenerCamaraActiva();
        if (cam == null) return;

        if (!ObtenerPosicionesPuntas(ptr, cam, out Vector3 posIndice, out Vector3 posPulgar, out Vector3 dirMano))
        {
            if (ptr.lineaRayo.enabled || ptr.hoverActual != null || ptr.bustoHoverActual != null || ptr.sliderArrastrado != null)
            {
                ResetearPuntero(ptr);
            }
            return;
        }

        // 1. Detección explícita de pellizco con histéresis (< 2.2 cm cierra, > 3.5 cm re-arma)
        float distPinch = Vector3.Distance(posIndice, posPulgar);
        bool pinchDown = false;
        bool pinchUp   = false;

        if (!ptr.estaPellizcando && distPinch < distanciaCerrarPellizco)
        {
            ptr.estaPellizcando = true;
            pinchDown = true;
        }
        else if (ptr.estaPellizcando && distPinch > distanciaAbrirPellizco)
        {
            ptr.estaPellizcando = false;
            pinchUp = true;
        }

        // 2. Calcular origen (punto medio índice-pulgar en espacio mundial real) y dirección estable
        Vector3 origenCrudo = (posIndice + posPulgar) * 0.5f;
        Vector3 hombroAprox = cam.transform.position
                              + cam.transform.right * (ptr.esDerecha ? 0.16f : -0.16f)
                              + Vector3.down * 0.20f;
        Vector3 dirDesdeHombro = (origenCrudo - hombroAprox).normalized;
        Vector3 dirCruda = dirMano.sqrMagnitude > 0.01f
            ? Vector3.Slerp(dirDesdeHombro, dirMano.normalized, 0.30f).normalized
            : dirDesdeHombro;

        if (!ptr.rayoInicializado)
        {
            ptr.origenSuavizado = origenCrudo;
            ptr.direccionSuavizada = dirCruda;
            ptr.rayoInicializado = true;
        }
        else
        {
            float suavizado = 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime);
            ptr.origenSuavizado = Vector3.Lerp(ptr.origenSuavizado, origenCrudo, suavizado);
            ptr.direccionSuavizada = Vector3.Slerp(ptr.direccionSuavizada, dirCruda, suavizado).normalized;
        }

        Ray rayo = new Ray(ptr.origenSuavizado, ptr.direccionSuavizada);

        // 3. Si estamos arrastrando un Slider con el pellizco mantenido, actualizarlo continuamente
        if (ptr.sliderArrastrado != null)
        {
            if (pinchUp || !ptr.estaPellizcando || !ptr.sliderArrastrado.gameObject.activeInHierarchy)
            {
                ptr.sliderArrastrado = null;
                ptr.canvasSlider = null;
            }
            else if (ptr.canvasSlider != null && IntersectarPlanoCanvas(rayo, ptr.canvasSlider, out Vector3 hitSlider, out _))
            {
                ActualizarValorSlider(ptr.sliderArrastrado, hitSlider);
                ActualizarVisualesRayo(ptr, rayo.origin, hitSlider, ptr.canvasSlider.transform.forward, colorRayoPellizco, true);
                return;
            }
        }

        // 4. Evaluar impacto contra Canvas World Space y contra Bustos 3D (MainMenu_VR), priorizando el más cercano
        bool hitCanvas = BuscarImpactoCanvas(rayo, out Canvas canvasHit, out Vector3 puntoImpacto, out float distCanvas);
        StatueMenuInteractable bustoHit = null;
        Vector3 puntoBusto = Vector3.zero;
        Vector3 normalBusto = -rayo.direction;
        float distBusto = float.MaxValue;
        bool hitBusto = bustosEnEscena.Count > 0 &&
                        BuscarImpactoBusto(rayo, out bustoHit, out puntoBusto, out normalBusto, out distBusto);

        if (hitBusto && (!hitCanvas || distBusto < distCanvas))
        {
            LimpiarHover(ptr);

            if (bustoHit != ptr.bustoHoverActual)
            {
                LimpiarHoverBusto(ptr);
                ptr.bustoHoverActual = bustoHit;
                ptr.bustoHoverActual.SetHovered(true);
            }

            if (pinchDown && ptr.bustoHoverActual != null)
            {
                Debug.Log($"[XRUIInteractionEnabler] Pellizco sobre busto 3D '{ptr.bustoHoverActual.name}' -> ExecuteAction()");
                ptr.bustoHoverActual.ExecuteAction();
            }

            Color colorBusto = ptr.estaPellizcando ? colorRayoPellizco : colorRayoHover;
            ActualizarVisualesRayo(ptr, rayo.origin, puntoBusto, -normalBusto, colorBusto, ptr.estaPellizcando);
            return;
        }

        LimpiarHoverBusto(ptr);

        if (!hitCanvas)
        {
            LimpiarHover(ptr);
            ptr.lineaRayo.enabled = false;
            ptr.reticulaObj.SetActive(false);
            return;
        }

        // 5. Detectar control UI (Button, Toggle, Slider) bajo el punto de mira
        Selectable controlBajoMira = BuscarControlEnPunto(canvasHit, puntoImpacto);

        // Actualizar Hover
        if (controlBajoMira != ptr.hoverActual)
        {
            LimpiarHover(ptr);
            ptr.hoverActual = controlBajoMira;
            if (ptr.hoverActual != null)
            {
                AsegurarPointerData(ptr);
                ExecuteEvents.Execute(ptr.hoverActual.gameObject, ptr.pointerData, ExecuteEvents.pointerEnterHandler);
            }
        }

        // 6. Ejecutar clic o inicio de arrastre exactamente en el instante de cerrar el pellizco
        if (pinchDown && ptr.hoverActual != null && ptr.hoverActual.IsInteractable())
        {
            AsegurarPointerData(ptr);

            if (ptr.hoverActual is Slider slider)
            {
                ptr.sliderArrastrado = slider;
                ptr.canvasSlider = canvasHit;
                ActualizarValorSlider(slider, puntoImpacto);
            }
            else if (ptr.hoverActual is Button || ptr.hoverActual is Toggle)
            {
                GameObject targetObj = ptr.hoverActual.gameObject;
                ExecuteEvents.Execute(targetObj, ptr.pointerData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(targetObj, ptr.pointerData, ExecuteEvents.pointerClickHandler);
                ExecuteEvents.Execute(targetObj, ptr.pointerData, ExecuteEvents.pointerUpHandler);
            }
        }

        // 7. Dibujar rayo fino y retícula sobre el Canvas
        Color colorActual = ptr.estaPellizcando ? colorRayoPellizco : (ptr.hoverActual != null ? colorRayoHover : colorRayoNormal);
        ActualizarVisualesRayo(ptr, rayo.origin, puntoImpacto, canvasHit.transform.forward, colorActual, ptr.estaPellizcando);
    }

    private bool ObtenerPosicionesPuntas(HandPinchPointer ptr, Camera cam, out Vector3 posIndice, out Vector3 posPulgar, out Vector3 dirMano)
    {
        posIndice = Vector3.zero;
        posPulgar = Vector3.zero;
        dirMano   = Vector3.zero;

        // Fuente principal: XRHandSubsystem convertido con el espacio de tracking real (CameraFloorOffsetObject)
        if (handSubsystem != null)
        {
            XRHand hand = ptr.esDerecha ? handSubsystem.rightHand : handSubsystem.leftHand;
            if (hand.isTracked &&
                hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose idxPose) &&
                hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thmPose))
            {
                Transform trackingTf = ObtenerTransformEspacioTracking(cam);
                posIndice = trackingTf != null ? trackingTf.TransformPoint(idxPose.position) : idxPose.position;
                posPulgar = trackingTf != null ? trackingTf.TransformPoint(thmPose.position) : thmPose.position;

                if (hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palmPose))
                {
                    Quaternion palmRotWorld = trackingTf != null ? trackingTf.rotation * palmPose.rotation : palmPose.rotation;
                    dirMano = palmRotWorld * Vector3.forward;
                }
                return true;
            }
        }

        // Fallback: huesos Transform reales del XR Origin en coordenadas mundiales
        if (ptr.fallbackIndexTip == null || ptr.fallbackThumbTip == null)
        {
            BuscarHuesosFallback(ptr);
        }

        if (ptr.fallbackIndexTip != null && ptr.fallbackThumbTip != null &&
            ptr.fallbackIndexTip.gameObject.activeInHierarchy &&
            ptr.fallbackThumbTip.gameObject.activeInHierarchy)
        {
            posIndice = ptr.fallbackIndexTip.position;
            posPulgar = ptr.fallbackThumbTip.position;
            dirMano   = ptr.fallbackIndexTip.forward;
            return true;
        }

        return false;
    }

    private bool BuscarImpactoCanvas(Ray rayo, out Canvas mejorCanvas, out Vector3 mejorPunto, out float menorDist)
    {
        mejorCanvas = null;
        mejorPunto = Vector3.zero;
        menorDist = float.MaxValue;

        for (int i = canvasesWorldSpace.Count - 1; i >= 0; i--)
        {
            Canvas c = canvasesWorldSpace[i];
            if (c == null)
            {
                canvasesWorldSpace.RemoveAt(i);
                continue;
            }

            if (!c.isActiveAndEnabled || !c.gameObject.activeInHierarchy) continue;

            // Respetar CanvasGroup (ej. cuando PinchMenuToggle oculta el menú)
            CanvasGroup cg = c.GetComponent<CanvasGroup>();
            if (cg != null && (!cg.blocksRaycasts || cg.alpha < 0.05f)) continue;

            if (!IntersectarPlanoCanvas(rayo, c, out Vector3 punto, out float dist)) continue;
            if (dist < 0.02f || dist > alcanceMaximoRayoUI || dist >= menorDist) continue;

            RectTransform rt = c.GetComponent<RectTransform>();
            if (rt == null) continue;

            Vector3 localPt = rt.InverseTransformPoint(punto);
            Rect rectAmpliado = rt.rect;
            rectAmpliado.xMin -= 20f;
            rectAmpliado.xMax += 20f;
            rectAmpliado.yMin -= 20f;
            rectAmpliado.yMax += 20f;

            if (rectAmpliado.Contains(new Vector2(localPt.x, localPt.y)))
            {
                menorDist = dist;
                mejorCanvas = c;
                mejorPunto = punto;
            }
        }

        return mejorCanvas != null;
    }

    private bool BuscarImpactoBusto(Ray rayo, out StatueMenuInteractable mejorBusto, out Vector3 mejorPunto, out Vector3 mejorNormal, out float menorDist)
    {
        mejorBusto  = null;
        mejorPunto  = Vector3.zero;
        mejorNormal = -rayo.direction;
        menorDist   = float.MaxValue;

        int hits = Physics.RaycastNonAlloc(rayo, bufferRaycast3D, alcanceMaximoRayoUI, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits; i++)
        {
            RaycastHit h = bufferRaycast3D[i];
            if (h.collider == null || h.distance < 0.05f || h.distance >= menorDist) continue;

            StatueMenuInteractable busto = h.collider.GetComponentInParent<StatueMenuInteractable>();
            if (busto == null || !busto.isActiveAndEnabled) continue;

            menorDist   = h.distance;
            mejorBusto  = busto;
            mejorPunto  = h.point;
            mejorNormal = h.normal.sqrMagnitude > 0.001f ? h.normal : -rayo.direction;
        }

        return mejorBusto != null;
    }

    private void LimpiarHoverBusto(HandPinchPointer ptr)
    {
        if (ptr.bustoHoverActual != null)
        {
            ptr.bustoHoverActual.SetHovered(false);
            ptr.bustoHoverActual = null;
        }
    }

    private static bool IntersectarPlanoCanvas(Ray rayo, Canvas canvas, out Vector3 punto, out float distancia)
    {
        punto = Vector3.zero;
        distancia = 0f;

        // Solo cara frontal del Canvas (evita disparos desde atrás)
        if (Vector3.Dot(rayo.direction, canvas.transform.forward) <= 0.1f) return false;

        Plane plano = new Plane(-canvas.transform.forward, canvas.transform.position);
        if (plano.Raycast(rayo, out distancia) && distancia > 0f)
        {
            punto = rayo.GetPoint(distancia);
            return true;
        }
        return false;
    }

    private Selectable BuscarControlEnPunto(Canvas canvas, Vector3 puntoMundial)
    {
        Selectable[] controles = canvas.GetComponentsInChildren<Selectable>(false);
        Selectable elegido = null;

        // Recorrer en orden de jerarquía (los últimos dibujados quedan encima visualmente)
        for (int i = 0; i < controles.Length; i++)
        {
            Selectable sel = controles[i];
            if (sel == null || !sel.isActiveAndEnabled || !sel.IsInteractable()) continue;
            if (!(sel is Button || sel is Toggle || sel is Slider)) continue;

            RectTransform rt = sel.GetComponent<RectTransform>();
            if (rt == null) continue;

            Vector3 localPt = rt.InverseTransformPoint(puntoMundial);
            Rect rect = rt.rect;
            rect.xMin -= margenHitCanvas;
            rect.xMax += margenHitCanvas;
            rect.yMin -= margenHitCanvas;
            rect.yMax += margenHitCanvas;

            if (rect.Contains(new Vector2(localPt.x, localPt.y)))
            {
                elegido = sel;
            }
        }

        return elegido;
    }

    private static void ActualizarValorSlider(Slider slider, Vector3 puntoMundial)
    {
        RectTransform rt = slider.fillRect != null && slider.fillRect.parent is RectTransform parentRt
            ? parentRt
            : slider.GetComponent<RectTransform>();
        if (rt == null) return;

        Vector3 localPt = rt.InverseTransformPoint(puntoMundial);
        Rect rect = rt.rect;
        if (rect.width <= 0.001f) return;

        float norm = Mathf.InverseLerp(rect.xMin, rect.xMax, localPt.x);
        if (slider.direction == Slider.Direction.RightToLeft) norm = 1f - norm;
        slider.normalizedValue = Mathf.Clamp01(norm);
    }

    private void ActualizarVisualesRayo(HandPinchPointer ptr, Vector3 origen, Vector3 impacto, Vector3 normalCanvas, Color color, bool pellizcando)
    {
        ptr.lineaRayo.enabled = true;
        ptr.lineaRayo.SetPosition(0, origen);
        ptr.lineaRayo.SetPosition(1, impacto);
        ptr.lineaRayo.startColor = new Color(color.r, color.g, color.b, color.a * 0.45f);
        ptr.lineaRayo.endColor   = color;

        ptr.reticulaObj.SetActive(true);
        ptr.reticulaObj.transform.position = impacto - normalCanvas * 0.003f;
        ptr.reticulaObj.transform.localScale = Vector3.one * (pellizcando ? 0.0085f : 0.0125f);

        ptr.reticulaRenderer.GetPropertyBlock(ptr.reticulaProps);
        ptr.reticulaProps.SetColor("_BaseColor", color);
        ptr.reticulaProps.SetColor("_Color", color);
        ptr.reticulaRenderer.SetPropertyBlock(ptr.reticulaProps);
    }

    private void AsegurarPointerData(HandPinchPointer ptr)
    {
        if (ptr.pointerData == null && EventSystem.current != null)
        {
            ptr.pointerData = new PointerEventData(EventSystem.current);
        }
    }

    private void LimpiarHover(HandPinchPointer ptr)
    {
        if (ptr.hoverActual != null)
        {
            AsegurarPointerData(ptr);
            if (ptr.pointerData != null)
            {
                ExecuteEvents.Execute(ptr.hoverActual.gameObject, ptr.pointerData, ExecuteEvents.pointerExitHandler);
            }
            ptr.hoverActual = null;
        }
    }
}

/// <summary>
/// Provee respuesta visual inmediata (escala en hover y click) y un debounce breve (~0.25s)
/// en botones y toggles para evitar dobles clics accidentales.
/// </summary>
public class VRUIControlFeedback : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    private Selectable selectable;
    private float debounceSeconds = 0.25f;
    private Vector3 escalaBase = Vector3.one;
    private Vector3 escalaObjetivo = Vector3.one;
    private bool inicializado = false;
    private bool enDebounce = false;
    private bool hoverActivo = false;
    private Color disabledColorOriginal;

    public void Configurar(Selectable target, float debounce)
    {
        selectable = target;
        debounceSeconds = Mathf.Max(0f, debounce);

        if (!inicializado)
        {
            escalaBase = transform.localScale;
            escalaObjetivo = escalaBase;
            inicializado = true;

            if (selectable is Button btn)
            {
                btn.onClick.AddListener(IniciarDebounce);
            }
            else if (selectable is Toggle tgl)
            {
                tgl.onValueChanged.AddListener(_ => IniciarDebounce());
            }
        }
    }

    private void Update()
    {
        if (!inicializado) return;

        if (transform.localScale != escalaObjetivo)
        {
            float t = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            transform.localScale = Vector3.Lerp(transform.localScale, escalaObjetivo, t);
            if ((transform.localScale - escalaObjetivo).sqrMagnitude < 0.00001f)
            {
                transform.localScale = escalaObjetivo;
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hoverActivo = true;
        if (selectable != null && selectable.interactable)
        {
            escalaObjetivo = escalaBase * 1.06f;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hoverActivo = false;
        escalaObjetivo = escalaBase;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (selectable != null && selectable.interactable)
        {
            escalaObjetivo = escalaBase * 0.94f;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        escalaObjetivo = hoverActivo ? escalaBase * 1.06f : escalaBase;
    }

    private void IniciarDebounce()
    {
        if (enDebounce || debounceSeconds <= 0f || selectable == null || !isActiveAndEnabled) return;
        StartCoroutine(RutinaDebounce());
    }

    private IEnumerator RutinaDebounce()
    {
        enDebounce = true;

        ColorBlock cb = selectable.colors;
        disabledColorOriginal = cb.disabledColor;
        cb.disabledColor = cb.pressedColor;
        selectable.colors = cb;

        selectable.interactable = false;
        yield return new WaitForSecondsRealtime(debounceSeconds);

        if (selectable != null)
        {
            ColorBlock cbRestore = selectable.colors;
            cbRestore.disabledColor = disabledColorOriginal;
            selectable.colors = cbRestore;
            selectable.interactable = true;
        }

        enDebounce = false;
    }

    private void OnDisable()
    {
        if (inicializado)
        {
            transform.localScale = escalaBase;
            escalaObjetivo = escalaBase;
        }

        if (enDebounce && selectable != null)
        {
            ColorBlock cbRestore = selectable.colors;
            cbRestore.disabledColor = disabledColorOriginal;
            selectable.colors = cbRestore;
            selectable.interactable = true;
            enDebounce = false;
        }
    }
}
