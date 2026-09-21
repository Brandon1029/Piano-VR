using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class NoteSpawner : MonoBehaviour
{
    public GameObject notePrefab;
    public Transform[] keyPositions;
    public SongData song;
    public float spawnAlturaExtra = 6f;

    private Dictionary<string, Transform> keyMap = new Dictionary<string, Transform>();
    private float startTime;
    private Coroutine spawnCoroutine;
    private List<GameObject> activeNotes = new List<GameObject>();

    void Awake()
    {
        // Mapeamos las teclas al inicio para tenerlas listas
        foreach (var key in keyPositions)
        {
            if (key != null && !keyMap.ContainsKey(key.name))
                keyMap[key.name] = key;
        }
    }

    // Metodo publico que llama nuestro menu de canciones
    public void StartSong(SongData newSong)
    {
        if (newSong == null) return;

        // Si habia una cancion corriendo, la detenemos y limpiamos notas
        StopCurrentSong();

        song = newSong;
        startTime = Time.time;
        spawnCoroutine = StartCoroutine(SpawnNotes());
    }

    public void StopCurrentSong()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        // Destruir cualquier nota que haya quedado a medio camino
        foreach (var note in activeNotes)
        {
            if (note != null)
                Destroy(note);
        }
        activeNotes.Clear();
    }

    IEnumerator SpawnNotes()
    {
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
                activeNotes.Add(nota); // La registramos para poder limpiarla si cambia la cancion

                NotaMovimiento mov = nota.GetComponent<NotaMovimiento>();
                if (mov != null)
                {
                    PianoKey pianoKey = keyTransform.GetComponent<PianoKey>();
                    mov.SetTarget(keyTransform.position.y, spawnPos.y, pianoKey);
                }
            }
            else
            {
                Debug.LogWarning($"Tecla no encontrada: {note.key}");
            }
        }

        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm != null)
            gm.ShowResults();
    }
}