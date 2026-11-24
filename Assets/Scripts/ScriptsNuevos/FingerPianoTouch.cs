using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Detección táctil por profundidad relativa desde el contacto + Pre-travel visual + Dedo Líder + Anti-Rebote.
///
/// 1. Permite posar los dedos sobre las teclas sin exigir velocidad inicial: OnTriggerEnter
///    registra 'entryY'.
/// 2. En OnTriggerStay calcula 'profundidad = entryY - transform.position.y' y actualiza
///    el hundimiento visual progresivo (0 mm a 3.5 mm) de la malla separada de la tecla.
/// 3. Al alcanzar 3.5 mm (0.0035 m) bajando (deltaY <= 0), entra en la ventana de "Dedo Líder"
///    de 45 ms por mano: si varios dedos de la misma mano bajan por simpatía, dispara únicamente
///    el que bajó con mayor amplitud.
/// 4. Una vez disparada, aplica bloqueo por histéresis espacial (histeresisElevacionReentrada = 2.5 mm
///    o salida completa del collider) y bloqueo temporal por dedo (cooldownReentradaDedo = 0.14 s)
///    para eliminar dobles clics por micro-temblor del Hand Tracking en el borde del collider.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class FingerPianoTouch : MonoBehaviour
{
    [Header("Ajustes de Toque Único")]
    [Tooltip("Radio de la yema del dedo (metros). Se ajusta a 0.008 m en Awake.")]
    public float radioYema = 0.008f;

    [Tooltip("Multiplicador del radio para considerar que el dedo salió de la tecla.")]
    public float multiplicadorSalida = 1.6f;

    [Header("Activación por Profundidad Relativa")]
    [Tooltip("Descenso mínimo (metros) desde el contacto inicial 'entryY' para disparar la nota (3.5 mm).")]
    public float profundidadDisparo = 0.0035f;

    [Header("Filtro de Dedo Líder (por Mano)")]
    [Tooltip("Ventana de tiempo (segundos) para comparar dedos de la misma mano y elegir solo el líder (45 ms).")]
    public float ventanaDedoLider = 0.045f;

    [Header("Anti-Rebote e Histéresis de Re-Entrada por Dedo")]
    [Tooltip("Tiempo mínimo (segundos) tras un disparo antes de que este mismo dedo pueda volver a activar una pulsación (140 ms).")]
    public float cooldownReentradaDedo = 0.14f;

    [Tooltip("Elevación mínima hacia arriba (metros) desde el punto más bajo del toque requerida para re-armar el dedo sin salir del trigger (2.5 mm).")]
    public float histeresisElevacionReentrada = 0.0025f;

    private struct CandidatoDedo
    {
        public FingerPianoTouch dedo;
        public PianoKey tecla;
        public float amplitudDescenso;
        public float velocidadImpacto;
    }

    private class VentanaMano
    {
        public bool activa;
        public float tiempoInicio;
        public readonly List<CandidatoDedo> candidatos = new List<CandidatoDedo>(5);
    }

    // Exclusividad 1 dedo físico = 1 tecla y ventanas de Dedo Líder independientes por mano ("L" / "R")
    private static readonly Dictionary<string, FingerPianoTouch> dedosFisicosActivos = new Dictionary<string, FingerPianoTouch>();
    private static readonly Dictionary<string, VentanaMano> ventanasPorMano = new Dictionary<string, VentanaMano>();
    private static readonly Dictionary<string, float> ultimoTiempoDisparoPorDedoFisico = new Dictionary<string, float>();

    private string   idDedoFisico = string.Empty;
    private string   ladoMano     = "U";
    private PianoKey teclaContacto = null; // tecla en pre-travel (0 mm .. 3.5 mm)
    private PianoKey teclaActiva   = null; // tecla disparada y bloqueada hasta salir
    private float    entryY = 0f;
    private float    prevY  = 0f;
    private float    velocidadDescensoMax = 0f;
    private bool     enVentanaLider = false;
    private bool     descartadoPorSimpatia = false;

    // Estado de histéresis y bloqueo de re-entrada por dedo
    private float    ultimoTiempoDisparoLocal = -10f;
    private float    ultimoTiempoLiberacionLocal = -10f;
    private float    alturaMinimaToqueActual = 0f;
    private bool     requiereSeparacionHisteresis = false;
    private PianoKey ultimaTeclaDisparada = null;

    private void Awake()
    {
        radioYema = 0.008f;
        idDedoFisico = PianoKey.ObtenerIdDedoFisico(transform);
        ladoMano = ExtraerLadoMano(idDedoFisico);

        SphereCollider col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = radioYema;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Start()
    {
        // Si FingerPianoTouch está presente en la escena, tomar el control limpio y evitar que HandColliders duplique toques
        HandColliders hc = FindAnyObjectByType<HandColliders>();
        if (hc != null && hc.enabled)
        {
            hc.enabled = false;
            Debug.Log("[FingerPianoTouch] Control táctil por profundidad relativa, Dedo Líder y Anti-Rebote activo (HandColliders pausado para evitar duplicados).");
        }
    }

    /// <summary>
    /// Verifica si este dedo físico aún se encuentra dentro de la ventana temporal de bloqueo
    /// (cooldownReentradaDedo = 0.14s) desde su última pulsación.
    /// </summary>
    private bool DedoEnCooldownReentrada()
    {
        float ahora = Time.time;
        if ((ahora - ultimoTiempoDisparoLocal) < cooldownReentradaDedo)
            return true;

        // Pequeño margen tras soltar el volumen de la tecla para absorber micro-temblor en el borde del collider
        if ((ahora - ultimoTiempoLiberacionLocal) < (cooldownReentradaDedo * 0.45f))
            return true;

        if (!string.IsNullOrEmpty(idDedoFisico) &&
            ultimoTiempoDisparoPorDedoFisico.TryGetValue(idDedoFisico, out float tGlobal) &&
            (ahora - tGlobal) < cooldownReentradaDedo)
        {
            return true;
        }

        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (teclaActiva != null) return;
        if (dedosFisicosActivos.TryGetValue(idDedoFisico, out FingerPianoTouch dueno) && dueno != null)
            return;

        PianoKey key = other.GetComponentInParent<PianoKey>();
        if (key == null || key.isPressed || key.EstaEnCooldownRebote()) return;

        // Si el mismo dedo acaba de pulsar esta tecla y re-entra por micro-temblor antes del cooldown, bloquear
        if (DedoEnCooldownReentrada() && key == ultimaTeclaDisparada)
            return;

        // Si estaba en pre-travel en otra tecla distinta, limpiar su pre-travel visual
        if (teclaContacto != null && teclaContacto != key)
        {
            teclaContacto.EstablecerPreTravelVisual(0f, profundidadDisparo);
        }

        // Registrar altura de entrada sin exigir velocidad mínima previa (permite posar dedos sobre el teclado)
        teclaContacto = key;
        entryY = transform.position.y;
        prevY  = entryY;
        velocidadDescensoMax = 0f;
        enVentanaLider = false;
        descartadoPorSimpatia = false;
    }

    private void OnTriggerStay(Collider other)
    {
        if (other == null) return;
        if (teclaActiva != null) return;
        if (dedosFisicosActivos.TryGetValue(idDedoFisico, out FingerPianoTouch dueno) && dueno != null)
            return;

        PianoKey key = other.GetComponentInParent<PianoKey>();
        if (key == null) return;

        // Si el dedo sigue en cooldown o la tecla está en cooldown anti-rebote, mantener pre-travel en 0 y esperar
        if (DedoEnCooldownReentrada() || key.EstaEnCooldownRebote())
        {
            if (teclaContacto != null)
            {
                teclaContacto.EstablecerPreTravelVisual(0f, profundidadDisparo);
            }
            prevY = transform.position.y;
            return;
        }

        // Si este dedo acaba de disparar esta misma tecla y no ha salido completamente del trigger,
        // exigir que suba claramente al menos 'histeresisElevacionReentrada' (2.5 mm) desde el fondo del toque
        if (requiereSeparacionHisteresis && key == ultimaTeclaDisparada)
        {
            if (transform.position.y >= (alturaMinimaToqueActual + histeresisElevacionReentrada))
            {
                requiereSeparacionHisteresis = false;
                entryY = transform.position.y;
                prevY  = entryY;
                velocidadDescensoMax = 0f;
            }
            else
            {
                prevY = transform.position.y;
                return;
            }
        }

        if (teclaContacto == null)
        {
            if (key.isPressed) return;
            teclaContacto = key;
            entryY = transform.position.y;
            prevY  = entryY;
            velocidadDescensoMax = 0f;
            enVentanaLider = false;
            descartadoPorSimpatia = false;
        }

        // Mantener foco en la tecla con la que hizo contacto inicial
        if (key != teclaContacto) return;

        float currentY = transform.position.y;
        float deltaY   = currentY - prevY;
        float dt       = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        prevY = currentY;

        // Si el dedo sube ligeramente estando posado, reajustar entryY (y re-armar si fue descartado por simpatía)
        if (currentY > entryY)
        {
            entryY = currentY;
            velocidadDescensoMax = 0f;
            if (descartadoPorSimpatia)
            {
                descartadoPorSimpatia = false;
            }
        }

        if (descartadoPorSimpatia)
        {
            teclaContacto.EstablecerPreTravelVisual(0f, profundidadDisparo);
            return;
        }

        float profundidad = Mathf.Max(0f, entryY - currentY);

        if (deltaY < 0f)
        {
            float velInst = Mathf.Abs(deltaY) / dt;
            if (velInst > velocidadDescensoMax) velocidadDescensoMax = velInst;
        }

        // 2. Hundimiento visual progresivo (Pre-travel visual 0 mm .. 3.5 mm)
        teclaContacto.EstablecerPreTravelVisual(profundidad, profundidadDisparo);

        // 1 & 3. Al alcanzar 3.5 mm bajando (deltaY <= 0), registrar en la ventana de Dedo Líder (45 ms)
        if (profundidad >= profundidadDisparo && deltaY <= 0f)
        {
            RegistrarEnVentanaDedoLider(teclaContacto, profundidad, velocidadDescensoMax);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) return;

        PianoKey key = other.GetComponentInParent<PianoKey>();

        // Si el dedo salió físicamente del trigger de la última tecla disparada, cumple la separación espacial
        if (key != null && key == ultimaTeclaDisparada)
        {
            requiereSeparacionHisteresis = false;
        }

        // NUNCA desbloquear 'teclaActiva' por OnTriggerExit (evita falsos releases).
        // Solo limpiar el pre-travel si el dedo salió antes de activar la nota.
        if (teclaActiva == null && teclaContacto != null && key == teclaContacto)
        {
            teclaContacto.EstablecerPreTravelVisual(0f, profundidadDisparo);
            teclaContacto = null;
            enVentanaLider = false;
            descartadoPorSimpatia = false;
        }
    }

    private void Update()
    {
        EvaluarVentanaDedoLider(ladoMano, ventanaDedoLider);
    }

    private void FixedUpdate()
    {
        if (teclaActiva == null) return;

        float currentY = transform.position.y;
        if (currentY < alturaMinimaToqueActual)
        {
            alturaMinimaToqueActual = currentY;
        }

        // Liberar la tecla cuando el dedo salga del volumen expandido o se eleve claramente con histéresis
        // después de haber cumplido el tiempo mínimo de cooldownReentradaDedo
        bool salioDeVolumen = !teclaActiva.DedoDentroDeVolumen(transform.position, radioYema * multiplicadorSalida);
        bool elevoConHisteresis = (Time.time - ultimoTiempoDisparoLocal) >= cooldownReentradaDedo &&
                                  currentY >= (alturaMinimaToqueActual + histeresisElevacionReentrada + 0.001f);

        if (salioDeVolumen || elevoConHisteresis)
        {
            LiberarTeclaActual();
        }
    }

    private void RegistrarEnVentanaDedoLider(PianoKey tecla, float amplitud, float velocidad)
    {
        if (tecla == null || tecla.EstaEnCooldownRebote() || DedoEnCooldownReentrada())
            return;

        if (!ventanasPorMano.TryGetValue(ladoMano, out VentanaMano ventana))
        {
            ventana = new VentanaMano();
            ventanasPorMano[ladoMano] = ventana;
        }

        if (!ventana.activa)
        {
            ventana.activa = true;
            ventana.tiempoInicio = Time.unscaledTime;
            ventana.candidatos.Clear();
        }

        enVentanaLider = true;

        // Actualizar si este dedo ya estaba en la ventana (para capturar su máxima amplitud durante los 45 ms)
        for (int i = 0; i < ventana.candidatos.Count; i++)
        {
            if (ventana.candidatos[i].dedo == this)
            {
                CandidatoDedo c = ventana.candidatos[i];
                if (amplitud > c.amplitudDescenso) c.amplitudDescenso = amplitud;
                if (velocidad > c.velocidadImpacto) c.velocidadImpacto = velocidad;
                ventana.candidatos[i] = c;
                return;
            }
        }

        ventana.candidatos.Add(new CandidatoDedo
        {
            dedo = this,
            tecla = tecla,
            amplitudDescenso = amplitud,
            velocidadImpacto = velocidad
        });
    }

    private static void EvaluarVentanaDedoLider(string lado, float duracionVentana)
    {
        if (!ventanasPorMano.TryGetValue(lado, out VentanaMano ventana) || !ventana.activa)
            return;

        if (Time.unscaledTime - ventana.tiempoInicio < duracionVentana)
            return;

        // Elegir el único dedo líder de esta mano con mayor amplitud descendente (y velocidad como desempate)
        int indiceLider = -1;
        float mejorAmplitud = -1f;
        float mejorVelocidad = -1f;

        for (int i = 0; i < ventana.candidatos.Count; i++)
        {
            CandidatoDedo c = ventana.candidatos[i];
            if (c.dedo == null || c.tecla == null || !c.dedo.isActiveAndEnabled) continue;
            if (c.dedo.teclaActiva != null || c.tecla.isPressed || c.tecla.EstaEnCooldownRebote()) continue;
            if (c.dedo.DedoEnCooldownReentrada()) continue;

            if (c.amplitudDescenso > mejorAmplitud + 0.0002f ||
                (Mathf.Abs(c.amplitudDescenso - mejorAmplitud) <= 0.0002f && c.velocidadImpacto > mejorVelocidad))
            {
                mejorAmplitud = c.amplitudDescenso;
                mejorVelocidad = c.velocidadImpacto;
                indiceLider = i;
            }
        }

        for (int i = 0; i < ventana.candidatos.Count; i++)
        {
            CandidatoDedo c = ventana.candidatos[i];
            if (c.dedo == null) continue;

            c.dedo.enVentanaLider = false;

            if (i == indiceLider)
            {
                c.dedo.ConfirmarDisparoLider(c.tecla, c.velocidadImpacto);
            }
            else
            {
                // Descartar dedos vecinos que cayeron por simpatía/inercia
                c.dedo.DescartarPorSimpatia(c.tecla);
            }
        }

        ventana.candidatos.Clear();
        ventana.activa = false;
    }

    private void ConfirmarDisparoLider(PianoKey tecla, float velocidadImpacto)
    {
        if (tecla == null || teclaActiva != null) return;
        if (tecla.EstaEnCooldownRebote() || DedoEnCooldownReentrada()) return;
        if (dedosFisicosActivos.TryGetValue(idDedoFisico, out FingerPianoTouch dueno) && dueno != null)
            return;

        if (tecla.RegistrarToqueDedo(transform, velocidadImpacto))
        {
            float ahora = Time.time;
            ultimoTiempoDisparoLocal = ahora;
            if (!string.IsNullOrEmpty(idDedoFisico))
            {
                ultimoTiempoDisparoPorDedoFisico[idDedoFisico] = ahora;
            }

            alturaMinimaToqueActual = transform.position.y;
            ultimaTeclaDisparada = tecla;
            requiereSeparacionHisteresis = true;

            teclaActiva = tecla;
            teclaContacto = null;
            dedosFisicosActivos[idDedoFisico] = this;
            Debug.Log($"[FingerPianoTouch] LÍDER '{idDedoFisico}' disparó '{tecla.nombreNotaCompleto}' (vel={velocidadImpacto:F2} m/s)");
        }
    }

    private void DescartarPorSimpatia(PianoKey tecla)
    {
        if (tecla != null)
        {
            tecla.EstablecerPreTravelVisual(0f, profundidadDisparo);
        }
        descartadoPorSimpatia = true;
    }

    public void LiberarTeclaActual()
    {
        if (teclaContacto != null)
        {
            teclaContacto.EstablecerPreTravelVisual(0f, profundidadDisparo);
            teclaContacto = null;
        }

        if (teclaActiva != null)
        {
            teclaActiva.LiberarToqueDedo(transform);
            teclaActiva = null;
            ultimoTiempoLiberacionLocal = Time.time;
        }

        if (!string.IsNullOrEmpty(idDedoFisico) &&
            dedosFisicosActivos.TryGetValue(idDedoFisico, out FingerPianoTouch dueno) && dueno == this)
        {
            dedosFisicosActivos.Remove(idDedoFisico);
        }

        enVentanaLider = false;
        descartadoPorSimpatia = false;
    }

    private static string ExtraerLadoMano(string idDedo)
    {
        if (string.IsNullOrEmpty(idDedo)) return "U";
        if (idDedo.StartsWith("L_")) return "L";
        if (idDedo.StartsWith("R_")) return "R";
        return "U";
    }

    private void OnDisable()
    {
        LiberarTeclaActual();
    }
}
