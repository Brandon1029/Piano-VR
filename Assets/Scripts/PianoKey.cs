using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class PianoKey : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler
{
    [Header("Física y Movimiento")]
    public float pressDepth  = 0.008f;
    public float pressSpeed  = 20f;
    public float returnSpeed = 8f;

    [Header("Protección al Iniciar la Escena")]
    [Tooltip("Tiempo en segundos tras cargar la escena donde se ignoran colisiones para evitar que las teclas se presionen solas al arrancar.")]
    public float startupGracePeriod = 0.25f;

    [Header("Configuración de Impacto")]
    [Tooltip("Distancia vertical máxima (en metros) entre la nota y la tecla para considerar un acierto.")]
    public float rangoToleranciaHit = 0.08f;

    [Header("Colores de Turno")]
    [Tooltip("Color de la tecla que debe tocarse ya")]
    [ColorUsage(true, true)]
    public Color colorTurnoActual = new Color(1f, 0.92f, 0.016f, 1f); // Amarillo neón

    [Tooltip("Color de las teclas que tienen notas en camino")]
    [ColorUsage(true, true)]
    public Color colorEnEspera = new Color(0f, 0.6f, 1f, 1f);        // Azul cian

    [Tooltip("Intensidad máxima del brillo HDR")]
    public float maxGlowIntensity = 2.5f;

    [Header("Datos de la Nota Dinámica")]
    public string notaNombreBase = "";     // Ej: "C", "C#", "D"
    public int offsetOctavaRelativa = 0;    // 0 para la 1ra octava física, 1 para la 2da, etc.
    public int octavaCalculada = 4;        // Octava resultante activa
    public string nombreNotaCompleto = ""; // Ej: "C4", "A#3"

    [Header("Etiqueta Visual de Octava")]
    public TMPro.TMP_Text textoEtiquetaOctava;

    [Header("Pop-up Visual de Nota")]
    public GameObject prefabPopUpNota;
    public bool mostrarPopUpNota = true;

    AudioSource audioSource;
    Rigidbody   rb;
    BoxCollider boxCollider;
    Vector3     restLocalPosition;
    Vector3     pressedLocalPosition;
    public bool isPressed = false;

    Renderer keyRenderer;
    MaterialPropertyBlock propBlock;
    static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

    public static List<PianoKey> todasLasTeclas = new List<PianoKey>();
    public static List<NotaMovimiento> notasEnEscena = new List<NotaMovimiento>();

    // Contador de interactores/contactos activos para evitar dobles disparos o liberaciones prematuras
    private int interactoresActivos = 0;

    // Diccionario original para validación de notas base
    static readonly Dictionary<string, string> notaES = new Dictionary<string, string>()
    {
        {"C", "C"}, {"D", "D"}, {"E", "E"},
        {"F", "F"}, {"G", "G"}, {"A", "A"}, {"B", "B"}
    };

    // Palabras clave para reconocer colisionadores de manos y dedos (Hand Tracking) de Meta Quest.
    // Se excluyen explícitamente mandos y controladores para que NO toquen teclas por error físico.
    private static readonly string[] PalabrasClaveVR = new string[]
    {
        "poke", "hand", "finger", "index", "tip",
        "interactor", "capsule", "xr", "mano", "dedo",
        "distal", "ring", "middle", "pinky", "little", "thumb",
        "touch", "direct", "bone", "joint",
        "wrist", "palm", "b_r_", "b_l_", "ovr"
    };

    // ── Control centralizado de dedos (identificador único = Transform) ─────
    // Comportamiento de clic de ratón: Down -> isPressed = true, Up -> isPressed = false.
    // Sin cooldowns ni temporizadores: solo el contacto físico decide.
    private readonly HashSet<Transform> dedosPresionando = new HashSet<Transform>();

    /// <summary>
    /// Intenta presionar la tecla con un dedo. Devuelve true solo si la tecla estaba
    /// libre (ningún dedo ni interactor) y este dedo pasa a ser su dueño.
    /// </summary>
    public bool RegistrarToqueDedo(Transform dedo)
    {
        if (dedo == null) return false;
        if (Time.timeSinceLevelLoad < startupGracePeriod) return false;
        if (dedosPresionando.Contains(dedo)) return true; // ya es el dueño, no vuelve a sonar

        // Tecla ocupada (hundida, otro dedo o un interactor/rayo) -> rechazar
        if (isPressed || dedosPresionando.Count > 0 || interactoresActivos > 0) return false;

        dedosPresionando.Add(dedo);
        OnPokeEnter(); // isPressed = true
        return true;
    }

    /// <summary>
    /// Libera el dedo. Si ya no queda ningún dedo ni interactor, la tecla vuelve a reposo.
    /// </summary>
    public void LiberarToqueDedo(Transform dedo)
    {
        if (dedo == null) return;

        if (dedosPresionando.Remove(dedo) &&
            dedosPresionando.Count == 0 && interactoresActivos == 0 && isPressed)
        {
            OnPokeExit(); // isPressed = false
        }
    }

    /// <summary>
    /// Comprobación de salida compartida por HandColliders y FingerPianoTouch:
    /// el dedo sigue "dentro" si está a menos de 'radio' del collider de la tecla,
    /// evaluado en su posición y en su posición desplazada pressDepth hacia abajo
    /// (compensa el hundimiento de la tecla con el dedo quieto). No usa OnTriggerExit.
    /// </summary>
    public bool DedoDentroDeVolumen(Vector3 posicionDedo, float radio)
    {
        Collider col = boxCollider != null ? boxCollider : GetComponent<Collider>();
        if (col == null) return false;

        Vector3 posCompensada = posicionDedo + Vector3.down * pressDepth;

        return Vector3.Distance(posicionDedo,  col.ClosestPoint(posicionDedo))  <= radio ||
               Vector3.Distance(posCompensada, col.ClosestPoint(posCompensada)) <= radio;
    }

    void Awake()
    {
        keyRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();
        if (!todasLasTeclas.Contains(this))
            todasLasTeclas.Add(this);

        ExtraerNotaYOctavaInicial();
        AsegurarFisicasTecla();
    }

    void OnDestroy()
    {
        todasLasTeclas.Remove(this);
    }

    void Start()
    {
        audioSource          = GetComponent<AudioSource>();
        rb                   = GetComponent<Rigidbody>();
        boxCollider          = GetComponent<BoxCollider>();
        restLocalPosition    = transform.localPosition;
        pressedLocalPosition = restLocalPosition - new Vector3(0, pressDepth, 0);

        int octavaBase = (PianoManager.Instance != null) ? PianoManager.Instance.octavaBaseActual : 3;
        ReconfigurarOctava(octavaBase);

        AsegurarFisicasTecla();
        IgnorarColisionesEntreTeclas();
        ConfigurarInteraccionVR();
    }

    /// <summary>
    /// Garantiza que la tecla tenga un BoxCollider como Trigger y un Rigidbody cinemático.
    /// </summary>
    public void AsegurarFisicasTecla()
    {
        // 1. BoxCollider marcado como Is Trigger
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null)
            boxCollider = gameObject.AddComponent<BoxCollider>();
        boxCollider.isTrigger = true;

        // 2. Rigidbody marcado como Is Kinematic y sin gravedad
        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic            = true;
        rb.useGravity             = false;
        rb.interpolation          = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.constraints            = RigidbodyConstraints.FreezeAll;

        // Capa Teclas (capa 6)
        gameObject.layer = 6;
    }

    private void IgnorarColisionesEntreTeclas()
    {
        Collider myCol = GetComponent<Collider>();
        if (myCol == null) return;

        foreach (var otherKey in todasLasTeclas)
        {
            if (otherKey != null && otherKey != this)
            {
                Collider otherCol = otherKey.GetComponent<Collider>();
                if (otherCol != null)
                {
                    Physics.IgnoreCollision(myCol, otherCol, true);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Interacción por Rayo / Puntero (XR Ray Interactor & XR Simple Interactable)
    // ─────────────────────────────────────────────────────────────────────────

    private void ConfigurarInteraccionVR()
    {
        var simpleInteractable = GetComponent<XRSimpleInteractable>();
        if (simpleInteractable == null)
        {
            simpleInteractable = gameObject.AddComponent<XRSimpleInteractable>();
        }

        if (simpleInteractable != null)
        {
            // Aceptar todas las capas de interacción
            simpleInteractable.interactionLayers = ~0;

            // Asegurar que el interactable incluya el BoxCollider de la tecla
            Collider col = GetComponent<Collider>();
            if (col != null && !simpleInteractable.colliders.Contains(col))
            {
                simpleInteractable.colliders.Clear();
                simpleInteractable.colliders.Add(col);
            }

            // Limpiar eventos anteriores para evitar llamadas duplicadas
            simpleInteractable.selectEntered.RemoveAllListeners();
            simpleInteractable.selectExited.RemoveAllListeners();
            simpleInteractable.hoverEntered.RemoveAllListeners();
            simpleInteractable.hoverExited.RemoveAllListeners();

            // Interacción por Raycast de Controles:
            // Al apuntar el raycast de un control hacia una tecla y presionar el gatillo (Select/Activate),
            // debe tocar exactamente esa única tecla apuntada y soltarla al soltar el gatillo o salir de la tecla.
            simpleInteractable.selectEntered.AddListener(args =>
            {
                string interactorName = args.interactorObject != null ? args.interactorObject.transform.name : "XR Ray Interactor";
                Debug.Log($"[PianoKey] [Ray Select (Gatillo)] Tecla '{nombreNotaCompleto}' activada por '{interactorName}'");
                RegistrarEntradaInteractuador();
            });

            simpleInteractable.selectExited.AddListener(args =>
            {
                string interactorName = args.interactorObject != null ? args.interactorObject.transform.name : "XR Ray Interactor";
                Debug.Log($"[PianoKey] [Ray Select Soltado] Tecla '{nombreNotaCompleto}' liberada por '{interactorName}'");
                RegistrarSalidaInteractuador();
            });

            // Si el rayo sale de la tecla mientras se mantenía presionado el gatillo, liberar la tecla
            simpleInteractable.hoverExited.AddListener(args =>
            {
                if (simpleInteractable.isSelected)
                {
                    Debug.Log($"[PianoKey] [Ray Salió con Gatillo Presionado] Tecla '{nombreNotaCompleto}' liberada");
                    RegistrarSalidaInteractuador();
                }
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EventSystem Ray Hover (UI / TrackedDeviceGraphicRaycaster / PhysicsRaycaster)
    // ─────────────────────────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        // El rayo entra a la tecla (hover visual). NO suena hasta presionar el gatillo / clic.
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Si el puntero tenía la tecla presionada (gatillo/clic) y sale de la tecla, soltarla
        if (eventData.dragging || (eventData.pointerPress != null && eventData.pointerPress == gameObject))
        {
            RegistrarSalidaInteractuador();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log($"[PianoKey] [OnPointerDown (Gatillo/Clic)] Tecla '{nombreNotaCompleto}' presionada");
        RegistrarEntradaInteractuador();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Debug.Log($"[PianoKey] [OnPointerUp] Tecla '{nombreNotaCompleto}' liberada");
        RegistrarSalidaInteractuador();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Detección Física de Hand Tracking y Dedos (OnTriggerEnter / OnTriggerExit)
    // ─────────────────────────────────────────────────────────────────────────

    /*private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        // 1. Protección inicial: ignorar colisiones con el propio chasis del piano (transform.parent)
        // para evitar que las teclas se presionen solas al dar Play.
        if (transform.parent != null && (other.transform == transform.parent || other.transform.IsChildOf(transform.parent)))
            return;

        // 2. Ignorar colisiones con otras teclas o notas que caen
        if (other.GetComponentInParent<PianoKey>() != null) return;
        if (other.GetComponentInParent<NotaMovimiento>() != null) return;

        // 3. Protección de inicio: ignorar durante los primeros instantes de arranque
        if (Time.timeSinceLevelLoad < startupGracePeriod) return;

        // 4. Validación de colisionadores de manos y dedos de Meta Quest
        if (EsColisionadorVRValido(other))
        {
            Debug.Log($"[PianoKey] [OnTriggerEnter] Tecla '{nombreNotaCompleto}' tocada por dedo/mano '{other.name}' ({other.GetType().Name})");
            RegistrarEntradaInteractuador();
        }
    }*/

    /*private void OnTriggerExit(Collider other)
    {
        if (other == null) return;

        if (transform.parent != null && (other.transform == transform.parent || other.transform.IsChildOf(transform.parent)))
            return;

        if (other.GetComponentInParent<PianoKey>() != null) return;

        if (EsColisionadorVRValido(other))
        {
            Debug.Log($"[PianoKey] [OnTriggerExit] Dedo/mano '{other.name}' salió de la tecla '{nombreNotaCompleto}'");
            RegistrarSalidaInteractuador();
        }
    }*/

    /*private void OnCollisionEnter(Collision collision)
    {
        if (collision != null && collision.collider != null)
        {
            OnTriggerEnter(collision.collider);
        }
    }*/

    /*private void OnCollisionExit(Collision collision)
    {
        if (collision != null && collision.collider != null)
        {
            OnTriggerExit(collision.collider);
        }
    }*/

    /// <summary>
    /// Valida si el colisionador entrante corresponde a las cápsulas/articulaciones de los dedos
    /// generadas por el SDK de Meta (Hand Tracking).
    /// EXCLUYE expresamente colisionadores de mandos físicos para que no toquen teclas por error físico.
    /// </summary>
    private bool EsColisionadorVRValido(Collider col)
    {
        if (col == null) return false;

        // Protección contra el propio chasis del piano o padres fijos
        if (transform.parent != null && (col.transform == transform.parent || col.transform.IsChildOf(transform.parent)))
            return false;

        // Ignorar si es otra tecla del piano o nota
        if (col.GetComponentInParent<PianoKey>() != null) return false;
        if (col.GetComponentInParent<NotaMovimiento>() != null) return false;

        // Descartar objetos estáticos de entorno
        if (col.gameObject.isStatic) return false;

        string colName = col.name.ToLower();

        // EXCLUSIÓN ESTRICTA: Los mandos/controles físicos NUNCA deben tocar teclas por choque físico (solo por raycast)
        if (colName.Contains("controller") || colName.Contains("mando") || colName.Contains("ray") || colName.Contains("teleport"))
        {
            return false;
        }
        if (col.transform.parent != null)
        {
            string parentName = col.transform.parent.name.ToLower();
            if (parentName.Contains("controller") || parentName.Contains("mando") || parentName.Contains("teleport"))
            {
                return false;
            }
        }

        if (colName.Contains("piso") || colName.Contains("floor") || colName.Contains("stand") ||
            colName.Contains("synthesizer") || colName.Contains("piano") || colName.Contains("table") ||
            colName.Contains("mesa") || colName.Contains("wall") || colName.Contains("ceiling") ||
            colName.Contains("room") || colName.Contains("chassis"))
        {
            return false;
        }

        // 1. Reconocer componentes de tipo CapsuleCollider o SphereCollider (articulaciones de dedos en Meta XR)
        if (col is CapsuleCollider || col is SphereCollider)
        {
            return true;
        }

        // 2. Reconocer colisionadores dinámicos que tengan Rigidbody (kinemático o dinámico)
        // El SDK de Meta asocia Rigidbodys kinemáticos a las cápsulas o articulaciones de las manos
        if (col.attachedRigidbody != null)
        {
            return true;
        }

        // 3. Reconocer componentes de XR Hands o Interactors en la jerarquía
        if (col.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>() != null)
            return true;

        // 4. Reconocer por tags seguros (sin excepción si el tag no está registrado)
        string colTag = col.gameObject.tag;
        if (colTag == "PlayerHand" || colTag == "PlayerFinger" || colTag == "Hand")
            return true;

        if (col.transform.parent != null)
        {
            string parentTag = col.transform.parent.gameObject.tag;
            if (parentTag == "PlayerHand" || parentTag == "PlayerFinger" || parentTag == "Hand")
                return true;
        }

        // 5. Validación por palabras clave en el nombre del colisionador
        for (int i = 0; i < PalabrasClaveVR.Length; i++)
        {
            if (colName.Contains(PalabrasClaveVR[i]))
                return true;
        }

        // 6. Validación por palabras clave en el nombre del Transform padre
        if (col.transform.parent != null)
        {
            string parentName = col.transform.parent.name.ToLower();
            for (int i = 0; i < PalabrasClaveVR.Length; i++)
            {
                if (parentName.Contains(PalabrasClaveVR[i]))
                    return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Control Centralizado de Activación y Reposo
    // ─────────────────────────────────────────────────────────────────────────

    private void RegistrarEntradaInteractuador()
    {
        if (Time.timeSinceLevelLoad < startupGracePeriod) return;

        interactoresActivos++;

        if (!isPressed)
        {
            OnPokeEnter();
        }
    }

    private void RegistrarSalidaInteractuador()
    {
        interactoresActivos = Mathf.Max(0, interactoresActivos - 1);

        if (interactoresActivos == 0 && isPressed)
        {
            OnPokeExit();
        }
    }

    public void OnPokeEnter()
    {
        // Una tecla hundida jamás vuelve a sonar mientras siga presionada
        if (isPressed) return;
        isPressed = true;
        Debug.Log($"[PianoKey] Sonando tecla: '{nombreNotaCompleto}' ({gameObject.name}) | Octava calculada: {octavaCalculada}");

        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        if (audioSource != null)
        {
            if (audioSource.isPlaying)
            {
                audioSource.Stop();
            }

            audioSource.time = 0f;
            audioSource.Play();
        }

        // Notifica interacción para silenciar la música ambiental
        if (AmbientAudioManager.Instance != null)
        {
            AmbientAudioManager.Instance.NotificarTeclaTocada();
        }

        // Instancia el Pop-up flotante en 3D
        if (mostrarPopUpNota && prefabPopUpNota != null)
        {
            Vector3 posicionSpawn = transform.position + new Vector3(0, 0.08f, 0);
            GameObject popUpObj = Instantiate(prefabPopUpNota, posicionSpawn, Quaternion.identity);
            EfectoPopUpNota popupScript = popUpObj.GetComponent<EfectoPopUpNota>();
            if (popupScript != null)
            {
                popupScript.Inicializar(nombreNotaCompleto);
            }
        }

        ApplyEmission(Color.black, 0f);

        EvaluarAciertoNota();
    }

    public void OnPokeExit()
    {
        isPressed = false;
        Debug.Log($"[PianoKey] Reposo de tecla: '{nombreNotaCompleto}' ({gameObject.name})");
    }

    private void EvaluarAciertoNota()
    {
        NotaMovimiento notaCercana = null;
        float menorDistanciaY = float.MaxValue;

        // Recorre las notas activas en pantalla
        foreach (var nota in notasEnEscena)
        {
            if (nota != null && nota.targetKey == this)
            {
                float yImpactoIdeal = transform.position.y;
                float diferenciaDistancia = Mathf.Abs(nota.transform.position.y - yImpactoIdeal);

                // Evalúa si la nota está dentro del margen de tolerancia vertical
                if (diferenciaDistancia <= rangoToleranciaHit && diferenciaDistancia < menorDistanciaY)
                {
                    menorDistanciaY = diferenciaDistancia;
                    notaCercana = nota;
                }
            }
        }

        if (notaCercana != null)
        {
            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.RegistrarAcierto();
                Debug.Log($"[PianoKey] ¡Acierto registrado en {nombreNotaCompleto}! Precisión actual: {ScoreManager.Instance.ObtenerPorcentajePrecision():F1}%");
            }

            // Remover de la lista global y destruir la nota acertada
            notasEnEscena.Remove(notaCercana);
            Destroy(notaCercana.gameObject);
        }
        else
        {
            Debug.Log($"[PianoKey] Se presionó {nombreNotaCompleto} pero no había ninguna nota dentro del rango (Tolerancia: {rangoToleranciaHit}m).");
        }
    }

    private void ExtraerNotaYOctavaInicial()
    {
        string nombre = gameObject.name.Trim();
        string notaEN = "";

        for (int i = 0; i < nombre.Length; i++)
        {
            string c = nombre[i].ToString().ToUpper();
            if (notaES.ContainsKey(c))
            {
                notaEN = c;
                break;
            }
        }

        if (string.IsNullOrEmpty(notaEN)) return;

        bool sostenido = nombre.Contains("#");
        notaNombreBase = notaEN + (sostenido ? "#" : "");

        int octavaEnNombre = 3;
        for (int i = nombre.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(nombre[i]))
            {
                octavaEnNombre = int.Parse(nombre[i].ToString());
                break;
            }
        }

        offsetOctavaRelativa = Mathf.Max(0, octavaEnNombre - 3);
    }

    public void ReconfigurarOctava(int octavaBaseGlobal)
    {
        if (string.IsNullOrEmpty(notaNombreBase)) return;

        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        octavaCalculada = octavaBaseGlobal + offsetOctavaRelativa;
        nombreNotaCompleto = notaNombreBase + octavaCalculada;

        ConfigurarNota();

        // Actualiza la etiqueta si es una tecla C (Do)
        if (notaNombreBase == "C" && textoEtiquetaOctava != null)
        {
            textoEtiquetaOctava.text = nombreNotaCompleto;
        }
    }

    void ConfigurarNota()
    {
        bool sostenido = notaNombreBase.Contains("#");
        string notaLimpia = notaNombreBase.Replace("#", "");

        if (!notaES.ContainsKey(notaLimpia)) return;

        // 1. Si es sostenido (#), obtiene su equivalente en bemol (b) (ej: C#3 -> Db3)
        string nombreNotaParaArchivo = notaES[notaLimpia];
        if (sostenido)
        {
            nombreNotaParaArchivo = ObtenerEquivalenteBemol(notaLimpia);
        }

        string nombreArchivoExacto = nombreNotaParaArchivo + octavaCalculada;
        AudioClip clipExacto = Resources.Load<AudioClip>(nombreArchivoExacto);

        if (clipExacto != null)
        {
            audioSource.clip  = clipExacto;
            audioSource.pitch = 1f; // Sin alteración de pitch
            return;
        }

        // 2. Respaldo: Si no existe el archivo alterado, usa la nota base y modifica el pitch (1.0595)
        string nombreBase = notaES[notaLimpia] + octavaCalculada;
        AudioClip clipBase = Resources.Load<AudioClip>(nombreBase);

        if (clipBase != null)
        {
            audioSource.clip  = clipBase;
            audioSource.pitch = sostenido ? 1.0595f : 1f;
            return;
        }

        // 3. Respaldo por octava 4 si la octava calculada no existe
        string nombreRespaldoOctava4 = notaES[notaLimpia] + "4";
        AudioClip clipOctava4 = Resources.Load<AudioClip>(nombreRespaldoOctava4);

        if (clipOctava4 != null)
        {
            audioSource.clip = clipOctava4;
            float multiplicadorOctava = Mathf.Pow(2f, octavaCalculada - 4);
            audioSource.pitch = multiplicadorOctava * (sostenido ? 1.0595f : 1f);
        }
    }

    private string ObtenerEquivalenteBemol(string notaBase)
    {
        switch (notaBase)
        {
            case "C": return "Db";
            case "D": return "Eb";
            case "F": return "Gb";
            case "G": return "Ab";
            case "A": return "Bb";
            default: return notaBase;
        }
    }

    void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        ActualizarMouseEditor();
#endif
    }

#if UNITY_EDITOR || UNITY_STANDALONE
    private void ActualizarMouseEditor()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                    if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                    {
                        if (hit.transform == transform || hit.transform.IsChildOf(transform))
                        {
                            Debug.Log($"[PianoKey] [PC Mouse Click - New Input] Tecla '{nombreNotaCompleto}' presionada");
                            RegistrarEntradaInteractuador();
                        }
                    }
                }
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                RegistrarSalidaInteractuador();
            }
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                {
                    if (hit.transform == transform || hit.transform.IsChildOf(transform))
                    {
                        Debug.Log($"[PianoKey] [PC Mouse Click - Legacy] Tecla '{nombreNotaCompleto}' presionada");
                        RegistrarEntradaInteractuador();
                    }
                }
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            RegistrarSalidaInteractuador();
        }
#endif
    }
#endif

    void LateUpdate()
    {
        if (todasLasTeclas.Count > 0 && this == todasLasTeclas[0])
        {
            ActualizarColoresGlobales();
        }
    }

    private static void ActualizarColoresGlobales()
    {
        notasEnEscena.RemoveAll(n => n == null);

        foreach (var key in todasLasTeclas)
        {
            key.ApplyEmission(Color.black, 0f);
        }

        if (notasEnEscena.Count == 0) return;

        Dictionary<PianoKey, NotaMovimiento> notaMasBajaPorTecla = new Dictionary<PianoKey, NotaMovimiento>();
        float menorYGlobal = float.MaxValue;

        foreach (var nota in notasEnEscena)
        {
            if (nota.targetKey == null || nota.targetKey.isPressed) continue;

            float yActual = nota.transform.position.y;

            if (yActual < menorYGlobal)
                menorYGlobal = yActual;

            if (!notaMasBajaPorTecla.ContainsKey(nota.targetKey))
            {
                notaMasBajaPorTecla[nota.targetKey] = nota;
            }
            else
            {
                if (yActual < notaMasBajaPorTecla[nota.targetKey].transform.position.y)
                {
                    notaMasBajaPorTecla[nota.targetKey] = nota;
                }
            }
        }

        foreach (var par in notaMasBajaPorTecla)
        {
            PianoKey key = par.Key;
            NotaMovimiento nota = par.Value;

            bool esTurnoActual = Mathf.Abs(nota.transform.position.y - menorYGlobal) <= 0.08f;
            Color baseColor = esTurnoActual ? key.colorTurnoActual : key.colorEnEspera;

            float progreso = nota.ObtenerProgreso();

            key.ApplyEmission(baseColor, progreso);
        }
    }

    public void ApplyEmission(Color color, float progress)
    {
        if (keyRenderer == null) return;

        progress = Mathf.Clamp01(progress);
        Color finalEmission = color * (progress * maxGlowIntensity);

        keyRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(EmissionColorProp, finalEmission);
        keyRenderer.SetPropertyBlock(propBlock);
    }

    void FixedUpdate()
    {
        Vector3 targetLocal = isPressed ? pressedLocalPosition : restLocalPosition;
        float   speed       = isPressed ? pressSpeed : returnSpeed;
        Vector3 targetWorld = transform.parent != null
            ? transform.parent.TransformPoint(targetLocal)
            : targetLocal;

        if (rb != null && !rb.isKinematic)
        {
            rb.MovePosition(Vector3.Lerp(transform.position, targetWorld, Time.fixedDeltaTime * speed));
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, targetWorld, Time.fixedDeltaTime * speed);
        }
    }

#if UNITY_EDITOR || UNITY_STANDALONE
    void OnMouseDown()
    {
        Debug.Log($"[PianoKey] [Legacy OnMouseDown] Tecla '{nombreNotaCompleto}' presionada");
        RegistrarEntradaInteractuador();
    }

    void OnMouseUp()
    {
        Debug.Log($"[PianoKey] [Legacy OnMouseUp] Tecla '{nombreNotaCompleto}' liberada");
        RegistrarSalidaInteractuador();
    }
#endif
}
