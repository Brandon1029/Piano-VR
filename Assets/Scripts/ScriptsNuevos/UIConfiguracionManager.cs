using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIConfiguracionManager : MonoBehaviour
{
    public static UIConfiguracionManager Instance { get; private set; }

    [Header("Paneles Principales")]
    public GameObject panelCanciones;
    public GameObject panelConfiguracion;

    [Header("Controles de Octava")]
    public TMP_Text txtRangoOctavas;
    public Button btnBajarOctava;
    public Button btnSubirOctava;

    [Header("Toggles de Opciones")]
    public Toggle togglePopUpNotas;
    public Toggle toggleMusicaAmbiental;
    public Toggle toggleAutoplayer;

    [Header("Referencias de Sistemas")]
    [Tooltip("Asigna la referencia del AutoPlayManager en la escena")]
    public AutoPlayManager autoPlayManager; // <--- Referencia directa a la clase de tu script

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (btnBajarOctava != null)
            btnBajarOctava.onClick.AddListener(BotonBajarOctava);

        if (btnSubirOctava != null)
            btnSubirOctava.onClick.AddListener(BotonSubirOctava);

        if (togglePopUpNotas != null)
            togglePopUpNotas.onValueChanged.AddListener(OnTogglePopUpChanged);

        if (toggleMusicaAmbiental != null)
            toggleMusicaAmbiental.onValueChanged.AddListener(OnToggleMusicaAmbientalChanged);

        if (toggleAutoplayer != null)
            toggleAutoplayer.onValueChanged.AddListener(OnToggleAutoplayerChanged);

        ActualizarTextoOctava();
    }

    public void AbrirConfiguracion()
    {
        if (panelCanciones != null) panelCanciones.SetActive(false);
        if (panelConfiguracion != null) panelConfiguracion.SetActive(true);
        ActualizarTextoOctava();
    }

    public void VolverACanciones()
    {
        if (panelConfiguracion != null) panelConfiguracion.SetActive(false);
        if (panelCanciones != null) panelCanciones.SetActive(true);
    }

    public void BotonSubirOctava()
    {
        if (PianoManager.Instance != null)
        {
            PianoManager.Instance.SubirOctava();
            ActualizarTextoOctava();
        }
    }

    public void BotonBajarOctava()
    {
        if (PianoManager.Instance != null)
        {
            PianoManager.Instance.BajarOctava();
            ActualizarTextoOctava();
        }
    }

    private void ActualizarTextoOctava()
    {
        if (txtRangoOctavas != null && PianoManager.Instance != null)
        {
            int baseOct = PianoManager.Instance.octavaBaseActual;
            txtRangoOctavas.text = $"C{baseOct} - C{baseOct + 5}";
        }
    }

    private void OnTogglePopUpChanged(bool estado)
    {
        foreach (var key in PianoKey.todasLasTeclas)
        {
            if (key != null)
            {
                key.mostrarPopUpNota = estado;
            }
        }
    }

    private void OnToggleMusicaAmbientalChanged(bool estado)
    {
        if (AmbientAudioManager.Instance != null)
        {
            AmbientAudioManager.Instance.SetAmbientHabilitado(estado);
        }
    }

    private void OnToggleAutoplayerChanged(bool estado)
    {
        if (PianoManager.Instance != null)
        {
            PianoManager.Instance.autoplayerHabilitado = estado;
        }

        // Modifica directamente la variable de control del AutoPlayManager
        if (autoPlayManager != null)
        {
            // Cambia la variable booleana expuesta en la casilla del Inspector
            autoPlayManager.isAutoPlayEnabled = estado; 

            // Opcional: También activa o desactiva el componente entero
            autoPlayManager.enabled = estado; 
        }
    }
}