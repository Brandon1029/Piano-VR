using UnityEngine;

[CreateAssetMenu(fileName = "SongData", menuName = "Piano/SongData")]
public class SongData : ScriptableObject
{
    [System.Serializable]
    public class NoteEvent
    {
        public float time;
        public string key;
    }

    public string songName;
    public float bpm;
    public NoteEvent[] notes;
}

