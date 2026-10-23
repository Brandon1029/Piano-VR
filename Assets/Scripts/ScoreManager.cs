using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("UI del Juego")]
    public TextMeshProUGUI precisionText;
    public TextMeshProUGUI comboText;

    [Header("Configuración de Estrellas (%)")]
    [Tooltip("Porcentaje mínimo requerido para obtener 1 Estrella")]
    [Range(0f, 100f)] public float umbralEstrella1 = 50f;

    [Tooltip("Porcentaje mínimo requerido para obtener 2 Estrellas")]
    [Range(0f, 100f)] public float umbralEstrella2 = 70f;

    [Tooltip("Porcentaje mínimo requerido para obtener 3 Estrellas")]
    [Range(0f, 100f)] public float umbralEstrella3 = 85f;

    public int TotalNotasCancion { get; private set; }
    public int NotasAcertadas { get; private set; }
    public int ComboActual { get; private set; }

    private string idCancionActual = "";

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    public void IniciarSesion(string nombreCancion, int totalNotas)
    {
        idCancionActual = nombreCancion;
        TotalNotasCancion = Mathf.Max(1, totalNotas);
        NotasAcertadas = 0;
        ComboActual = 0;
        ActualizarUI();
    }

    public void RegistrarAcierto()
    {
        NotasAcertadas++;
        ComboActual++;
        ActualizarUI();
    }

    public void RegistrarFallo()
    {
        ComboActual = 0;
        ActualizarUI();
    }

    // CORRECCIÓN CLAVE: Cast explícito en ambos términos para evitar división de enteros
    public float ObtenerPorcentajePrecision()
    {
        if (TotalNotasCancion <= 0) return 0f;
        
        float precision = ((float)NotasAcertadas / (float)TotalNotasCancion) * 100f;
        return precision;
    }

    public int CalcularEstrellas(float precision)
    {
        if (precision >= 100f) return 4; // 4 Estrellas (Perfecto)
        if (precision >= umbralEstrella3) return 3;
        if (precision >= umbralEstrella2) return 2;
        if (precision >= umbralEstrella1) return 1;
        return 0;
    }

    public void GuardarResultado()
{
    if (string.IsNullOrEmpty(idCancionActual)) return;

    float precisionActual = ObtenerPorcentajePrecision();
    float mejorPrecisionPreviamente = GetBestAccuracy(idCancionActual);

    int estrellasObtenidas = CalcularEstrellas(precisionActual);

    if (precisionActual > mejorPrecisionPreviamente)
    {
        PlayerPrefs.SetFloat("Accuracy_" + idCancionActual, precisionActual);
        PlayerPrefs.SetInt("Stars_" + idCancionActual, estrellasObtenidas);
    }

    PlayerPrefs.SetInt("Completed_" + idCancionActual, 1);
    PlayerPrefs.Save();

    Debug.Log($"[ScoreManager] '{idCancionActual}' finalizada. Precisión: {precisionActual:F1}% | Estrellas: {estrellasObtenidas}");

    // NOTIFICACIÓN EN TIEMPO REAL:
    // Actualiza automáticamente la interfaz del menú (récord y estrellas) sin romper los datos guardados
    if (SongSelectionMenu.Instance != null)
    {
        SongSelectionMenu.Instance.ActualizarRécordUI();
    }
}

    public static float GetBestAccuracy(string nombreCancion)
    {
        return PlayerPrefs.GetFloat("Accuracy_" + nombreCancion, 0f);
    }

    public static int GetBestStars(string nombreCancion)
    {
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