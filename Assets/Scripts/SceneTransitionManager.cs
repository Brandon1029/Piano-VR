using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PianoVR
{
    /// <summary>
    /// Gestor reutilizable de transiciones entre escenas con fundido a negro (Fade) en VR y PC.
    /// Funciona de forma totalmente autónoma: persiste entre escenas (DontDestroyOnLoad),
    /// busca dinámicamente la cámara VR (Main Camera) y expone el método público TransitionToScene().
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInicializar()
        {
            if (Instance == null)
            {
                GameObject managerObj = new GameObject("[SceneTransitionManager]");
                Instance = managerObj.AddComponent<SceneTransitionManager>();
                DontDestroyOnLoad(managerObj);
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
            // Aclarado suave inicial al arrancar
            StartCoroutine(FadeRoutine(1f, 0f, fadeDuration));
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
            _fadeImage.color = fadeColor;
            _fadeImage.raycastTarget = false;

            RectTransform rect = _fadeImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            VincularCamaraActual();
        }

        /// <summary>
        /// Vincula el canvas de fundido a la cámara principal activa en la escena VR.
        /// </summary>
        public void VincularCamaraActual()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = FindFirstObjectByType<Camera>();
            }

            if (_fadeCanvas != null && cam != null)
            {
                _fadeCanvas.worldCamera = cam;
            }
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

            StartCoroutine(TransicionRoutine(sceneName));
        }

        private IEnumerator TransicionRoutine(string sceneName)
        {
            _isTransitioning = true;
            Debug.Log($"[SceneTransitionManager] Iniciando transición hacia la escena '{sceneName}'...");

            VincularCamaraActual();

            // 1. Fundido a negro (Fade Out)
            yield return StartCoroutine(FadeRoutine(0f, 1f, fadeDuration));

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

            // 3. Aclarado de negro a transparente (Fade In)
            yield return StartCoroutine(FadeRoutine(1f, 0f, fadeDuration));

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
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                yield return null;
            }

            _canvasGroup.alpha = endAlpha;
        }
    }
}
