using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>
    /// 主面板宿主：负责页签切换（实训任务台 / 孪生监控台 / 工业数据台）、
    /// 共享顶栏的模式显示，以及启动时默认页。三期增加第三页（工业数据台）。
    /// </summary>
    public sealed class TwinUIPageHost : MonoBehaviour
    {
        [SerializeField] private GameObject missionRoot;
        [SerializeField] private GameObject twinOpsRoot;
        [SerializeField] private GameObject industrialRoot;
        [SerializeField] private TwinUIHeaderView header;
        [SerializeField] private TwinUIMissionView missionView;
        [SerializeField] private TwinUITwinOpsView twinOpsView;
        [SerializeField] private TwinUiIndustrialHost industrialHost;
        [SerializeField] private TwinUIStatusBinder binder;
        [SerializeField] private TwinUIButton missionTab;
        [SerializeField] private TwinUIButton twinOpsTab;
        [SerializeField] private TwinUIButton industrialTab;
        [SerializeField] private int defaultPage;

        private int _page = -1;
        private float _lastMissionRefresh;

        public int Page => _page;
        public bool HasIndustrial => industrialRoot != null;

        public void Configure(GameObject mission, GameObject twinOps, TwinUIHeaderView headerRef,
            TwinUIMissionView missionRef, TwinUITwinOpsView opsRef, TwinUIStatusBinder binderRef,
            TwinUIButton missionTabRef, TwinUIButton opsTabRef)
        {
            Configure(mission, twinOps, null, headerRef, missionRef, opsRef, null, binderRef,
                missionTabRef, opsTabRef, null);
        }

        public void Configure(GameObject mission, GameObject twinOps, GameObject industrial,
            TwinUIHeaderView headerRef, TwinUIMissionView missionRef, TwinUITwinOpsView opsRef,
            TwinUiIndustrialHost industrialRef, TwinUIStatusBinder binderRef,
            TwinUIButton missionTabRef, TwinUIButton opsTabRef, TwinUIButton industrialTabRef)
        {
            missionRoot = mission;
            twinOpsRoot = twinOps;
            industrialRoot = industrial;
            header = headerRef;
            missionView = missionRef;
            twinOpsView = opsRef;
            industrialHost = industrialRef;
            binder = binderRef;
            missionTab = missionTabRef;
            twinOpsTab = opsTabRef;
            industrialTab = industrialTabRef;
        }

        private void Start()
        {
            // 运行时重新接线：构建器在编辑态写的 UnityEvent 监听不会序列化。
            Wire();
            Show(defaultPage);
        }

        public void Show(int page)
        {
            _page = Mathf.Clamp(page, 0, industrialRoot != null ? 2 : 1);
            if (missionRoot != null)
            {
                missionRoot.SetActive(_page == 0);
            }

            if (twinOpsRoot != null)
            {
                twinOpsRoot.SetActive(_page == 1);
            }

            if (industrialRoot != null)
            {
                industrialRoot.SetActive(_page == 2);
            }

            if (missionTab != null)
            {
                missionTab.SetSelected(_page == 0);
            }

            if (twinOpsTab != null)
            {
                twinOpsTab.SetSelected(_page == 1);
            }

            if (industrialTab != null)
            {
                industrialTab.SetSelected(_page == 2);
            }

            if (header != null)
            {
                header.SetMode(_page == 0);
            }

            if (_page == 0 && missionView != null)
            {
                missionView.Refresh();
            }

            if (_page == 1 && twinOpsView != null)
            {
                twinOpsView.Refresh();
            }

            if (_page == 2 && industrialHost != null)
            {
                industrialHost.StartHost();
                industrialHost.ShowSub(0);
                industrialHost.RefreshNow();
            }
        }

        public void Wire()
        {
            if (missionTab != null && missionTab.Button != null)
            {
                missionTab.Button.onClick.RemoveAllListeners();
                missionTab.Button.onClick.AddListener(() => Show(0));
            }

            if (twinOpsTab != null && twinOpsTab.Button != null)
            {
                twinOpsTab.Button.onClick.RemoveAllListeners();
                twinOpsTab.Button.onClick.AddListener(() => Show(1));
            }

            if (industrialTab != null && industrialTab.Button != null)
            {
                industrialTab.Button.onClick.RemoveAllListeners();
                industrialTab.Button.onClick.AddListener(() => Show(2));
            }
        }
    }
}