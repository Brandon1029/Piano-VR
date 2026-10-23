using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class ResultsManager : MonoBehaviour
{
    public GameObject resultsPanel;
    public TextMeshProUGUI finalScoreText;
    public ScoreManager scoreManager;

    /// <summary>
    /// Muestra la pantalla de resultados y guarda la puntuación máxima de la canción activa.
    /// </summary>
    public void ShowResults(string songName = "")
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
        SceneManager.LoadScene("MainMenu");
    }
}