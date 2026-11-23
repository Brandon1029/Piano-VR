using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("UI del Juego")]
    public TextMeshProUGUI precisionText;
    public TextMeshProUGUI comboText;

    [Header("Configuración de Estrellas (0 a 4 ★)")]
    [Tooltip("Porcentaje mínimo requerido para obtener 1 Estrella (>= 40%)")]
    [Range(0f, 100f)] public float umbralEstrella1 = 40f;

    [Tooltip("Porcentaje mínimo requerido para obtener 2 Estrellas (>= 65%)")]
    [Range(0f, 100f)] public float umbralEstrella2 = 65f;

    [Tooltip("Porcentaje mínimo requerido para obtener 3 Estrellas (>= 85%)")]
    [Range(0f, 100f)] public float umbralEstrella3 = 85f;

    [Tooltip("Porcentaje mínimo requerido para obtener 4 Estrellas (>= 95%)")]
    [Range(0f, 100f)] public float umbralEstrella4 = 95f;

    public int TotalNotasCancion { get; private set; }
    public int NotasAcertadas { get; private set; }
    public int ComboActual { get; private set; }
    public int ComboMaximoSesion { get; private set; }
    public string IdCancionActual => idCancionActual;
    public bool UltimoFueNuevoRecord { get; private set; }

    private string idCancionActual = "";

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }

        // Garantizar la escala oficial de 4 estrellas (1★ >= 40%, 2★ >= 65%, 3★ >= 85%, 4★ >= 95%)
        umbralEstrella1 = 40f;
        umbralEstrella2 = 65f;
        umbralEstrella3 = 85f;
        umbralEstrella4 = 95f;
    }

    public void IniciarSesion(string nombreCancion, int totalNotas)
    {
        idCancionActual = nombreCancion;
        TotalNotasCancion = Mathf.Max(1, totalNotas);
        NotasAcertadas = 0;
        ComboActual = 0;
        ComboMaximoSesion = 0;
        UltimoFueNuevoRecord = false;
        ActualizarUI();
    }

    public void RegistrarAcierto()
    {
        NotasAcertadas = Mathf.Min(TotalNotasCancion, NotasAcertadas + 1);
        ComboActual++;
        if (ComboActual > ComboMaximoSesion) ComboMaximoSesion = ComboActual;
        ActualizarUI();
    }

    public void RegistrarFallo()
    {
        ComboActual = 0;
        ActualizarUI();
    }

    public float ObtenerPorcentajePrecision()
    {
        if (TotalNotasCancion <= 0) return 0f;

        float precision = ((float)NotasAcertadas / (float)TotalNotasCancion) * 100f;
        return Mathf.Clamp(precision, 0f, 100f);
    }

    public int CalcularEstrellas(float precision)
    {
        if (precision >= umbralEstrella4) return 4;
        if (precision >= umbralEstrella3) return 3;
        if (precision >= umbralEstrella2) return 2;
        if (precision >= umbralEstrella1) return 1;
        return 0;
    }

    public void GuardarResultado()
    {
        if (string.IsNullOrEmpty(idCancionActual)) return;

        float precisionActual = ObtenerPorcentajePrecision();
        int estrellasObtenidas = CalcularEstrellas(precisionActual);

        float mejorPrecisionPrevia = GetBestAccuracy(idCancionActual);
        int mejoresEstrellasPrevias = GetBestStars(idCancionActual);

        bool superoPrecision = precisionActual > (mejorPrecisionPrevia + 0.01f);
        bool superoEstrellas = estrellasObtenidas > mejoresEstrellasPrevias;
        UltimoFueNuevoRecord = superoPrecision || superoEstrellas;

        // Conservar siempre la marca más alta en PlayerPrefs
        if (precisionActual > mejorPrecisionPrevia)
        {
            PlayerPrefs.SetFloat("Accuracy_" + idCancionActual, precisionActual);
        }
        if (estrellasObtenidas > mejoresEstrellasPrevias)
        {
            PlayerPrefs.SetInt("Stars_" + idCancionActual, estrellasObtenidas);
        }

        PlayerPrefs.SetInt("Completed_" + idCancionActual, 1);
        PlayerPrefs.Save();

        Debug.Log($"[ScoreManager] '{idCancionActual}' finalizada. Precisión: {precisionActual:F1}% | Estrellas: {estrellasObtenidas}/4 | NuevoRécord: {UltimoFueNuevoRecord}");

        if (SongSelectionMenu.Instance != null)
        {
            SongSelectionMenu.Instance.ActualizarRécordUI();
        }
    }

    public static float GetBestAccuracy(string nombreCancion)
    {
        if (string.IsNullOrEmpty(nombreCancion)) return 0f;
        return PlayerPrefs.GetFloat("Accuracy_" + nombreCancion, 0f);
    }

    public static int GetBestStars(string nombreCancion)
    {
        if (string.IsNullOrEmpty(nombreCancion)) return 0;
        return PlayerPrefs.GetInt("Stars_" + nombreCancion, 0);
    }

    private void ActualizarUI()
    {
        if (precisionText != null)
            precisionText.text = $"Precisión: {ObtenerPorcentajePrecision():F1}%";

        if (comboText != null)
            comboText.text = ComboActual > 1 ? $"Combo x{ComboActual}" : "";
    }
}
