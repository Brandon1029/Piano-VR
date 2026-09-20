using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    [Header("Referencias de Menús")]
    public GameObject mainMenu;
    public GameObject opcionesMenu;
    public GameObject terapiaMenu; // nuevo menú de terapia

    [Header("UI Opciones")]
    public Slider volumenSlider;
    public AudioMixer audioMixer;

    void Start()
    {
        if (volumenSlider != null)
        {
            volumenSlider.value = 0.5f;
            volumenSlider.onValueChanged.AddListener(CambiarVolumen);
        }
    }

    public void CambiarVolumen(float valor)
    {
        float volumenDB = Mathf.Lerp(-30f, 0f, valor);
        audioMixer.SetFloat("Volume", volumenDB);
    }

    // --- Botones ---
    public void IrModoLibre() => SceneManager.LoadScene("ModoLibre");

    public void IrOpciones()
    {
        mainMenu.SetActive(false);
        opcionesMenu.SetActive(true);
    }

    public void VolverAlMainMenu()
    {
        opcionesMenu.SetActive(false);
        terapiaMenu.SetActive(false);
        mainMenu.SetActive(true);
    }

    public void SalirJuego() => Application.Quit();

    // --- Menú Terapia ---
    public void IrTerapeutico()
    {
        mainMenu.SetActive(false);
        terapiaMenu.SetActive(true);
    }

public void IrTerapia() => SceneManager.LoadScene("ModoTerapia");

}
