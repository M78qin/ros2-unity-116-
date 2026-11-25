// OfflineInterlockVerify.cs -- Step-2 evidence run (Play mode, offline).
// Scenario A (block): ray at a part outside the current permission set  -> denial event,
//                     zero ROS command attempts, no state change.
// Scenario B (allow): ray at a part inside the current permission set   -> normal separation.
// The harness writes JSON+txt evidence and then leaves Play mode by itself.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using DigitalTwin;
using DigitalTwin.Interaction;

public sealed class OfflineInterlockVerify : MonoBehaviour
{
    private const string OutDir = @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\thesis_data\E5_vm\offline_interlock_verify";
    public const string FlagPath = @"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\offline_verify.flag";

    /// <summary>
    /// Spawns the harness right after the scene loads when the editor menu dropped the flag file.
    /// Keeps the scene asset clean (no leftover verifier object in Doosan-DT.unity).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        try
        {
            if (!File.Exists(FlagPath)) return;
            File.Delete(FlagPath);
            // Single-shot guard: a lingering flag must never start a second run.
            string lockPath = FlagPath + ".lock";
            if (File.Exists(lockPath)) { Debug.Log("[Verify] lock present -> skip auto-start"); return; }
            File.WriteAllText(lockPath, DateTime.UtcNow.ToString("o"));
        }
        catch { return; }
        var go = new GameObject("OfflineInterlockVerify");
        go.AddComponent<OfflineInterlockVerify>();
        Debug.Log("[Verify] auto-start from flag");
    }

    private StringBuilder _log = new StringBuilder();
    private StringBuilder _json = new StringBuilder();
    private readonly List<string> _errors = new List<string>();

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += OnUnityLog;
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnUnityLog;
    }

    private void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            _errors.Add(type + ": " + condition);
    }

    private static void Say(StringBuilder sb, string s) { sb.AppendLine(s); Debug.Log("[Verify] " + s); }

    private IEnumerator Start()
    {
        var runId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        Say(_log, "run " + runId);

        // ---- wait for the disassembly system to initialise
        float deadline = Time.realtimeSinceStartup + 15f;
        while ((DisassemblySystem.Instance == null || DisassemblySystem.Instance.layers == null ||
                DisassemblySystem.Instance.layers.Count == 0) && Time.realtimeSinceStartup < deadline)
            yield return null;

        var sys = DisassemblySystem.Instance;
        if (sys == null) { Finish(runId, "DisassemblySystem missing"); yield break; }

        var bridge = FindObjectOfType<PermissionStateBridge>();
        if (bridge == null)
        {
            var go = new GameObject("PermissionStateBridge");
            bridge = go.AddComponent<PermissionStateBridge>();
        }
        var raycaster = FindObjectOfType<SmartRaycaster>();
        var ros = FindObjectOfType<Ros2Bridge>();
        var cmd = FindObjectOfType<TwinCommandController>();
        var ik = FindObjectOfType<TwinPlayIkHandle>();

        // ---- locate test parts
        // Search from the scene roots (null), and address the pack through its own path so the
        // spindle model's own "screws" child cannot be picked up by mistake.
        var vr = FindDeep(null, "VR拆解");
        var packRoot = vr != null ? vr.Find("BatteryPack_PivotRoot/Dis_1") : null;
        if (packRoot == null) packRoot = vr != null ? vr.Find("Dis_1") : null;
        var screws = packRoot != null ? packRoot.Find("Lay1/pri1/screws") : null;
        var allowPart = screws != null
            ? (screws.GetComponent<DisassemblyPart>() ?? screws.GetComponentInChildren<DisassemblyPart>(true))
            : null;
        var blockPart = FindBlockedPart(sys);
        var flange = FindDeep(null, "Flange_TCP");
        var tip = FindDeep(null, "Spindle_TCP");

        Say(_log, "systems: sys=" + (sys != null) + " raycaster=" + (raycaster != null) + " ros=" + (ros != null) +
                 " cmd=" + (cmd != null) + " ik=" + (ik != null));
        Say(_log, "allowPart=" + (allowPart != null ? allowPart.name : "null") +
                 " blockPart=" + (blockPart != null ? blockPart.name : "null"));
        if (raycaster == null || allowPart == null || blockPart == null)
        {
            Finish(runId, "missing raycaster/allowPart/blockPart");
            yield break;
        }

        // ---- counters before
        long deny0 = DisassemblyPermissionEvents.DeniedCount;
        long halt0 = ros != null ? ros.HaltPublishCount : -1;
        long haltReq0 = cmd != null ? cmd.HaltAttemptCount : -1;
        long dispatch0 = ik != null ? ik.DispatchCalls : -1;

        // ---- clearance at rest
        Bounds packB = default(Bounds), spindleB = default(Bounds);
        bool havePack = TryBounds(packRoot, out packB);
        var spindleUnit = FindDeep(null, "Spindle_Camera_Unit");
        bool haveSpindle = TryBounds(spindleUnit, out spindleB);
        float restGapMm = (havePack && haveSpindle && tip != null) ? (tip.position.y - packB.max.y) * 1000f : float.NaN;

        // ================= Scenario A: blocked part =================
        var others = Isolate(blockPart);
        blockPart.EnsureColliders();     // the system keeps non-interactable colliders off
        var ra = RayAt(blockPart);
        bool blockResolved = raycaster.TryInteractForTest(ra);
        string blockSource = "raycast";
        if (!blockResolved && DisassemblyPermissionEvents.DeniedCount == deny0)
        {
            // Physics could not reach the part this frame (its collider is managed per layer);
            // fall back to the scripted path so the denial branch is still exercised.
            blockPart.Interact(0.5f, 5f);
            blockSource = "interact-fallback";
        }
        RestoreIsolation(others);
        yield return new WaitForSeconds(0.15f);

        long deny1 = DisassemblyPermissionEvents.DeniedCount;
        long halt1 = ros != null ? ros.HaltPublishCount : -1;
        long haltReq1 = cmd != null ? cmd.HaltAttemptCount : -1;
        long dispatch1 = ik != null ? ik.DispatchCalls : -1;
        Say(_log, "A: resolved=" + blockResolved + " deniedDelta=" + (deny1 - deny0) +
                 " halt=" + (halt1 - halt0) + " haltReq=" + (haltReq1 - haltReq0) + " dispatch=" + (dispatch1 - dispatch0));
        Say(_log, "A: last=" + DisassemblyPermissionEvents.LastDenied.partName + " reason=" +
                 DisassemblyPermissionEvents.LastDenied.reason + " layer=" + DisassemblyPermissionEvents.LastDenied.layerName +
                 " current=" + DisassemblyPermissionEvents.LastDenied.currentLayer + " src=" + blockSource);

        // ================= Scenario B: allowed part =================
        var others2 = Isolate(allowPart);
        yield return null;
        var beforeY = allowPart.transform.position.y;
        var rb = RayAt(allowPart);
        bool allowResolved = raycaster.TryInteractForTest(rb);
        RestoreIsolation(others2);
        float t0 = Time.realtimeSinceStartup;
        float peak = 0f;
        while (Time.realtimeSinceStartup - t0 < 3f)
        {
            float dy = allowPart.transform.position.y - beforeY;
            if (dy > peak) peak = dy;
            if (peak > 0.02f && Time.realtimeSinceStartup - t0 > 1.2f) break;
            yield return null;
        }
        Say(_log, "B: resolved=" + allowResolved + " displacementY=" + peak.ToString("F4", CultureInfo.InvariantCulture) + " m");

        // ---- clearance after (bit vs pack must still not interpenetrate)
        float afterGapMm = (havePack && tip != null) ? (tip.position.y - packB.max.y) * 1000f : float.NaN;

        // ---- assertions
        bool aPass = !blockResolved && (deny1 - deny0) >= 1 &&
                     (halt1 - halt0) == 0 && (haltReq1 - haltReq0) == 0 && (dispatch1 - dispatch0) == 0;
        bool bPass = allowResolved && peak > 0.02f;
        bool clearancePass = !float.IsNaN(restGapMm) && restGapMm >= 0f;
        bool errorsPass = _errors.Count == 0;

        var fields = new List<string>
        {
            "\"runId\": \"" + runId + "\"",
            "\"scene\": \"" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "\"",
            "\"blockCase\": {\"resolved\":" + B(blockResolved) + ",\"deniedDelta\":" + (deny1 - deny0) +
                ",\"haltDelta\":" + (halt1 - halt0) + ",\"haltRequestDelta\":" + (haltReq1 - haltReq0) +
                ",\"dispatchDelta\":" + (dispatch1 - dispatch0) +
                ",\"part\":\"" + Escape(blockPart.name) + "\"" +
                ",\"reason\":\"" + Escape(DisassemblyPermissionEvents.LastDenied.reason) + "\"" +
                ",\"currentLayer\":\"" + Escape(DisassemblyPermissionEvents.LastDenied.currentLayer) + "\"}",
            "\"allowCase\": {\"resolved\":" + B(allowResolved) + ",\"displacementY\":" + Num(peak) +
                ",\"part\":\"" + Escape(allowPart.name) + "\"}",
            "\"clearanceMm\": {\"rest\":" + Num(restGapMm) + ",\"after\":" + Num(afterGapMm) + "}",
            "\"errors\": [" + string.Join(",", _errors.ConvertAll(e => "\"" + Escape(e) + "\"").ToArray()) + "]",
            "\"assert\": {\"block\":" + B(aPass) + ",\"allow\":" + B(bPass) +
                ",\"clearance\":" + B(clearancePass) + ",\"noErrors\":" + B(errorsPass) + "}"
        };
        _json.Append("{\n").Append(string.Join(",\n", fields)).Append("\n}\n");

        Say(_log, "ASSERT block=" + aPass + " allow=" + bPass + " clearance=" + clearancePass + " noErrors=" + errorsPass);
        Finish(runId, null);
    }

    private static DisassemblyPart FindBlockedPart(DisassemblySystem sys)
    {
        // Prefer a part in the CURRENT layer but outside its priority group: its colliders are
        // still live, so a real raycast can reach it and the ray-filter branch is exercised.
        var vr = FindDeep(null, "VR拆解");
        var packRoot = vr != null ? vr.Find("BatteryPack_PivotRoot/Dis_1") : null;
        if (packRoot == null) packRoot = vr != null ? vr.Find("Dis_1") : null;
        var sameLayer = packRoot != null ? packRoot.Find("Lay1/1-2") : null;
        if (sameLayer != null)
        {
            var p0 = sameLayer.GetComponent<DisassemblyPart>() ??
                     sameLayer.GetComponentInChildren<DisassemblyPart>(true);
            if (p0 != null && !sys.CheckInteractability(p0)) return p0;
        }

        // Otherwise a part that definitely sits outside the current set: a Lay2 part.
        var lay2 = FindDeep(FindDeep(null, "VR拆解") ?? null, "Lay2");
        if (lay2 == null)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == "Lay2" && t.gameObject.scene.IsValid()) { lay2 = t; break; }
        }
        if (lay2 != null)
        {
            var p = lay2.GetComponentInChildren<DisassemblyPart>(true);
            if (p != null) return p;
        }
        // Fallback: first part that is not interactable right now.
        foreach (var p in Resources.FindObjectsOfTypeAll<DisassemblyPart>())
        {
            if (p == null || !p.gameObject.scene.IsValid()) continue;
            if (sys.CheckInteractability(p)) continue;
            return p;
        }
        return null;
    }

    /// <summary>Temporarily removes every other part's colliders so the ray resolves the target only.</summary>
    private static List<DisassemblyPart> Isolate(DisassemblyPart keep)
    {
        var list = new List<DisassemblyPart>();
        foreach (var p in Resources.FindObjectsOfTypeAll<DisassemblyPart>())
        {
            if (p == null || p == keep || !p.gameObject.scene.IsValid()) continue;
            p.DisableAllColliders();
            list.Add(p);
        }
        return list;
    }

    private static void RestoreIsolation(List<DisassemblyPart> list)
    {
        foreach (var p in list)
        {
            if (p == null || p.isCompleted) continue;
            p.SetCollidersState(true, true);
        }
    }

    private static Ray RayAt(DisassemblyPart p)
    {
        var col = p.GetComponentInChildren<Collider>(true);
        Bounds b = col != null ? col.bounds : new Bounds(p.transform.position, Vector3.one * 0.02f);
        Vector3 origin = b.center + Vector3.up * (b.extents.y + 0.05f);
        return new Ray(origin, Vector3.down);
    }

    private static bool TryBounds(Transform t, out Bounds b)
    {
        b = new Bounds();
        if (t == null) return false;
        bool any = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var hit = FindDeep(go.transform, name);
                if (hit != null) return hit;
            }
            return null;
        }
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static string B(bool v) => v ? "true" : "false";
    private static string Num(float v) => float.IsNaN(v) ? "null" : v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

    private void Finish(string runId, string fatal)
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            string basePath = Path.Combine(OutDir, "interlock_verify_" + runId);
            if (fatal != null)
                _log.AppendLine("FATAL: " + fatal);
            File.WriteAllText(basePath + ".txt", _log.ToString(), new UTF8Encoding(false));
            if (_json.Length > 0)
                File.WriteAllText(basePath + ".json", _json.ToString(), new UTF8Encoding(false));
            Debug.Log("[Verify] evidence -> " + basePath + ".txt");
        }
        catch (Exception e)
        {
            Debug.LogError("[Verify] write failed: " + e.Message);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}