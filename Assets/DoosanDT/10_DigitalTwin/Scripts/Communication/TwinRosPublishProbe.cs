// TwinRosPublishProbe.cs — Ros2Bridge 发布计数器（只读取证用，2026-11-18）。
//
// 目的：为「TCP 拖动 / IK 只驱动 Ghost，零下发」提供可直接断言的证据。
// 做法：在 Ros2Bridge 每个真正执行 `_ros.Publish(...)` 的位置前自增一次计数。
// 约束：
//   * 只自增计数，不改变发送内容、不注册发布者、不建立/断开连接；
//   * Ros2Bridge 原有返回值与异常语义完全不变（计数发生在调用前一行，不吞异常）；
//   * 计数是静态的，跨场景/跨测试累计，测试用 Reset() 归零后取 delta。
using System.Threading;

namespace DigitalTwin
{
    public static class TwinRosPublishProbe
    {
        private static long _total;
        private static long _command;
        private static long _motion;
        private static long _plan;
        private static string _lastTopic = string.Empty;

        /// <summary>所有话题的发布尝试次数（每次真正调用 _ros.Publish 计 1）。</summary>
        public static long Total => Interlocked.Read(ref _total);
        public static long CommandTotal => Interlocked.Read(ref _command);
        public static long MotionTotal => Interlocked.Read(ref _motion);
        public static long PlanTotal => Interlocked.Read(ref _plan);
        public static string LastTopic => Volatile.Read(ref _lastTopic) ?? string.Empty;

        public static void CountCommand(string topic)
        {
            Record(topic);
            Interlocked.Increment(ref _command);
        }

        public static void CountMotion(string topic)
        {
            Record(topic);
            Interlocked.Increment(ref _motion);
        }

        public static void CountPlan(string topic)
        {
            Record(topic);
            Interlocked.Increment(ref _plan);
        }

        public static void Reset()
        {
            Interlocked.Exchange(ref _total, 0);
            Interlocked.Exchange(ref _command, 0);
            Interlocked.Exchange(ref _motion, 0);
            Interlocked.Exchange(ref _plan, 0);
            Volatile.Write(ref _lastTopic, string.Empty);
        }

        private static void Record(string topic)
        {
            Interlocked.Increment(ref _total);
            if (!string.IsNullOrEmpty(topic))
            {
                Volatile.Write(ref _lastTopic, topic);
            }
        }
    }
}