using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SongSelectionMenu : MonoBehaviour
{
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
    [SerializeField] private Button btnPlay;

    [Header("Conexion al Sistema")]
    [SerializeField] private NoteSpawner noteSpawner;

    private SongData selectedSong;

    private void Start()
    {
        btnPrincipiante.onClick.AddListener(() => FilterByDifficulty(DifficultyLevel.Principiante));
        btnIntermedio.onClick.AddListener(() => FilterByDifficulty(DifficultyLevel.Intermedio));
        btnAvanzado.onClick.AddListener(() => FilterByDifficulty(DifficultyLevel.Avanzado));

        btnPlay.onClick.AddListener(OnPlayPressed);

        // Iniciar con la lista de nivel Principiante
        FilterByDifficulty(DifficultyLevel.Principiante);
    }

    public void FilterByDifficulty(DifficultyLevel difficulty)
    {
        // Limpiar botones de canciones previos
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
                itemBtn.onClick.AddListener(() => SelectSong(song));
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
        btnPlay.interactable = true;
    }

    private void ClearDetails()
    {
        selectedSong = null;
        txtTitle.text = "Sin canciones";
        txtComposer.text = "";
        txtBPM.text = "";
        txtFocus.text = "No hay piezas disponibles en esta dificultad.";
        btnPlay.interactable = false;
    }

    private void OnPlayPressed()
    {
        if (selectedSong == null || noteSpawner == null) return;

        // Arranca la nueva cancion (NoteSpawner limpiará automáticamente la anterior)
        noteSpawner.StartSong(selectedSong);
        
        // Ya no ocultamos el menú: se queda fijo a la derecha
    }
}