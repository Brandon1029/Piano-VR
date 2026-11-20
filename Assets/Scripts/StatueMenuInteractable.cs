// =============================================================================
//  StatueMenuInteractable.cs
//  Piano VR — Menú Principal
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_XR_INTERACTION_TOOLKIT
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#endif

namespace PianoVR.MainMenu
{
    public enum MenuAction
    {
        LoadModoJuego,
        LoadModoMusica,
        ShowSettings
    }

    [RequireComponent(typeof(Collider))]
#if UNITY_XR_INTERACTION_TOOLKIT
    [RequireComponent(typeof(XRSimpleInteractable))]
#endif
    public class StatueMenuInteractable : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        [System.Serializable]
        public class SceneField
        {
            [SerializeField] private Object sceneAsset;
            [SerializeField] private string sceneName = "";

            public string SceneName => sceneName;

            public static implicit operator string(SceneField sceneField)
            {
                return sceneField != null ? sceneField.SceneName : "";
            }
        }

#if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(SceneField))]
        public class SceneFieldPropertyDrawer : PropertyDrawer
        {
            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
            {
                EditorGUI.BeginProperty(position, GUIContent.none, property);
                SerializedProperty sceneAsset = property.FindPropertyRelative("sceneAsset");
                SerializedProperty sceneName = property.FindPropertyRelative("sceneName");

                position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
                if (sceneAsset != null)
                {
                    sceneAsset.objectReferenceValue = EditorGUI.ObjectField(position, sceneAsset.objectReferenceValue, typeof(SceneAsset), false);
                    if (sceneAsset.objectReferenceValue != null)
                    {
                        sceneName.stringValue = (sceneAsset.objectReferenceValue as SceneAsset).name;
                    }
                }
                EditorGUI.EndProperty();
            }
        }
#endif

        [Header("Acción del pedestal")]
        [Tooltip("Qué hace este pedestal al ser seleccionado.")]
        public MenuAction action = MenuAction.LoadModoJuego;

        [Tooltip("Arrastra aquí la escena de destino desde la carpeta Project.")]
        public SceneField escenaDestino;

        [HideInInspector]
        public string sceneToLoad = "ModoLibreMenu";

        [Header("Referencias de UI")]
        [Tooltip("Panel de Ajustes que se muestra/oculta (solo necesario en Busto_Ajustes).")]
        public GameObject settingsPanel;

        [Tooltip("Canvas World Space con la etiqueta del pedestal.")]
        public Canvas labelCanvas;

        [Header("Iluminación")]
        [Tooltip("Spotlight focal que ilumina este pedestal.")]
        public Light spotLight;

        [Tooltip("Intensidad normal del Spotlight.")]
        public float spotLightBaseIntensity = 1f;

        [Tooltip("Intensidad al hacer hover.")]
        public float spotLightHoverIntensity = 3f;

        [Header("Animación de flotación")]
        [Tooltip("Amplitud del movimiento de flotación (metros).")]
        public float floatAmplitude = 0.08f;

        [Tooltip("Velocidad de la animación de flotación.")]
        public float floatSpeed = 1.2f;

        [Tooltip("Cuánto sube el pedestal al hacer hover (metros adicionales).")]
        public float hoverRaiseAmount = 0.15f;

        [Tooltip("Velocidad de transición hacia la posición de hover.")]
        public float hoverTransitionSpeed = 6f;

        [Header("Feedback Visual en Hover")]
        [Tooltip("Multiplicador de escala suave al apuntar al busto (ej. 1.06 = +6%).")]
        public float hoverScaleMultiplier = 1.06f;

        [Tooltip("Color de brillo/emisión sutil al hacer hover sobre el busto.")]
        [ColorUsage(true, true)]
        public Color hoverEmissionColor = new Color(0.20f, 0.75f, 1f, 1f);

        [Tooltip("Intensidad de la emisión en hover.")]
        public float hoverEmissionIntensity = 0.65f;

        [Header("Protección de Inicio")]
        [Tooltip("Segundos iniciales tras cargar la escena donde se ignoran toques directos para evitar activaciones en falso.")]
        public float startupGracePeriod = 0.6f;

