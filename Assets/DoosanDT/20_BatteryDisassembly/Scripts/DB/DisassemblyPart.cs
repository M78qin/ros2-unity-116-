using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 零件脚本 - 挂载于最底层可拆零件
/// 持有 BoxCollider（粗）和 MeshCollider（精），执行分离动画，维护完成状态
/// </summary>
public class DisassemblyPart : MonoBehaviour
{
    // ── Inspector 参数 ─────────────────────────────────────

    [Header("碰撞体（运行时自动生成，也可手动拖入）")]
    public BoxCollider coarseCollider;
    public MeshCollider fineCollider;

    [Header("拆解设置")]
    [Tooltip("拆解完成后隐藏零件（Renderer 关闭）")]
    public bool hideOnComplete = false;

    [Header("同步组设置（用于螺钉等容器下的同步分离）")]
    [Tooltip("勾选后，点击此零件会触发 syncGroupRoot 下所有兄弟零件同步分离")]
    public bool isSynchronizedGroupMember = false;
    [Tooltip("同步分离的根物体（如 screw 父物体）")]
    public Transform syncGroupRoot;

    [Header("调试功能")]
    [Tooltip("勾选后立即模拟拆解该零件（运行时调试用）")]
    [SerializeField] private bool debug_ForceDisassemble = false;

    // ── 公共只读状态 ───────────────────────────────────────

    public bool isCompleted { get; private set; } = false;
    public LayerGroupConfig.PriorityGroup myPriorityGroup { get; private set; }

    // ── 私有状态 ───────────────────────────────────────────

    private bool _isMoving = false;
    private bool _initialized = false;
    private LayerGroupConfig _layerConfig;
    private Transform _resolvedSyncRoot;

    // ── Unity 生命周期 ────────────────────────────────────

    private void Awake()
    {
        EnsureRuntimeReady();
    }

    private void Update()
    {
        if (!debug_ForceDisassemble) return;
        debug_ForceDisassemble = false;
        TriggerDebugDisassemble();
    }

    private void OnValidate()
    {
        _resolvedSyncRoot = null;
    }

    // ── 初始化工具 ────────────────────────────────────────

    public void EnsureRuntimeReady()
    {
        EnsureColliders();
        EnsureKinematicRigidbody();
        _initialized = true;
    }

    /// <summary>
    /// 确保 BoxCollider 和 MeshCollider 存在（Awake 时调用，也可由 LayerGroupConfig 调用）
    /// </summary>
    public void EnsureColliders()
    {
        if (!TryEnsureBoxCollider(out BoxCollider box))
        {
            Debug.LogWarning($"[碰撞体] {name} 无法创建 BoxCollider，射线可能无法正常命中");
            return;
        }
        coarseCollider = box;
        coarseCollider.enabled = false;

        if (fineCollider == null)
        {
            if (!TryGetComponent(out fineCollider))
            {
                MeshFilter mf = GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    try
                    {
                        fineCollider = gameObject.AddComponent<MeshCollider>();
                        fineCollider.sharedMesh = mf.sharedMesh;
                        fineCollider.convex = true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[碰撞体] {name} 无法创建 MeshCollider：{ex.Message}");
                        fineCollider = null;
                    }
                }
            }
        }

