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

    void Start()
    {
        foreach (var key in keyPositions)
        {
            if (key != null && !keyMap.ContainsKey(key.name))
                keyMap[key.name] = key;
        }

        startTime = Time.time;
        StartCoroutine(SpawnNotes());
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
                NotaMovimiento mov = nota.GetComponent<NotaMovimiento>();
                if (mov != null)
                {
                    // Obtenemos la tecla y se la asignamos al movimiento
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