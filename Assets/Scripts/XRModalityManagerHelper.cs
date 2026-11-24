using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace PianoVR
{
    /// <summary>
    /// Gestiona de manera centralizada y persistente (DontDestroyOnLoad):
    /// 1. Alternancia automática entre Hand Tracking y Mandos Físicos (Meta Quest).
    ///    - Si se usan Manos: apaga por completo los mandos (mallas y rayos).
    ///    - Si se toman los Mandos: apaga mallas/colisionadores de manos y activa mandos con raycast limpio.
    /// 2. Eliminación de líneas azules de teleport, ruedas radiales (Affordance Callouts) y HUDs de debug.
    /// 3. Supresión de colisionadores físicos en los mandos para evitar toques accidentales en el piano.
    /// 4. Verificación de UI: XRUIInputModule en EventSystem y TrackedDeviceGraphicRaycaster en Canvas.
    /// </summary>
    public class XRModalityManagerHelper : MonoBehaviour
    {
        public static XRModalityManagerHelper Instance { get; private set; }

        private static readonly List<XRHandSubsystem> s_HandSubsystems = new List<XRHandSubsystem>();

        private readonly List<GameObject> _handObjects = new List<GameObject>();
        private readonly List<GameObject> _controllerObjects = new List<GameObject>();
        private readonly List<XRRayInteractor> _controllerRayInteractors = new List<XRRayInteractor>();

        private bool _lastHandsTracked = false;
        private bool _isInitialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInicializar()
        {
            if (Instance == null)
            {
                GameObject runner = new GameObject("[XRModalityManagerHelper]");
                Instance = runner.AddComponent<XRModalityManagerHelper>();
                DontDestroyOnLoad(runner);
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
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            AuditarYConfigurarEscena();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AuditarYConfigurarEscena();
        }

        public void AuditarYConfigurarEscena()
        {
            LocalizarReferenciasRigs();
            LimpiarTeleportYCallouts();
            DesactivarColisionadoresFisicosDeMandos();
            ConfigurarUIEventSystemYCanvases();
            _isInitialized = true;
        }

        private void LocalizarReferenciasRigs()
        {
            _handObjects.Clear();
            _controllerObjects.Clear();
            _controllerRayInteractors.Clear();

            Transform[] todos = FindObjectsByType<Transform>(FindObjectsSortMode.None);

            foreach (var t in todos)
            {
                if (t == null) continue;
                string n = t.name.ToLower();

                // Identificar mallas y jerarquías de manos virtuales (XR Hands / Hand Tracking)
                if (n.Contains("lefthandquestvisual") || n.Contains("righthandquestvisual") ||
                    n.Contains("left hand visual") || n.Contains("right hand visual") ||
                    n.Contains("left hand tracking") || n.Contains("right hand tracking") ||
                    (n.Contains("hand visualizer") && !n.Contains("controller")))
                {
                    if (!_handObjects.Contains(t.gameObject))
                    {
                        _handObjects.Add(t.gameObject);
                    }
                }

                // Identificar jerarquías de mandos físicos (Controllers)
                if (n.Contains("left controller") || n.Contains("right controller") ||
                    n.Contains("leftbasecontroller") || n.Contains("rightbasecontroller"))
                {
                    if (!_controllerObjects.Contains(t.gameObject))
                    {
                        _controllerObjects.Add(t.gameObject);
                    }

                    // Buscar los Ray Interactors de UI
                    var rays = t.GetComponentsInChildren<XRRayInteractor>(true);
                    foreach (var r in rays)
                    {
                        string rayName = r.name.ToLower();
                        if (!rayName.Contains("teleport") && !_controllerRayInteractors.Contains(r))
                        {
                            _controllerRayInteractors.Add(r);
                        }
                    }
                }
            }

            Debug.Log($"[XRModalityManagerHelper] Rigs localizados: {_handObjects.Count} objetos de manos, {_controllerObjects.Count} mandos, {_controllerRayInteractors.Count} rayos de interacción.");
        }

        /// <summary>
        /// Desactiva permanentemente líneas de teleportación, ruedas radiales / tutoriales
        /// y cualquier retícula/cursor o esfera blanca (Pinch Visual / Pinch_Pointer_LOD0 / Gaze / Reticle).
        /// </summary>
        private void LimpiarTeleportYCallouts()
        {
            Transform[] todos = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in todos)
            {
                if (t == null) continue;
                string n = t.name.ToLowerInvariant();

                // 1. Eliminar ruedas radiales / callouts
                if (n.Contains("affordance callouts") || n.Contains("callout"))
                {
                    t.gameObject.SetActive(false);
                }

                // 2. Desactivar interactores y líneas de teleportación
                if (n.Contains("teleport"))
                {
                    t.gameObject.SetActive(false);
                }

                // 3. Desactivar cualquier canvas de diagnóstico o debug HUD
                if (n.Contains("diagnostic") || n.Contains("debug hud") || n.Contains("debug canvas"))
                {
                    t.gameObject.SetActive(false);
                }

                // 4. Eliminar permanentemente la "bolita blanca" (Pinch Visual, Pinch_Pointer_LOD0, Poke Point, Gaze, Torus Cursor)
                if (n == "pinch visual" ||
                    n == "pinch_pointer_lod0" ||
                    n == "poke point" ||
                    n == "gaze interactor" ||
                    n == "gaze stabilized" ||
                    n == "torus cursor" ||
                    n == "blink visuals" ||
                    n == "handinteractoraffordances" ||
                    n == "handpokeinteractoraffordances")
                {
                    Renderer[] rends = t.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rends.Length; i++)
                    {
                        if (rends[i] != null) rends[i].enabled = false;
                    }
                    t.gameObject.SetActive(false);
                }
            }

            // 5. Limpiar referencias de Reticle en todos los XRInteractorLineVisual y XRGazeInteractor
            XRInteractorLineVisual[] lineVisuals = FindObjectsByType<XRInteractorLineVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var lv in lineVisuals)
            {
                if (lv == null) continue;
                if (lv.reticle != null) lv.reticle.SetActive(false);
                if (lv.blockedReticle != null) lv.blockedReticle.SetActive(false);
                lv.reticle = null;
                lv.blockedReticle = null;
            }

            XRGazeInteractor[] gazeInteractors = FindObjectsByType<XRGazeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var gaze in gazeInteractors)
            {
                if (gaze == null) continue;
                gaze.enabled = false;
                gaze.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Elimina o desactiva colisionadores físicos no deseados en los mandos físicos
        /// para que los mandos NUNCA toquen las teclas por choque físico.
        /// </summary>
        private void DesactivarColisionadoresFisicosDeMandos()
        {
            foreach (var ctrlObj in _controllerObjects)
            {
                if (ctrlObj == null) continue;

                Collider[] colliders = ctrlObj.GetComponentsInChildren<Collider>(true);
                foreach (var col in colliders)
                {
                    if (col == null) continue;

                    string colName = col.name.ToLower();
                    // Si no es un Poke Interactor explícito, apagar el colisionador físico
                    if (!colName.Contains("poke"))
                    {
                        col.enabled = false;
                        Debug.Log($"[XRModalityManagerHelper] Desactivado colisionador físico '{col.name}' en mando para evitar colisiones con el piano.");
                    }
                }
            }
        }

        /// <summary>
        /// Asegura que el EventSystem use XRUIInputModule y que los Canvas tengan TrackedDeviceGraphicRaycaster.
        /// </summary>
        private void ConfigurarUIEventSystemYCanvases()
        {
            EventSystem es = FindFirstObjectByType<EventSystem>();
            if (es != null)
            {
                var xrui = es.GetComponent<XRUIInputModule>();
                if (xrui == null)
                {
                    xrui = es.gameObject.AddComponent<XRUIInputModule>();
                }
                xrui.enableXRInput = true;
                xrui.enableMouseInput = true;
                xrui.enableTouchInput = true;
            }

            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Camera mainCam = Camera.main;

            foreach (var c in canvases)
            {
                if (c == null) continue;

                if (c.renderMode == RenderMode.WorldSpace)
                {
                    if (c.worldCamera == null && mainCam != null)
                    {
                        c.worldCamera = mainCam;
                    }

                    var trackedRaycaster = c.GetComponent<TrackedDeviceGraphicRaycaster>();
                    if (trackedRaycaster == null)
                    {
                        c.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                    }

                    var graphicRaycaster = c.GetComponent<GraphicRaycaster>();
                    if (graphicRaycaster == null)
                    {
                        c.gameObject.AddComponent<GraphicRaycaster>();
                    }
                }
            }
        }

        private void Update()
        {
            if (!_isInitialized) return;
            ActualizarAlternanciaModos();
        }

        /// <summary>
        /// Alternancia dinámica entre Manos y Mandos según el subsistema de XR Hands y dispositivos activos.
        /// </summary>
        private void ActualizarAlternanciaModos()
        {
            bool manosRastreadas = VerificarManosRastreadas();

            // Si cambió el estado de detección o en el primer frame
            if (manosRastreadas != _lastHandsTracked)
            {
                _lastHandsTracked = manosRastreadas;
                AplicarModoEntrada(manosRastreadas);
            }
        }

        private bool VerificarManosRastreadas()
        {
            s_HandSubsystems.Clear();
            SubsystemManager.GetSubsystems(s_HandSubsystems);

            foreach (var sub in s_HandSubsystems)
            {
                if (sub != null && sub.running)
                {
                    if (sub.leftHand.isTracked || sub.rightHand.isTracked)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void AplicarModoEntrada(bool manosActivas)
        {
            if (manosActivas)
            {
                Debug.Log("[XRModalityManagerHelper] >> Modo Hand Tracking ACTIVO. Apagando mandos y activando manos virtuales.");

                // 1. Apagar por completo mandos (mallas y rayos)
                foreach (var ctrl in _controllerObjects)
                {
                    if (ctrl != null) ctrl.SetActive(false);
                }

                foreach (var ray in _controllerRayInteractors)
                {
                    if (ray != null) ray.gameObject.SetActive(false);
                }

                // 2. Activar mallas y colisionadores de manos
                foreach (var hand in _handObjects)
                {
                    if (hand != null) hand.SetActive(true);
                }
            }
            else
            {
                Debug.Log("[XRModalityManagerHelper] >> Modo Mandos Físicos ACTIVO. Apagando mallas de manos y activando mandos con raycast limpio.");

                // 1. Apagar mallas y colisionadores de manos
                foreach (var hand in _handObjects)
                {
                    if (hand != null) hand.SetActive(false);
                }

                // 2. Activar mandos físicos y sus rayos limpios
                foreach (var ctrl in _controllerObjects)
                {
                    if (ctrl != null) ctrl.SetActive(true);
                }

                foreach (var ray in _controllerRayInteractors)
                {
                    if (ray != null) ray.gameObject.SetActive(true);
                }
            }
        }
    }
}
