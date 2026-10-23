using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class NoteSpawner : MonoBehaviour
{
    [Header("Configuración de la Canción")]
    public SongData song;
    public GameObject notePrefab;

    [Header("Configuración de Teclas")]
    [Tooltip("Arreglo de posiciones/transforms de teclas para el asignador automático de referencias.")]
    public Transform[] keyPositions; // Reincorporado para corregir CS1061 con AutoAssignKeys.cs

    [Header("Parámetros de Spawning")]
    public float spawnAlturaExtra = 0.5f;

    private Dictionary<string, Transform> keyMap = new Dictionary<string, Transform>();
    private List<GameObject> activeNotes = new List<GameObject>();
    private Coroutine spawnCoroutine;
    private float startTime;

    void Start()
    {
        MapPianoKeys();
    }

    /// <summary>
    /// Mapea todas las teclas del piano en la escena asociándolas a su nombre (ej. C4, D#3, Db2).
    /// </summary>
    void MapPianoKeys()
    {
        // Si hay un arreglo manual asignado en el Inspector, se mapea primero
        if (keyPositions != null && keyPositions.Length > 0)
        {
            foreach (var key in keyPositions)
            {
                if (key != null && !keyMap.ContainsKey(key.name.Trim()))
                {
                    keyMap.Add(key.name.Trim(), key);
                }
            }
        }

        // Búsqueda dinámica global en escena para cualquier tecla omitida
        PianoKey[] keys = Object.FindObjectsByType<PianoKey>(FindObjectsSortMode.None);
        foreach (var key in keys)
        {
            string keyName = key.gameObject.name.Trim();
            if (!keyMap.ContainsKey(keyName))
            {
                keyMap.Add(keyName, key.transform);
            }
        }
    }

    /// <summary>
    /// Inicia la reproducción de una nueva pieza musical.
    /// </summary>
    public void StartSong(SongData newSong)
{
    if (newSong == null) return;

    StopCurrentSong();

    song = newSong;
    startTime = Time.time;

    // 1. OBTENER TOTAL DE NOTAS REALES DE LA CANCIÓN
    int totalNotas = (song.notes != null) ? song.notes.Length : 0;

    // 2. INICIALIZAR SESIÓN EN SCOREMANAGER
    if (ScoreManager.Instance != null)
    {
        ScoreManager.Instance.IniciarSesion(song.songName, totalNotas);
    }

    if (PianoManager.Instance != null)
    {
        PianoManager.Instance.IniciarModoCancion(PianoManager.Instance.octavaBaseActual);
    }

    spawnCoroutine = StartCoroutine(SpawnNotes());
}

    /// <summary>
    /// Detiene la canción activa, destruye las notas flotantes y regresa a Modo Libre.
    /// </summary>
    public void StopCurrentSong()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        // Limpia las notas que estaban cayendo en pantalla
        foreach (var note in activeNotes)
        {
            if (note != null) Destroy(note);
        }
        activeNotes.Clear();

        // Restaura el estado a Modo Libre en el PianoManager
        if (PianoManager.Instance != null)
        {
            PianoManager.Instance.TerminarModoCancion();
        }
    }

    /// <summary>
    /// Corrutina encargada de instanciar cada nota en su tiempo musical programado.
    /// </summary>
    IEnumerator SpawnNotes()
{
    if (song == null || song.notes == null) yield break;

    foreach (var note in song.notes)
    {
        float delay = note.time - (Time.time - startTime);
        if (delay > 0) yield return new WaitForSeconds(delay);

        if (keyMap.TryGetValue(note.key, out Transform keyTransform))
        {
            Vector3 spawnPos = new Vector3(
                keyTransform.position.x,
                keyTransform.position.y + spawnAlturaExtra,
                keyTransform.position.z
            );

            GameObject nota = Instantiate(notePrefab, spawnPos, keyTransform.rotation);
            activeNotes.Add(nota);

            NotaMovimiento mov = nota.GetComponent<NotaMovimiento>();
            if (mov != null)
            {
                PianoKey pianoKey = keyTransform.GetComponent<PianoKey>();
                mov.SetTarget(keyTransform.position.y, spawnPos.y, pianoKey);
            }
        }
        else
        {
            Debug.LogWarning($"[NoteSpawner] Tecla no encontrada en el piano: {note.key}");
        }
    }

    // Espera a que las últimas notas caigan por completo
    yield return new WaitForSeconds(2.0f);

    // 1. Guardar resultados en el ScoreManager
    if (ScoreManager.Instance != null)
    {
        ScoreManager.Instance.GuardarResultado();
    }

    // 2. Refrescar el estado del botón y las estrellas en el menú principal
    if (SongSelectionMenu.Instance != null)
    {
        SongSelectionMenu.Instance.OnSongEnded();
    }

    // 3. Desactivar el modo canción en PianoManager al concluir la pista
    if (PianoManager.Instance != null)
    {
        PianoManager.Instance.TerminarModoCancion();
    }

    // 4. Muestra la pantalla de resultados si el GameManager está configurado
    GameManager gm = Object.FindAnyObjectByType<GameManager>();
    if (gm != null)
    {
        gm.ShowResults();
    }
}
}