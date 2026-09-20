using UnityEngine;

public class NotaMovimiento : MonoBehaviour
{
    public GameObject explosionPrefab;
    public float velocidadMaxima = 1.5f;

    private float targetY;
    private float spawnY;
    private bool targetSet = false;
    private Rigidbody rb;
    private PianoKey targetKey;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void SetTarget(float y, float startY, PianoKey key)
    {
        targetY = y;
        spawnY = startY;
        targetKey = key;
        targetSet = true;
    }

    // Sobrecarga de compatibilidad si alguna llamada antigua solo enviaba 'y'
    public void SetTarget(float y)
    {
        targetY = y;
        spawnY = transform.position.y;
        targetSet = true;
    }

    void FixedUpdate()
    {
        // Limitar velocidad de caida
        if (rb != null && rb.linearVelocity.y < -velocidadMaxima)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -velocidadMaxima, rb.linearVelocity.z);
    }

    void Update()
    {
        if (!targetSet) return;

        // Actualizar intensidad de brillo según la distancia recorrida (0 = arriba, 1 = tecla)
        if (targetKey != null)
        {
            float progress = Mathf.InverseLerp(spawnY, targetY, transform.position.y);
            targetKey.SetGlowProgress(progress);
        }

        if (transform.position.y <= targetY)
        {
            Explotar();
            return;
        }

        if (transform.position.y < -20f)
        {
            ApagarBrillo();
            Destroy(gameObject);
        }
    }

    void Explotar()
    {
        ApagarBrillo();

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }

    void ApagarBrillo()
    {
        if (targetKey != null)
        {
            targetKey.SetGlowProgress(0f);
        }
    }

    void OnDestroy()
    {
        ApagarBrillo();
    }
}