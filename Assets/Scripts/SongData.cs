using System.Collections.Generic;
using UnityEngine;

public enum DifficultyLevel
{
    Principiante,
    Intermedio,
    Avanzado
}

[CreateAssetMenu(fileName = "SongData", menuName = "Piano/SongData")]
public class SongData : ScriptableObject
{
    [System.Serializable]
    public class NoteEvent
    {
        public float time;
        public string key;
    }

    [Header("Información General")]
    public string songName;
    public string composer = "Desconocido";
    public DifficultyLevel difficulty = DifficultyLevel.Principiante;

    [Header("Parámetros Musicales")]
    public float bpm;
    [TextArea(2, 4)]
    public string technicalFocus;

    [Header("Notas")]
    public NoteEvent[] notes;
}