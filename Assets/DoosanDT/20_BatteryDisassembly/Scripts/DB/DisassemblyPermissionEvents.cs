// DisassemblyPermissionEvents.cs -- throttled denial signal for the gated disassembly flow.
// Emitted from SmartRaycaster (ray filter) and DisassemblyPart.Interact (fallback).
// READ-ONLY by design: nothing here can reach /dt/cmd/*.
using System;
using System.Collections.Generic;
using UnityEngine;

public struct DisassemblyPermissionDenied
{
    public string partName;
    public string layerName;
    public string currentLayer;
    public string reason;
    public string source;      // "raycast" | "interact"
    public long monoNs;
    public int frame;
}

public static class DisassemblyPermissionEvents
{
    /// <summary>Throttle window per part, seconds. Repeated hovers inside the window are counted, not re-emitted.</summary>
    public const float ThrottleSeconds = 0.25f;

    public static event Action<DisassemblyPermissionDenied> Denied;

    public static long DeniedCount { get; private set; }
    public static long ThrottledCount { get; private set; }
    public static DisassemblyPermissionDenied LastDenied { get; private set; }
    public static bool HasLastDenied { get; private set; }

    private static readonly Dictionary<int, float> LastByPart = new Dictionary<int, float>(64);

    public static void NotifyDenied(DisassemblyPart part, string reason, string source)
    {
        if (part == null) return;

        float now = Time.unscaledTime;
        int id = part.GetInstanceID();
        float last;
        if (LastByPart.TryGetValue(id, out last) && (now - last) < ThrottleSeconds)
        {
            ThrottledCount++;
            return;
        }
        LastByPart[id] = now;

        var layer = part.GetComponentInParent<LayerGroupConfig>();
        var sys = DisassemblySystem.Instance;
        var d = new DisassemblyPermissionDenied
        {
            partName = part.name,
            layerName = layer != null ? layer.name : "n/a",
            currentLayer = sys != null && sys.layers != null && sys.currentLayerIndex >= 0 &&
                           sys.currentLayerIndex < sys.layers.Count && sys.layers[sys.currentLayerIndex] != null
                ? sys.layers[sys.currentLayerIndex].name
                : "n/a",
            reason = reason,
            source = source,
            monoNs = (long)(Time.realtimeSinceStartupAsDouble * 1e9),
            frame = Time.frameCount
        };

        LastDenied = d;
        HasLastDenied = true;
        DeniedCount++;

        var handler = Denied;
        if (handler != null)
        {
            try { handler(d); }
            catch (Exception e) { Debug.LogError("[Perm] denied handler threw: " + e.Message); }
        }
    }

    public static void ResetCounters()
    {
        DeniedCount = 0;
        ThrottledCount = 0;
        HasLastDenied = false;
        LastDenied = default(DisassemblyPermissionDenied);
        LastByPart.Clear();
    }
}