using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using PianoVR;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SongSelectionMenu : MonoBehaviour
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

    public static SongSelectionMenu Instance { get; private set; }

    [Header("Base de Datos de Canciones")]
    [SerializeField] private List<SongData> allSongs = new List<SongData>();

    [Header("Filtros de Dificultad")]
    [SerializeField] private Button btnPrincipiante;
    [SerializeField] private Button btnIntermedio;
    [SerializeField] private Button btnAvanzado;

    [Header("Lista de Canciones")]
    [SerializeField] private Transform songListContainer;
    [SerializeField] private GameObject songItemButtonPrefab;
    [SerializeField] private ScrollRect scrollRectCanciones;
    [SerializeField] private Button btnSubirLista;
    [SerializeField] private Button btnBajarLista;
    [Tooltip("Fracción o paso de desplazamiento al pulsar los botones táctiles Subir ▲ / Bajar ▼.")]
    [SerializeField] private float pasoNavegacionVertical = 0.38f;

    [Header("Detalles de la Cancion")]
    [SerializeField] private TextMeshProUGUI txtTitle;
    [SerializeField] private TextMeshProUGUI txtComposer;
    [SerializeField] private TextMeshProUGUI txtBPM;
    [SerializeField] private TextMeshProUGUI txtFocus;
    [SerializeField] private Image imgCover;
    [SerializeField] private Button btnPlay;

    [Header("UI de Récords y Estrellas")]
    [SerializeField] private TextMeshProUGUI txtRecord;
    [SerializeField] private Image[] starImages; 
    [SerializeField] private Sprite starFilledSprite; 
    [SerializeField] private Sprite starEmptySprite;  

    [Header("Conexion al Sistema")]
    [SerializeField] private NoteSpawner noteSpawner;

    [Header("Configuración de Salida")]
    [SerializeField] private SceneField escenaDestino;

    private SongData selectedSong;    // Canción actualmente mostrada/seleccionada en el panel
    private SongData playingSong;     // Canción que está SONANDO actualmente en la escena
    private bool isPlaying = false;
    private Coroutine rutinaScrollSuave;
    private CanvasGroup cgBotonSubir;
    private CanvasGroup cgBotonBajar;
    private float posicionVerticalNormalizada = 1f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    private void Start()
    {
        ConfigurarScrollRectYNavegacionTactil();

        if (btnPrincipiante != null)
            btnPrincipiante.onClick.AddListener(() => FilterByDifficulty(DifficultyLevel.Principiante));
        if (btnIntermedio != null)
            btnIntermedio.onClick.AddListener(() => FilterByDifficulty(DifficultyLevel.Intermedio));
        if (btnAvanzado != null)
            btnAvanzado.onClick.AddListener(() => FilterByDifficulty(DifficultyLevel.Avanzado));

        if (btnPlay != null)
            btnPlay.onClick.AddListener(OnPlayPressed);

        Debug.Log("[SongSelectionMenu] Menú de canciones iniciado.");
        FilterByDifficulty(DifficultyLevel.Principiante);
    }

    public void FilterByDifficulty(DifficultyLevel difficulty)
    {
        Debug.Log($"[SongSelectionMenu] [UI] Filtro de dificultad presionado: {difficulty}");

        if (songListContainer == null && scrollRectCanciones != null)
        {
            songListContainer = scrollRectCanciones.content;
        }
        if (songListContainer == null)
        {
            Debug.LogError("[SongSelectionMenu] songListContainer (Content) no está asignado.");
            return;
        }

        ConfigurarScrollRectYNavegacionTactil();

        // Limpiar tarjetas previas desactivándolas y desvinculándolas de inmediato para no interferir con el layout del mismo frame
        for (int i = songListContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = songListContainer.GetChild(i);
            if (child != null)
            {
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        List<SongData> filtered = allSongs.FindAll(s => s != null && s.difficulty == difficulty);

        if (filtered.Count > 0)
        {
            foreach (SongData song in filtered)
            {
                GameObject itemObj = Instantiate(songItemButtonPrefab, songListContainer, false);
                itemObj.SetActive(true);
                itemObj.transform.localScale = Vector3.one;

                TextMeshProUGUI label = itemObj.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.text = song.songName;
                }

                Button itemBtn = itemObj.GetComponent<Button>();
                if (itemBtn != null)
                {
                    SongData capturedSong = song;
                    itemBtn.onClick.AddListener(() => {
                        Debug.Log($"[SongSelectionMenu] [UI] Canción seleccionada de la lista: '{capturedSong.songName}'");
                        SelectSong(capturedSong);
                    });
                }

                if (XRUIInteractionEnabler.Instance != null)
                {
                    XRUIInteractionEnabler.Instance.MejorarControlesEnJerarquia(itemObj.transform);
                }
            }

            SelectSong(filtered[0]);
        }
        else
        {
            ClearDetails();
        }

        // Forzar reconstrucción inmediata del Content y posicionar en el tope (0, 0)
        RectTransform contentRt = songListContainer as RectTransform;
        if (contentRt != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
        }
        EstablecerPosicionVerticalLista(1f);

        if (isActiveAndEnabled)
        {
            StartCoroutine(ReiniciarScrollAlTopeTrasLayout());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Configuración de ScrollRect + Botones Táctiles Subir ▲ / Bajar ▼
    // ─────────────────────────────────────────────────────────────────────────

    private void ConfigurarScrollRectYNavegacionTactil()
    {
        if (scrollRectCanciones == null && songListContainer != null)
        {
            scrollRectCanciones = songListContainer.GetComponentInParent<ScrollRect>();
        }

        if (scrollRectCanciones == null) return;

        // 1. Evitar que ScrollRect colapse el Viewport a (0,0) al quitar las barras de scroll
        scrollRectCanciones.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scrollRectCanciones.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        if (scrollRectCanciones.horizontalScrollbar != null)
        {
            scrollRectCanciones.horizontalScrollbar.gameObject.SetActive(false);
            scrollRectCanciones.horizontalScrollbar = null;
        }
        if (scrollRectCanciones.verticalScrollbar != null)
        {
            scrollRectCanciones.verticalScrollbar.gameObject.SetActive(false);
            scrollRectCanciones.verticalScrollbar = null;
        }

        Transform sbH = scrollRectCanciones.transform.Find("Scrollbar Horizontal");
        if (sbH != null) sbH.gameObject.SetActive(false);
        Transform sbV = scrollRectCanciones.transform.Find("Scrollbar Vertical");
        if (sbV != null) sbV.gameObject.SetActive(false);

        // 2. Desactivar el arrastre táctil directo en el ScrollRect para que tocar una canción NUNCA mueva la lista ni cancele el onClick
        scrollRectCanciones.vertical = false;
        scrollRectCanciones.horizontal = false;
        scrollRectCanciones.movementType = ScrollRect.MovementType.Clamped;
        scrollRectCanciones.scrollSensitivity = 0f;
        scrollRectCanciones.inertia = false;

        // 3. Garantizar que el Viewport ocupe toda el área visible de SongScrollView (dejando 52px a la derecha para los botones ▲ / ▼)
        RectTransform vpRt = scrollRectCanciones.viewport;
        if (vpRt != null)
        {
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.pivot = new Vector2(0f, 1f);
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = new Vector2(-52f, 0f);
            vpRt.localScale = Vector3.one;
        }

        // 4. Garantizar que Content tenga escala (1,1,1), anclaje superior y VerticalLayoutGroup + ContentSizeFitter activos
        RectTransform contentRt = scrollRectCanciones.content != null
            ? scrollRectCanciones.content
            : (songListContainer as RectTransform);
        if (contentRt != null)
        {
            scrollRectCanciones.content = contentRt;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.localScale = Vector3.one;
            contentRt.sizeDelta = new Vector2(0f, contentRt.sizeDelta.y);

            VerticalLayoutGroup vlg = contentRt.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.enabled = true;

            ContentSizeFitter csf = contentRt.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.enabled = true;
            }
        }

        AsegurarBotonesTactilesSubirBajar();
        ActualizarEstadoBotonesScroll();
    }

    private void AsegurarBotonesTactilesSubirBajar()
    {
        if (scrollRectCanciones == null) return;

        Transform scrollTransform = scrollRectCanciones.transform;

        // Si ya existen dentro de SongScrollView o están asignados en Inspector, conectarlos
        if (btnSubirLista == null || btnBajarLista == null)
        {
            Transform panelExistente = scrollTransform.Find("PanelNavegacionLista");
            if (panelExistente != null)
            {
                Transform tUp = panelExistente.Find("Btn_SubirLista");
                Transform tDown = panelExistente.Find("Btn_BajarLista");
                if (tUp != null) btnSubirLista = tUp.GetComponent<Button>();
                if (tDown != null) btnBajarLista = tDown.GetComponent<Button>();
            }
        }

        if (btnSubirLista == null || btnBajarLista == null)
        {
            // Colocar PanelNavegacionLista en la franja derecha de SongScrollView (50px de ancho),
            // sin alterar el HorizontalLayoutGroup de ContentArea ni el tamaño de SongDetailsPanel
            GameObject panelNav = new GameObject("PanelNavegacionLista", typeof(RectTransform), typeof(VerticalLayoutGroup));
            panelNav.layer = scrollTransform.gameObject.layer;
            panelNav.transform.SetParent(scrollTransform, false);

            RectTransform navRt = panelNav.GetComponent<RectTransform>();
            navRt.anchorMin = new Vector2(1f, 0f);
            navRt.anchorMax = new Vector2(1f, 1f);
            navRt.pivot = new Vector2(1f, 0.5f);
            navRt.anchoredPosition = new Vector2(-2f, 0f);
            navRt.sizeDelta = new Vector2(48f, -8f);
            navRt.localScale = Vector3.one;

            VerticalLayoutGroup vlg = panelNav.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 4, 4);
            vlg.spacing = 10f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = true;

            Sprite spriteRedondeado = ObtenerSpriteRedondeado();
            TMP_FontAsset fuente = txtTitle != null ? txtTitle.font : null;

            btnSubirLista = CrearBotonNavegacionVertical(panelNav.transform, "Btn_SubirLista", "▲", spriteRedondeado, fuente);
            btnBajarLista = CrearBotonNavegacionVertical(panelNav.transform, "Btn_BajarLista", "▼", spriteRedondeado, fuente);
        }

        if (btnSubirLista != null)
        {
            cgBotonSubir = btnSubirLista.GetComponent<CanvasGroup>();
            if (cgBotonSubir == null) cgBotonSubir = btnSubirLista.gameObject.AddComponent<CanvasGroup>();
            btnSubirLista.onClick.RemoveListener(SubirListaCanciones);
            btnSubirLista.onClick.AddListener(SubirListaCanciones);
            if (XRUIInteractionEnabler.Instance != null)
                XRUIInteractionEnabler.Instance.MejorarControlUI(btnSubirLista);
        }

        if (btnBajarLista != null)
        {
            cgBotonBajar = btnBajarLista.GetComponent<CanvasGroup>();
            if (cgBotonBajar == null) cgBotonBajar = btnBajarLista.gameObject.AddComponent<CanvasGroup>();
            btnBajarLista.onClick.RemoveListener(BajarListaCanciones);
            btnBajarLista.onClick.AddListener(BajarListaCanciones);
            if (XRUIInteractionEnabler.Instance != null)
                XRUIInteractionEnabler.Instance.MejorarControlUI(btnBajarLista);
        }
    }

    private Button CrearBotonNavegacionVertical(Transform parent, string nombre, string etiqueta, Sprite spriteFondo, TMP_FontAsset fuente)
    {
        GameObject btnGO = new GameObject(nombre, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGO.layer = parent.gameObject.layer;
        btnGO.transform.SetParent(parent, false);

        RectTransform rt = btnGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(44f, 140f);
        rt.localScale = Vector3.one;

        LayoutElement le = btnGO.GetComponent<LayoutElement>();
        le.minWidth = 44f;
        le.minHeight = 90f;
        le.preferredWidth = 44f;
        le.preferredHeight = 145f;
        le.flexibleHeight = 1f;

        Image img = btnGO.GetComponent<Image>();
        img.color = new Color(0.11f, 0.16f, 0.24f, 0.92f);
        if (spriteFondo != null)
        {
            img.sprite = spriteFondo;
            img.type = Image.Type.Sliced;
        }

        Button btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;

        GameObject txtGO = new GameObject("Text (TMP)", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        txtGO.layer = parent.gameObject.layer;
        txtGO.transform.SetParent(btnGO.transform, false);

        RectTransform txtRt = txtGO.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
        txtRt.localScale = Vector3.one;

        TextMeshProUGUI tmp = txtGO.GetComponent<TextMeshProUGUI>();
        if (fuente != null) tmp.font = fuente;
        tmp.text = etiqueta;
        tmp.fontSize = 26f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(0.22f, 0.74f, 0.97f, 1f); // #38BDF8
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return btn;
    }

    private Sprite ObtenerSpriteRedondeado()
    {
        Image[] imgs = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < imgs.Length; i++)
        {
            if (imgs[i] != null && imgs[i].sprite != null && imgs[i].type == Image.Type.Sliced)
            {
                return imgs[i].sprite;
            }
        }
        return null;
    }

    public void SubirListaCanciones()
    {
        DesplazarListaPorPaso(+CalcularPasoNormalizado());
    }

    public void BajarListaCanciones()
    {
        DesplazarListaPorPaso(-CalcularPasoNormalizado());
    }

    private float CalcularPasoNormalizado()
    {
        if (scrollRectCanciones == null || scrollRectCanciones.content == null)
            return pasoNavegacionVertical;

        RectTransform vp = scrollRectCanciones.viewport != null
            ? scrollRectCanciones.viewport
            : scrollRectCanciones.GetComponent<RectTransform>();

        float vpH = vp != null ? vp.rect.height : 340f;
        float contentH = scrollRectCanciones.content.rect.height;
        float exceso = contentH - vpH;

        if (exceso <= 5f) return 1f;

        // Desplazar aproximadamente 2.5 canciones (~190px) por toque para recorrer toda la lista cómodamente
        float pasoCalculado = 190f / exceso;
        return Mathf.Clamp(pasoCalculado, 0.25f, 0.65f);
    }

    private void DesplazarListaPorPaso(float deltaNormalizado)
    {
        if (scrollRectCanciones == null) return;

        float destino = Mathf.Clamp01(posicionVerticalNormalizada + deltaNormalizado);
        if (rutinaScrollSuave != null)
        {
            StopCoroutine(rutinaScrollSuave);
        }
        rutinaScrollSuave = StartCoroutine(RutinaScrollSuave(destino));
    }

    private IEnumerator RutinaScrollSuave(float destino)
    {
        if (scrollRectCanciones == null) yield break;

        scrollRectCanciones.velocity = Vector2.zero;
        float inicio = posicionVerticalNormalizada;
        float duracion = 0.16f;
        float t = 0f;

        while (t < duracion && scrollRectCanciones != null)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duracion));
            EstablecerPosicionVerticalLista(Mathf.Lerp(inicio, destino, u));
            yield return null;
        }

        if (scrollRectCanciones != null)
        {
            EstablecerPosicionVerticalLista(destino);
        }
        rutinaScrollSuave = null;
    }

    private IEnumerator ReiniciarScrollAlTopeTrasLayout()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (scrollRectCanciones != null && scrollRectCanciones.content != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRectCanciones.content);
        }
        EstablecerPosicionVerticalLista(1f);
    }

    private void EstablecerPosicionVerticalLista(float norm)
    {
        posicionVerticalNormalizada = Mathf.Clamp01(norm);

        if (scrollRectCanciones != null)
        {
            scrollRectCanciones.velocity = Vector2.zero;
            scrollRectCanciones.verticalNormalizedPosition = posicionVerticalNormalizada;

            if (scrollRectCanciones.content != null)
            {
                RectTransform vp = scrollRectCanciones.viewport != null
                    ? scrollRectCanciones.viewport
                    : scrollRectCanciones.GetComponent<RectTransform>();
                float vpH = vp != null ? vp.rect.height : 340f;
                float contentH = scrollRectCanciones.content.rect.height;
                float exceso = Mathf.Max(0f, contentH - vpH);

                Vector2 pos = scrollRectCanciones.content.anchoredPosition;
                pos.y = (1f - posicionVerticalNormalizada) * exceso;
                scrollRectCanciones.content.anchoredPosition = pos;
            }
        }

        ActualizarEstadoBotonesScroll();
    }

    private void ActualizarEstadoBotonesScroll()
    {
        if (scrollRectCanciones == null) return;

        bool requiereScroll = true;
        if (scrollRectCanciones.content != null)
        {
            RectTransform vp = scrollRectCanciones.viewport != null
                ? scrollRectCanciones.viewport
                : scrollRectCanciones.GetComponent<RectTransform>();
            if (vp != null && vp.rect.height > 10f && scrollRectCanciones.content.rect.height > 10f)
            {
                requiereScroll = scrollRectCanciones.content.rect.height > (vp.rect.height + 4f);
            }
        }

        bool puedeSubir = requiereScroll && posicionVerticalNormalizada < 0.985f;
        bool puedeBajar = requiereScroll && posicionVerticalNormalizada > 0.015f;

        if (btnSubirLista != null)
        {
            btnSubirLista.interactable = puedeSubir;
            if (cgBotonSubir != null) cgBotonSubir.alpha = puedeSubir ? 1f : 0.35f;
        }

        if (btnBajarLista != null)
        {
            btnBajarLista.interactable = puedeBajar;
            if (cgBotonBajar != null) cgBotonBajar.alpha = puedeBajar ? 1f : 0.35f;
        }
    }

    private void SelectSong(SongData song)
    {
        selectedSong = song;
        txtTitle.text = song.songName;
        txtComposer.text = "Compositor: " + song.composer;
        txtBPM.text = "Tempo: " + song.bpm + " BPM";
        txtFocus.text = string.IsNullOrEmpty(song.technicalFocus) ? "" : song.technicalFocus;

        if (imgCover != null)
        {
            if (song.coverImage != null)
            {
                imgCover.sprite = song.coverImage;
                imgCover.gameObject.SetActive(true);
            }
            else
            {
                imgCover.gameObject.SetActive(false);
            }
        }

        btnPlay.interactable = true;

        bool esLaCancionSonando = isPlaying && (playingSong == selectedSong);
        ActualizarTextoBoton(esLaCancionSonando);
        ActualizarRécordUI();

        Debug.Log($"[SongSelectionMenu] Canción activa: '{song.songName}' | Compositor: {song.composer} | BPM: {song.bpm}");
    }

    public void ActualizarRécordUI()
    {
        if (selectedSong == null) return;

        float mejorPrecision = SaveManager.GetBestAccuracy(selectedSong.SongId);
        int mejoresEstrellas = SaveManager.GetBestStars(selectedSong.SongId);

        if (txtRecord != null)
        {
            txtRecord.text = mejorPrecision > 0f 
                ? $"Mejor Récord: {mejorPrecision:F1}%" 
                : "Sin completar";
        }

        if (starImages != null && starImages.Length > 0)
        {
            for (int i = 0; i < starImages.Length; i++)
            {
                if (starImages[i] == null) continue;

                if (i < mejoresEstrellas)
                {
                    if (starFilledSprite != null) starImages[i].sprite = starFilledSprite;
                    starImages[i].color = Color.white;
                }
                else
                {
                    if (starEmptySprite != null) starImages[i].sprite = starEmptySprite;
                    starImages[i].color = new Color(0.4f, 0.4f, 0.4f, 0.6f);
                }
            }
        }
    }

    private void ClearDetails()
    {
        selectedSong = null;
        txtTitle.text = "Sin canciones";
        txtComposer.text = "";
        txtBPM.text = "";
        txtFocus.text = "No hay piezas disponibles en esta dificultad.";

        if (imgCover != null) imgCover.gameObject.SetActive(false);
        btnPlay.interactable = false;

        if (txtRecord != null) txtRecord.text = "";

        if (starImages != null)
        {
            foreach (var img in starImages)
            {
                if (img != null) img.color = Color.clear;
            }
        }

        ActualizarTextoBoton(false);
    }

    private void OnPlayPressed()
    {
        if (selectedSong == null || noteSpawner == null) return;

        bool esModoLibre = SceneManager.GetActiveScene().name == "ModoLibreMenu";

        if (!esModoLibre && isPlaying && playingSong == selectedSong)
        {
            Debug.Log($"[SongSelectionMenu] [UI] Botón Play pulsado -> Deteniendo canción '{selectedSong.songName}'");
            DetenerModoCancion();
        }
        else
        {
            Debug.Log($"[SongSelectionMenu] [UI] Botón Play pulsado -> Iniciando/Reiniciando canción '{selectedSong.songName}' ({selectedSong.bpm} BPM)");

            if (noteSpawner != null)
            {
                noteSpawner.StopCurrentSong();
            }

            playingSong = selectedSong;
            isPlaying = true;
            ActualizarTextoBoton(true);

            noteSpawner.StartSong(selectedSong);
        }
    }

    public void NotificarCancionIniciada(SongData cancion)
    {
        if (cancion == null) return;
        playingSong = cancion;
        isPlaying = true;
        ActualizarTextoBoton(selectedSong == playingSong);
    }

    public void DetenerModoCancion()
    {
        if (noteSpawner != null)
        {
            noteSpawner.StopCurrentSong();
        }

        playingSong = null;
        isPlaying = false;
        ActualizarTextoBoton(false);
        Debug.Log("[SongSelectionMenu] Reproducción de canción detenida.");
    }

    private void ActualizarTextoBoton(bool estaSonandoEstaCancion)
    {
        if (btnPlay == null) return;

        TextMeshProUGUI btnText = btnPlay.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            bool esModoLibre = SceneManager.GetActiveScene().name == "ModoLibreMenu";
            if (estaSonandoEstaCancion)
            {
                btnText.text = esModoLibre ? "Reintentar" : "Detener la canción";
            }
            else
            {
                btnText.text = "Comenzar a Tocar";
            }
        }
    }

    public void OnSongEnded()
    {
        Debug.Log($"[SongSelectionMenu] Canción finalizada.");
        playingSong = null;
        isPlaying = false;

        ActualizarTextoBoton(false);
        ActualizarRécordUI();

        if (SongResultsAnimator.Instance != null)
        {
            SongResultsAnimator.Instance.SincronizarTarjetasDeLista();
        }
    }

    public void SalirAEscenaDestino()
    {
        if (escenaDestino == null || string.IsNullOrEmpty(escenaDestino.SceneName))
        {
            Debug.LogWarning("[SongSelectionMenu] No has arrastrado ninguna escena en 'Escena Destino'.");
            return;
        }

        if (isPlaying)
        {
            DetenerModoCancion();
        }

        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TransitionToScene(escenaDestino);
        }
    }
}