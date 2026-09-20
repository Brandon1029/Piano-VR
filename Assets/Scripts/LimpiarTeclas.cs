using UnityEngine;

public class LimpiarTeclas : MonoBehaviour
{
    [ContextMenu("Limpiar Componentes")]
    void Limpiar()
    {
        PianoKey[] teclas = GetComponentsInChildren<PianoKey>(true);

        foreach (PianoKey tecla in teclas)
        {
            GameObject obj = tecla.gameObject;

            // Quitar PokeInteractable si existe
            var poke = obj.GetComponent("PokeInteractable");
            if (poke != null) DestroyImmediate(poke);

            // Quitar PianoKeyPokeHandler si existe
            var handler = obj.GetComponent("PianoKeyPokeHandler");
            if (handler != null) DestroyImmediate(handler);

            // Quitar Rigidbody viejo si existe
            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null) DestroyImmediate(rb);

            // Quitar PianoKey viejo
            var pk = obj.GetComponent<PianoKey>();
            if (pk != null) DestroyImmediate(pk);
        }

        Debug.Log("[LimpiarTeclas] Listo.");
    }
}