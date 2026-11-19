using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Garantiza que en cualquier escena el EventSystem utilice XRUIInputModule con soporte
/// completo para Meta Quest (Hand Tracking + mandos XR) y PC, y mejora automáticamente
/// la ergonomía, área táctil, feedback visual (hover/click) y debounce (~0.25s) de los
/// controles UI en World Space.
/// </summary>
public class XRUIInteractionEnabler : MonoBehaviour
{
    public static XRUIInteractionEnabler Instance { get; private set; }

    [Header("Ergonomía y Respuesta Táctil UI")]
    [Tooltip("Tiempo de enfriamiento (debounce) en segundos para evitar dobles clics accidentales con la mano.")]
    public float debounceBotones = 0.25f;

    [Tooltip("Tamaño mínimo táctil en unidades de Canvas para botones y controles pequeños.")]
    public float tamanoMinimoTactil = 64f;

    [Tooltip("Margen extra (en unidades de Canvas) para el BoxCollider de botones y sliders.")]
    public float margenExtraCollider = 14f;

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
        ConfigurarEscenaActual();
    }

    public void ConfigurarEscenaActual()
    {
        ConfigurarEventSystem();
        ConfigurarCanvases();
        ConfigurarCamara();
    }

    private void ConfigurarEventSystem()
    {
        EventSystem es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            es = esObj.AddComponent<EventSystem>();
            Debug.Log("[XRUIInteractionEnabler] EventSystem no encontrado en escena; se ha creado uno automáticamente.");
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
            Debug.Log("[XRUIInteractionEnabler] Se ha añadido y configurado XRUIInputModule en el EventSystem.");
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

    private void ConfigurarCanvases()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Camera mainCam = Camera.main;

        foreach (Canvas canvas in canvases)
        {
            if (canvas == null) continue;

            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                if (canvas.worldCamera == null && mainCam != null)
                {
                    canvas.worldCamera = mainCam;
                }

                // 1. Raycaster para mandos y manos VR (ignorar cara trasera para evitar clics accidentales)
                var trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                if (trackedRaycaster == null)
                {
                    trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                    Debug.Log($"[XRUIInteractionEnabler] TrackedDeviceGraphicRaycaster añadido a Canvas '{canvas.name}'.");
                }
                trackedRaycaster.ignoreReversedGraphics = true;

                // 2. Raycaster para ratón en PC Editor
                var graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
                if (graphicRaycaster == null)
                {
                    graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                }
                graphicRaycaster.ignoreReversedGraphics = true;

                // 3. Mejorar área táctil, feedback visual y debounce en todos los controles hijos
                MejorarControlesEnJerarquia(canvas.transform);
            }
        }
    }

    /// <summary>
    /// Aplica ergonomía táctil, feedback visual de hover/click y debounce a todos los
    /// botones, toggles y sliders dentro de una jerarquía (útil también para elementos instanciados dinámicamente).
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

        // 1. Asegurar feedback de color claro en hover y click
        ColorBlock cb = sel.colors;
        cb.highlightedColor = new Color(0.75f, 0.95f, 1f, 1f);
        cb.pressedColor     = new Color(0.45f, 0.80f, 1f, 1f);
        cb.selectedColor    = new Color(0.85f, 0.96f, 1f, 1f);
        cb.colorMultiplier  = Mathf.Max(cb.colorMultiplier, 1.1f);
        cb.fadeDuration     = 0.08f;
        sel.colors = cb;

        // 2. Ampliar área táctil y añadir BoxCollider cómodo para Hand Tracking / Raycast
        RectTransform rt = sel.GetComponent<RectTransform>();
        if (rt != null)
        {
            float ancho = Mathf.Max(rt.rect.width,  tamanoMinimoTactil);
            float alto  = Mathf.Max(rt.rect.height, tamanoMinimoTactil);

            // En botones pequeños de tamaño fijo (ej. +/- de octava 50x50), ampliar visual y táctilmente
            if (sel is Button && rt.sizeDelta.x > 0f && rt.sizeDelta.y > 0f &&
                (rt.sizeDelta.x < tamanoMinimoTactil || rt.sizeDelta.y < tamanoMinimoTactil))
            {
                rt.sizeDelta = new Vector2(
                    Mathf.Max(rt.sizeDelta.x, tamanoMinimoTactil),
                    Mathf.Max(rt.sizeDelta.y, tamanoMinimoTactil)
                );
                ancho = rt.sizeDelta.x;
                alto  = rt.sizeDelta.y;
            }

            // En sliders, ampliar el área del handle para que sea fácil arrastrarlo con la mano
            if (sel is Slider slider && slider.handleRect != null)
            {
                Vector2 hSize = slider.handleRect.sizeDelta;
                if (hSize.x < 44f || hSize.y < 44f)
                {
                    slider.handleRect.sizeDelta = new Vector2(Mathf.Max(hSize.x, 44f), Mathf.Max(hSize.y, 44f));
                }
            }

            BoxCollider col = sel.GetComponent<BoxCollider>();
            if (col == null)
            {
                col = sel.gameObject.AddComponent<BoxCollider>();
            }
            col.isTrigger = true;
            col.size   = new Vector3(ancho + margenExtraCollider, alto + margenExtraCollider, 18f);
            col.center = new Vector3(rt.rect.center.x, rt.rect.center.y, 0f);
        }

        // 3. Añadir feedback visual de escala (hover/click) y debounce de 0.25s
        var feedback = sel.GetComponent<VRUIControlFeedback>();
        if (feedback == null)
        {
            feedback = sel.gameObject.AddComponent<VRUIControlFeedback>();
        }
        feedback.Configurar(sel, debounceBotones);
    }

    private void ConfigurarCamara()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var physRaycaster = mainCam.GetComponent<PhysicsRaycaster>();
            if (physRaycaster == null)
            {
                physRaycaster = mainCam.gameObject.AddComponent<PhysicsRaycaster>();
                Debug.Log("[XRUIInteractionEnabler] PhysicsRaycaster añadido a MainCamera para clics en objetos 3D.");
            }
        }
    }
}

/// <summary>
/// Provee respuesta visual inmediata (escala en hover y click) y un debounce breve (~0.25s)
/// en botones y toggles para evitar dobles clics accidentales con Hand Tracking en Meta Quest.
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

        // Mantener apariencia activa/presionada durante el breve enfriamiento para evitar parpadeo gris
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
