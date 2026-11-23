using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

namespace PianoVR
{
    /// <summary>
    /// Gestor reutilizable de transiciones entre escenas con fundido a negro (Fade) en VR y PC.
    /// Funciona de forma totalmente autónoma: persiste entre escenas (DontDestroyOnLoad),
    /// prioriza la cámara VR del XROrigin (ajustando planeDistance por delante del nearClipPlane
    /// para evitar recortes del frustum) y expone TransitionToScene().
    /// </summary>
    public class SceneTransitionManager : MonoBehaviour
    {
        public static SceneTransitionManager Instance { get; private set; }

        [Header("Configuración del Fundido")]
        [Tooltip("Duración en segundos del fundido a negro y aclarado.")]
        public float fadeDuration = 0.5f;

        [Tooltip("Color del fundido (normalmente negro opaco).")]
        public Color fadeColor = Color.black;

        private Canvas _fadeCanvas;
        private CanvasGroup _canvasGroup;
        private Image _fadeImage;
        private bool _isTransitioning = false;
        private Coroutine _activeFadeCoroutine;

        public bool IsTransitioning => _isTransitioning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInicializar()
        {
            if (Instance == null)
            {
                var existente = FindFirstObjectByType<SceneTransitionManager>();
                if (existente != null)
                {
                    Instance = existente;
                }
                else
                {
                    GameObject managerObj = new GameObject("[SceneTransitionManager]");
                    Instance = managerObj.AddComponent<SceneTransitionManager>();
                    DontDestroyOnLoad(managerObj);
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
            DontDestroyOnLoad(gameObject);

            ConstruirCanvasFade();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            VincularCamaraActual();

            // Aclarado suave inicial al arrancar (solo si no se disparó ya una transición)
            if (!_isTransitioning)
            {
                if (_activeFadeCoroutine != null) StopCoroutine(_activeFadeCoroutine);
                _activeFadeCoroutine = StartCoroutine(FadeRoutine(1f, 0f, fadeDuration));
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            VincularCamaraActual();
        }

        /// <summary>
        /// Construye el Canvas ScreenSpace-Camera y la imagen de fundido optimizada para Meta Quest y VR.
        /// </summary>
        private void ConstruirCanvasFade()
        {
            if (_fadeCanvas != null) return;

            GameObject canvasObj = new GameObject("FadeCanvas");
            canvasObj.transform.SetParent(transform, false);

            _fadeCanvas = canvasObj.AddComponent<Canvas>();
            _fadeCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _fadeCanvas.planeDistance = 0.15f;
            _fadeCanvas.sortingOrder = 32767; // Encima de todo

            _canvasGroup = canvasObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 1f; // Empieza en negro para aclarar suavemente
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            GameObject imageObj = new GameObject("FadeImage");
            imageObj.transform.SetParent(canvasObj.transform, false);

            _fadeImage = imageObj.AddComponent<Image>();
            _fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 1f);
            _fadeImage.raycastTarget = false;

            RectTransform rect = _fadeImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            VincularCamaraActual();
        }

        /// <summary>
        /// Localiza la cámara VR activa priorizando XROrigin.Camera sobre cámaras sueltas en escena,
        /// y ajusta planeDistance siempre por delante de cam.nearClipPlane para que jamás se recorte.
        /// </summary>
        public void VincularCamaraActual()
        {
            Camera cam = ObtenerCamaraVRActiva();

            if (_fadeCanvas != null && cam != null)
            {
                _fadeCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                _fadeCanvas.worldCamera = cam;
                // Garantizar que planeDistance supere siempre el nearClipPlane de la cámara activa
                _fadeCanvas.planeDistance = Mathf.Clamp(cam.nearClipPlane + 0.08f, 0.12f, 0.55f);
                _fadeCanvas.sortingOrder = 32767;
            }
        }

        private Camera ObtenerCamaraVRActiva()
        {
            // 1. Priorizar la cámara del XROrigin activo en la escena VR
            XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                if (xrOrigin.Camera != null && xrOrigin.Camera.isActiveAndEnabled)
                {
                    return xrOrigin.Camera;
                }

                Camera[] camsRig = xrOrigin.GetComponentsInChildren<Camera>(false);
                for (int i = 0; i < camsRig.Length; i++)
                {
                    if (camsRig[i] != null && camsRig[i].isActiveAndEnabled)
                    {
                        return camsRig[i];
                    }
                }
            }

            // 2. Respaldo: Camera.main activa
            if (Camera.main != null && Camera.main.isActiveAndEnabled)
            {
                return Camera.main;
            }

            // 3. Respaldo final: cualquier cámara activa en la escena
            Camera[] todas = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < todas.Length; i++)
            {
                if (todas[i] != null && todas[i].isActiveAndEnabled)
                {
                    return todas[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Método público para iniciar la transición hacia una nueva escena con fundido a negro.
        /// Conectable desde el Inspector en eventos de botones UI y scripts interactuables.
        /// </summary>
        /// <param name="sceneName">Nombre de la escena a cargar.</param>
        public void TransitionToScene(string sceneName)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[SceneTransitionManager] Transición ignorada: ya hay una transición en curso hacia '{sceneName}'.");
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[SceneTransitionManager] El nombre de la escena especificado está vacío.");
                return;
            }

            if (_activeFadeCoroutine != null)
            {
                StopCoroutine(_activeFadeCoroutine);
                _activeFadeCoroutine = null;
            }

            StartCoroutine(TransicionRoutine(sceneName));
        }

        private IEnumerator TransicionRoutine(string sceneName)
        {
            _isTransitioning = true;
            Debug.Log($"[SceneTransitionManager] Iniciando transición hacia la escena '{sceneName}'...");

            if (_fadeCanvas == null)
            {
                ConstruirCanvasFade();
            }

            if (_fadeImage != null)
            {
                _fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 1f);
            }

            VincularCamaraActual();

            // 1. Fundido a negro (Fade Out)
            _activeFadeCoroutine = StartCoroutine(FadeRoutine(0f, 1f, fadeDuration));
            yield return _activeFadeCoroutine;
            _activeFadeCoroutine = null;

            // 2. Carga asíncrona de la nueva escena
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            if (op != null)
            {
                while (!op.isDone)
                {
                    yield return null;
                }
            }

            VincularCamaraActual();
            yield return null; // Espera un fotograma para que se asienten los objetos de la nueva escena
            VincularCamaraActual();

            // 3. Aclarado de negro a transparente (Fade In)
            _activeFadeCoroutine = StartCoroutine(FadeRoutine(1f, 0f, fadeDuration));
            yield return _activeFadeCoroutine;
            _activeFadeCoroutine = null;

            _isTransitioning = false;
            Debug.Log($"[SceneTransitionManager] Transición a '{sceneName}' completada exitosamente.");
        }

        private IEnumerator FadeRoutine(float startAlpha, float endAlpha, float duration)
        {
            if (_canvasGroup == null) yield break;

            float elapsed = 0f;
            _canvasGroup.alpha = startAlpha;

            while (elapsed < duration)
            {
                if (_fadeCanvas != null && _fadeCanvas.worldCamera == null)
                {
                    VincularCamaraActual();
                }

                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, duration));
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                yield return null;
            }

            _canvasGroup.alpha = endAlpha;
        }
    }
}