        private Vector3 _basePosition;
        private Vector3 _baseScale;
        private bool _isHovered = false;
        private float _currentRaise = 0f;
        private float _currentEmission = 0f;
        private bool _hasTriggered = false;
        private Renderer[] _bustRenderers;
        private MaterialPropertyBlock _propBlock;
        private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

#if UNITY_XR_INTERACTION_TOOLKIT
        private XRSimpleInteractable _xrInteractable;
#endif

        private void Awake()
        {
            _basePosition = transform.position;
            _baseScale = transform.localScale;
            _bustRenderers = GetComponentsInChildren<Renderer>(true);
            _propBlock = new MaterialPropertyBlock();

            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

#if UNITY_XR_INTERACTION_TOOLKIT
            _xrInteractable = GetComponent<XRSimpleInteractable>();
            if (_xrInteractable == null)
                _xrInteractable = gameObject.AddComponent<XRSimpleInteractable>();
#endif
        }

        private void OnEnable()
        {
#if UNITY_XR_INTERACTION_TOOLKIT
            if (_xrInteractable != null)
            {
                _xrInteractable.hoverEntered.AddListener(OnXRHoverEnter);
                _xrInteractable.hoverExited.AddListener(OnXRHoverExit);
                _xrInteractable.selectEntered.AddListener(OnXRSelectEntered);
            }
#endif

            if (spotLight != null)
                spotLight.intensity = spotLightBaseIntensity;

            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            _hasTriggered = false;
        }

        private void OnDisable()
        {
#if UNITY_XR_INTERACTION_TOOLKIT
            if (_xrInteractable != null)
            {
                _xrInteractable.hoverEntered.RemoveListener(OnXRHoverEnter);
                _xrInteractable.hoverExited.RemoveListener(OnXRHoverExit);
                _xrInteractable.selectEntered.RemoveListener(OnXRSelectEntered);
            }
#endif
        }

        private void Update()
        {
            AnimateFloat();
        }

        private void AnimateFloat()
        {
            float targetRaise = _isHovered ? hoverRaiseAmount : 0f;
            _currentRaise = Mathf.Lerp(_currentRaise, targetRaise, Time.deltaTime * hoverTransitionSpeed);

            float sinOffset = Mathf.Sin(Time.time * floatSpeed) * floatAmplitude;

            transform.position = new Vector3(
                _basePosition.x,
                _basePosition.y + sinOffset + _currentRaise,
                _basePosition.z
            );

            // Escala suave (~1.06x) y pulso de emisión en hover
            Vector3 targetScale = _isHovered ? _baseScale * hoverScaleMultiplier : _baseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * hoverTransitionSpeed);

