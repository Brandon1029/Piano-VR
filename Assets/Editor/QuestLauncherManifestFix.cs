#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PianoVR.Editor
{
    /// <summary>
    /// Solución definitiva para Meta Quest 3 en Unity 6 con OpenXR Meta Quest Support:
    ///
    /// 1. IPostGenerateGradleAndroidProject con callbackOrder => int.MaxValue:
    ///    Se ejecuta al final absoluto tras el post-procesador de Meta OpenXR.
    ///    Busca recursivamente todos los AndroidManifest.xml generados en el árbol del proyecto Gradle
    ///    (unityLibrary, launcher/src/main, launcher/build/intermediates/merged_manifests, etc.)
    ///    y asegura que el intent-filter de la actividad principal conserve:
    ///      - android.intent.category.LAUNCHER (requerido por ADB para 'Build And Run')
    ///      - com.oculus.intent.category.VR (requerido por Meta Quest para modo inmersivo)
    ///
    /// 2. [PostProcessBuild(9999)]:
    ///    Al finalizar el build hacia Android, detecta si hay un visor Quest conectado por ADB
    ///    y ejecuta comandos de respaldo ('monkey' con categoría VR y 'am start') para iniciar
    ///    la aplicación automáticamente en el visor sin depender exclusivamente del despliegue interno de Unity.
    /// </summary>
    public class QuestLauncherManifestFix : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            Debug.Log($"[QuestLauncherManifestFix] Ejecutando corrección de manifiestos con callbackOrder = int.MaxValue en: {path}");

            // Identificar el directorio raíz de Gradle (padre de unityLibrary o launcher)
            string rootGradleDir = path;
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(path);
                if (dirInfo.Name.Equals("unityLibrary", StringComparison.OrdinalIgnoreCase) ||
                    dirInfo.Name.Equals("launcher", StringComparison.OrdinalIgnoreCase))
                {
                    if (dirInfo.Parent != null && dirInfo.Parent.Exists)
                    {
                        rootGradleDir = dirInfo.Parent.FullName;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestLauncherManifestFix] Error al resolver directorio raíz de Gradle: {ex.Message}");
            }

            Debug.Log($"[QuestLauncherManifestFix] Directorio raíz de búsqueda Gradle: {rootGradleDir}");

            // Buscar recursivamente todos los AndroidManifest.xml generados en el proyecto Gradle
            HashSet<string> manifestFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (Directory.Exists(rootGradleDir))
                {
                    string[] found = Directory.GetFiles(rootGradleDir, "AndroidManifest.xml", SearchOption.AllDirectories);
                    foreach (var f in found)
                    {
                        manifestFiles.Add(Path.GetFullPath(f));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestLauncherManifestFix] Error al listar archivos en {rootGradleDir}: {ex.Message}");
            }

            try
            {
                if (Directory.Exists(path) && !path.Equals(rootGradleDir, StringComparison.OrdinalIgnoreCase))
                {
                    string[] found = Directory.GetFiles(path, "AndroidManifest.xml", SearchOption.AllDirectories);
                    foreach (var f in found)
                    {
                        manifestFiles.Add(Path.GetFullPath(f));
                    }
                }
            }
            catch { }

            Debug.Log($"[QuestLauncherManifestFix] Total de manifiestos localizados para auditar: {manifestFiles.Count}");

            int modificados = 0;
            foreach (string manifestPath in manifestFiles)
            {
                if (CorregirManifiesto(manifestPath))
                {
                    modificados++;
                }
            }

            Debug.Log($"[QuestLauncherManifestFix] Proceso completado: {modificados} manifiesto(s) verificado(s) y actualizados con LAUNCHER y VR.");
        }

        private static bool CorregirManifiesto(string manifestPath)
        {
            try
            {
                if (!File.Exists(manifestPath)) return false;

                XDocument doc = XDocument.Load(manifestPath);
                XNamespace androidNs = "http://schemas.android.com/apk/res/android";
                bool modificado = false;

                // Localizar actividades (UnityPlayerActivity, UnityPlayerGameActivity o cualquier actividad con action MAIN)
                var activities = doc.Descendants("activity").ToList();

                foreach (var activity in activities)
                {
                    string activityName = (string)activity.Attribute(androidNs + "name") ?? "";

                    // Buscar intent-filters que contengan la acción MAIN
                    var mainFilters = activity.Elements("intent-filter")
                        .Where(filter => filter.Elements("action")
                            .Any(a => (string)a.Attribute(androidNs + "name") == "android.intent.action.MAIN"))
                        .ToList();

                    if (mainFilters.Count == 0) continue;

                    foreach (var filter in mainFilters)
                    {
                        bool hasLauncher = filter.Elements("category")
                            .Any(c => (string)c.Attribute(androidNs + "name") == "android.intent.category.LAUNCHER");

                        bool hasVR = filter.Elements("category")
                            .Any(c => (string)c.Attribute(androidNs + "name") == "com.oculus.intent.category.VR");

                        if (!hasLauncher)
                        {
                            filter.Add(new XElement("category", new XAttribute(androidNs + "name", "android.intent.category.LAUNCHER")));
                            modificado = true;
                            Debug.Log($"[QuestLauncherManifestFix] + Añadida 'android.intent.category.LAUNCHER' a actividad '{activityName}' en: {manifestPath}");
                        }

                        if (!hasVR)
                        {
                            filter.Add(new XElement("category", new XAttribute(androidNs + "name", "com.oculus.intent.category.VR")));
                            modificado = true;
                            Debug.Log($"[QuestLauncherManifestFix] + Añadida 'com.oculus.intent.category.VR' a actividad '{activityName}' en: {manifestPath}");
                        }
                    }
                }

                if (modificado)
                {
                    doc.Save(manifestPath);
                    Debug.Log($"[QuestLauncherManifestFix] ✅ Manifiesto guardado correctamente: {manifestPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestLauncherManifestFix] Advertencia al procesar '{manifestPath}': {ex.Message}");
            }

            return false;
        }

        // =========================================================================
        // Respaldo de Lanzamiento Automático vía ADB
        // =========================================================================

        [PostProcessBuild(9999)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.Android) return;

            string appId = string.Empty;
            try
            {
                appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            }
            catch
            {
                appId = PlayerSettings.applicationIdentifier;
            }

            if (string.IsNullOrEmpty(appId))
            {
                appId = "com.DefaultCompany.VRTemplate";
            }

            string prodName = PlayerSettings.productName;

            Debug.Log($"[QuestLauncherManifestFix] [PostProcessBuild] Verificando dispositivo conectado para App ID: '{appId}' | Producto: '{prodName}'");

            string adbPath = ObtenerRutaAdb();
            if (string.IsNullOrEmpty(adbPath) || !File.Exists(adbPath))
            {
                Debug.LogWarning("[QuestLauncherManifestFix] No se encontró el ejecutable de ADB en el sistema o editor. Omitiendo lanzamiento directo.");
                return;
            }

            // Comprobar dispositivos ADB conectados
            string devicesOutput = EjecutarComandoAdb(adbPath, "devices");
            if (string.IsNullOrEmpty(devicesOutput)) return;

            string[] lines = devicesOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool dispositivoConectado = lines.Skip(1).Any(l => l.Contains("\tdevice") || l.EndsWith("device"));

            if (!dispositivoConectado)
            {
                Debug.Log("[QuestLauncherManifestFix] No hay dispositivos Quest/Android activos conectados por ADB en este momento.");
                return;
            }

            Debug.Log($"[QuestLauncherManifestFix] 🚀 Dispositivo Quest conectado detectado. Iniciando aplicación '{prodName}' ({appId}) vía ADB...");

            // 1. Lanzamiento por monkey con categoría VR
            string monkeyCmd = $"shell monkey -p {appId} -c com.oculus.intent.category.VR 1";
            string monkeyResult = EjecutarComandoAdb(adbPath, monkeyCmd);
            Debug.Log($"[QuestLauncherManifestFix] ADB Monkey VR: {monkeyResult.Trim()}");

            // 2. Respaldo por am start directo
            string amStartCmd = $"shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n {appId}/com.unity3d.player.UnityPlayerActivity";
            string amResult = EjecutarComandoAdb(adbPath, amStartCmd);
            Debug.Log($"[QuestLauncherManifestFix] ADB am start: {amResult.Trim()}");

            Debug.Log($"[QuestLauncherManifestFix] ✅ Lanzamiento automático completado para '{prodName}' ({appId}).");
        }

        private static string ObtenerRutaAdb()
        {
            // 1. Ruta interna del SDK de Android incluido en Unity Editor
            try
            {
                string editorAppDir = Path.GetDirectoryName(EditorApplication.applicationPath);
                if (!string.IsNullOrEmpty(editorAppDir))
                {
                    string defaultAdb = Path.Combine(editorAppDir, "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
                    if (File.Exists(defaultAdb)) return defaultAdb;
                }
            }
            catch { }

            // 2. Ruta configurada en EditorPrefs (AndroidSdkRoot)
            try
            {
                string sdkRoot = EditorPrefs.GetString("AndroidSdkRoot");
                if (!string.IsNullOrEmpty(sdkRoot))
                {
                    string prefAdb = Path.Combine(sdkRoot, "platform-tools", "adb.exe");
                    if (File.Exists(prefAdb)) return prefAdb;
                }
            }
            catch { }

            // 3. Variables de entorno ANDROID_HOME o ANDROID_SDK_ROOT
            try
            {
                string envHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
                if (!string.IsNullOrEmpty(envHome))
                {
                    string envAdb = Path.Combine(envHome, "platform-tools", "adb.exe");
                    if (File.Exists(envAdb)) return envAdb;
                }

                string envSdk = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
                if (!string.IsNullOrEmpty(envSdk))
                {
                    string envAdb = Path.Combine(envSdk, "platform-tools", "adb.exe");
                    if (File.Exists(envAdb)) return envAdb;
                }
            }
            catch { }

            return "adb";
        }

        private static string EjecutarComandoAdb(string adbPath, string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    if (process == null) return string.Empty;
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);

                    return !string.IsNullOrEmpty(output) ? output : error;
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}

#endif
