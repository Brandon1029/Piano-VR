using UnityEngine;

public class ControlAmbiente : MonoBehaviour
{
    public AudioSource emisor; 
    public AudioClip sonidoNaturaleza; // Arrastra aquí el de naturaleza
    public AudioClip sonidoPajaros;    // Arrastra aquí el de pajaros

    public void PonerNaturaleza()
    {
        emisor.clip = sonidoNaturaleza;
        emisor.Play();
    }

    public void PonerPajaros()
    {
        emisor.clip = sonidoPajaros;
        emisor.Play();
    }
    
    public void DetenerSonido()
    {
        emisor.Stop();
    }
    void Awake()
{
    // Esto evita que el objeto se destruya al cambiar de escena
    DontDestroyOnLoad(this.gameObject);
}
}