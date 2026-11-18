// =============================================================================
//  CreateNewMainMenu.cs
//  Piano VR — Editor Script
//
//  Genera la escena MainMenuScene.unity en Assets/Scenes/ con toda la
//  jerarquía de objetos, UI World Space, EventSystem XRI, iluminación y
//  los tres pedestales interactivos en semicírculo.
//
//  Menú: Tools ▸ Generar Nuevo Menú Principal
// =============================================================================

#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// XRI Editor utilities (present if com.unity.xr.interaction.toolkit is installed)
#if UNITY_XR_INTERACTION_TOOLKIT
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
#endif

namespace PianoVR.Editor
{
    public static class CreateNewMainMenu
    {
        // ─── Rutas ────────────────────────────────────────────────────────────
        private const string SCENE_PATH      = "Assets/Scenes/MainMenuScene.unity";
        private const string XR_ORIGIN_GUID  = "77e7c27b2c5525e4aa8cc9f99d654486"; // Complete XR Origin Set Up Hands Variant
        private const string XRUI_MOD_GUID   = "ab68ce6587aab0146b8dabefbd806791"; // XRUIInputModule
        private const string XRI_ACTIONS_GUID = "c348712bda248c246b8c49b3db54643f"; // XRI Default Input Actions

        // Nombres de escenas a cargar
        private const string SCENE_MODO_JUEGO   = "ModoTerapia";
        private const string SCENE_MODO_MUSICA  = "ModoLibreMenu";

        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Tools/Generar Nuevo Menú Principal")]
        public static void GenerateMainMenuScene()
        {
            // 1. Confirmar con el usuario si la escena ya existe
            if (File.Exists(Path.Combine(Application.dataPath.Replace("Assets", ""), SCENE_PATH)))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Escena ya existe",
                    $"Ya existe '{SCENE_PATH}'.\n¿Deseas sobreescribirla?",
                    "Sí, sobreescribir",
                    "Cancelar");

                if (!overwrite)
                {
                    Debug.Log("[CreateMainMenu] Operación cancelada por el usuario.");
                    return;
                }
            }

            // 2. Crear escena nueva vacía
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Debug.Log("[CreateMainMenu] Escena nueva creada. Construyendo jerarquía…");

            // 3. Construir todos los elementos
            SetupLighting();
            SetupFloor();
            GameObject settingsPanel = BuildSettingsPanel();
            BuildPedestals(settingsPanel);
            SetupCamera();
            SetupEventSystem();

            // 4. Ajustes de escena (RenderSettings)
            RenderSettings.ambientMode    = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight   = new Color(0.18f, 0.16f, 0.22f); // azul noche suave
            RenderSettings.fogColor       = new Color(0.08f, 0.07f, 0.12f);
            RenderSettings.fog            = false;

            // 5. Guardar la escena
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.Refresh();

            // 6. Añadir a Build Settings si aún no está
            AddSceneToBuildSettings(SCENE_PATH);

