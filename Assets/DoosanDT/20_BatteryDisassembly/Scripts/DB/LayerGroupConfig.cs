using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 层级配置脚本 - 挂载于每一拆解层父物体（Lay1 / Lay2 / Lay3 等）
/// 负责：层内优先级调度、双阶段碰撞切换、自动挂载碰撞体组件
/// </summary>
public class LayerGroupConfig : MonoBehaviour
{
    // ── 优先级组数据结构 ───────────────────────────────────

    [System.Serializable]
    public class PriorityGroup
    {
        [Tooltip("优先组名称（如 pri1）")]
        public string groupName;

        [Tooltip("优先组根物体（将 pri1 这个 GameObject 拖入）")]
        public Transform groupRoot;

        [HideInInspector]
        public List<DisassemblyPart> parts = new List<DisassemblyPart>();

        public bool IsGroupComplete() => parts.Count > 0 && parts.All(p => p.isCompleted);
        public bool IsValid() => groupRoot != null && parts.Count > 0;
    }

    // ── Inspector 参数 ─────────────────────────────────────

    [Header("拆解参数（统一控制层内所有零件）")]
    [Tooltip("分离方向（世界空间，默认向上）")]
    public Vector3 separationDirection = Vector3.up;
    [Tooltip("分离距离（米）")]
    public float liftHeight = 0.2f;
    [Tooltip("分离速度（米/秒）")]
    public float liftSpeed = 2.0f;

    [Header("优先组配置（从上到下优先级递减）")]
    [Tooltip("按顺序拖入 pri1、pri2 等 Transform；留空则按 Hierarchy 顺序依次解锁")]
    public List<PriorityGroup> priorityGroups = new List<PriorityGroup>();

    [Header("自动创建零件组件")]
    [Tooltip("勾选后运行时自动给层内有 MeshRenderer 的子物体挂载 DisassemblyPart 和碰撞体")]
    public bool autoCreateParts = true;

    [Header("顺序解锁规则")]
    [Tooltip("没有 pri 时，按空间从上到下解锁；同一行内按右到左")]
    public bool useSpatialOrderWhenNoPriority = true;

    [Header("Hybrid 接触检测")]
    [Tooltip("AABB 额外扩张距离（米），避免边界浮点误差导致漏检")]
    public float contactPadding = 0.002f;
    [Tooltip("打印每一对检测到接触的零件（调试用）")]
    public bool verboseContactLog = false;

    // ── 内部状态（HideInInspector 避免误改） ────────────────

    [HideInInspector] public List<DisassemblyPart> allParts = new List<DisassemblyPart>();
    [HideInInspector] public List<DisassemblyPart> nonPriorityParts = new List<DisassemblyPart>();
    [HideInInspector] public bool hasValidPriorityGroups = false;
    public bool isLayerCompleted { get; private set; } = false;

    private enum LayerStage
    {
        PriorityGroup,
        NonPrioritySequential,
        Completed
    }

    private LayerStage _stage = LayerStage.NonPrioritySequential;
    private int _activePriGroupIdx = -1;
    private int _activeNonPriIdx = -1;
    private bool _initialized = false;
    private DisassemblySystem _system;
    private readonly HashSet<DisassemblyPart> _allPartSet = new HashSet<DisassemblyPart>();

    // ── Unity 生命周期 ────────────────────────────────────

    private void Start()
    {
        // 由 DisassemblySystem 统一初始化，避免执行顺序不确定。
    }

    // ── 初始化 ────────────────────────────────────────────

    public void InitializeForRuntime(DisassemblySystem ownerSystem, int layerIndex)
    {
        _system = ownerSystem;

        NormalizeSettings();
        if (autoCreateParts) AutoCreateMissingPartComponents();
        InitializeGroupAndParts();
        ResolveInitialStage();

        _initialized = true;
    }

    private void NormalizeSettings()
    {
        separationDirection = separationDirection.sqrMagnitude > 0.0001f
            ? separationDirection.normalized
            : Vector3.up;
        liftHeight = Mathf.Max(0.01f, liftHeight);
        liftSpeed = Mathf.Max(0.01f, liftSpeed);
        contactPadding = Mathf.Max(0f, contactPadding);
    }

    /// <summary>
    /// 自动给层内含 MeshRenderer、但缺少 DisassemblyPart 和碰撞体的子物体挂载组件。
    /// 空容器物体（仅 Transform，无 Renderer）跳过。
    /// </summary>
    private void AutoCreateMissingPartComponents()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            var go = child.gameObject;
            if (go == gameObject) continue;
            if (go.GetComponent<Renderer>() == null) continue;       // 空容器跳过
            if (go.GetComponent<DisassemblyPart>() != null) continue; // 已有则跳过

