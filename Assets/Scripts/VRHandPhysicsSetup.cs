using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Configura y garantiza de forma automática en tiempo de ejecución (tanto en Meta Quest como en el Editor)
/// que las manos virtuales (Hand Tracking), los mandos físicos y los Poke Interactors tengan los componentes
/// de física necesarios: SphereCollider (Is Trigger = true) y Rigidbody (Is Kinematic = true, Use Gravity = false).
/// </summary>
public class VRHandPhysicsSetup : MonoBehaviour
{
    private static VRHandPhysicsSetup instance;

    [Header("Configuración de Colisionadores")]
    [Tooltip("Radio del SphereCollider para las puntas de los dedos y Poke Interactors (metros).")]
    public float radioDedosYPoke = 0.025f;

    [Tooltip("Radio del SphereCollider para la base de las manos o mandos (metros).")]
    public float radioManosYMandos = 0.035f;

    private readonly HashSet<int> configurados = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInicializar()
    {
        if (instance == null)
        {
            GameObject runner = new GameObject("[VRHandPhysicsSetup]");
            instance = runner.AddComponent<VRHandPhysicsSetup>();
            DontDestroyOnLoad(runner);
            Debug.Log("[VRHandPhysicsSetup] Auto-inicializado para garantizar físicas de manos, mandos y piano.");
        }

        instance.AuditarYConfigurarTodo();
    }

    void Start()
    {
        AuditarYConfigurarTodo();
        StartCoroutine(RutinaMonitoreoContinuo());
    }

    /// <summary>
    /// Corrutina que verifica periódicamente si el Hand Tracking ha generado nuevos huesos o dedos en tiempo real.
    /// </summary>
    private IEnumerator RutinaMonitoreoContinuo()
    {
        WaitForSeconds espera = new WaitForSeconds(0.6f);
        while (true)
        {
            yield return espera;
            ConfigurarManosYMandosEnEscena();
        }
    }

    public void AuditarYConfigurarTodo()
    {
        ConfigurarTeclasPiano();
        ConfigurarManosYMandosEnEscena();
    }

    /// <summary>
    /// Garantiza que todas las teclas del piano en la escena tengan BoxCollider (Is Trigger) y Rigidbody (Is Kinematic).
    /// </summary>
    public void ConfigurarTeclasPiano()
    {
        PianoKey[] teclas = FindObjectsByType<PianoKey>(FindObjectsSortMode.None);
        int count = 0;

        foreach (var tecla in teclas)
        {
            if (tecla == null) continue;
            tecla.AsegurarFisicasTecla();
            count++;
        }

        if (count > 0)
        {
            Debug.Log($"[VRHandPhysicsSetup] {count} teclas de piano auditadas y configuradas con BoxCollider (Trigger) y Rigidbody (Kinematic).");
        }
    }

    /// <summary>
    /// Busca y configura SphereCollider y Rigidbody ÚNICAMENTE en las yemas de los dedos (Hand Tracking) y Poke Interactor.
    /// Excluye explícitamente jerarquías completas de mandos físicos para evitar que se fragmenten en piezas al perder tracking.
    /// </summary>
    public void ConfigurarManosYMandosEnEscena()
    {
        Transform[] todos = FindObjectsByType<Transform>(FindObjectsSortMode.None);

        foreach (Transform t in todos)
        {
            if (t == null) continue;

            // Ignorar teclas del piano, notas, chasis y cámaras
            if (t.GetComponent<PianoKey>() != null || t.GetComponentInParent<PianoKey>() != null) continue;
            if (t.GetComponent<NotaMovimiento>() != null || t.GetComponentInParent<NotaMovimiento>() != null) continue;
            if (t.GetComponent<Camera>() != null) continue;

            string n = t.name.ToLower();

            // EXCLUSIÓN CRÍTICA: No fragmentar mandos físicos ni añadirles colliders espurios a sus subobjetos
            bool esJerarquiaMando = (n.Contains("controller") || n.Contains("ray") || n.Contains("direct") || n.Contains("teleport"))
                                    && !n.Contains("poke");

            if (esJerarquiaMando) continue;

            // Identificar Poke Interactors específicos
            bool esPoke = n == "poke interactor" || n.EndsWith("poke");

            // Identificar Puntas de Dedos (Hand Tracking de Meta Quest y XR Hands)
            // Se enfoca estrictamente en las puntas (distales/tips), NO en huesos intermedios ni muñecas
            bool esPuntaDedo = n.Contains("tip") || 
                               (n.Contains("distal") && (n.Contains("thumb") || n.Contains("index") || n.Contains("middle") || n.Contains("ring") || n.Contains("pinky") || n.Contains("little")));

            if (esPoke || esPuntaDedo)
            {
                int id = t.gameObject.GetInstanceID();
                if (!configurados.Contains(id))
                {
                    ConfigurarColisionadorVR(t.gameObject, true);
                    configurados.Add(id);
                }
            }
        }
    }

    private void ConfigurarColisionadorVR(GameObject obj, bool esPunta)
    {
        // 1. SphereCollider como Trigger
        SphereCollider sc = obj.GetComponent<SphereCollider>();
        if (sc == null)
        {
            sc = obj.AddComponent<SphereCollider>();
        }

        sc.isTrigger = true;
        float radioDeseado = esPunta ? radioDedosYPoke : radioManosYMandos;

        // Si el collider no tenía radio o era diminuto, ajustarlo al radio óptimo
        if (sc.radius < 0.015f || sc.radius > 0.08f)
        {
            sc.radius = radioDeseado;
        }

        // 2. Rigidbody cinemático y sin gravedad
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = obj.AddComponent<Rigidbody>();
        }

        rb.isKinematic            = true;
        rb.useGravity             = false;
        rb.interpolation          = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        Debug.Log($"[VRHandPhysicsSetup] ✅ Configurado '{obj.name}' con SphereCollider (Trigger={sc.isTrigger}, R={sc.radius:F3}m) y Rigidbody (Kinematic={rb.isKinematic})");
    }
}
