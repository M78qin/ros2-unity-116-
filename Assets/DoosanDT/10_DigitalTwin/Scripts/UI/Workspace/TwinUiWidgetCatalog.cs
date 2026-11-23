// TwinUiWidgetCatalog.cs —— V2 卡片清单（P8）
// 新增一张卡片 = 写一个 TwinUiModule 子类 + 在这里 Register 一行 + 在 TwinUiWorkspaceRegistry 的页里排进 WidgetIds。
// 不需要改场景、不需要改布局代码、不需要动外壳。
namespace DigitalTwin.UI
{
    public static class TwinUiWidgetCatalog
    {
        public static int RegisterAll(TwinUiSurfaceHost host)
        {
            if (host == null)
            {
                return 0;
            }

            // 总览 / 数字孪生 / 双向控制 / 拆解实训 / 实验数据 五页共用这 14 张卡片。
            host.Register(new TwinUiWidgetDatalink());
            host.Register(new TwinUiWidgetPerformance());
            host.Register(new TwinUiWidgetTask());
            host.Register(new TwinUiWidgetSync());
            host.Register(new TwinUiWidgetAlarm());
            host.Register(new TwinUiWidgetSession());

            host.Register(new TwinUiWidgetJoints());
            host.Register(new TwinUiWidgetTorque());
            host.Register(new TwinUiWidgetTcp());
            host.Register(new TwinUiWidgetWrench());

            host.Register(new TwinUiWidgetAuthority());
            host.Register(new TwinUiWidgetPlan());
            host.Register(new TwinUiWidgetSteps());
            host.Register(new TwinUiWidgetEval());

            // P9：散落 UI 收编 + 漫游完善
            host.Register(new TwinUiWidgetSelection());
            host.Register(new TwinUiWidgetIk());
            host.Register(new TwinUiWidgetCommand());
            host.Register(new TwinUiWidgetRoamGuide());
            host.Register(new TwinUiWidgetWelcome());

            return host.ModuleCount;
        }
    }
}