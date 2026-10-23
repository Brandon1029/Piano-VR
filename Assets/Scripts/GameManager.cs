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
    noteSpawner.enabled = false;
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

    StartCoroutine(StartCountdown());
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

        noteSpawner.enabled = true;
    }

    public void ShowResults()
{
    if (resultsPanel != null) resultsPanel.SetActive(true);

    if (scoreManager != null)
    {
        float precision = scoreManager.ObtenerPorcentajePrecision();
        int estrellas = scoreManager.CalcularEstrellas(precision);

        if (finalScoreText != null)
            finalScoreText.text = $"Precisión: {precision:F1}%\nEstrellas: {estrellas}★";

        scoreManager.GuardarResultado();
    }
}

    public void RetryLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ExitToMenu()
    {
        SceneManager.LoadScene("ModoLibreMenu");
    }
}