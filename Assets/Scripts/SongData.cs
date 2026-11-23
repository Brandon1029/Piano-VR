using UnityEngine;

public enum DifficultyLevel
{
    Principiante,
    Intermedio,
    Avanzado
}

/// <summary>
/// ScriptableObject de solo lectura con los metadatos estáticos y partitura de una canción.
/// Los récords y estrellas del jugador NUNCA se almacenan en este asset; se gestionan
/// de forma independiente en almacenamiento local a través de SaveManager.
/// </summary>
[CreateAssetMenu(fileName = "SongData", menuName = "Piano/SongData")]
public class SongData : ScriptableObject
{
    [System.Serializable]
    public class NoteEvent
    {
        public float time;
        public string key;
    }

    [Header("Información General (Solo Lectura)")]
    public string songName;
    public string composer = "Desconocido";
    public DifficultyLevel difficulty = DifficultyLevel.Principiante;

    [Header("UI Visual")]
    public Sprite coverImage;

    [Header("Parámetros Musicales")]
    public float bpm;
    [TextArea(2, 4)]
    public string technicalFocus;

    [Header("Notas")]
    public NoteEvent[] notes;

    /// <summary>
    /// Identificador único de solo lectura usado como clave en SaveManager.
    /// </summary>
    public string SongId => !string.IsNullOrEmpty(songName) ? songName.Trim() : name.Trim();
}
