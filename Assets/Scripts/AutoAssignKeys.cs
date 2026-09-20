using UnityEngine;

public class AutoAssignKeys : MonoBehaviour
{
    public NoteSpawner noteSpawner;

    void Start()
    {
        GameObject[] keys = GameObject.FindGameObjectsWithTag("Teclas");
        noteSpawner.keyPositions = new Transform[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            noteSpawner.keyPositions[i] = keys[i].transform;
        
        Destroy(this); // se elimina solo después de asignar
    }
}