        if (fineCollider != null)
            fineCollider.enabled = false;
    }

    private bool TryEnsureBoxCollider(out BoxCollider collider)
    {
        collider = null;

        if (coarseCollider != null)
            collider = coarseCollider;
        else if (!TryGetComponent(out collider))
        {
            try
            {
                collider = gameObject.AddComponent<BoxCollider>();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[碰撞体] {name} 无法创建 BoxCollider：{ex.Message}");
                collider = null;
            }
        }

        return collider != null;
    }

    /// <summary>确保有运动学 Rigidbody（防止零件受重力掉落）</summary>
    private void EnsureKinematicRigidbody()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeAll;
    }

    // ── 公共配置接口 ──────────────────────────────────────

    public void SetLayerConfig(LayerGroupConfig config) => _layerConfig = config;
    public void SetPriorityGroup(LayerGroupConfig.PriorityGroup group) => myPriorityGroup = group;
    public Transform GetResolvedSyncRoot() => ResolveSyncRoot();

    // ── 碰撞体状态控制 ────────────────────────────────────

    /// <summary>启用/禁用 Box 和 Mesh 碰撞体（无 MeshCollider 时自动回退 Box）</summary>
    public void SetCollidersState(bool boxOn, bool meshOn)
    {
        if (isCompleted)
        {
            DisableAllColliders();
            return;
        }

        if (coarseCollider == null && fineCollider == null)
            EnsureColliders();

        bool hasFine = fineCollider != null;
        bool enableFine = meshOn && hasFine;
        bool enableBox = boxOn || (meshOn && !hasFine);

        if (coarseCollider != null) coarseCollider.enabled = enableBox;
        if (fineCollider != null) fineCollider.enabled = enableFine;
    }

    /// <summary>关闭所有碰撞体</summary>
    public void DisableAllColliders()
    {
        if (coarseCollider != null) coarseCollider.enabled = false;
        if (fineCollider != null) fineCollider.enabled = false;
    }

    // 兼容接口：仍支持系统按模式设置碰撞体
    public void UpdateCollisionState(bool isActive, DisassemblySystem.CollisionMode mode)
    {
        if (!isActive || isCompleted)
        {
            DisableAllColliders();
            return;
        }

        switch (mode)
        {
            case DisassemblySystem.CollisionMode.Coarse:
                SetCollidersState(true, false);
                break;
            case DisassemblySystem.CollisionMode.Fine:
                SetCollidersState(false, true);
                break;
            case DisassemblySystem.CollisionMode.Hybrid:
                SetCollidersState(true, false);
                break;
        }
    }

    // ── 交互触发 ──────────────────────────────────────────

    /// <summary>
    /// 由 SmartRaycaster 调用，触发分离动画。
    /// 自动判断是否为同步组成员，决定单件分离还是整组分离。
    /// </summary>
    public void Interact(float height, float speed)
    {
        if (isCompleted || _isMoving) return;
        if (!_initialized) EnsureRuntimeReady();
        if (_layerConfig == null)
            _layerConfig = GetComponentInParent<LayerGroupConfig>();

        if (DisassemblySystem.Instance != null &&
            !DisassemblySystem.Instance.CheckInteractability(this))
            return;

        Transform syncRoot = ResolveSyncRoot();
        bool useSyncGroup = (syncRoot != null && syncRoot.GetComponentsInChildren<DisassemblyPart>(true).Length > 1);
        if (isSynchronizedGroupMember || useSyncGroup)
            StartCoroutine(SyncGroupDisassembly(height, speed));
        else
            StartCoroutine(SingleDisassembly(height, speed));
    }

    // ── 分离协程 ──────────────────────────────────────────

    /// <summary>单件分离：该零件独立向上移动</summary>
    private IEnumerator SingleDisassembly(float height, float speed)
    {
        BeginDisassembly();

        Vector3 dir = (_layerConfig != null ? _layerConfig.separationDirection : Vector3.up).normalized;
        Vector3 start = transform.position;
        Vector3 target = start + dir * Mathf.Max(0.01f, height);

        while (Vector3.Distance(transform.position, target) > 0.001f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                target,
                Mathf.Max(0.01f, speed) * Time.deltaTime);
            yield return null;
        }
        transform.position = target;

        ApplyCompletionVisual();
        _isMoving = false;

        PostDisassemblyContactCheck();
        _layerConfig?.CheckProgress();
    }

    /// <summary>同步组分离：syncGroupRoot 下所有未完成零件一起向上移动</summary>
    private IEnumerator SyncGroupDisassembly(float height, float speed)
    {
        Transform root = ResolveSyncRoot();
        if (root == null) yield break;

        List<DisassemblyPart> groupParts = root
            .GetComponentsInChildren<DisassemblyPart>(true)
            .Where(p => p != null && !p.isCompleted)
            .ToList();

        if (groupParts.Count == 0) yield break;

        foreach (DisassemblyPart p in groupParts)
            p.BeginDisassembly();

        Vector3 dir = (_layerConfig != null ? _layerConfig.separationDirection : Vector3.up).normalized;
        Vector3 startPos = root.position;
        Vector3 target = startPos + dir * Mathf.Max(0.01f, height);

        while (Vector3.Distance(root.position, target) > 0.001f)
        {
            root.position = Vector3.MoveTowards(
                root.position,
                target,
                Mathf.Max(0.01f, speed) * Time.deltaTime);
            yield return null;
        }
        root.position = target;

        foreach (DisassemblyPart p in groupParts)
        {
            p.ApplyCompletionVisual();
            p._isMoving = false;
        }

        if (_layerConfig == null)
            _layerConfig = groupParts[0]._layerConfig;

        PostDisassemblyContactCheck();
        _layerConfig?.CheckProgress();
    }

    // ── 分离后接触状态重检 ────────────────────────────────

    /// <summary>
    /// 零件分离后，重新检测同层可交互零件的接触状态，更新 Box/Mesh 碰撞体。
    /// 仅在 Hybrid 模式下执行。
    /// </summary>
    private void PostDisassemblyContactCheck()
    {
        if (_layerConfig == null) return;
        if (DisassemblySystem.Instance == null) return;
        if (DisassemblySystem.Instance.detectionMode != DisassemblySystem.CollisionMode.Hybrid) return;

        List<DisassemblyPart> remaining = _layerConfig.GetCurrentInteractableParts();
        if (remaining.Count > 0)
            _layerConfig.RunContactDetection(remaining);
    }

    private void BeginDisassembly()
    {
        _isMoving = true;
        isCompleted = true;
        DisableAllColliders();
    }

    private Transform ResolveSyncRoot()
    {
        if (_resolvedSyncRoot != null) return _resolvedSyncRoot;
        if (syncGroupRoot != null)
        {
            _resolvedSyncRoot = syncGroupRoot;
            return _resolvedSyncRoot;
        }

        // 优先认“自己就是同步组根”
        if (IsSyncRootCandidate(transform))
        {
            _resolvedSyncRoot = transform;
            return _resolvedSyncRoot;
        }

        // 再向上找最近的同步组根：父物体本身挂了 DisassemblyPart，且勾选了同步组；
        // 或者父物体名字像 screw/bolt/fastener 这类容器，并且其下确实有多个零件。
        Transform current = transform.parent;
        while (current != null)
        {
            if (IsSyncRootCandidate(current))
            {
                _resolvedSyncRoot = current;
                return _resolvedSyncRoot;
            }
            current = current.parent;
        }

        return null;
    }

    private bool IsSyncRootCandidate(Transform candidate)
    {
        if (candidate == null) return false;

        DisassemblyPart candidatePart = candidate.GetComponent<DisassemblyPart>();
        if (candidatePart != null && candidatePart.isSynchronizedGroupMember)
            return true;

        if (LooksLikeScrewGroup(candidate.name) && HasMultipleChildParts(candidate))
            return true;

        return false;
    }

    private bool HasMultipleChildParts(Transform root)
    {
        if (root == null) return false;
        return root.GetComponentsInChildren<DisassemblyPart>(true).Length > 1;
    }

    private bool LooksLikeScrewGroup(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string n = name.ToLowerInvariant();
        return n.Contains("screw") || n.Contains("bolt") || n.Contains("fastener") || n.Contains("nut");
    }

    private void ApplyCompletionVisual()
    {
        if (!hideOnComplete) return;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = false;
    }

    // ── 调试强制拆解 ──────────────────────────────────────

    private void TriggerDebugDisassemble()
    {
        if (isCompleted || _isMoving)
        {
            Debug.LogWarning($"[调试] {name} 无法拆解：已完成或正在移动");
            return;
        }

        if (_layerConfig == null)
            _layerConfig = GetComponentInParent<LayerGroupConfig>();
        if (_layerConfig == null)
        {
            Debug.LogError($"[调试] {name} 未找到所属 LayerGroupConfig");
            return;
        }

        if (DisassemblySystem.Instance != null &&
            !DisassemblySystem.Instance.CheckInteractability(this))
        {
            Debug.LogWarning($"[调试] {name} 当前不在可交互序列中，已阻止强拆");
            return;
        }

        Debug.Log($"[调试] 强制拆解 {name}");
        Interact(_layerConfig.liftHeight, _layerConfig.liftSpeed);
    }
}
