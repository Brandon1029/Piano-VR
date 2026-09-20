using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class PinchMenuToggle : MonoBehaviour
{
    [Header("Referencias")]
    public GameObject menuCanvas;
    
    private XRHandSubsystem handSubsystem;
    private bool menuVisible = false;
    private bool wasPinching = false;

    void Start()
    {
        var xrManager = XRGeneralSettings.Instance.Manager;
        handSubsystem = xrManager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
        
        if (menuCanvas == null)
            menuCanvas = GameObject.Find("CanvasMenus");
            
        menuCanvas.SetActive(false);
    }

    void Update()
{
    if (handSubsystem == null) return;
    
    try
    {
        var leftHand = handSubsystem.leftHand;
        if (!leftHand.isTracked) return;

        bool isPinching = IsPinching(leftHand);

        if (isPinching && !wasPinching)
        {
            menuVisible = !menuVisible;
            menuCanvas.SetActive(menuVisible);
        }

        wasPinching = isPinching;
    }
    catch { }
}

    bool IsPinching(XRHand hand)
    {
        if (!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexPose)) return false;
        if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbPose)) return false;

        float distance = Vector3.Distance(indexPose.position, thumbPose.position);
        return distance < 0.02f;
    }
}