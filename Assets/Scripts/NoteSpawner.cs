using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Controlador de generación de notas musicales y construcción de la Pista Frontal (Highway
/// estilo Guitar Hero / PianoVision) en ModoLibreMenu.
/// Mantiene compatibilidad con el modo de solo audio en ModoMusica.
/// </summary>
public class NoteSpawner : MonoBehaviour
{
    [Header("Configuración de la Canción")]
    public SongData song;
    public GameObject notePrefab;

    [Header("Configuración de Teclas")]
    [Tooltip("Arreglo de posiciones/transforms de teclas para el asignador automático de referencias.")]
    public Transform[] keyPositions; // Requerido por AutoAssignKeys.cs

    [Header("Pista Frontal (Highway Guitar Hero / PianoVision)")]
    [Tooltip("Activa la pista frontal inclinada hacia las teclas (se desactiva automáticamente en ModoMusica).")]
    public bool usarPistaFrontal = true;

    [Tooltip("Longitud total de la pista desde el borde trasero de las teclas hacia el frente (2.0 a 2.5 m).")]
    [Range(1.5f, 3.5f)]
    public float distanciaPista = 2.2f;

    [Tooltip("Ángulo de inclinación ergonómica de la pista hacia arriba en grados (10° a 15°).")]
    [Range(5f, 25f)]
    public float anguloInclinacionPista = 13f;

    [Tooltip("Tiempo en segundos que tarda una nota en recorrer toda la pista hasta la línea de impacto.")]
    [Range(0.8f, 5.0f)]
    public float tiempoAnticipacion = 2.5f;

    [Tooltip("Si está activo, ajusta tiempoAnticipacion según la dificultad de la canción (3.0s Principiante, 2.3s Intermedio, 1.7s Avanzado) sin alterar el BPM.")]
    public bool ajustarAnticipacionPorDificultad = true;
    public float anticipacionPrincipiante = 3.0f;
    public float anticipacionIntermedio   = 2.3f;
    public float anticipacionAvanzado     = 1.7f;

    [Header("Ajuste Fino de Borde de Teclas")]
    [Tooltip("Desplazamiento en Z desde el centro de las teclas blancas hasta su borde trasero.")]
    public float offsetZLineaImpacto = 0.068f;
    [Tooltip("Desplazamiento vertical sobre el centro de las teclas blancas para asentar la pista.")]
    public float offsetYLineaImpacto = 0.008f;

    [Header("Parámetros de Spawning (Fallback Vertical)")]
    public float spawnAlturaExtra = 0.5f;

    private Dictionary<string, Transform> keyMap = new Dictionary<string, Transform>();
    private List<GameObject> activeNotes = new List<GameObject>();
    private Coroutine spawnCoroutine;
    private float startTime;

    // Geometría calculada de la pista frontal
    private GameObject pistaRaizGO;
    private Quaternion rotacionPista = Quaternion.identity;
    private Vector3 direccionPistaHaciaFondo = Vector3.forward;
    private float zLineaImpacto = 2.533f;
    private float yLineaImpacto = 1.178f;
    private bool pistaInicializada = false;

    void Start()
    {
        if (SceneManager.GetActiveScene().name == "ModoMusica")
        {
            usarPistaFrontal = false;
        }

        MapPianoKeys();

        if (usarPistaFrontal)
        {
            StartCoroutine(InicializarPistaDiferida());
        }
    }

    private IEnumerator InicializarPistaDiferida()
    {
        // Esperar un frame por si las teclas terminan de registrarse en Start/Awake
        if (keyMap.Count == 0)
        {
            yield return null;
            MapPianoKeys();
        }

        ConstruirOPosicionarPistaVisual();
    }

    /// <summary>
    /// Mapea todas las teclas del piano en la escena asociándolas a su nombre (ej. C4, D#3, Db2).
    /// </summary>
    void MapPianoKeys()
    {
        if (keyPositions != null && keyPositions.Length > 0)
        {
            foreach (var key in keyPositions)
            {
                if (key != null && !keyMap.ContainsKey(key.name.Trim()))
                {
                    keyMap.Add(key.name.Trim(), key);
                }
            }
        }

        PianoKey[] keys = Object.FindObjectsByType<PianoKey>(FindObjectsSortMode.None);
        foreach (var key in keys)
        {
            if (key == null) continue;

            string keyName = key.gameObject.name.Trim();
            if (!keyMap.ContainsKey(keyName))
            {
                keyMap.Add(keyName, key.transform);
            }

            if (!string.IsNullOrEmpty(key.nombreNotaCompleto))
            {
                string notaCalc = key.nombreNotaCompleto.Trim();
                if (!keyMap.ContainsKey(notaCalc))
                {
                    keyMap.Add(notaCalc, key.transform);
                }
            }
        }
    }

    /// <summary>
    /// Construye la superficie guía oscura semitransparente detrás del piano,
    /// con líneas divisoras de tecla/octava y la línea brillante de impacto en el borde trasero.
    /// </summary>
    private void ConstruirOPosicionarPistaVisual()
    {
        if (!usarPistaFrontal) return;
        if (keyMap.Count == 0) MapPianoKeys();
        if (keyMap.Count == 0) return;

        // Recolectar transforms únicos de teclas blancas y de todas las teclas
        HashSet<Transform> uniqueKeys = new HashSet<Transform>();
        List<Transform> teclasBlancas = new List<Transform>();

        foreach (var kv in keyMap)
        {
            if (kv.Value == null || !uniqueKeys.Add(kv.Value)) continue;
            if (!kv.Value.name.Contains("#"))
            {
                teclasBlancas.Add(kv.Value);
            }
        }

        if (teclasBlancas.Count == 0)
        {
            teclasBlancas.AddRange(uniqueKeys);
        }

        teclasBlancas.Sort((a, b) => a.position.x.CompareTo(b.position.x));

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float sumaY = 0f;
        float sumaZ = 0f;

        foreach (var t in teclasBlancas)
        {
            if (t.position.x < minX) minX = t.position.x;
            if (t.position.x > maxX) maxX = t.position.x;
            sumaY += t.position.y;
            sumaZ += t.position.z;
        }

        float promY = sumaY / teclasBlancas.Count;
        float promZ = sumaZ / teclasBlancas.Count;

        // Paso promedio entre teclas blancas adyacentes (~23.8 mm)
        float pasoTeclaBlanca = teclasBlancas.Count > 1
            ? (maxX - minX) / (teclasBlancas.Count - 1)
            : 0.024f;

        float centroX = (minX + maxX) * 0.5f;
        float anchoTotalTeclado = (maxX - minX) + pasoTeclaBlanca * 1.35f;

        yLineaImpacto = promY + offsetYLineaImpacto;
        zLineaImpacto = promZ + offsetZLineaImpacto;

        // En Unity (mano izquierda), rotar -anguloInclinacionPista en X inclina el eje +Z hacia arriba (+Y)
        rotacionPista = Quaternion.Euler(-anguloInclinacionPista, 0f, 0f);
        direccionPistaHaciaFondo = (rotacionPista * Vector3.forward).normalized;

        if (pistaRaizGO != null)
        {
            Destroy(pistaRaizGO);
        }

        pistaRaizGO = new GameObject("PistaMusical_Highway");
        pistaRaizGO.transform.position = new Vector3(centroX, yLineaImpacto, zLineaImpacto);
        pistaRaizGO.transform.rotation = rotacionPista;

        // Materiales para la pista
        Material matSuperficie = CrearMaterialPista(
            new Color(0.03f, 0.04f, 0.07f, 0.82f),
            new Color(0.01f, 0.02f, 0.04f, 1f),
            true);

        Material matLineaCarril = CrearMaterialPista(
            new Color(0.55f, 0.72f, 0.90f, 0.16f),
            new Color(0.08f, 0.14f, 0.22f, 1f),
            true);

        Material matLineaOctava = CrearMaterialPista(
            new Color(0.35f, 0.82f, 1.00f, 0.42f),
            new Color(0.15f, 0.48f, 0.70f, 1f),
            true);

        Material matBordeLateral = CrearMaterialPista(
            new Color(0.14f, 0.55f, 0.82f, 0.70f),
            new Color(0.10f, 0.45f, 0.75f, 1f),
            true);

        Material matLineaImpacto = CrearMaterialPista(
            new Color(0.22f, 0.88f, 1.00f, 0.98f),
            new Color(0.20f, 0.85f, 1.00f, 1f) * 1.8f,
            false);

        // 1. Superficie principal oscura semitransparente (2.2 m hacia el frente)
        CrearCuboDecorativo(
            "SuperficiePista",
            pistaRaizGO.transform,
            new Vector3(0f, -0.0025f, distanciaPista * 0.5f),
            new Vector3(anchoTotalTeclado + 0.016f, 0.003f, distanciaPista),
            matSuperficie);

        // 2. Bordes laterales de la pista
        float xBorde = anchoTotalTeclado * 0.5f + 0.005f;
        CrearCuboDecorativo(
            "BordeIzquierdoPista",
            pistaRaizGO.transform,
            new Vector3(-xBorde, 0.002f, distanciaPista * 0.5f),
            new Vector3(0.006f, 0.008f, distanciaPista),
            matBordeLateral);

        CrearCuboDecorativo(
            "BordeDerechoPista",
            pistaRaizGO.transform,
            new Vector3(xBorde, 0.002f, distanciaPista * 0.5f),
            new Vector3(0.006f, 0.008f, distanciaPista),
            matBordeLateral);

        // 3. Línea brillante de impacto justo en el borde trasero de las teclas
        CrearCuboDecorativo(
            "LineaImpacto",
            pistaRaizGO.transform,
            new Vector3(0f, 0.0015f, 0.004f),
            new Vector3(anchoTotalTeclado + 0.012f, 0.0045f, 0.009f),
            matLineaImpacto);

        // 4. Líneas divisoras tenues alineadas entre teclas blancas y líneas destacadas por octava (antes de cada Do / C)
        for (int i = 0; i < teclasBlancas.Count - 1; i++)
        {
            Transform tActual = teclasBlancas[i];
            Transform tSig    = teclasBlancas[i + 1];

            float xDivisorMundo = (tActual.position.x + tSig.position.x) * 0.5f;
            float xLocal = xDivisorMundo - centroX;

            PianoKey pkSig = tSig.GetComponent<PianoKey>();
            bool esInicioOctava = (pkSig != null && pkSig.notaNombreBase == "C") ||
                                  tSig.name.Trim().StartsWith("C");

            CrearCuboDecorativo(
                esInicioOctava ? $"LineaOctava_{tSig.name}" : $"LineaCarril_{i}",
                pistaRaizGO.transform,
                new Vector3(xLocal, 0.0005f, distanciaPista * 0.5f),
                new Vector3(esInicioOctava ? 0.0024f : 0.0011f, 0.0015f, distanciaPista),
                esInicioOctava ? matLineaOctava : matLineaCarril);
        }

        pistaInicializada = true;
    }

    private void CrearCuboDecorativo(string nombre, Transform padre, Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = nombre;
        go.transform.SetParent(padre, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    private Material CrearMaterialPista(Color colorBase, Color colorEmision, bool transparente)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colorBase);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", colorBase);

        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", colorEmision);
        }

        if (transparente)
        {
            // Configurar transparencia compatible con URP Lit y Standard Shader
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // Transparent en URP
                mat.SetFloat("_Blend", 0f);
            }
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3f); // Transparent en Standard
            }

            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }

        return mat;
    }

    /// <summary>
    /// Devuelve el tiempo de anticipación activo en segundos según la configuración y dificultad,
    /// sin alterar el BPM ni el intervalo temporal entre las notas.
    /// </summary>
    public float ObtenerTiempoAnticipacionActivo()
    {
        if (!ajustarAnticipacionPorDificultad || song == null)
            return Mathf.Max(0.5f, tiempoAnticipacion);

        switch (song.difficulty)
        {
            case DifficultyLevel.Principiante:
                return Mathf.Max(0.5f, anticipacionPrincipiante);
            case DifficultyLevel.Intermedio:
                return Mathf.Max(0.5f, anticipacionIntermedio);
            case DifficultyLevel.Avanzado:
                return Mathf.Max(0.5f, anticipacionAvanzado);
            default:
                return Mathf.Max(0.5f, tiempoAnticipacion);
        }
    }

    /// <summary>
    /// Inicia la reproducción de una nueva pieza musical.
    /// </summary>
    public void StartSong(SongData newSong)
    {
        if (newSong == null) return;

        StopCurrentSong();

        song = newSong;
        startTime = Time.time;

        // 1. OBTENER TOTAL DE NOTAS REALES DE LA CANCIÓN
        int totalNotas = (song.notes != null) ? song.notes.Length : 0;

        // 2. INICIALIZAR SESIÓN EN SCOREMANAGER
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.IniciarSesion(song.songName, totalNotas);
        }

        if (PianoManager.Instance != null)
        {
            PianoManager.Instance.IniciarModoCancion(PianoManager.Instance.octavaBaseActual);
        }

        // En ModoLibreMenu, asegurar que el AutoPlayer permanezca desactivado al iniciar canción
        // a menos que el usuario lo haya activado explícitamente en el Toggle de Ajustes/Opciones.
        if (SceneManager.GetActiveScene().name == "ModoLibreMenu" && AutoPlayManager.Instance != null)
        {
            bool activadoEnUI = UIConfiguracionManager.Instance != null &&
                                UIConfiguracionManager.Instance.toggleAutoplayer != null &&
                                UIConfiguracionManager.Instance.toggleAutoplayer.isOn;
            if (!activadoEnUI)
            {
                AutoPlayManager.Instance.ToggleAutoPlay(false);
                if (PianoManager.Instance != null)
                {
                    PianoManager.Instance.autoplayerHabilitado = false;
                }
            }
        }

        if (usarPistaFrontal && !pistaInicializada)
        {
            MapPianoKeys();
            ConstruirOPosicionarPistaVisual();
        }

        spawnCoroutine = StartCoroutine(SpawnNotes());
    }

    /// <summary>
    /// Detiene la canción activa, destruye las notas flotantes y regresa a Modo Libre.
    /// </summary>
    public void StopCurrentSong()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        foreach (var note in activeNotes)
        {
            if (note != null) Destroy(note);
        }
        activeNotes.Clear();

        if (PianoManager.Instance != null)
        {
            PianoManager.Instance.TerminarModoCancion();
        }
    }

    /// <summary>
    /// Corrutina encargada de instanciar cada nota con la anticipación exacta para que llegue
    /// a la línea de impacto en su timestamp musical programado sin alterar el BPM.
    /// </summary>
    IEnumerator SpawnNotes()
    {
        if (song == null || song.notes == null || song.notes.Length == 0) yield break;

        MapPianoKeys();

        if (usarPistaFrontal)
        {
            if (!pistaInicializada)
            {
                ConstruirOPosicionarPistaVisual();
            }

            float anticipacion = ObtenerTiempoAnticipacionActivo();
            float primerTiempoNota = Mathf.Max(0f, song.notes[0].time);

            // Desplazar la línea de tiempo global si la primera nota empieza antes de tiempoAnticipacion,
            // garantizando que todas las barras recorran la pista completa conservando intactos los intervalos y el BPM.
            float offsetArranque = Mathf.Max(0f, anticipacion - primerTiempoNota);
            float duracionPulsoBase = (song.bpm > 10f) ? (60f / song.bpm) * 0.85f : 0.45f;

            for (int i = 0; i < song.notes.Length; i++)
            {
                SongData.NoteEvent note = song.notes[i];
                if (note == null || string.IsNullOrEmpty(note.key)) continue;

                float timestampImpacto = startTime + offsetArranque + note.time;
                float timestampSpawn   = timestampImpacto - anticipacion;

                float espera = timestampSpawn - Time.time;
                if (espera > 0f)
                {
                    yield return new WaitForSeconds(espera);
                }

                string claveNota = note.key.Trim();
                if (keyMap.TryGetValue(claveNota, out Transform keyTransform) && keyTransform != null)
                {
                    float duracionNota = CalcularDuracionVisualNota(i, claveNota, note.time, duracionPulsoBase);
                    bool esSostenido = claveNota.Contains("#") || keyTransform.name.Contains("#");

                    Vector3 puntoImpactoTecla = new Vector3(
                        keyTransform.position.x,
                        yLineaImpacto,
                        zLineaImpacto
                    );

                    Vector3 posOrigenPista = puntoImpactoTecla + direccionPistaHaciaFondo * distanciaPista;

                    GameObject notaGO = Instantiate(notePrefab, posOrigenPista, rotacionPista);
                    activeNotes.Add(notaGO);

                    NotaMovimiento mov = notaGO.GetComponent<NotaMovimiento>();
                    if (mov != null)
                    {
                        PianoKey pianoKey = keyTransform.GetComponent<PianoKey>();
                        mov.ConfigurarEnPistaFrontal(
                            pianoKey,
                            puntoImpactoTecla,
                            direccionPistaHaciaFondo,
                            rotacionPista,
                            distanciaPista,
                            anticipacion,
                            timestampImpacto,
                            duracionNota,
                            esSostenido);
                    }
                }
                else
                {
                    Debug.LogWarning($"[NoteSpawner] Tecla no encontrada en el piano: {note.key}");
                }
            }
        }
        else
        {
            // Modo vertical clásico (utilizado por las notas invisibles de audio en ModoMusica)
            foreach (var note in song.notes)
            {
                float delay = note.time - (Time.time - startTime);
                if (delay > 0) yield return new WaitForSeconds(delay);

                if (keyMap.TryGetValue(note.key.Trim(), out Transform keyTransform) && keyTransform != null)
                {
                    Vector3 spawnPos = new Vector3(
                        keyTransform.position.x,
                        keyTransform.position.y + spawnAlturaExtra,
                        keyTransform.position.z
                    );

                    GameObject nota = Instantiate(notePrefab, spawnPos, keyTransform.rotation);
                    activeNotes.Add(nota);

                    NotaMovimiento mov = nota.GetComponent<NotaMovimiento>();
                    if (mov != null)
                    {
                        PianoKey pianoKey = keyTransform.GetComponent<PianoKey>();
                        mov.SetTarget(keyTransform.position.y, spawnPos.y, pianoKey);
                    }
                }
                else
                {
                    Debug.LogWarning($"[NoteSpawner] Tecla no encontrada en el piano: {note.key}");
                }
            }
        }

        // Espera a que las últimas barras terminen de recorrer la pista y cruzar la línea de impacto
        float tiempoMaxEspera = usarPistaFrontal ? (ObtenerTiempoAnticipacionActivo() + 2.5f) : 3.5f;
        float tiempoEsperaFinal = 0f;
        while (PianoKey.notasEnEscena.Count > 0 && tiempoEsperaFinal < tiempoMaxEspera)
        {
            tiempoEsperaFinal += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(0.6f);

        // 1. Guardar resultados en el ScoreManager
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.GuardarResultado();
        }

        // 2. Refrescar el estado del botón y las estrellas en el menú principal
        if (SongSelectionMenu.Instance != null)
        {
            SongSelectionMenu.Instance.OnSongEnded();
        }

        // 3. Desactivar el modo canción en PianoManager al concluir la pista
        if (PianoManager.Instance != null)
        {
            PianoManager.Instance.TerminarModoCancion();
        }

        // 4. Muestra la pantalla de resultados si el GameManager o SongResultsAnimator está activo en la escena
        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm != null)
        {
            gm.ShowResults();
        }
        else if (SongResultsAnimator.Instance != null)
        {
            SongResultsAnimator.Instance.MostrarResultadosFinales();
        }
    }

    /// <summary>
    /// Calcula la duración visual de la barra de nota en función del BPM y del espacio libre
    /// hasta la siguiente repetición en la misma tecla para evitar solapamientos en el mismo carril.
    /// </summary>
    private float CalcularDuracionVisualNota(int indiceActual, string claveNota, float tiempoActual, float duracionBase)
    {
        float duracion = Mathf.Clamp(duracionBase, 0.24f, 0.90f);

        if (song != null && song.notes != null)
        {
            for (int j = indiceActual + 1; j < song.notes.Length; j++)
            {
                SongData.NoteEvent sig = song.notes[j];
                if (sig != null && !string.IsNullOrEmpty(sig.key) && sig.key.Trim() == claveNota)
                {
                    float intervaloMismaTecla = sig.time - tiempoActual;
                    if (intervaloMismaTecla > 0.05f)
                    {
                        // Dejar una pequeña separación visual respecto a la siguiente nota del mismo carril
                        duracion = Mathf.Min(duracion, Mathf.Max(0.18f, intervaloMismaTecla * 0.78f));
                    }
                    break;
                }
            }
        }

        return duracion;
    }
}
