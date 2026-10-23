using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PianoManager : MonoBehaviour
{
    public static PianoManager Instance { get; private set; }

    [Header("Configuración de Octavas")]
    [Tooltip("Octava en la que inicia la primera tecla 'C' del piano.")]
    public int octavaBaseActual = 3;

    [Tooltip("Límite mínimo de octava base que puede seleccionar el usuario.")]
    public int octavaMinima = 1;

    [Tooltip("Límite máximo de octava base que puede seleccionar el usuario.")]
    public int octavaMaxima = 5;

    [Header("Estado del Juego")]
    public bool modoCancionActivo = false;
    public bool autoplayerHabilitado = false;
    private int octavaGuardadaModoLibre = 3;

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
        // Aplica la octava inicial configurada al arrancar la escena
        AplicarOctavaBase(octavaBaseActual);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Permite que cualquier cambio en la variable desde el Inspector
    /// se aplique instantáneamente durante el modo Play.
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying && Instance != null)
        {
            AplicarOctavaBase(octavaBaseActual);
        }
    }
#endif

    /// <summary>
    /// Cambia la octava base del piano y reconfigura todas las teclas activas.
    /// </summary>
    [ContextMenu("Forzar Actualización de Octava")]
    public void AplicarOctavaBase(int nuevaOctavaBase)
    {
        octavaBaseActual = Mathf.Clamp(nuevaOctavaBase, octavaMinima, octavaMaxima);

        foreach (var key in PianoKey.todasLasTeclas)
        {
            if (key != null)
            {
                key.ReconfigurarOctava(octavaBaseActual);
            }
        }

        Debug.Log($"[PianoManager] Octava base actualizada a: {octavaBaseActual}");
    }

    /// <summary>
    /// Sube una octava (para el botón '>' de la UI).
    /// </summary>
    public void SubirOctava()
    {
        if (modoCancionActivo) return; // En modo canción no se cambia manualmente
        AplicarOctavaBase(octavaBaseActual + 1);
    }

    /// <summary>
    /// Baja una octava (para el botón '<' de la UI).
    /// </summary>
    public void BajarOctava()
    {
        if (modoCancionActivo) return; // En modo canción no se cambia manualmente
        AplicarOctavaBase(octavaBaseActual - 1);
    }

    /// <summary>
    /// Se llama cuando una canción inicia para fijar el rango que esta necesita.
    /// </summary>
    public void IniciarModoCancion(int octavaRequerida)
    {
        if (!modoCancionActivo)
        {
            octavaGuardadaModoLibre = octavaBaseActual; // Guarda la preferencia del usuario
            modoCancionActivo = true;
        }

        AplicarOctavaBase(octavaRequerida);
    }

    /// <summary>
    /// Se llama al terminar/cancelar la canción para volver a la configuración del Modo Libre.
    /// </summary>
    public void TerminarModoCancion()
    {
        modoCancionActivo = false;
        AplicarOctavaBase(octavaGuardadaModoLibre); // Restaura la octava del Modo Libre
    }
}