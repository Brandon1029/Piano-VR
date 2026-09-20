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

    void Start()
    {
        noteSpawner.enabled = false;
        resultsPanel.SetActive(false);
        StartCoroutine(StartCountdown());
    }

    IEnumerator StartCountdown()
    {
        int count = 3;
        while (count > 0)
        {
            countdownText.text = count.ToString();
            yield return new WaitForSeconds(1f);
            count--;
        }

        countdownText.text = "¡Vamos!";
        yield return new WaitForSeconds(1f);
        countdownText.gameObject.SetActive(false);

        noteSpawner.enabled = true;
    }

    public void ShowResults()
    {
        resultsPanel.SetActive(true);
        finalScoreText.text = "Puntaje final: " + scoreManager.score;
        PlayerPrefs.SetInt("HighScore_Sweden", scoreManager.score);
    }

    public void RetryLevel()
    {
        SceneManager.LoadScene("ModoTerapia");
    }

    public void ExitToMenu()
    {
        SceneManager.LoadScene("ModoLibreMenu");
    }
}