            float targetEmission = _isHovered ? hoverEmissionIntensity : 0f;
            if (Mathf.Abs(_currentEmission - targetEmission) > 0.002f)
            {
                _currentEmission = Mathf.Lerp(_currentEmission, targetEmission, Time.deltaTime * (hoverTransitionSpeed * 1.5f));
                AplicarEmisionBusto(_currentEmission);
            }
        }

        private void AplicarEmisionBusto(float intensidad)
        {
            if (_bustRenderers == null || _propBlock == null) return;
            Color emision = hoverEmissionColor * intensidad;
            for (int i = 0; i < _bustRenderers.Length; i++)
            {
                Renderer r = _bustRenderers[i];
                if (r == null || r is CanvasRenderer) continue;
                r.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(EmissionColorProp, emision);
                r.SetPropertyBlock(_propBlock);
            }
        }

        public void SetHovered(bool hovered)
        {
            if (_isHovered == hovered) return;

            _isHovered = hovered;

            if (spotLight != null)
            {
                spotLight.intensity = hovered ? spotLightHoverIntensity : spotLightBaseIntensity;
            }

            Debug.Log($"[StatueMenu] '{gameObject.name}' hover={hovered}");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            if (Time.timeSinceLevelLoad < startupGracePeriod) return;

            if (EsManoOInteractors(other))
            {
                Debug.Log($"[StatueMenu] '{gameObject.name}' tocado directamente por mano/dedo '{other.name}'");
                SetHovered(true);
                ExecuteAction();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            if (EsManoOInteractors(other))
            {
                SetHovered(false);
            }
        }

        private bool EsManoOInteractors(Collider col)
        {
            if (col == null) return false;
            string n = col.name.ToLower();

            if (n.Contains("floor") || n.Contains("piso") || n.Contains("pedestal") || n.Contains("wall") || n.Contains("busto"))
                return false;

            if (n.Contains("tip") || n.Contains("poke") || n.Contains("finger") || n.Contains("hand") || n.Contains("mano") || n.Contains("dedo"))
                return true;

            if (col.attachedRigidbody != null && (n.Contains("distal") || n.Contains("joint") || n.Contains("bone")))
                return true;

            return false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Debug.Log($"[StatueMenu][PC] OnPointerEnter -> '{gameObject.name}'");
            SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Debug.Log($"[StatueMenu][PC] OnPointerExit -> '{gameObject.name}'");
            SetHovered(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log($"[StatueMenu][PC] OnPointerClick -> '{gameObject.name}' | Acción: {action}");
            ExecuteAction();
        }

#if UNITY_XR_INTERACTION_TOOLKIT
        private void OnXRHoverEnter(HoverEnterEventArgs args)
        {
            Debug.Log($"[StatueMenu][XR] HoverEnter -> '{gameObject.name}' | Interactor: {args.interactorObject}");
            SetHovered(true);
        }

        private void OnXRHoverExit(HoverExitEventArgs args)
        {
            Debug.Log($"[StatueMenu][XR] HoverExit -> '{gameObject.name}'");
            SetHovered(false);
        }

        private void OnXRSelectEntered(SelectEnterEventArgs args)
        {
            Debug.Log($"[StatueMenu][XR] SelectEntered -> '{gameObject.name}' | Acción: {action}");
            ExecuteAction();
        }
#endif

        public void ExecuteAction()
        {
            if (_hasTriggered && action != MenuAction.ShowSettings)
                return;

            string escenaFinal = (escenaDestino != null && !string.IsNullOrEmpty(escenaDestino.SceneName))
                ? escenaDestino.SceneName
                : sceneToLoad;

            switch (action)
            {
                case MenuAction.LoadModoJuego:
                    _hasTriggered = true;
                    string targetGameScene = !string.IsNullOrEmpty(escenaFinal) ? escenaFinal : "ModoLibreMenu";
                    CargarEscenaConFade(targetGameScene);
                    break;

                case MenuAction.LoadModoMusica:
                    _hasTriggered = true;
                    string targetMusicScene = !string.IsNullOrEmpty(escenaFinal) ? escenaFinal : "ModoMusica";
                    if (targetMusicScene == "ModoLibreMenu" && gameObject.name.Contains("Musica"))
                    {
                        targetMusicScene = "ModoMusica";
                    }
                    CargarEscenaConFade(targetMusicScene);
                    break;

                case MenuAction.ShowSettings:
                    if (settingsPanel != null)
                    {
                        bool nuevoEstado = !settingsPanel.activeSelf;
                        settingsPanel.SetActive(nuevoEstado);
                        Debug.Log($"[StatueMenu] Panel de Ajustes -> {(nuevoEstado ? "VISIBLE" : "OCULTO")}");
                    }
                    else
                    {
                        Debug.LogWarning("[StatueMenu] 'settingsPanel' no asignado en el Inspector del Busto_Ajustes.");
                    }
                    break;
            }
        }

        private void CargarEscenaConFade(string targetScene)
        {
            Debug.Log($"[StatueMenu] Cargando escena '{targetScene}' con transición suave a negro...");

            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.TransitionToScene(targetScene);
            }
            else
            {
                GameObject managerObj = new GameObject("[SceneTransitionManager]");
                var manager = managerObj.AddComponent<SceneTransitionManager>();
                manager.TransitionToScene(targetScene);
            }
        }

#if UNITY_EDITOR
        private void OnMouseEnter()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            SetHovered(true);
        }

        private void OnMouseExit()
        {
            SetHovered(false);
        }

        private void OnMouseDown()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            ExecuteAction();
        }
#endif
    }
}