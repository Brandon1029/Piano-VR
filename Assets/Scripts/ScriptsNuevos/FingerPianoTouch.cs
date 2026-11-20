using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Detección táctil por profundidad relativa desde el contacto + Pre-travel visual + Dedo Líder.
///
/// 1. Permite posar los dedos sobre las teclas sin exigir velocidad inicial: OnTriggerEnter
///    registra 'entryY'.
/// 2. En OnTriggerStay calcula 'profundidad = entryY - transform.position.y' y actualiza
///    el hundimiento visual progresivo (0 mm a 3.5 mm) de la malla separada de la tecla.
/// 3. Al alcanzar 3.5 mm (0.0035 m) bajando (deltaY <= 0), entra en la ventana de "Dedo Líder"
///    de 45 ms por mano: si varios dedos de la misma mano bajan por simpatía, dispara únicamente
///    el que bajó con mayor amplitud.
/// 4. Una vez disparada, la tecla permanece bloqueada hasta salir físicamente de DedoDentroDeVolumen.
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

    private string   idDedoFisico = string.Empty;
    private string   ladoMano     = "U";
    private PianoKey teclaContacto = null; // tecla en pre-travel (0 mm .. 3.5 mm)
    private PianoKey teclaActiva   = null; // tecla disparada y bloqueada hasta salir
    private float    entryY = 0f;
    private float    prevY  = 0f;
    private float    velocidadDescensoMax = 0f;
    private bool     enVentanaLider = false;
    private bool     descartadoPorSimpatia = false;

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
            Debug.Log("[FingerPianoTouch] Control táctil por profundidad relativa y Dedo Líder activo (HandColliders pausado para evitar duplicados).");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (teclaActiva != null) return;
        if (dedosFisicosActivos.TryGetValue(idDedoFisico, out FingerPianoTouch dueno) && dueno != null)
            return;

        PianoKey key = other.GetComponentInParent<PianoKey>();
        if (key == null || key.isPressed) return;

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

        // NUNCA desbloquear 'teclaActiva' por OnTriggerExit (evita falsos releases).
        // Solo limpiar el pre-travel si el dedo salió antes de activar la nota.
        if (teclaActiva == null && teclaContacto != null)
        {
            PianoKey key = other.GetComponentInParent<PianoKey>();
            if (key == teclaContacto)
            {
                teclaContacto.EstablecerPreTravelVisual(0f, profundidadDisparo);
                teclaContacto = null;
                enVentanaLider = false;
                descartadoPorSimpatia = false;
            }
        }
    }

    private void Update()
    {
        EvaluarVentanaDedoLider(ladoMano, ventanaDedoLider);
    }

    private void FixedUpdate()
    {
        if (teclaActiva == null) return;

        // Bloqueado hasta que el dedo salga físicamente del volumen estático de la tecla
        if (!teclaActiva.DedoDentroDeVolumen(transform.position, radioYema * multiplicadorSalida))
        {
            LiberarTeclaActual();
        }
    }

    private void RegistrarEnVentanaDedoLider(PianoKey tecla, float amplitud, float velocidad)
    {
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
            if (c.dedo.teclaActiva != null || c.tecla.isPressed) continue;

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
        if (dedosFisicosActivos.TryGetValue(idDedoFisico, out FingerPianoTouch dueno) && dueno != null)
            return;

        if (tecla.RegistrarToqueDedo(transform, velocidadImpacto))
        {
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
