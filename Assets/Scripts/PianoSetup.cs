using UnityEngine;

public class PianoSetup : MonoBehaviour
{
    void Awake()
    {
        PianoKey[] teclas = GetComponentsInChildren<PianoKey>();
        int configuradas = 0;

        foreach (PianoKey tecla in teclas)
        {
            GameObject obj = tecla.gameObject;

            // ── 1. Box Collider ──
            BoxCollider col = obj.GetComponent<BoxCollider>();
            if (col == null)
                col = obj.AddComponent<BoxCollider>();
            col.isTrigger = false;

            // ── 2. Rigidbody ──
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null)
                rb = obj.AddComponent<Rigidbody>();

            rb.useGravity             = false;
            rb.isKinematic            = false;
            rb.mass                   = 0.1f;
            rb.linearDamping          = 5f;
            rb.angularDamping         = 5f;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints =
                RigidbodyConstraints.FreezePositionX |
                RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationY |
                RigidbodyConstraints.FreezeRotationZ;

            configuradas++;
        }

        Debug.Log($"[PianoSetup] {configuradas} teclas configuradas.");
    }
}