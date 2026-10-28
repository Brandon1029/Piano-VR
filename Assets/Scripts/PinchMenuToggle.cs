using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PinchMenuToggle : MonoBehaviour
{
    [Header("Referencias")]
    public GameObject menuCanvas;
    
    private XRHandSubsystem handSubsystem;
    private bool menuVisible = false;
    private bool wasPinching = false;

    void Start()
    {
        try
        {
            if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null && XRGeneralSettings.Instance.Manager.activeLoader != null)
            {
                handSubsystem = XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PinchMenuToggle] No se pudo obtener XRHandSubsystem (normal en modo Editor sin VR): {ex.Message}");
        }
        
        if (menuCanvas == null)
            menuCanvas = GameObject.Find("CanvasMenus");
            
        if (menuCanvas != null)
            menuCanvas.SetActive(false);
    }

    void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        // Soporte para alternar el menú en PC con la tecla 'M'
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            AlternarMenu("PC Teclado 'M'");
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.M))
        {
            AlternarMenu("PC Teclado 'M'");
        }
#endif
#endif

        if (handSubsystem == null) return;
        
        try
        {
            var leftHand = handSubsystem.leftHand;
            if (!leftHand.isTracked) return;

            bool isPinching = IsPinching(leftHand);

            if (isPinching && !wasPinching)
            {
                AlternarMenu("Gesto de Pellizco VR (Mano Izquierda)");
            }

            wasPinching = isPinching;
        }
        catch { }
    }

    private void AlternarMenu(string origen)
    {
        menuVisible = !menuVisible;
        if (menuCanvas != null)
        {
            menuCanvas.SetActive(menuVisible);
        }
        Debug.Log($"[PinchMenuToggle] Menú Canvas alternado a: {menuVisible} (Disparado por: {origen})");
    }

    bool IsPinching(XRHand hand)
    {
        if (!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexPose)) return false;
        if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbPose)) return false;

        float distance = Vector3.Distance(indexPose.position, thumbPose.position);
        return distance < 0.02f;
    }
}
