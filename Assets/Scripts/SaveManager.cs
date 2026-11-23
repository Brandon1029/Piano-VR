using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Gestor centralizado y persistente de progreso por canción y configuración del usuario.
/// Almacena los datos localmente en el dispositivo mediante JSON en Application.persistentDataPath
/// y sincronización con PlayerPrefs, garantizando que toda build exportada inicie limpia en 0%
/// sin depender ni modificar jamás los assets SongData.
/// </summary>
public static class SaveManager
{
    private const string SaveFileName = "pianovr_savedata.json";

    // Claves de PlayerPrefs para ajustes
    private const string KeyMasterVolume      = "PianoVR_Setting_MasterVolume";
    private const string KeyBaseOctave        = "PianoVR_Setting_BaseOctave";
    private const string KeyShowNotePopups    = "PianoVR_Setting_ShowNotePopups";
    private const string KeyAmbientMusic      = "PianoVR_Setting_AmbientMusic";
    private const string KeySemitoneTolerance = "PianoVR_Setting_SemitoneTolerance";

    // Prefijos de PlayerPrefs para progreso de canciones
    private const string PrefixAccuracy  = "Accuracy_";
    private const string PrefixStars     = "Stars_";
    private const string PrefixCompleted = "Completed_";

    [Serializable]
    public class SongRecordEntry
    {
        public string songId = "";
        public float bestAccuracy = 0f;
        public int bestStars = 0;
        public bool completed = false;
    }

    [Serializable]
    public class UserSettingsData
    {
        public float masterVolume = 0.85f;
        public int baseOctave = 3;
        public bool showNotePopups = true;
        public bool ambientMusicEnabled = true;
        public int semitoneTolerance = 2;
    }

    [Serializable]
    public class SaveDatabase
    {
        public UserSettingsData settings = new UserSettingsData();
        public List<SongRecordEntry> songRecords = new List<SongRecordEntry>();
    }

    private static SaveDatabase _db;
    private static readonly Dictionary<string, SongRecordEntry> _recordsMap = new Dictionary<string, SongRecordEntry>(StringComparer.Ordinal);
    private static bool _initialized = false;
    private static bool _sceneHookRegistered = false;

    public static string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitializeOnLoad()
    {
        EnsureInitialized();

        if (!_sceneHookRegistered)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _sceneHookRegistered = true;
        }

        ApplySettingsToCurrentScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureInitialized();
        ApplySettingsToCurrentScene();
    }

    public static void EnsureInitialized()
    {
        if (_initialized && _db != null) return;
        LoadFromDisk();
        _initialized = true;
    }

    private static string NormalizeSongKey(string songId)
    {
        return string.IsNullOrEmpty(songId) ? "" : songId.Trim();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Progreso de Canciones (0% y 0★ por defecto)
    // ─────────────────────────────────────────────────────────────────────────

    public static float GetBestAccuracy(string songId)
    {
        EnsureInitialized();
        string key = NormalizeSongKey(songId);
        if (string.IsNullOrEmpty(key)) return 0f;

        if (_recordsMap.TryGetValue(key, out SongRecordEntry entry) && entry != null)
        {
            return Mathf.Clamp(entry.bestAccuracy, 0f, 100f);
        }

        if (PlayerPrefs.HasKey(PrefixAccuracy + key))
        {
            return Mathf.Clamp(PlayerPrefs.GetFloat(PrefixAccuracy + key, 0f), 0f, 100f);
        }

        return 0f;
    }

    public static int GetBestStars(string songId)
    {
        EnsureInitialized();
        string key = NormalizeSongKey(songId);
        if (string.IsNullOrEmpty(key)) return 0;

        if (_recordsMap.TryGetValue(key, out SongRecordEntry entry) && entry != null)
        {
            return Mathf.Clamp(entry.bestStars, 0, 4);
        }

        if (PlayerPrefs.HasKey(PrefixStars + key))
        {
            return Mathf.Clamp(PlayerPrefs.GetInt(PrefixStars + key, 0), 0, 4);
        }

        return 0;
    }

    public static bool IsSongCompleted(string songId)
    {
        EnsureInitialized();
        string key = NormalizeSongKey(songId);
        if (string.IsNullOrEmpty(key)) return false;

        if (_recordsMap.TryGetValue(key, out SongRecordEntry entry) && entry != null)
        {
            return entry.completed;
        }

        return PlayerPrefs.GetInt(PrefixCompleted + key, 0) == 1;
    }

    /// <summary>
    /// Guarda el resultado de una canción únicamente si supera el mejor porcentaje o estrellas previos.
    /// </summary>
    public static void SaveSongResult(string songId, float accuracy, int stars, out bool isNewRecord)
    {
        EnsureInitialized();
        isNewRecord = false;

        string key = NormalizeSongKey(songId);
        if (string.IsNullOrEmpty(key)) return;

        float clampedAcc = Mathf.Clamp(accuracy, 0f, 100f);
        int clampedStars = Mathf.Clamp(stars, 0, 4);

        float prevAcc = GetBestAccuracy(key);
        int prevStars = GetBestStars(key);

        bool beatAccuracy = clampedAcc > (prevAcc + 0.01f);
        bool beatStars    = clampedStars > prevStars;
        isNewRecord       = beatAccuracy || beatStars;

        if (!_recordsMap.TryGetValue(key, out SongRecordEntry entry) || entry == null)
        {
            entry = new SongRecordEntry
            {
                songId = key,
                bestAccuracy = 0f,
                bestStars = 0,
                completed = false
            };
            _recordsMap[key] = entry;
            _db.songRecords.Add(entry);
        }

        if (clampedAcc > entry.bestAccuracy)
        {
            entry.bestAccuracy = clampedAcc;
            PlayerPrefs.SetFloat(PrefixAccuracy + key, clampedAcc);
        }

        if (clampedStars > entry.bestStars)
        {
            entry.bestStars = clampedStars;
            PlayerPrefs.SetInt(PrefixStars + key, clampedStars);
        }

        entry.completed = true;
        PlayerPrefs.SetInt(PrefixCompleted + key, 1);

        SaveToDisk();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Configuración Persistente del Usuario
    // ─────────────────────────────────────────────────────────────────────────

    public static float GetMasterVolume()
    {
        EnsureInitialized();
        return Mathf.Clamp01(_db.settings.masterVolume);
    }

    public static void SetMasterVolume(float volume)
    {
        EnsureInitialized();
        float clamped = Mathf.Clamp01(volume);
        _db.settings.masterVolume = clamped;
        AudioListener.volume = clamped;
        PlayerPrefs.SetFloat(KeyMasterVolume, clamped);
        SaveToDisk();
    }

    public static int GetBaseOctave()
    {
        EnsureInitialized();
        return Mathf.Clamp(_db.settings.baseOctave, 1, 5);
    }

    public static void SetBaseOctave(int octave)
    {
        EnsureInitialized();
        int clamped = Mathf.Clamp(octave, 1, 5);
        _db.settings.baseOctave = clamped;
        PlayerPrefs.SetInt(KeyBaseOctave, clamped);
        SaveToDisk();
    }

    public static bool GetShowNotePopups()
    {
        EnsureInitialized();
        return _db.settings.showNotePopups;
    }

    public static void SetShowNotePopups(bool show)
    {
        EnsureInitialized();
        _db.settings.showNotePopups = show;
        PlayerPrefs.SetInt(KeyShowNotePopups, show ? 1 : 0);
        SaveToDisk();
    }

    public static bool GetAmbientMusicEnabled()
    {
        EnsureInitialized();
        return _db.settings.ambientMusicEnabled;
    }

    public static void SetAmbientMusicEnabled(bool enabled)
    {
        EnsureInitialized();
        _db.settings.ambientMusicEnabled = enabled;
        PlayerPrefs.SetInt(KeyAmbientMusic, enabled ? 1 : 0);
        SaveToDisk();
    }

    public static int GetSemitoneTolerance()
    {
        EnsureInitialized();
        return Mathf.Clamp(_db.settings.semitoneTolerance, 0, 6);
    }

    public static void SetSemitoneTolerance(int semitones)
    {
        EnsureInitialized();
        int clamped = Mathf.Clamp(semitones, 0, 6);
        _db.settings.semitoneTolerance = clamped;
        PlayerPrefs.SetInt(KeySemitoneTolerance, clamped);
        SaveToDisk();
    }

    /// <summary>
    /// Aplica los ajustes guardados a los sistemas activos de la escena actual (MainMenu_VR, ModoLibreMenu, ModoMusica, etc.).
    /// </summary>
    public static void ApplySettingsToCurrentScene()
    {
        EnsureInitialized();

        // 1. Volumen General
        AudioListener.volume = GetMasterVolume();

        // Vincular automáticamente el Slider_Volume del panel de ajustes de MainMenu_VR si existe en la escena
        Slider[] sliders = UnityEngine.Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sliders.Length; i++)
        {
            Slider s = sliders[i];
            if (s != null && (s.name == "Slider_Volume" || s.name.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                s.SetValueWithoutNotify(GetMasterVolume());
                s.onValueChanged.RemoveListener(OnMainMenuVolumeSliderChanged);
                s.onValueChanged.AddListener(OnMainMenuVolumeSliderChanged);
            }
        }

        // 2. Octava Base en PianoManager
        if (PianoManager.Instance != null && !PianoManager.Instance.modoCancionActivo)
        {
            PianoManager.Instance.AplicarOctavaBase(GetBaseOctave());
        }

        // 3. Pop-ups de notas en todas las teclas
        bool showPopups = GetShowNotePopups();
        for (int i = 0; i < PianoKey.todasLasTeclas.Count; i++)
        {
            if (PianoKey.todasLasTeclas[i] != null)
            {
                PianoKey.todasLasTeclas[i].mostrarPopUpNota = showPopups;
            }
        }

        // 4. Música Ambiental
        if (AmbientAudioManager.Instance != null)
        {
            AmbientAudioManager.Instance.SetAmbientHabilitado(GetAmbientMusicEnabled());
        }
    }

    private static void OnMainMenuVolumeSliderChanged(float nuevoVolumen)
    {
        SetMasterVolume(nuevoVolumen);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Serialización en Disco (Application.persistentDataPath + PlayerPrefs)
    // ─────────────────────────────────────────────────────────────────────────

    private static void LoadFromDisk()
    {
        _db = null;
        _recordsMap.Clear();

        try
        {
            string path = SaveFilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    _db = JsonUtility.FromJson<SaveDatabase>(json);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SaveManager] No se pudo leer el archivo de guardado local: {ex.Message}");
        }

        if (_db == null)
        {
            _db = new SaveDatabase();

            // Migrar ajustes si ya existían en PlayerPrefs
            if (PlayerPrefs.HasKey(KeyMasterVolume))
                _db.settings.masterVolume = PlayerPrefs.GetFloat(KeyMasterVolume, 0.85f);
            if (PlayerPrefs.HasKey(KeyBaseOctave))
                _db.settings.baseOctave = PlayerPrefs.GetInt(KeyBaseOctave, 3);
            if (PlayerPrefs.HasKey(KeyShowNotePopups))
                _db.settings.showNotePopups = PlayerPrefs.GetInt(KeyShowNotePopups, 1) == 1;
            if (PlayerPrefs.HasKey(KeyAmbientMusic))
                _db.settings.ambientMusicEnabled = PlayerPrefs.GetInt(KeyAmbientMusic, 1) == 1;
            if (PlayerPrefs.HasKey(KeySemitoneTolerance))
                _db.settings.semitoneTolerance = PlayerPrefs.GetInt(KeySemitoneTolerance, 2);
        }

        if (_db.settings == null)
            _db.settings = new UserSettingsData();

        if (_db.songRecords == null)
            _db.songRecords = new List<SongRecordEntry>();

        for (int i = 0; i < _db.songRecords.Count; i++)
        {
            SongRecordEntry entry = _db.songRecords[i];
            if (entry != null && !string.IsNullOrEmpty(entry.songId))
            {
                string k = NormalizeSongKey(entry.songId);
                _recordsMap[k] = entry;
            }
        }
    }

    private static void SaveToDisk()
    {
        try
        {
            PlayerPrefs.Save();
            if (_db != null)
            {
                string json = JsonUtility.ToJson(_db, true);
                File.WriteAllText(SaveFilePath, json);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SaveManager] Error al guardar en disco: {ex.Message}");
        }
    }

    /// <summary>
    /// Borra todos los récords de canciones y restaura los ajustes por defecto (estado usuario nuevo 0%).
    /// </summary>
    public static void ResetAllProgressAndSettings()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        try
        {
            string path = SaveFilePath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SaveManager] No se pudo eliminar el archivo de guardado: {ex.Message}");
        }

        _db = new SaveDatabase();
        _recordsMap.Clear();
        _initialized = true;

        if (Application.isPlaying)
        {
            ApplySettingsToCurrentScene();

            if (SongSelectionMenu.Instance != null)
            {
                SongSelectionMenu.Instance.ActualizarRécordUI();
            }
            if (SongResultsAnimator.Instance != null)
            {
                SongResultsAnimator.Instance.SincronizarTarjetasDeLista();
            }
        }

        Debug.Log("[SaveManager] Todo el progreso de canciones (0% / 0★) y los ajustes guardados han sido reiniciados.");
    }

#if UNITY_EDITOR
    [MenuItem("PianoVR/Borrar Progreso y Ajustes Guardados")]
    public static void EditorMenuResetProgress()
    {
        ResetAllProgressAndSettings();
    }
#endif
}
