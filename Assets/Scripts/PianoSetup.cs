using UnityEngine;

public class PianoSetup : MonoBehaviour
{
    void Awake()
    {
        PianoKey[] teclas = GetComponentsInChildren<PianoKey>(true);
        int configuradas = 0;

        foreach (PianoKey tecla in teclas)
        {
            GameObject obj = tecla.gameObject;

            // Asignar capa de Teclas (capa 6)
            obj.layer = 6;

            // Tag Teclas seguro
            try
            {
                obj.tag = "Teclas";
            }
            catch
            {
                // Tag no registrado, continuar sin error
            }

            // ── 1. Box Collider (Trigger para detección suave por toque/roce) ──
            BoxCollider col = obj.GetComponent<BoxCollider>();
            if (col == null)
                col = obj.AddComponent<BoxCollider>();
            
            // Trigger activado para permitir que las manos y mandos rocen sin rebotes físicos
            col.isTrigger = true;

            // ── 2. Rigidbody (Cinemático para evitar que fuerzas externas o gravedad muevan las teclas) ──
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null)
                rb = obj.AddComponent<Rigidbody>();

            rb.useGravity             = false;
            rb.isKinematic            = true; // Cinemático: la tecla se anima suavemente por código
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints            = RigidbodyConstraints.FreezeAll;

            configuradas++;
        }

        // Ignorar colisiones mutuas entre todas las teclas del piano para evitar falsos contactos
        for (int i = 0; i < teclas.Length; i++)
        {
            Collider colA = teclas[i].GetComponent<Collider>();
            if (colA == null) continue;

            for (int j = i + 1; j < teclas.Length; j++)
            {
                Collider colB = teclas[j].GetComponent<Collider>();
                if (colB != null)
                {
                    Physics.IgnoreCollision(colA, colB, true);
                }
            }
        }

        Debug.Log($"[PianoSetup] {configuradas} teclas configuradas como Triggers cinemáticos en capa 6.");
    }
}
