#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

namespace PianoVR.Editor
{
    /// <summary>
    /// Herramienta de Editor para auditar y configurar automáticamente todos los componentes de física
    /// necesarios para la interacción del piano con Meta Quest (Hand Tracking y Mandos) y pruebas en PC.
    /// Menú: Tools ▸ Auditar y Configurar Físicas VR del Piano
    /// </summary>
    public static class AuditVRPhysics
    {
        private const string XR_ORIGIN_HANDS_PREFAB_PATH = "Assets/Samples/XR Interaction Toolkit/3.4.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";

        [MenuItem("Tools/Auditar y Configurar Físicas VR del Piano")]
        public static void EjecutarAuditoria()
        {
            int teclasConfiguradas = ConfigurarTeclasEnEscena();
            int manosConfiguradas = ConfigurarManosYMandosEnEscena();
            bool prefabActualizado = AuditarYConfigurarPrefabXROrigin();

            string mensaje = $"Auditoría y configuración completada con éxito:\n\n" +
                             $"• Teclas de piano verificadas: {teclasConfiguradas} (BoxCollider Trigger + Rigidbody Kinematic)\n" +
                             $"• Objetos de manos y mandos configurados en escena: {manosConfiguradas} (SphereCollider Trigger + Rigidbody Kinematic)\n" +
                             $"• Prefab XR Origin actualizado: {(prefabActualizado ? "SÍ" : "Ya estaba configurado o no encontrado")}\n\n" +
                             $"Ahora tanto los dedos de Hand Tracking como los mandos físicos y el ratón en PC activarán las teclas instantáneamente.";

            Debug.Log($"[AuditVRPhysics] {mensaje}");
            EditorUtility.DisplayDialog("Físicas VR del Piano Configuradas", mensaje, "Aceptar");
        }

        private static int ConfigurarTeclasEnEscena()
        {
            PianoKey[] teclas = Object.FindObjectsByType<PianoKey>(FindObjectsSortMode.None);
            int count = 0;

            foreach (var tecla in teclas)
            {
                if (tecla == null) continue;

                Undo.RecordObject(tecla.gameObject, "Configurar Físicas PianoKey");

                tecla.AsegurarFisicasTecla();

                EditorUtility.SetDirty(tecla.gameObject);
                count++;
            }

            return count;
        }

        private static int ConfigurarManosYMandosEnEscena()
        {
            Transform[] todos = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            int count = 0;

            foreach (Transform t in todos)
            {
                if (t == null) continue;

                // Ignorar teclas del piano, notas y chasis
                if (t.GetComponent<PianoKey>() != null || t.GetComponentInParent<PianoKey>() != null) continue;
                if (t.GetComponent<NotaMovimiento>() != null || t.GetComponentInParent<NotaMovimiento>() != null) continue;

                string n = t.name.ToLower();

                bool esPoke = n.Contains("poke");
                bool esManoOMando = n.Contains("left hand") || n.Contains("right hand") ||
                                    n.Contains("lefthand")   || n.Contains("righthand")  ||
                                    n.Contains("controller") || n.Contains("interactor") ||
                                    n.Contains("hand tracking") || n.Contains("questvisual");

                bool esPuntaDedo = (n.Contains("index")  && (n.Contains("tip") || n.Contains("distal"))) ||
                                   (n.Contains("middle") && (n.Contains("tip") || n.Contains("distal"))) ||
                                   (n.Contains("ring")   && (n.Contains("tip") || n.Contains("distal"))) ||
                                   (n.Contains("pinky")  && (n.Contains("tip") || n.Contains("distal"))) ||
                                   (n.Contains("little") && (n.Contains("tip") || n.Contains("distal"))) ||
                                   (n.Contains("thumb")  && (n.Contains("tip") || n.Contains("distal")));

                if (esPoke || esManoOMando || esPuntaDedo)
                {
                    ConfigurarObjetoVR(t.gameObject, esPoke || esPuntaDedo);
                    count++;
                }
            }

            return count;
        }

        private static void ConfigurarObjetoVR(GameObject obj, bool esPunta)
        {
            Undo.RecordObject(obj, "Configurar Colisionador VR");

            SphereCollider sc = obj.GetComponent<SphereCollider>();
            if (sc == null)
            {
                sc = Undo.AddComponent<SphereCollider>(obj);
            }

            sc.isTrigger = true;
            float radioDeseado = esPunta ? 0.025f : 0.035f;
            if (sc.radius < 0.015f || sc.radius > 0.08f)
            {
                sc.radius = radioDeseado;
            }

            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(obj);
            }

            rb.isKinematic            = true;
            rb.useGravity             = false;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            EditorUtility.SetDirty(obj);
        }

        private static bool AuditarYConfigurarPrefabXROrigin()
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(XR_ORIGIN_HANDS_PREFAB_PATH);
            if (prefabAsset == null) return false;

            string assetPath = AssetDatabase.GetAssetPath(prefabAsset);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
            if (prefabRoot == null) return false;

            bool modificado = false;
            Transform[] transforms = prefabRoot.GetComponentsInChildren<Transform>(true);

            foreach (var t in transforms)
            {
                if (t == null) continue;
                string n = t.name.ToLower();

                bool esPoke = n.Contains("poke");
                bool esMano = n.Contains("left hand") || n.Contains("right hand");

                if (esPoke || esMano)
                {
                    SphereCollider sc = t.GetComponent<SphereCollider>();
                    if (sc == null)
                    {
                        sc = t.gameObject.AddComponent<SphereCollider>();
                        modificado = true;
                    }

                    sc.isTrigger = true;
                    float radioDeseado = esPoke ? 0.025f : 0.035f;
                    if (sc.radius < 0.015f || sc.radius > 0.08f)
                    {
                        sc.radius = radioDeseado;
                        modificado = true;
                    }

                    Rigidbody rb = t.GetComponent<Rigidbody>();
                    if (rb == null)
                    {
                        rb = t.gameObject.AddComponent<Rigidbody>();
                        modificado = true;
                    }

                    rb.isKinematic            = true;
                    rb.useGravity             = false;
                    rb.interpolation          = RigidbodyInterpolation.Interpolate;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }
            }

            if (modificado)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                Debug.Log($"[AuditVRPhysics] ✅ Prefab guardado con nuevos SphereColliders y Rigidbodies: {assetPath}");
            }

            PrefabUtility.UnloadPrefabContents(prefabRoot);
            return modificado;
        }
    }
}
#endif
