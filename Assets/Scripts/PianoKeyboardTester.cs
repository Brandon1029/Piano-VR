using UnityEngine;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Permite probar la interacción del Piano y la UI mediante el TECLADO y RATÓN en el Unity Editor
/// o en compilaciones de PC sin necesidad de conectar las gafas Meta Quest.
/// </summary>
public class PianoKeyboardTester : MonoBehaviour
{
    private static PianoKeyboardTester instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInicializar()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (instance == null)
        {
            GameObject testerObj = new GameObject("[Editor_PianoKeyboardTester]");
            instance = testerObj.AddComponent<PianoKeyboardTester>();
            DontDestroyOnLoad(testerObj);
            Debug.Log("[PianoKeyboardTester] Soporte de pruebas de Teclado y Ratón para PC inicializado.");
            Debug.Log("[PianoKeyboardTester] Controles: A=Do, W=Do#, S=Re, E=Re#, D=Mi, F=Fa, T=Fa#, G=Sol, Y=Sol#, H=La, U=La#, J=Si, K=Do(sig) | Z=Bajar octava, X=Subir octava, Barra Espaciadora=Play/Stop");
        }
#endif
    }

    // Teclas presionadas actualmente para poder hacer OnPokeExit al soltar
    private Dictionary<string, PianoKey> teclasActivasPorNota = new Dictionary<string, PianoKey>();

    void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        ProcesarTeclado();
#endif
    }

