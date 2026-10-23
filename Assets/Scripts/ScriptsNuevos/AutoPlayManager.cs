using UnityEngine;

public class AutoPlayManager : MonoBehaviour
{
    public static AutoPlayManager Instance { get; private set; }

    [Header("Configuración")]
    [Tooltip("Activa o desactiva la reproducción automática de las notas al caer.")]
    public bool isAutoPlayEnabled = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void ToggleAutoPlay(bool value)
    {
        isAutoPlayEnabled = value;
        Debug.Log($"[AutoPlayManager] Modo automático: {(isAutoPlayEnabled ? "ACTIVADO" : "DESACTIVADO")}");
    }
}