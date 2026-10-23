using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class AmbientAudioManager : MonoBehaviour
{
    public static AmbientAudioManager Instance { get; private set; }

    [Header("Configuración de Sonido Ambiental")]
    [Tooltip("Volumen máximo de la música de fondo en inactividad")]
    [Range(0f, 1f)]
    public float volumenMaximo = 0.35f;

    [Tooltip("Tiempo en segundos sin tocar teclas antes de reactivar la música ambiental")]
    public float tiempoEsperaParaReactivar = 4f;

    [Tooltip("Velocidad con la que sube y baja el volumen (Fade In / Fade Out)")]
    public float velocidadFade = 1.2f;

    [Header("Estado")]
    public bool ambientHabilitado = true;

    private AudioSource audioSource;
    private float tiempoDesdeUltimaInteraccion = 0f;
    private float volumenObjetivo = 0f;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        if (audioSource.clip != null)
        {
            audioSource.loop = true;
            audioSource.volume = 0f;
            if (!audioSource.isPlaying) audioSource.Play();
        }
    }

    void Update()
    {
        // Si el ambient está desactivado o hay una canción en curso en el NoteSpawner, silencia la música de fondo
        if (!ambientHabilitado || (PianoManager.Instance != null && PianoManager.Instance.modoCancionActivo))
        {
            volumenObjetivo = 0f;
        }
        else
        {
            tiempoDesdeUltimaInteraccion += Time.deltaTime;

            if (tiempoDesdeUltimaInteraccion >= tiempoEsperaParaReactivar)
            {
                volumenObjetivo = volumenMaximo;
            }
            else
            {
                volumenObjetivo = 0f;
            }
        }

        // Transición suave del volumen
        audioSource.volume = Mathf.MoveTowards(audioSource.volume, volumenObjetivo, velocidadFade * Time.deltaTime);
    }

    /// <summary>
    /// Notifica que el usuario ha interactuado con el piano para silenciar la música ambiental.
    /// </summary>
    public void NotificarTeclaTocada()
    {
        tiempoDesdeUltimaInteraccion = 0f;
    }

    /// <summary>
    /// Habilita o deshabilita la opción desde el panel de UI.
    /// </summary>
    public void SetAmbientHabilitado(bool estado)
    {
        ambientHabilitado = estado;
    }
}