// PermissionStateBridge.cs -- READ-ONLY bridge for the Step-3 -> Step-4 permission interlock.
// It only OBSERVES DisassemblyPermissionEvents; it never calls Ros2Bridge, TwinCommandController
// or any /dt/cmd/* publisher. Counters here are the evidence surface for the offline verification.
using UnityEngine;
using System.Text;

public sealed class PermissionStateBridge : MonoBehaviour
{
    [Header("Runtime counters (read-only)")]
    public long deniedCount;
    public long throttledCount;
    public string lastPartName = "";
    public string lastReason = "";
    public string lastSource = "";
    public string lastLayerName = "";
    public string lastCurrentLayer = "";
    public long lastMonoNs;
    public int lastFrame = -1;

    [Header("Options")]
    [Tooltip("Also mirror every accepted denial to a normal log line (throttled upstream).")]
    public bool logDenials = true;

    [Tooltip("Keep at most this many denial records for inspection.")]
    public int historyLimit = 32;

    public readonly System.Collections.Generic.List<DisassemblyPermissionDenied> History =
        new System.Collections.Generic.List<DisassemblyPermissionDenied>();

    private void OnEnable()
    {
        DisassemblyPermissionEvents.Denied -= OnDenied;
        DisassemblyPermissionEvents.Denied += OnDenied;
    }

    private void OnDisable()
    {
        DisassemblyPermissionEvents.Denied -= OnDenied;
    }

    private void OnDenied(DisassemblyPermissionDenied d)
    {
        deniedCount = DisassemblyPermissionEvents.DeniedCount;
        throttledCount = DisassemblyPermissionEvents.ThrottledCount;
        lastPartName = d.partName;
        lastReason = d.reason;
        lastSource = d.source;
        lastLayerName = d.layerName;
        lastCurrentLayer = d.currentLayer;
        lastMonoNs = d.monoNs;
        lastFrame = d.frame;

        History.Add(d);
        if (History.Count > Mathf.Max(1, historyLimit)) History.RemoveAt(0);

        if (logDenials)
            Debug.Log("[Perm] DENIED part='" + d.partName + "' layer='" + d.layerName +
                      "' current='" + d.currentLayer + "' reason=" + d.reason + " src=" + d.source +
                      " count=" + deniedCount);
    }

    /// <summary>One-line snapshot used by the offline verifier and by HUD overlays.</summary>
    public string Snapshot()
    {
        var sb = new StringBuilder();
        sb.Append("denied=").Append(deniedCount)
          .Append(" throttled=").Append(throttledCount)
          .Append(" last=").Append(lastPartName)
          .Append('/').Append(lastReason)
          .Append(" cur=").Append(lastCurrentLayer);
        return sb.ToString();
    }

    /// <summary>Composite key so repeated hovers on the same part inside the window are one record.</summary>
    public static string KeyOf(DisassemblyPermissionDenied d)
    {
        return d.partName + "|" + d.reason + "|" + d.source;
    }
}