#if UNITY_EDITOR || UNITY_STANDALONE
    private void ProcesarTeclado()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;

        // Notas musicales (1 octava completa + extensiones)
        ManejarNota("C",  0, kb.aKey.wasPressedThisFrame, kb.aKey.wasReleasedThisFrame, "A");
        ManejarNota("C#", 0, kb.wKey.wasPressedThisFrame, kb.wKey.wasReleasedThisFrame, "W");
        ManejarNota("D",  0, kb.sKey.wasPressedThisFrame, kb.sKey.wasReleasedThisFrame, "S");
        ManejarNota("D#", 0, kb.eKey.wasPressedThisFrame, kb.eKey.wasReleasedThisFrame, "E");
        ManejarNota("E",  0, kb.dKey.wasPressedThisFrame, kb.dKey.wasReleasedThisFrame, "D");
        ManejarNota("F",  0, kb.fKey.wasPressedThisFrame, kb.fKey.wasReleasedThisFrame, "F");
        ManejarNota("F#", 0, kb.tKey.wasPressedThisFrame, kb.tKey.wasReleasedThisFrame, "T");
        ManejarNota("G",  0, kb.gKey.wasPressedThisFrame, kb.gKey.wasReleasedThisFrame, "G");
        ManejarNota("G#", 0, kb.yKey.wasPressedThisFrame, kb.yKey.wasReleasedThisFrame, "Y");
        ManejarNota("A",  0, kb.hKey.wasPressedThisFrame, kb.hKey.wasReleasedThisFrame, "H");
        ManejarNota("A#", 0, kb.uKey.wasPressedThisFrame, kb.uKey.wasReleasedThisFrame, "U");
        ManejarNota("B",  0, kb.jKey.wasPressedThisFrame, kb.jKey.wasReleasedThisFrame, "J");
        ManejarNota("C",  1, kb.kKey.wasPressedThisFrame, kb.kKey.wasReleasedThisFrame, "K");
        ManejarNota("C#", 1, kb.oKey.wasPressedThisFrame, kb.oKey.wasReleasedThisFrame, "O");
        ManejarNota("D",  1, kb.lKey.wasPressedThisFrame, kb.lKey.wasReleasedThisFrame, "L");

        // Cambios de octava
        if (kb.zKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
        {
            if (PianoManager.Instance != null)
            {
                Debug.Log("[PianoKeyboardTester] [PC Teclado 'Z'/Flecha Izq] Bajar octava");
                PianoManager.Instance.BajarOctava();
            }
        }
        if (kb.xKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
        {
            if (PianoManager.Instance != null)
            {
                Debug.Log("[PianoKeyboardTester] [PC Teclado 'X'/Flecha Der] Subir octava");
                PianoManager.Instance.SubirOctava();
            }
        }

        // Play/Stop canción
        if (kb.spaceKey.wasPressedThisFrame)
        {
            if (SongSelectionMenu.Instance != null)
            {
                Debug.Log("[PianoKeyboardTester] [PC Teclado 'Espacio'] Alternar reproducción de canción");
                // Dispara el evento del botón de reproducción
                var btnPlayField = typeof(SongSelectionMenu).GetField("btnPlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (btnPlayField != null)
                {
                    var btn = btnPlayField.GetValue(SongSelectionMenu.Instance) as UnityEngine.UI.Button;
                    btn?.onClick.Invoke();
                }
            }
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        // Respaldo Legacy Input
        ManejarNota("C",  0, Input.GetKeyDown(KeyCode.A), Input.GetKeyUp(KeyCode.A), "A");
        ManejarNota("C#", 0, Input.GetKeyDown(KeyCode.W), Input.GetKeyUp(KeyCode.W), "W");
        ManejarNota("D",  0, Input.GetKeyDown(KeyCode.S), Input.GetKeyUp(KeyCode.S), "S");
        ManejarNota("D#", 0, Input.GetKeyDown(KeyCode.E), Input.GetKeyUp(KeyCode.E), "E");
        ManejarNota("E",  0, Input.GetKeyDown(KeyCode.D), Input.GetKeyUp(KeyCode.D), "D");
        ManejarNota("F",  0, Input.GetKeyDown(KeyCode.F), Input.GetKeyUp(KeyCode.F), "F");
        ManejarNota("F#", 0, Input.GetKeyDown(KeyCode.T), Input.GetKeyUp(KeyCode.T), "T");
        ManejarNota("G",  0, Input.GetKeyDown(KeyCode.G), Input.GetKeyUp(KeyCode.G), "G");
        ManejarNota("G#", 0, Input.GetKeyDown(KeyCode.Y), Input.GetKeyUp(KeyCode.Y), "Y");
        ManejarNota("A",  0, Input.GetKeyDown(KeyCode.H), Input.GetKeyUp(KeyCode.H), "H");
        ManejarNota("A#", 0, Input.GetKeyDown(KeyCode.U), Input.GetKeyUp(KeyCode.U), "U");
        ManejarNota("B",  0, Input.GetKeyDown(KeyCode.J), Input.GetKeyUp(KeyCode.J), "J");
        ManejarNota("C",  1, Input.GetKeyDown(KeyCode.K), Input.GetKeyUp(KeyCode.K), "K");
        ManejarNota("C#", 1, Input.GetKeyDown(KeyCode.O), Input.GetKeyUp(KeyCode.O), "O");
        ManejarNota("D",  1, Input.GetKeyDown(KeyCode.L), Input.GetKeyUp(KeyCode.L), "L");

        if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (PianoManager.Instance != null) PianoManager.Instance.BajarOctava();
        }
        if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (PianoManager.Instance != null) PianoManager.Instance.SubirOctava();
        }
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (SongSelectionMenu.Instance != null)
            {
                var btnPlayField = typeof(SongSelectionMenu).GetField("btnPlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (btnPlayField != null)
                {
                    var btn = btnPlayField.GetValue(SongSelectionMenu.Instance) as UnityEngine.UI.Button;
                    btn?.onClick.Invoke();
                }
            }
        }
#endif
    }

    private void ManejarNota(string notaBase, int offsetOctava, bool pressed, bool released, string letraTeclado)
    {
        int octavaBaseActual = (PianoManager.Instance != null) ? PianoManager.Instance.octavaBaseActual : 3;
        int octavaDeseada = octavaBaseActual + offsetOctava;
        string nombreBuscado = notaBase + octavaDeseada;

        if (pressed)
        {
            PianoKey key = EncontrarTecla(notaBase, octavaDeseada);
            if (key != null)
            {
                Debug.Log($"[PianoKeyboardTester] [PC Teclado '{letraTeclado}'] Tocando tecla '{key.nombreNotaCompleto}'");
                teclasActivasPorNota[nombreBuscado] = key;
                key.OnPokeEnter();
            }
            else
            {
                Debug.LogWarning($"[PianoKeyboardTester] No se encontró tecla física para '{nombreBuscado}'");
            }
        }
        else if (released)
        {
            if (teclasActivasPorNota.TryGetValue(nombreBuscado, out PianoKey key) && key != null)
            {
                Debug.Log($"[PianoKeyboardTester] [PC Teclado '{letraTeclado}'] Soltando tecla '{key.nombreNotaCompleto}'");
                key.OnPokeExit();
                teclasActivasPorNota.Remove(nombreBuscado);
            }
        }
    }

    private PianoKey EncontrarTecla(string notaBase, int octava)
    {
        foreach (var k in PianoKey.todasLasTeclas)
        {
            if (k != null && k.notaNombreBase == notaBase && k.octavaCalculada == octava)
            {
                return k;
            }
        }

        // Fallback: buscar solo por nota base si la octava no coincide exactamente
        foreach (var k in PianoKey.todasLasTeclas)
        {
            if (k != null && k.notaNombreBase == notaBase)
            {
                return k;
            }
        }

        return null;
    }
#endif
}
