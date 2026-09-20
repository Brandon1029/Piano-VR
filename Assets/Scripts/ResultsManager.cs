using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class ResultsManager : MonoBehaviour
{
    public GameObject resultsPanel;
    public TextMeshProUGUI finalScoreText;
    public ScoreManager scoreManager;

    public void ShowResults()
    {
        resultsPanel.SetActive(true);
        finalScoreText.text = "Puntaje final: " + scoreManager.score;
        PlayerPrefs.SetInt("HighScore_Sweden", scoreManager.score);
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
