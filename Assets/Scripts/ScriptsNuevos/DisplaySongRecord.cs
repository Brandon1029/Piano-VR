using UnityEngine;
using TMPro;

public class DisplaySongRecord : MonoBehaviour
{
    [Header("Referencias de UI en el Menú")]
    public TextMeshProUGUI recordText;
    public TextMeshProUGUI starsText;

    /// <summary>
    /// Invoca esta función cada vez que cambies o selecciones una canción en el menú.
    /// </summary>
    public void MostrarRecord(SongData song)
    {
        if (song == null) return;

        // 1. Obtener la precisión guardada y las estrellas
        float mejorPrecision = ScoreManager.GetBestAccuracy(song.songName);
        int mejoresEstrellas = ScoreManager.GetBestStars(song.songName);

        // 2. Mostrar Porcentaje o "Sin Récord"
        if (recordText != null)
        {
            recordText.text = mejorPrecision > 0f 
                ? $"Mejor Récord: {mejorPrecision:F1}%" 
                : "Sin completar";
        }

        // 3. Dibujar Estrellas (★ / ☆)
        if (starsText != null)
        {
            string estrellasVisuales = "";
            for (int i = 1; i <= 4; i++)
            {
                estrellasVisuales += (i <= mejoresEstrellas) ? "★" : "☆";
            }
            starsText.text = estrellasVisuales;
        }
    }
}