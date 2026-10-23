using UnityEngine;
using TMPro;

public class EfectoPopUpNota : MonoBehaviour
{
    public float velocidadSubida = 0.2f;
    public float tiempoVida = 0.6f;
    private TMP_Text texto;
    private Color colorInicial;
    private float contadorTiempo = 0f;

    void Awake()
    {
        texto = GetComponent<TMP_Text>();
        if (texto != null) colorInicial = texto.color;
    }

    public void Inicializar(string nombreNota)
    {
        if (texto == null) texto = GetComponent<TMP_Text>();
        texto.text = nombreNota;
    }

    void Update()
    {
        // 1. Eleva el texto flotante en el espacio 3D
        transform.position += Vector3.up * velocidadSubida * Time.deltaTime;

        // 2. Hace billboard hacia la cámara del usuario en VR
        if (Camera.main != null)
        {
            transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
        }

        // 3. Desvanece el alfa suavemente
        contadorTiempo += Time.deltaTime;
        float alfa = Mathf.Lerp(1f, 0f, contadorTiempo / tiempoVida);

        if (texto != null)
        {
            texto.color = new Color(colorInicial.r, colorInicial.g, colorInicial.b, alfa);
        }

        // 4. Se autodestruye tras cumplir su tiempo
        if (contadorTiempo >= tiempoVida)
        {
            Destroy(gameObject);
        }
    }
}