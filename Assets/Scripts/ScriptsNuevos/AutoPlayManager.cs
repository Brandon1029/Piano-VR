using UnityEngine;
using UnityEngine.SceneManagement;

public class AutoPlayManager : MonoBehaviour
{
    public static AutoPlayManager Instance { get; private set; }

    [Header("Configuración")]
    [Tooltip("Activa o desactiva la reproducción automática de las notas al caer.")]
    public bool isAutoPlayEnabled = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        // En ModoLibreMenu el AutoPlayer debe arrancar siempre DESACTIVADO por defecto
        // y solo activarse cuando el jugador lo habilite explícitamente desde la UI de Ajustes.
        // En ModoMusica permanece activo para reproducción de audio de las canciones.
        if (SceneManager.GetActiveScene().name == "ModoLibreMenu" || !enabled)
        {
            isAutoPlayEnabled = false;
            enabled = false;
        }
    }

    public void ToggleAutoPlay(bool value)
    {
        isAutoPlayEnabled = value;
        enabled = value;
        Debug.Log($"[AutoPlayManager] Modo automático: {(isAutoPlayEnabled ? "ACTIVADO" : "DESACTIVADO")}");
    }
}
