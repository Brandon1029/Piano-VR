using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    [Header("Referencias de Menús")]
    public GameObject mainMenu;
    public GameObject opcionesMenu;
    public GameObject terapiaMenu;

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
        Debug.Log("[MenuManager] Menú principal inicializado.");
    }

    public void CambiarVolumen(float valor)
    {
        float volumenDB = Mathf.Lerp(-30f, 0f, valor);
        if (audioMixer != null)
        {
            audioMixer.SetFloat("Volume", volumenDB);
        }
        Debug.Log($"[MenuManager] [UI] Volumen modificado a {valor:F2} ({volumenDB:F1} dB)");
    }

    // --- Botones ---
    public void IrModoLibre()
    {
        Debug.Log("[MenuManager] [UI] Botón Modo Libre presionado. Cargando 'ModoLibre'...");
        SceneManager.LoadScene("ModoLibre");
    }

    public void IrOpciones()
    {
        Debug.Log("[MenuManager] [UI] Botón Opciones presionado.");
        if (mainMenu != null) mainMenu.SetActive(false);
        if (opcionesMenu != null) opcionesMenu.SetActive(true);
    }

    public void VolverAlMainMenu()
    {
        Debug.Log("[MenuManager] [UI] Botón Volver al Menú Principal presionado.");
        if (opcionesMenu != null) opcionesMenu.SetActive(false);
        if (terapiaMenu != null) terapiaMenu.SetActive(false);
        if (mainMenu != null) mainMenu.SetActive(true);
    }

    public void SalirJuego()
    {
        Debug.Log("[MenuManager] [UI] Botón Salir del Juego presionado.");
        Application.Quit();
    }

    // --- Menú Terapia ---
    public void IrTerapeutico()
    {
        Debug.Log("[MenuManager] [UI] Botón Menú Terapéutico presionado.");
        if (mainMenu != null) mainMenu.SetActive(false);
        if (terapiaMenu != null) terapiaMenu.SetActive(true);
    }

    public void IrTerapia()
    {
        Debug.Log("[MenuManager] [UI] Botón Iniciar Terapia presionado. Cargando 'ModoTerapia'...");
        SceneManager.LoadScene("ModoTerapia");
    }
}