            go.AddComponent<DisassemblyPart>();
            Debug.Log($"[自动挂载] {go.name} ← DisassemblyPart");
        }
    }

    /// <summary>
    /// 解析层内零件归属：优先组零件 / 散件，并绑定配置引用。
    /// </summary>
    private void InitializeGroupAndParts()
    {
        allParts.Clear();
        nonPriorityParts.Clear();
        _allPartSet.Clear();
        allParts = GetComponentsInChildren<DisassemblyPart>(true).Distinct().ToList();

        var priorityPartSet = new HashSet<DisassemblyPart>();

        foreach (var group in priorityGroups)
        {
            if (group == null) continue;
            group.parts.Clear();
            if (group.groupRoot == null) continue;

            var partsInGroup = group.groupRoot.GetComponentsInChildren<DisassemblyPart>(true);
            if (partsInGroup.Length == 0) continue;

            foreach (var part in partsInGroup)
            {
                if (part == null) continue;
                if (priorityPartSet.Contains(part))
                {
                    Debug.LogWarning($"[配置警告] 零件 {part.name} 在多个优先组重复出现，后续组将忽略该零件");
                    continue;
                }

                group.parts.Add(part);
                priorityPartSet.Add(part);
                part.SetLayerConfig(this);
                part.SetPriorityGroup(group);
                try
                {
                    part.EnsureColliders();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[层初始化] {part.name} 碰撞体初始化失败：{ex.Message}");
                }
                _allPartSet.Add(part);
            }
        }

        // 散件：不在任何优先组里，按 Hierarchy 顺序排列
        nonPriorityParts = allParts
            .Where(p => !priorityPartSet.Contains(p))
            .OrderByDescending(p => GetNonPrioritySortKey(p).y)
            .ThenByDescending(p => GetNonPrioritySortKey(p).x)
            .ThenBy(p => GetHierarchyOrder(p.transform))
            .ToList();

        foreach (var part in nonPriorityParts)
        {
            part.SetLayerConfig(this);
            part.SetPriorityGroup(null);
            try
            {
                part.EnsureColliders();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[层初始化] {part.name} 碰撞体初始化失败：{ex.Message}");
            }
            _allPartSet.Add(part);
        }

        hasValidPriorityGroups = priorityGroups.Any(g => g != null && g.IsValid());
    }

    private void ResolveInitialStage()
    {
        if (allParts.Count == 0)
        {
            isLayerCompleted = true;
            _stage = LayerStage.Completed;
            _activePriGroupIdx = -1;
            _activeNonPriIdx = -1;
            return;
        }

        if (hasValidPriorityGroups)
        {
            _activePriGroupIdx = FindNextIncompletePriorityGroup(0);
            if (_activePriGroupIdx >= 0)
            {
                _stage = LayerStage.PriorityGroup;
                _activeNonPriIdx = -1;
                isLayerCompleted = false;
                return;
            }
        }

        _activeNonPriIdx = FindNextIncompleteNonPriority(0);
        if (_activeNonPriIdx >= 0)
        {
            _stage = LayerStage.NonPrioritySequential;
            _activePriGroupIdx = -1;
            isLayerCompleted = false;
            return;
        }

        isLayerCompleted = true;
        _stage = LayerStage.Completed;
        _activePriGroupIdx = -1;
        _activeNonPriIdx = -1;
    }

    // ── 设置层激活状态（由 DisassemblySystem 调用） ────────

    /// <summary>
    /// isActive=true  → 开启当前可交互零件的碰撞检测，并执行初始接触检测
    /// isActive=false → 关闭层内所有碰撞体
    /// </summary>
    public void SetLayerActiveState(bool isActive, DisassemblySystem.CollisionMode mode)
    {
        if (!_initialized) return;

        if (!isActive)
        {
            foreach (var part in allParts)
                if (part != null)
                    part.DisableAllColliders();
            return;
        }

        // 关闭所有碰撞体，再按规则开启可交互零件
        foreach (var part in allParts)
            if (part != null)
                part.DisableAllColliders();

        // 找出当前可交互零件列表
        List<DisassemblyPart> interactableParts = GetCurrentInteractableParts();

        foreach (var part in interactableParts)
        {
            switch (mode)
            {
                case DisassemblySystem.CollisionMode.Coarse:
                    part.SetCollidersState(true, false);
                    break;
                case DisassemblySystem.CollisionMode.Fine:
                    part.SetCollidersState(false, true);
                    break;
                case DisassemblySystem.CollisionMode.Hybrid:
                    part.SetCollidersState(true, false); // 初始 Box，接触后动态切换
                    break;
            }
        }

        // Hybrid 模式：激活时执行一次静态接触检测（解决初始静止不触发 OnCollisionEnter 的问题）
        if (mode == DisassemblySystem.CollisionMode.Hybrid && interactableParts.Count > 0)
            RunContactDetection(interactableParts);

        if (_system != null && _system.verboseLog)
            Debug.Log($"[层激活] 层「{name}」| {GetCurrentPhaseDescription()} | 可交互零件数：{interactableParts.Count}");
    }

    // ── 双阶段碰撞：AABB 静态接触检测 ────────────────────

    /// <summary>
    /// 在状态变化时（层激活、零件分离后）执行一次 AABB 包围盒重叠检测，
    /// 动态切换 Box/Mesh 碰撞体，替代依赖物理事件的实时检测。
    /// </summary>
    public void RunContactDetection(List<DisassemblyPart> targetParts)
    {
        if (targetParts == null || targetParts.Count == 0) return;

        var validParts = targetParts
            .Where(p => p != null && !p.isCompleted)
            .Distinct()
            .ToList();
        if (validParts.Count == 0) return;

        // 第一步：全部重置为 Box（粗碰撞）
        foreach (var part in validParts)
            part.SetCollidersState(true, false);

        // 第二步：两两检测 AABB 重叠，有重叠则切换为 Mesh（精碰撞）
        var touchingSet = new HashSet<DisassemblyPart>();
        for (int i = 0; i < validParts.Count; i++)
        {
            if (!TryGetExpandedBounds(validParts[i], out Bounds boundsA)) continue;
            for (int j = i + 1; j < validParts.Count; j++)
            {
                if (!TryGetExpandedBounds(validParts[j], out Bounds boundsB)) continue;
                if (boundsA.Intersects(boundsB))
                {
                    touchingSet.Add(validParts[i]);
                    touchingSet.Add(validParts[j]);
                    if (verboseContactLog)
                        Debug.Log($"[接触检测] {validParts[i].name} ↔ {validParts[j].name} → Mesh");
                }
            }
        }

        // 第三步：接触零件切换为 Mesh；无 MeshCollider 的零件继续使用 Box
        foreach (var part in touchingSet)
        {
            if (part.fineCollider != null) part.SetCollidersState(false, true);
            else part.SetCollidersState(true, false);
        }

        if (_system != null && _system.verboseLog)
            Debug.Log($"[接触检测完成] 层「{name}」接触零件数：{touchingSet.Count} / {validParts.Count}");
    }

    // ── 进度检查（由 DisassemblyPart 完成拆解后调用） ──────

    /// <summary>
    /// 零件完成拆解后，检查当前阶段是否结束并推进到下一阶段。
    /// </summary>
    public void CheckProgress()
    {
        CheckProgress(true);
    }

    public void CheckProgress(bool notifySystem)
    {
        if (!_initialized || isLayerCompleted) return;

        // 1) 优先组阶段推进
        if (_stage == LayerStage.PriorityGroup)
        {
            if (_activePriGroupIdx >= 0 && _activePriGroupIdx < priorityGroups.Count)
            {
                if (!priorityGroups[_activePriGroupIdx].IsGroupComplete())
                {
                    if (notifySystem) _system?.NotifyLayerStateMaybeChanged(this);
                    return;
                }
            }

            int nextGroup = FindNextIncompletePriorityGroup(_activePriGroupIdx + 1);
            if (nextGroup >= 0)
            {
                _activePriGroupIdx = nextGroup;
                if (_system != null && _system.verboseLog)
                    Debug.Log($"[进度] 层「{name}」切换到优先组：{priorityGroups[_activePriGroupIdx].groupName}");
                if (notifySystem) _system?.NotifyLayerStateMaybeChanged(this);
                return;
            }

            _stage = LayerStage.NonPrioritySequential;
            _activePriGroupIdx = -1;
            _activeNonPriIdx = FindNextIncompleteNonPriority(0);
            if (_activeNonPriIdx >= 0)
            {
                if (_system != null && _system.verboseLog)
                    Debug.Log($"[进度] 层「{name}」优先组阶段完成，进入散件顺序阶段");
                if (notifySystem) _system?.NotifyLayerStateMaybeChanged(this);
                return;
            }
        }

        // 2) 散件阶段推进
        if (_stage == LayerStage.NonPrioritySequential)
        {
            int start = Mathf.Max(0, _activeNonPriIdx);
            _activeNonPriIdx = FindNextIncompleteNonPriority(start);
            if (_activeNonPriIdx >= 0)
            {
                if (_system != null && _system.verboseLog)
                    Debug.Log($"[进度] 层「{name}」散件阶段，下一个零件：{nonPriorityParts[_activeNonPriIdx].name}");
                if (notifySystem) _system?.NotifyLayerStateMaybeChanged(this);
                return;
            }
        }

        // 3) 层完成判定（优先组 + 散件都完成）
        if (allParts.All(p => p == null || p.isCompleted))
        {
            isLayerCompleted = true;
            _stage = LayerStage.Completed;
            _activePriGroupIdx = -1;
            _activeNonPriIdx = -1;
            if (_system != null && _system.verboseLog)
                Debug.Log($"[层完成] 层「{name}」全部拆解完成");
        }

        if (notifySystem) _system?.NotifyLayerStateMaybeChanged(this);
    }

    /// <summary>
    /// 当当前阶段没有可交互目标时，尝试推进内部状态。
    /// 主要用于自动跳过“空组/空阶段”，让系统不要卡住。
    /// </summary>
    public bool TryResolveEmptyStage()
    {
        if (!_initialized || isLayerCompleted) return false;
        if (GetCurrentInteractableCount() > 0) return false;

        int beforePri = _activePriGroupIdx;
        int beforeNonPri = _activeNonPriIdx;
        LayerStage beforeStage = _stage;

        CheckProgress(false);

        return beforePri != _activePriGroupIdx ||
               beforeNonPri != _activeNonPriIdx ||
               beforeStage != _stage ||
               isLayerCompleted;
    }

    /// <summary>
    /// 强制把当前层标记为完成，用于空层/坏配置自动跳过。
    /// </summary>
    public void ForceCompleteLayer(string reason = "")
    {
        if (isLayerCompleted) return;

        foreach (var part in allParts)
            if (part != null)
                part.DisableAllColliders();

        isLayerCompleted = true;
        _stage = LayerStage.Completed;
        _activePriGroupIdx = -1;
        _activeNonPriIdx = -1;

        if (_system != null && _system.verboseLog)
        {
            string suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" | {reason}";
            Debug.Log($"[层跳过] 层「{name}」被自动跳过{suffix}");
        }
    }

    // ── 工具方法 ──────────────────────────────────────────

    private bool TryGetExpandedBounds(DisassemblyPart part, out Bounds bounds)
    {
        bounds = default;
        if (part == null || part.coarseCollider == null) return false;
        bounds = part.coarseCollider.bounds;
        if (bounds.size.sqrMagnitude <= 0.0000001f) return false;
        if (contactPadding > 0f) bounds.Expand(contactPadding);
        return true;
    }

    private int FindNextIncompletePriorityGroup(int startIndex)
    {
        for (int i = Mathf.Max(0, startIndex); i < priorityGroups.Count; i++)
        {
            PriorityGroup group = priorityGroups[i];
            if (group == null || !group.IsValid()) continue;
            if (!group.IsGroupComplete()) return i;
        }
        return -1;
    }

    private int FindNextIncompleteNonPriority(int startIndex)
    {
        for (int i = Mathf.Max(0, startIndex); i < nonPriorityParts.Count; i++)
        {
            DisassemblyPart part = nonPriorityParts[i];
            if (part != null && !part.isCompleted) return i;
        }
        return -1;
    }

    public bool ContainsPart(DisassemblyPart part)
    {
        return part != null && _allPartSet.Contains(part);
    }

    // ── 核心逻辑：零件是否可交互 ──────────────────────────

    /// <summary>
    /// 判断零件当前是否允许射线交互。
    /// 规则：
    ///   1. 优先组阶段：仅当前活跃优先组可交互
    ///   2. 散件阶段：按 Hierarchy 顺序逐一解锁
    ///   3. 层完成后：全部不可交互
    /// </summary>
    public bool IsPartInteractable(DisassemblyPart part)
    {
        if (!_initialized || isLayerCompleted || part == null || part.isCompleted) return false;
        if (!ContainsPart(part)) return false;

        if (_stage == LayerStage.PriorityGroup)
        {
            if (_activePriGroupIdx < 0 || _activePriGroupIdx >= priorityGroups.Count) return false;
            PriorityGroup activeGroup = priorityGroups[_activePriGroupIdx];
            return activeGroup != null && activeGroup.parts.Contains(part);
        }

        if (_stage == LayerStage.NonPrioritySequential)
        {
            if (_activeNonPriIdx < 0 || _activeNonPriIdx >= nonPriorityParts.Count) return false;
            return part == nonPriorityParts[_activeNonPriIdx];
        }

        return false;
    }

    /// <summary>获取零件在 Hierarchy 中的全局排序值（父层 * 1000 + 自身索引，支持嵌套）</summary>
    private int GetHierarchyOrder(Transform t)
    {
        int order = t.GetSiblingIndex();
        Transform parent = t.parent;
        int depth = 0;
        while (parent != null && parent != transform)
        {
            order += parent.GetSiblingIndex() * (int)Mathf.Pow(100, ++depth);
            parent = parent.parent;
        }
        return order;
    }

    private Vector3 GetNonPrioritySortKey(DisassemblyPart part)
    {
        if (part == null) return Vector3.zero;
        if (!useSpatialOrderWhenNoPriority) return part.transform.position;
        return transform.InverseTransformPoint(part.transform.position);
    }

    /// <summary>获取当前阶段的可读描述（用于调试输出）</summary>
    public string GetCurrentPhaseDescription()
    {
        if (isLayerCompleted) return "层已完成";
        if (_stage == LayerStage.PriorityGroup &&
            _activePriGroupIdx >= 0 &&
            _activePriGroupIdx < priorityGroups.Count)
        {
            PriorityGroup group = priorityGroups[_activePriGroupIdx];
            string groupName = group != null ? group.groupName : "(null)";
            return $"优先组阶段：{groupName}";
        }
        if (_stage == LayerStage.NonPrioritySequential)
        {
            string partName = (_activeNonPriIdx >= 0 && _activeNonPriIdx < nonPriorityParts.Count)
                ? nonPriorityParts[_activeNonPriIdx].name : "（全部完成）";
            return $"散件阶段，当前：{partName}";
        }
        return "未知阶段";
    }

    /// <summary>获取当前可交互零件列表（供 PerformanceProfiler 精确统计）</summary>
    public List<DisassemblyPart> GetCurrentInteractableParts()
    {
        if (!_initialized || isLayerCompleted) return new List<DisassemblyPart>();

        if (_stage == LayerStage.PriorityGroup)
        {
            if (_activePriGroupIdx < 0 || _activePriGroupIdx >= priorityGroups.Count)
                return new List<DisassemblyPart>();
            PriorityGroup group = priorityGroups[_activePriGroupIdx];
            if (group == null) return new List<DisassemblyPart>();
            return group.parts
                .Where(p => p != null && !p.isCompleted)
                .ToList();
        }

        if (_stage == LayerStage.NonPrioritySequential)
        {
            if (_activeNonPriIdx < 0 || _activeNonPriIdx >= nonPriorityParts.Count)
                return new List<DisassemblyPart>();
            DisassemblyPart part = nonPriorityParts[_activeNonPriIdx];
            if (part == null || part.isCompleted) return new List<DisassemblyPart>();
            return new List<DisassemblyPart> { part };
        }

        return new List<DisassemblyPart>();
    }

    public int GetCurrentInteractableCount()
    {
        return GetCurrentInteractableParts().Count;
    }

    public int GetRemainingPartCount()
    {
        return allParts.Count(p => p != null && !p.isCompleted);
    }

    public bool HasAnyRemainingParts()
    {
        return GetRemainingPartCount() > 0;
    }

    public string GetDebugSummary()
    {
        string stage = GetCurrentPhaseDescription();
        int remaining = GetRemainingPartCount();
        int interactable = GetCurrentInteractableCount();
        string groupInfo = "无优先组";

        if (_stage == LayerStage.PriorityGroup &&
            _activePriGroupIdx >= 0 &&
            _activePriGroupIdx < priorityGroups.Count &&
            priorityGroups[_activePriGroupIdx] != null)
        {
            var group = priorityGroups[_activePriGroupIdx];
            int groupRemaining = group.parts.Count(p => p != null && !p.isCompleted);
            groupInfo = $"{group.groupName} 剩余 {groupRemaining}/{group.parts.Count}";
        }

        string priSummary = priorityGroups
            .Where(g => g != null && g.IsValid())
            .Select(g => $"{g.groupName}:{g.parts.Count(p => p != null && !p.isCompleted)}/{g.parts.Count}")
            .DefaultIfEmpty("无pri")
            .Aggregate((a, b) => a + " | " + b);

        return $"Layer={name} | Stage={stage} | Interactable={interactable} | Remaining={remaining} | Current={groupInfo} | Pri={priSummary}";
    }
}
