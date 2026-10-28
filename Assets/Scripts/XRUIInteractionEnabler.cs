using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Garantiza que en cualquier escena (ModoLibre, ModoLibreMenu, ModoTerapia, MainMenu)
/// el EventSystem utilice XRUIInputModule (o InputSystemUIInputModule adecuadamente configurado)
/// con soporte completo para mandos Meta Quest (XR Ray / Poke) Y Mouse/Teclado en PC.
/// Además, asegura que los Canvas en World Space tengan TrackedDeviceGraphicRaycaster y GraphicRaycaster.
/// </summary>
public class XRUIInteractionEnabler : MonoBehaviour
{
    private static XRUIInteractionEnabler instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInicializarUIEnabler()
    {
        if (instance == null)
        {
            GameObject runner = new GameObject("[XRUIInteractionEnabler]");
            instance = runner.AddComponent<XRUIInteractionEnabler>();
            DontDestroyOnLoad(runner);
        }

        instance.ConfigurarEscenaActual();
    }

    void OnLevelWasLoaded(int level)
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

        // Si tiene InputSystemUIInputModule y no XRUIInputModule, actualizamos a XRUIInputModule
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
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
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

                // 1. Raycaster para mandos y manos VR
                var trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                if (trackedRaycaster == null)
                {
                    trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                    Debug.Log($"[XRUIInteractionEnabler] TrackedDeviceGraphicRaycaster añadido a Canvas '{canvas.name}'.");
                }

                // 2. Raycaster para ratón en PC Editor
                var graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
                if (graphicRaycaster == null)
                {
                    graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                    Debug.Log($"[XRUIInteractionEnabler] GraphicRaycaster añadido a Canvas '{canvas.name}'.");
                }
            }
        }
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
                Debug.Log("[XRUIInteractionEnabler] PhysicsRaycaster añadido a MainCamera para clics en objetos 3D (teclas de piano).");
            }
        }
    }
}