            Debug.Log($"[CreateMainMenu] ✅ Escena guardada en '{SCENE_PATH}' y añadida a Build Settings.");
            EditorUtility.DisplayDialog("Éxito", $"MainMenuScene creada en:\n{SCENE_PATH}", "OK");
        }

        // ─────────────────────────────────────────────────────────────────────
        // ILUMINACIÓN
        // ─────────────────────────────────────────────────────────────────────

        private static void SetupLighting()
        {
            // Directional Light principal (luz de sala / conservatorio)
            var dirGO   = new GameObject("Directional Light");
            var dirLight = dirGO.AddComponent<Light>();
            dirLight.type      = LightType.Directional;
            dirLight.color     = new Color(1f, 0.95f, 0.88f);   // luz cálida
            dirLight.intensity = 0.8f;
            dirLight.shadows   = LightShadows.Soft;
            dirGO.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            // Point Light ambiental 1 — izquierda del salón
            CreatePointLight("PointLight_Left",  new Vector3(-4f, 3f, 0f),
                             new Color(1f, 0.9f, 0.75f), 3f, 8f);
            // Point Light ambiental 2 — derecha del salón
            CreatePointLight("PointLight_Right", new Vector3(4f, 3f, 0f),
                             new Color(1f, 0.9f, 0.75f), 3f, 8f);
            // Point Light de relleno — fondo
            CreatePointLight("PointLight_Back",  new Vector3(0f, 3f, 5f),
                             new Color(0.7f, 0.8f, 1f),  1.5f, 6f);

            Debug.Log("[CreateMainMenu] Iluminación configurada.");
        }

        private static GameObject CreatePointLight(string name, Vector3 pos, Color color,
                                                    float intensity, float range)
        {
            var go    = new GameObject(name);
            var light = go.AddComponent<Light>();
            light.type      = LightType.Point;
            light.color     = color;
            light.intensity = intensity;
            light.range     = range;
            light.shadows   = LightShadows.None;
            go.transform.position = pos;
            return go;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PISO
        // ─────────────────────────────────────────────────────────────────────

        private static void SetupFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position   = Vector3.zero;
            floor.transform.localScale = new Vector3(3f, 1f, 3f); // 30×30 m

            // Material neutral oscuro (parquet/mármol simulado)
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ??
                                   Shader.Find("Standard"));
            mat.color = new Color(0.22f, 0.18f, 0.14f);  // madera oscura
            floor.GetComponent<Renderer>().material = mat;

            // Quitar el collider de Trigger del plano (es suelo sólido)
            var col = floor.GetComponent<MeshCollider>();
            if (col != null) col.convex = false;

            Debug.Log("[CreateMainMenu] Piso creado.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // PANEL DE AJUSTES (oculto por defecto)
        // ─────────────────────────────────────────────────────────────────────

        private static GameObject BuildSettingsPanel()
        {
            // Canvas World Space flotante — centrado arriba, entre los pedestales
            var canvasGO = new GameObject("SettingsPanel_Canvas");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null; // se asignará en runtime
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 300f);
            canvasGO.transform.position = new Vector3(0f, 2.2f, 3.2f);
            canvasGO.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            canvasGO.transform.localScale = Vector3.one * 0.004f;

            // Fondo del panel
            var bg = CreateUIImage(canvasGO.transform, "Background",
                                   new Color(0.05f, 0.05f, 0.12f, 0.92f),
                                   Vector2.zero, new Vector2(400f, 300f));

            // Título
            CreateTMPLabel(canvasGO.transform, "Title_Ajustes",
                           "⚙ Ajustes",
                           new Vector2(0f, 100f), new Vector2(360f, 50f),
                           28, Color.white, FontStyles.Bold);

            // Label Volumen
            CreateTMPLabel(canvasGO.transform, "Label_Volume",
                           "Volumen General",
                           new Vector2(0f, 40f), new Vector2(300f, 35f),
                           20, new Color(0.85f, 0.85f, 0.85f));

            // Slider de Volumen
            var sliderGO = new GameObject("Slider_Volume");
            sliderGO.transform.SetParent(canvasGO.transform, false);
            var sliderRT = sliderGO.AddComponent<RectTransform>();
            sliderRT.sizeDelta        = new Vector2(320f, 30f);
            sliderRT.anchoredPosition = new Vector2(0f, -10f);

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value    = 0.8f;
            slider.wholeNumbers = false;

            // Construir partes del Slider manualmente
            BuildSliderVisuals(sliderGO, slider);

            // Botón Cerrar
            CreateCloseButton(canvasGO.transform, canvasGO);

            // OCULTO POR DEFECTO
            canvasGO.SetActive(false);

            Debug.Log("[CreateMainMenu] Panel de Ajustes creado (oculto).");
            return canvasGO;
        }

        private static void BuildSliderVisuals(GameObject sliderGO, Slider slider)
        {
            // Background
            var bg = CreateUIImage(sliderGO.transform, "Background",
                                   new Color(0.15f, 0.15f, 0.25f), Vector2.zero,
                                   new Vector2(320f, 10f));

            // Fill Area
            var fillAreaGO = new GameObject("Fill Area");
            fillAreaGO.transform.SetParent(sliderGO.transform, false);
            var fillAreaRT = fillAreaGO.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRT.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRT.offsetMin = new Vector2(5f, 0f);
            fillAreaRT.offsetMax = new Vector2(-15f, 0f);

            var fillGO = CreateUIImage(fillAreaGO.transform, "Fill",
                                       new Color(0.4f, 0.7f, 1f), Vector2.zero,
                                       Vector2.zero);
            fillGO.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            fillGO.GetComponent<RectTransform>().anchorMax = Vector2.one;
            slider.fillRect = fillGO.GetComponent<RectTransform>();

            // Handle Slide Area
            var handleAreaGO = new GameObject("Handle Slide Area");
            handleAreaGO.transform.SetParent(sliderGO.transform, false);
            var handleAreaRT = handleAreaGO.AddComponent<RectTransform>();
            handleAreaRT.anchorMin = Vector2.zero;
            handleAreaRT.anchorMax = Vector2.one;
            handleAreaRT.offsetMin = new Vector2(10f, 0f);
            handleAreaRT.offsetMax = new Vector2(-10f, 0f);

            var handleGO = CreateUIImage(handleAreaGO.transform, "Handle",
                                          Color.white, Vector2.zero,
                                          new Vector2(20f, 20f));
            slider.handleRect = handleGO.GetComponent<RectTransform>();
        }

        private static void CreateCloseButton(Transform parent, GameObject panelToHide)
        {
            var btnGO = new GameObject("Button_Close");
            btnGO.transform.SetParent(parent, false);
            var rt = btnGO.AddComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(120f, 40f);
            rt.anchoredPosition = new Vector2(0f, -110f);

            var img = btnGO.AddComponent<Image>();
            img.color = new Color(0.6f, 0.15f, 0.15f);

            var btn = btnGO.AddComponent<Button>();
            // Referencia al panel se asigna en runtime por StatueMenuInteractable
            // Solo añadimos listener de debug
            btn.onClick.AddListener(() =>
            {
                Debug.Log("[Ajustes] Botón Cerrar pulsado.");
                panelToHide.SetActive(false);
            });

            CreateTMPLabel(btnGO.transform, "Label_Close",
                           "Cerrar",
                           Vector2.zero, new Vector2(120f, 40f),
                           18, Color.white);
        }

        // ─────────────────────────────────────────────────────────────────────
        // PEDESTALES
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildPedestals(GameObject settingsPanel)
        {
            // Semicírculo frente a la cámara (que está en z≈0, mirando hacia +z)
            // Radio ≈ 2.5m, ángulo 0° = frente, ±40° = lados
            var pedestalDefs = new[]
            {
                new PedestalDef
                {
                    name        = "Busto_Juego",
                    label       = "Modo Juego",
                    color       = new Color(0.15f, 0.35f, 0.65f),   // azul
                    position    = AngleToPos(-40f, 2.5f, 1f),        // izquierda
                    action      = "LoadModoJuego",
                    sceneToLoad = SCENE_MODO_JUEGO
                },
                new PedestalDef
                {
                    name        = "Busto_Musica",
                    label       = "Modo Música",
                    color       = new Color(0.15f, 0.5f, 0.25f),    // verde
                    position    = AngleToPos(0f, 2.5f, 1f),          // centro
                    action      = "LoadModoMusica",
                    sceneToLoad = SCENE_MODO_MUSICA
                },
                new PedestalDef
                {
                    name        = "Busto_Ajustes",
                    label       = "Ajustes",
                    color       = new Color(0.5f, 0.35f, 0.08f),    // dorado
                    position    = AngleToPos(40f, 2.5f, 1f),         // derecha
                    action      = "ShowSettings",
                    sceneToLoad = ""
                }
            };

            foreach (var def in pedestalDefs)
                BuildOnePedestal(def, settingsPanel);

            Debug.Log("[CreateMainMenu] 3 pedestales creados en semicírculo.");
        }

        private struct PedestalDef
        {
            public string name;
            public string label;
            public Color  color;
            public Vector3 position;
            public string action;
            public string sceneToLoad;
        }

        /// <summary>
        /// Convierte un ángulo horizontal (grados) y radio al punto del semicírculo.
        /// La cámara mira hacia +Z, el jugador está en el origen.
        /// </summary>
        private static Vector3 AngleToPos(float angleDeg, float radius, float baseY)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Sin(rad) * radius,
                baseY,
                Mathf.Cos(rad) * radius
            );
        }

        private static void BuildOnePedestal(PedestalDef def, GameObject settingsPanel)
        {
            // — Pedestal (Cubo) —
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = def.name;
            pedestal.transform.position   = def.position;
            pedestal.transform.localScale = new Vector3(0.5f, 1.0f, 0.5f);

            // Collider como Trigger para XRI
            var col = pedestal.GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true;

            // Material del pedestal
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ??
                                   Shader.Find("Standard"));
            mat.color = def.color;
            pedestal.GetComponent<Renderer>().material = mat;

            // — Busto (pequeña esfera encima como placeholder) —
            var bust = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bust.name = "Bust_Model";
            bust.transform.SetParent(pedestal.transform, false);
            bust.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            bust.transform.localScale    = new Vector3(0.5f, 0.5f, 0.5f);
            var bustCol = bust.GetComponent<SphereCollider>();
            if (bustCol != null) bustCol.isTrigger = true;
            var bustMat = new Material(mat);
            bustMat.color = Color.white;
            bust.GetComponent<Renderer>().material = bustMat;

            // — Spotlight focal —
            var spotGO = new GameObject("Spotlight");
            spotGO.transform.SetParent(pedestal.transform, false);
            spotGO.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            spotGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var spot = spotGO.AddComponent<Light>();
            spot.type      = LightType.Spot;
            spot.color     = Color.white;
            spot.intensity = 1f;
            spot.range     = 6f;
            spot.spotAngle = 35f;
            spot.shadows   = LightShadows.None;

            // — Canvas de etiqueta (World Space) —
            var labelCanvas = BuildLabelCanvas(pedestal.transform, def.label);

            // — StatueMenuInteractable —
            var smi = pedestal.AddComponent<PianoVR.MainMenu.StatueMenuInteractable>();
            smi.spotLight          = spot;
            smi.spotLightBaseIntensity  = 1f;
            smi.spotLightHoverIntensity = 3.5f;
            smi.labelCanvas        = labelCanvas;
            smi.floatAmplitude     = 0.07f;
            smi.floatSpeed         = 1.1f + UnityEngine.Random.Range(-0.15f, 0.15f); // leve variación
            smi.hoverRaiseAmount   = 0.12f;

            // Asignar la acción
            switch (def.action)
            {
                case "LoadModoJuego":
                    smi.action      = PianoVR.MainMenu.MenuAction.LoadModoJuego;
                    smi.sceneToLoad = def.sceneToLoad;
                    break;
                case "LoadModoMusica":
                    smi.action      = PianoVR.MainMenu.MenuAction.LoadModoMusica;
                    smi.sceneToLoad = def.sceneToLoad;
                    break;
                case "ShowSettings":
                    smi.action        = PianoVR.MainMenu.MenuAction.ShowSettings;
                    smi.settingsPanel = settingsPanel;
                    break;
            }

            // Girar el pedestal para mirar hacia la cámara (origen)
            pedestal.transform.LookAt(new Vector3(0f, def.position.y, 0f));
            pedestal.transform.Rotate(0f, 180f, 0f);  // invertir para que la "cara" mire al jugador
        }

        private static Canvas BuildLabelCanvas(Transform parent, string labelText)
        {
            var canvasGO = new GameObject("Label_Canvas");
            canvasGO.transform.SetParent(parent, false);
            canvasGO.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            canvasGO.transform.localRotation = Quaternion.identity;
            canvasGO.transform.localScale    = Vector3.one * 0.005f;

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(300f, 80f);

            // Fondo semitransparente
            CreateUIImage(canvasGO.transform, "BG",
                          new Color(0f, 0f, 0f, 0.7f),
                          Vector2.zero, new Vector2(300f, 80f));

            // Texto TMP
            CreateTMPLabel(canvasGO.transform, "Label",
                           labelText,
                           Vector2.zero, new Vector2(280f, 70f),
                           36, Color.white, FontStyles.Bold);

            return canvas;
        }

        // ─────────────────────────────────────────────────────────────────────
        // CÁMARA / XR ORIGIN
        // ─────────────────────────────────────────────────────────────────────

        private static void SetupCamera()
        {
            // Intentar instanciar el prefab XR Origin con manos
            string xrPrefabPath = AssetDatabase.GUIDToAssetPath(XR_ORIGIN_GUID);
            if (!string.IsNullOrEmpty(xrPrefabPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(xrPrefabPath);
                if (prefab != null)
                {
                    var xrOrigin = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    xrOrigin.name = "XR Origin (Main Menu)";
                    xrOrigin.transform.position = Vector3.zero;

                    // Deshabilitar Continuous Move / Teleportation para que el jugador no se mueva
                    DisableLocomotion(xrOrigin);

                    Debug.Log("[CreateMainMenu] XR Origin instanciado desde prefab.");
                    return;
                }
            }

            // Fallback: cámara simple con PhysicsRaycaster para pruebas en PC
            Debug.LogWarning("[CreateMainMenu] No se encontró el prefab XR Origin. Usando Main Camera simple.");

            var camGO  = new GameObject("Main Camera");
            var cam    = camGO.AddComponent<Camera>();
            cam.tag    = "MainCamera";
            cam.clearFlags        = CameraClearFlags.SolidColor;
            cam.backgroundColor   = new Color(0.06f, 0.05f, 0.1f);
            cam.fieldOfView       = 70f;
            camGO.transform.position = new Vector3(0f, 1.6f, -0.5f); // altura de ojos
            camGO.AddComponent<AudioListener>();

            // PhysicsRaycaster permite que el ratón dispare eventos IPointer* en 3D
            var pr     = camGO.AddComponent<PhysicsRaycaster>();
            pr.eventMask = Physics.AllLayers;
        }

        /// <summary>
        /// Busca y desactiva componentes de locomoción (Move, Teleportation) en el XR Origin.
        /// La rotación (Snap Turn / Continuous Turn) se mantiene activa.
        /// </summary>
        private static void DisableLocomotion(GameObject xrOrigin)
        {
            // Nombres de componentes de movimiento a deshabilitar
            string[] locomotionTypes = new[]
            {
                "ContinuousMoveProvider",
                "DynamicMoveProvider",
                "TeleportationProvider",
                "ActionBasedContinuousMoveProvider",
                "ActionBasedTeleportationProvider"
            };

            var allBehaviours = xrOrigin.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var mb in allBehaviours)
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name;
                foreach (var lt in locomotionTypes)
                {
                    if (typeName.Contains(lt, StringComparison.OrdinalIgnoreCase))
                    {
                        mb.enabled = false;
                        Debug.Log($"[CreateMainMenu] Locomotión deshabilitada: {typeName}");
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // EVENT SYSTEM (XRUIInputModule)
        // ─────────────────────────────────────────────────────────────────────

        private static void SetupEventSystem()
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();

#if UNITY_XR_INTERACTION_TOOLKIT
            // XRUIInputModule — soporta XRI Ray Interactors y ratón simultáneamente
            var xrMod = esGO.AddComponent<XRUIInputModule>();

            // Intentar asignar las XRI Default Input Actions
            string actionsPath = AssetDatabase.GUIDToAssetPath(XRI_ACTIONS_GUID);
            if (!string.IsNullOrEmpty(actionsPath))
            {
                var actionsAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(actionsPath);
                if (actionsAsset != null)
                {
                    // XRUIInputModule expone actionsAsset como propiedad serializada
                    // Se accede vía SerializedObject para máxima compatibilidad de versiones
                    var so   = new SerializedObject(xrMod);
                    var prop = so.FindProperty("m_ActionsAsset") ??
                               so.FindProperty("actionsAsset");
                    if (prop != null)
                    {
                        prop.objectReferenceValue = actionsAsset;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        Debug.Log("[CreateMainMenu] XRI Default Input Actions asignado al XRUIInputModule.");
                    }
                }
            }

            // Activar ratón e input táctil además del XR
            var soMod = new SerializedObject(xrMod);
            SetBool(soMod, "m_EnableMouseInput",  true);
            SetBool(soMod, "m_EnableTouchInput",  true);
            SetBool(soMod, "m_EnableXRInput",     true);
            SetBool(soMod, "m_ActiveInputMode",   true);   // All = 1
            soMod.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[CreateMainMenu] EventSystem con XRUIInputModule configurado.");
#else
            // Fallback para proyectos sin XRI instalado
            esGO.AddComponent<StandaloneInputModule>();
            Debug.LogWarning("[CreateMainMenu] XRI no disponible. Se usa StandaloneInputModule (solo PC).");
#endif
        }

        // ─────────────────────────────────────────────────────────────────────
        // BUILD SETTINGS
        // ─────────────────────────────────────────────────────────────────────

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);

            foreach (var s in scenes)
            {
                if (s.path == scenePath)
                {
                    Debug.Log("[CreateMainMenu] La escena ya estaba en Build Settings.");
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[CreateMainMenu] Escena añadida a Build Settings: {scenePath}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static GameObject CreateUIImage(Transform parent, string name,
                                                Color color, Vector2 anchoredPos,
                                                Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt  = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static void CreateTMPLabel(Transform parent, string name,
                                           string text, Vector2 anchoredPos,
                                           Vector2 sizeDelta, int fontSize,
                                           Color color,
                                           FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize;
            tmp.color     = color;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
        }

        /// <summary>Helper para setear un bool en SerializedObject por nombre de campo.</summary>
        private static void SetBool(SerializedObject so, string fieldName, bool value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null && prop.propertyType == SerializedPropertyType.Boolean)
                prop.boolValue = value;
            else if (prop != null && prop.propertyType == SerializedPropertyType.Integer)
                prop.intValue  = value ? 1 : 0;
        }
    }
}

#endif // UNITY_EDITOR
