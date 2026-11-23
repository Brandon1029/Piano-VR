using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("Countdown")]
    public TextMeshProUGUI countdownText;
    public NoteSpawner noteSpawner;

    [Header("Score")]
    public ScoreManager scoreManager;

    [Header("Results")]
    public GameObject resultsPanel;
    public TextMeshProUGUI finalScoreText;

    private string nombreCancionActiva = "";

    void Start()
    {
        if (resultsPanel != null) resultsPanel.SetActive(false);

        if (noteSpawner != null && noteSpawner.song != null)
        {
            nombreCancionActiva = noteSpawner.song.songName;
            int totalNotas = noteSpawner.song.notes != null ? noteSpawner.song.notes.Length : 1;

            if (scoreManager != null)
            {
                scoreManager.IniciarSesion(nombreCancionActiva, totalNotas);
            }
        }

        // Solo ejecutar cuenta regresiva bloqueante si hay un countdownText asignado en la escena
        if (countdownText != null && noteSpawner != null)
        {
            noteSpawner.enabled = false;
            StartCoroutine(StartCountdown());
        }
    }

    IEnumerator StartCountdown()
    {
        int count = 3;
        while (count > 0)
        {
            if (countdownText != null) countdownText.text = count.ToString();
            yield return new WaitForSeconds(1f);
            count--;
        }

        if (countdownText != null)
        {
            countdownText.text = "¡Vamos!";
            yield return new WaitForSeconds(1f);
            countdownText.gameObject.SetActive(false);
        }

        if (noteSpawner != null) noteSpawner.enabled = true;
    }

    public void ShowResults()
    {
        if (scoreManager == null)
        {
            scoreManager = ScoreManager.Instance;
        }

        if (scoreManager != null)
        {
            float precision = scoreManager.ObtenerPorcentajePrecision();
            int estrellas = scoreManager.CalcularEstrellas(precision);

            if (finalScoreText != null)
                finalScoreText.text = $"Precisión: {precision:F1}%\nEstrellas: {estrellas}★";

            scoreManager.GuardarResultado();
        }

        // En ModoLibreMenu, delegar la animación visual de resultados a SongResultsAnimator
        if (SongResultsAnimator.Instance != null)
        {
            SongResultsAnimator.Instance.MostrarResultadosFinales();
            return;
        }

        if (resultsPanel != null)
        {
            resultsPanel.SetActive(true);
        }
    }

    public void RetryLevel()
    {
        if (SongResultsAnimator.Instance != null)
        {
            SongResultsAnimator.Instance.ReintentarCancion();
            return;
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ExitToMenu()
    {
        if (SongResultsAnimator.Instance != null)
        {
            SongResultsAnimator.Instance.VolverAlMenu();
            return;
        }
        SceneManager.LoadScene("ModoLibreMenu");
    }
}
