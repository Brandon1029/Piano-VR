using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SongSelectionMenu : MonoBehaviour
{
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

    private SongData selectedSong;    // Canción actualmente mostrada/seleccionada en el panel
    private SongData playingSong;     // Canción que está SONANDO actualmente en la escena
    private bool isPlaying = false; 

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    private void Start()
    {
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

        foreach (Transform child in songListContainer)
        {
            Destroy(child.gameObject);
        }

        List<SongData> filtered = allSongs.FindAll(s => s != null && s.difficulty == difficulty);

        if (filtered.Count > 0)
        {
            foreach (SongData song in filtered)
            {
                GameObject itemObj = Instantiate(songItemButtonPrefab, songListContainer);
                itemObj.GetComponentInChildren<TextMeshProUGUI>().text = song.songName;

                Button itemBtn = itemObj.GetComponent<Button>();
                itemBtn.onClick.AddListener(() => {
                    Debug.Log($"[SongSelectionMenu] [UI] Canción seleccionada de la lista: '{song.songName}'");
                    SelectSong(song);
                });
            }

            SelectSong(filtered[0]);
        }
        else
        {
            ClearDetails();
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

        float mejorPrecision = ScoreManager.GetBestAccuracy(selectedSong.songName);
        int mejoresEstrellas = ScoreManager.GetBestStars(selectedSong.songName);

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

        // CASO 1: Si presionamos el botón cuando estamos viendo la canción que YA ESTÁ SONANDO
        if (isPlaying && playingSong == selectedSong)
        {
            Debug.Log($"[SongSelectionMenu] [UI] Botón Play pulsado -> Deteniendo canción '{selectedSong.songName}'");
            DetenerModoCancion();
        }
        // CASO 2: Si presionamos el botón para iniciar una canción nueva (o reemplazar la anterior)
        else
        {
            Debug.Log($"[SongSelectionMenu] [UI] Botón Play pulsado -> Iniciando canción '{selectedSong.songName}' ({selectedSong.bpm} BPM)");

            // Detiene cualquier pista previa que estuviera cayendo
            if (noteSpawner != null)
            {
                noteSpawner.StopCurrentSong();
            }

            // Inicia la nueva canción seleccionada
            noteSpawner.StartSong(selectedSong);
            playingSong = selectedSong;
            isPlaying = true;

            ActualizarTextoBoton(true);
        }
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
            btnText.text = estaSonandoEstaCancion ? "Detener la canción" : "Comenzar a Tocar";
        }
    }

    public void OnSongEnded()
    {
        Debug.Log($"[SongSelectionMenu] Canción finalizada.");
        playingSong = null;
        isPlaying = false;

        // 1. Restaurar el texto del botón a "Comenzar a Tocar"
        ActualizarTextoBoton(false);

        // 2. Refrescar el récord y las estrellas en la interfaz
        ActualizarRécordUI();
    }
}
