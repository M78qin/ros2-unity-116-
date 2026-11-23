// P0StopAcceptanceTests.cs — P0 停止链路（F1/F2/F3）PlayMode 验收，2026-11-18。
//
// 运行方式（与 L1/L2 同一测试程序集，反射访问 Assembly-CSharp）：
//   Unity.exe -batchmode -projectPath <副本> -runTests -testPlatform PlayMode
//             -testFilter P0Stop -testResults results.xml -logFile run.log
// 环境变量：
//   P0_EVIDENCE_DIR  证据落盘目录（默认 <project>/Logs/P0Stop）
//   P0_LIVE=1        允许跑"真实 ROS2 环回"用例（需要 127.0.0.1:10002 endpoint + adapter）
//
// 判据（不得下调）：
//   T1  连续点运动指令期间 EmergencyStop() 之后 100 ms 内 /dt/cmd/motion 发布计数不再增长。
//       **这是"软件阻止新增运动命令"的口径，不代表物理停止时间。**
//   T2  /dt/cmd/halt 的消息类型唯一（std_msgs/Bool）；发布者身份明确（Ros2Bridge.SendHalt）；
//       ROS2 侧确实收到（P0_LIVE 用例 + WSL 侧 ros2 topic echo / adapter 日志）；
//       一次急停只产生一次 HALT（无连环刷屏）。**不要求"只有一个发布者"**。
//   T3  闩锁期间 CanSendRealMove=false、EnterExecute()==false。
//   T4  解除闩锁需远端停止确认；远端未知/失败时保持闩锁。
//   T5  10003/域87 隔离通道取消后：halt 不丢失、不重复、不跨域，闩锁不被误清。
//
// 安全边界：本测试不调用 MoveJoint/MoveLine/MoveStop 真指令，不启动机器人驱动；
// 只有 /dt/cmd/motion 与 /dt/cmd/halt 两个话题消息，由 adapter 以 dry_run:=true 消费。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalTwin.Acceptance
{
    /// <summary>P0 证据累积器：每个用例后落盘，进程意外丢失只损失当前用例。</summary>
    internal static class P0Ev
    {
        public static readonly string Dir =
            Environment.GetEnvironmentVariable("P0_EVIDENCE_DIR") ?? Path.GetFullPath("Logs/P0Stop");

        private static readonly List<string> Sections = new List<string>();
        private static readonly List<string> Notes = new List<string>();

        public static void Add(string key, string jsonLiteral)
        {
            Sections.RemoveAll(s => s.StartsWith("\"" + key + "\":", StringComparison.Ordinal));
            Sections.Add("\"" + key + "\": " + jsonLiteral);
        }

        public static void Note(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + s;
            Notes.Add(line);
            Debug.Log("[P0TEST] " + s);
        }

        public static string Q(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        public static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string B(bool v) => v ? "true" : "false";

        public static void Flush()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"generated\": " + Q(DateTime.Now.ToString("o", CultureInfo.InvariantCulture)) + ",");
                for (int i = 0; i < Sections.Count; i++)
                {
                    sb.AppendLine("  " + Sections[i] + (i < Sections.Count - 1 ? "," : ""));
                }

                sb.AppendLine("}");
                File.WriteAllText(Path.Combine(Dir, "raw_p0_stop.json"), sb.ToString());
                File.WriteAllLines(Path.Combine(Dir, "p0_run_notes.txt"), Notes);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P0TEST] evidence flush failed: " + e.Message);
            }
        }
    }

    /// <summary>对 Assembly-CSharp 组件的通用反射代理（测试程序集不能直接引用业务程序集）。</summary>
    internal sealed class P0Api
    {
        public readonly object O;
        public readonly Type T;

        private const BindingFlags Instance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private const BindingFlags Static =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private P0Api(object o, Type t)
        {
            O = o;
            T = t;
        }

        public static Type Resolve(string fullName)
        {
            Type t = Type.GetType(fullName + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType(fullName);
                if (t != null) return t;
            }

            return null;
        }

        public static P0Api Find(string fullName)
        {
            Type t = Resolve(fullName);
            if (t == null) return null;
            UnityEngine.Object[] found = UnityEngine.Object.FindObjectsOfType(t);
            if (found == null || found.Length == 0) return null;
            return new P0Api(found[0], t);
        }

        public static P0Api Wrap(object o)
        {
            return o == null ? null : new P0Api(o, o.GetType());
        }

        public object Get(string name)
        {
            PropertyInfo p = T.GetProperty(name, Instance);
            if (p != null) return p.GetValue(O);
            FieldInfo f = FindField(name);
            if (f != null) return f.GetValue(O);
            throw new MissingMemberException(T.FullName, name);
        }

        public bool TryGet(string name, out object value)
        {
            value = null;
            PropertyInfo p = T.GetProperty(name, Instance);
            if (p != null)
            {
                value = p.GetValue(O);
                return true;
            }

            FieldInfo f = FindField(name);
            if (f != null)
            {
                value = f.GetValue(O);
                return true;
            }

            return false;
        }

        public void Set(string name, object value)
        {
            FieldInfo f = FindField(name);
            if (f == null)
            {
                PropertyInfo p = T.GetProperty(name, Instance);
                if (p == null || !p.CanWrite) throw new MissingMemberException(T.FullName, name);
                p.SetValue(O, value);
                return;
            }

            f.SetValue(O, value);
        }

        public bool Bool(string name) => Convert.ToBoolean(Get(name));
        public long Long(string name) => Convert.ToInt64(Get(name));
        public int Int(string name) => Convert.ToInt32(Get(name));
        public string Str(string name) => Get(name) as string ?? string.Empty;

        public object Call(string name, params object[] args)
        {
            MethodInfo m = FindMethod(T, name, args, Instance);
            if (m == null) throw new MissingMethodException(T.FullName, name + "/" + args.Length);
            return m.Invoke(O, args);
        }

        /// <summary>调用带 out 参数的方法（args 里 out 位置传 null 占位），返回第一个 out 值。</summary>
        public object CallOut(string name, out object outValue, params object[] args)
        {
            MethodInfo m = FindMethod(T, name, args, Instance);
            if (m == null) throw new MissingMethodException(T.FullName, name + "/" + args.Length);
            object[] buffer = new object[args.Length];
            Array.Copy(args, buffer, args.Length);
            object ret = m.Invoke(O, buffer);
            outValue = null;
            ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].IsOut || ps[i].ParameterType.IsByRef)
                {
                    outValue = buffer[i];
                    break;
                }
            }

            return ret;
        }

        public static object FieldOf(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(name, Instance);
                if (f != null) return f.GetValue(target);
                PropertyInfo p = t.GetProperty(name, Instance);
                if (p != null) return p.GetValue(target);
                t = t.BaseType;
            }

            return null;
        }

        public static object StaticGet(Type t, string name)
        {
            PropertyInfo p = t.GetProperty(name, Static);
            if (p != null) return p.GetValue(null);
            FieldInfo f = t.GetField(name, Static);
            if (f != null) return f.GetValue(null);
            throw new MissingMemberException(t.FullName, name);
        }

        public static object StaticCall(Type t, string name, params object[] args)
        {
            MethodInfo m = FindMethod(t, name, args, Static);
            if (m == null) throw new MissingMethodException(t.FullName, name);
            return m.Invoke(null, args);
        }

        public static object EnumValue(string typeName, string member)
        {
            Type t = Resolve(typeName);
            return t == null ? null : Enum.Parse(t, member);
        }

        private FieldInfo FindField(string name)
        {
            Type t = T;
            while (t != null)
            {
                FieldInfo f = t.GetField(name, Instance);
                if (f != null) return f;
                t = t.BaseType;
            }

            return null;
        }

        private static MethodInfo FindMethod(Type owner, string name, object[] args, BindingFlags flags)
        {
            Type t = owner;
            while (t != null)
            {
                foreach (MethodInfo m in t.GetMethods(flags))
                {
                    if (m.Name != name) continue;
                    if (m.GetParameters().Length != args.Length) continue;
                    if (!ArgsFit(m.GetParameters(), args)) continue;
                    return m;
                }

                t = t.BaseType;
            }

            return null;
        }

        private static bool ArgsFit(ParameterInfo[] ps, object[] args)
        {
            for (int i = 0; i < ps.Length; i++)
            {
                if (args[i] == null)
                {
                    if (ps[i].ParameterType.IsValueType && !ps[i].ParameterType.IsByRef) return false;
                    continue;
                }

                Type want = ps[i].ParameterType;
                if (want.IsByRef) want = want.GetElementType();
                if (want == null) continue;
                if (!want.IsInstanceOfType(args[i])) return false;
            }

            return true;
        }
    }

    [TestFixture]
    public class P0StopAcceptanceTests
    {
        private const string ScenePath = "Assets/Scenes/Doosan-DT_L1L2.unity";
        private const string SceneName = "Doosan-DT_L1L2";
        private const string HaltTopicName = "/dt/cmd/halt";
        private const string MotionTopicName = "/dt/cmd/motion";

        private static P0Api _bridge;
        private static P0Api _controller;
        private static P0Api _runtime;
        private static Type _probeType;
        private static bool _sceneLoaded;
        private static string FullSetupError = string.Empty;

        // 场景控制器原始值（用例结束后恢复，避免污染后续用例）
        private static bool _origBidi;
        private static bool _origReal;
        private static bool _origDryRun;
        private static bool _origAllowExecute;
        private static bool _origLatch;
        private static object _origMode;
        private static bool _origBridgeEnabled = true;
        private static bool _flagsCaptured;

        private static readonly List<GameObject> TempObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            RestoreScene();
            for (int i = 0; i < TempObjects.Count; i++)
            {
                if (TempObjects[i] != null) UnityEngine.Object.Destroy(TempObjects[i]);
            }

            TempObjects.Clear();
            P0Ev.Flush();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            P0Ev.Flush();
        }

        // ================================================================ helpers

        private static IEnumerator EnsureReady()
        {
            // 场景加载/激活会打出既有工程的 Error 级日志（如 articulation immovable 提示），
            // 与本次停止链判据无关；在测试体内再次显式忽略，避免被当成 Unhandled log message 失败。
            LogAssert.ignoreFailingMessages = true;
            yield return EnsureScene();
            if (_bridge == null) _bridge = P0Api.Find("DigitalTwin.Ros2Bridge");
            if (_controller == null) _controller = P0Api.Find("DigitalTwin.TwinCommandController");
            if (_runtime == null) _runtime = P0Api.Find("DigitalTwin.DigitalTwinRuntime");
            if (_probeType == null) _probeType = P0Api.Resolve("DigitalTwin.TwinRosPublishProbe");

            if (_bridge == null || _controller == null)
            {
                FullSetupError = "scene components missing: bridge=" + (_bridge != null) +
                                 " controller=" + (_controller != null);
            }

            if (!_flagsCaptured && _controller != null)
            {
                _origBidi = _controller.Bool("EnableBidirectionalControl");
                _origReal = _controller.Bool("EnableRealRobotCommand");
                _origDryRun = _controller.Bool("EnableDryRun");
                _origAllowExecute = (bool)_controller.Get("allowExecuteMode");
                _origLatch = _controller.Bool("IsEmergencyStopped");
                _origMode = _controller.Get("CurrentMode");
                _flagsCaptured = true;
            }

            yield return null;
        }

        private static IEnumerator EnsureScene()
        {
            if (_sceneLoaded || SceneManager.GetSceneByName(SceneName).isLoaded)
            {
                _sceneLoaded = true;
                yield break;
            }

#if UNITY_EDITOR
            AsyncOperation op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return op;
#else
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
#endif
            Scene sc = SceneManager.GetSceneByName(SceneName);
            if (sc.isLoaded) SceneManager.SetActiveScene(sc);
            _sceneLoaded = true;
            P0Ev.Note("scene loaded: " + SceneName + " loaded=" + sc.isLoaded);
            yield return null;
        }

        private static void RestoreScene()
        {
            if (_controller != null)
            {
                try
                {
                    _controller.Set("emergencyStopped", _flagsCaptured ? _origLatch : false);
                    _controller.Set("enableBidirectionalControl", _flagsCaptured ? _origBidi : false);
                    _controller.Set("enableRealRobotCommand", _flagsCaptured ? _origReal : false);
                    _controller.Set("enableDryRun", _flagsCaptured ? _origDryRun : true);
                    _controller.Set("allowExecuteMode", _flagsCaptured ? _origAllowExecute : false);
                    if (_origMode != null) _controller.Set("currentMode", _origMode);
                }
                catch (Exception e)
                {
                    P0Ev.Note("restore controller failed: " + e.Message);
                }
            }

            if (_bridge != null)
            {
                try
                {
                    _bridge.Set("enableBridge", _origBridgeEnabled);
                }
                catch (Exception e)
                {
                    P0Ev.Note("restore bridge failed: " + e.Message);
                }
            }
        }

        private static IEnumerator ConnectLink()
        {
            _bridge.Call("Connect");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f && !_bridge.Bool("IsLinkConnected"))
            {
                yield return null;
            }
        }

        private static long Probe(string name)
        {
            return Convert.ToInt64(P0Api.StaticGet(_probeType, name));
        }

        private static void ProbeReset()
        {
            P0Api.StaticCall(_probeType, "Reset");
        }

        private static string ResultStatus(object result) => (string)P0Api.FieldOf(result, "Status") ?? string.Empty;

        private static bool ResultSuccess(object result)
        {
            object v = P0Api.FieldOf(result, "Success");
            return v != null && Convert.ToBoolean(v);
        }

        private static string ResultError(object result) => (string)P0Api.FieldOf(result, "ErrorMessage") ?? string.Empty;

        private static P0Api NewController(string name)
        {
            Type t = P0Api.Resolve("DigitalTwin.TwinCommandController");
            Assert.That(t, Is.Not.Null, "TwinCommandController type not found");
            GameObject go = new GameObject(name);
            TempObjects.Add(go);
            Component c = go.AddComponent(t);
            P0Api api = P0Api.Wrap(c);
            api.Call("Initialize", new object[] { null });
            return api;
        }

        private static P0Api NewPanelView(string name)
        {
            Type t = P0Api.Resolve("DigitalTwin.TwinCommandPanelView");
            Assert.That(t, Is.Not.Null, "TwinCommandPanelView type not found");
            GameObject go = new GameObject(name);
            TempObjects.Add(go);
            return P0Api.Wrap(go.AddComponent(t));
        }

        private static object RosConnectionOf(P0Api owner)
        {
            return owner.Get("_ros");
        }

        private static object RosTopic(object connection, string topic)
        {
            if (connection == null) return null;
            MethodInfo m = connection.GetType().GetMethod("GetTopic",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return m == null ? null : m.Invoke(connection, new object[] { topic });
        }

        private static string RosTopicType(object topicState)
        {
            if (topicState == null) return string.Empty;
            PropertyInfo p = topicState.GetType().GetProperty("RosMessageName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p == null ? string.Empty : (string)p.GetValue(topicState) ?? string.Empty;
        }

        private static bool RosTopicIsPublisher(object topicState)
        {
            if (topicState == null) return false;
            PropertyInfo p = topicState.GetType().GetProperty("IsPublisher",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p != null && Convert.ToBoolean(p.GetValue(topicState));
        }

        private static void ForceSceneControllerExecuteReady()
        {
            _controller.Set("enableBidirectionalControl", true);
            _controller.Set("enableRealRobotCommand", true);
            _controller.Set("enableDryRun", false);
            _controller.Set("allowExecuteMode", true);
            _controller.Set("emergencyStopped", false);
        }

        // ================================================================ T1

        [UnityTest]
        public IEnumerator T1_MotionPublish_StopsWithin100ms_AfterEmergencyStop()
        {
            yield return EnsureReady();
            Assert.That(_bridge, Is.Not.Null, "Ros2Bridge not found: " + FullSetupError);
            Assert.That(_controller, Is.Not.Null, "TwinCommandController not found: " + FullSetupError);
            Assert.That(_probeType, Is.Not.Null, "TwinRosPublishProbe not found");

            yield return ConnectLink();
            bool link = _bridge.Bool("IsLinkConnected");
            P0Ev.Note("T1 link=" + link + " err=" + _bridge.Str("LastError"));
            Assert.That(link, Is.True,
                "T1 需要 127.0.0.1:10002 的 ROS-TCP endpoint（本批隔离环境由 WSL 提供）；err=" +
                _bridge.Str("LastError"));

            ForceSceneControllerExecuteReady();
            string sourceKind = _runtime == null ? "(no runtime)" : _runtime.Get("ActiveSourceKind").ToString();

            // 先走生产路径：EnterPlan 取得起点帧；若没有实时帧，则仅在测试实例上关闭起点一致性校验，
            // 目的是把"停止闸门"与"起点一致性闸门"隔离（工程默认值不改）。
            object planResult = _controller.Call("EnterPlan");
            bool planOk = planResult != null && Convert.ToBoolean(planResult);
            string gate = "natural";
            if (!planOk)
            {
                gate = DisableStartStateMatch();
            }

            bool enterExecute = (bool)_controller.Call("EnterExecute");
            object first = _controller.Call("SendMoveJoint", new float[6], 5f);
            string firstStatus = ResultStatus(first);
            string preHaltPath = "controller";
            P0Ev.Note("T1 sourceKind=" + sourceKind + " planOk=" + planOk + " gate=" + gate +
                      " enterExecute=" + enterExecute + " firstSend=" + firstStatus +
                      " err=" + ResultError(first));

            if (firstStatus != "SENT")
            {
                // 兜底：生产发布方法本身（TwinCommandController 走的就是它），但记录为 bridge 路径。
                preHaltPath = "bridge";
                P0Ev.Note("T1 fallback to bridge.SendDoosanMotionAbsolute: " + firstStatus +
                          " / " + ResultError(first));
                object motion = _bridge.Call("SendDoosanMotionAbsolute", new float[6], 5f);
                Assert.That(ResultStatus(motion), Is.EqualTo("SENT"),
                    "T1 前置条件失败：连 bridge 的运动发布都发不出去（status=" + ResultStatus(motion) +
                    ", err=" + ResultError(motion) + "）");
            }

            ProbeReset();
            const int preHaltSends = 6;
            for (int i = 0; i < preHaltSends; i++)
            {
                if (preHaltPath == "controller")
                {
                    _controller.Call("SendMoveJoint", new float[6], 5f);
                }
                else
                {
                    _bridge.Call("SendDoosanMotionAbsolute", new float[6], 5f);
                }

                yield return null;
            }

            long motionBeforeHalt = Probe("MotionTotal");
            Assert.That(motionBeforeHalt, Is.GreaterThanOrEqualTo(1L),
                "T1 前置条件失败：急停前没有产生 motion 发布（path=" + preHaltPath + "）");

            object halt = _controller.Call("EmergencyStop");
            long motionAtHalt = Probe("MotionTotal");
            long haltCountAtHalt = _bridge.Long("HaltPublishCount");
            Assert.That(_controller.Bool("IsEmergencyStopped"), Is.True, "EmergencyStop 必须置本地闩锁");
            P0Ev.Note("T1 halt status=" + ResultStatus(halt) + " motionAtHalt=" + motionAtHalt +
                      " haltPublishes=" + haltCountAtHalt);

            // 100 ms 墙钟窗口内持续尝试下发点运动指令（比按帧采样更严格）。
            var sw = new System.Diagnostics.Stopwatch();
            int attempts = 0;
            string lastStatus = string.Empty;
            string lastError = string.Empty;
            sw.Start();
            while (sw.Elapsed.TotalMilliseconds < 100.0)
            {
                object r = _controller.Call("SendMoveJoint", new float[6], 5f);
                attempts++;
                lastStatus = ResultStatus(r);
                lastError = ResultError(r);
            }

            sw.Stop();
            long motionAfter = Probe("MotionTotal");
            double elapsedMs = sw.Elapsed.TotalMilliseconds;
            long delta = motionAfter - motionAtHalt;

            P0Ev.Note("T1 window=" + elapsedMs.ToString("F2") + "ms attempts=" + attempts +
                      " motionDelta=" + delta + " last=" + lastStatus + " err=" + lastError);
            P0Ev.Add("t1", "{" +
                "\"preHaltPath\": " + P0Ev.Q(preHaltPath) +
                ", \"startStateGate\": " + P0Ev.Q(gate) +
                ", \"activeSourceKind\": " + P0Ev.Q(sourceKind) +
                ", \"motionPublishesBeforeHalt\": " + motionBeforeHalt +
                ", \"motionPublishesAtHalt\": " + motionAtHalt +
                ", \"motionPublishesAfterWindow\": " + motionAfter +
                ", \"motionDeltaInWindow\": " + delta +
                ", \"windowMs\": " + P0Ev.N(elapsedMs) +
                ", \"commandAttemptsInWindow\": " + attempts +
                ", \"lastResultStatus\": " + P0Ev.Q(lastStatus) +
                ", \"lastResultError\": " + P0Ev.Q(lastError) +
                ", \"haltPublishCount\": " + haltCountAtHalt +
                ", \"latchAfterStop\": " + P0Ev.B(_controller.Bool("IsEmergencyStopped")) +
                ", \"canSendRealMove\": " + P0Ev.B(_controller.Bool("CanSendRealMove")) +
                ", \"criterion\": \"software block of new motion commands; NOT physical stop time\"}");

            Assert.That(delta, Is.EqualTo(0L),
                "急停后 100 ms 内不得再有 motion 发布（实测 delta=" + delta + "）");
            Assert.That(lastStatus, Is.EqualTo("BLOCKED"),
                "急停窗口内最后一次下发必须被 BLOCKED（实测 " + lastStatus + "）");
            Assert.That(lastError, Does.Contain("Emergency stop"),
                "阻断原因必须指向急停闩锁（实测 " + lastError + "）");
        }

        private static string DisableStartStateMatch()
        {
            try
            {
                object safety = _controller.Get("_safety");
                if (safety == null) return "safety-null";
                Type st = safety.GetType();
                FieldInfo f = st.GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance);
                object settings = f == null ? null : f.GetValue(safety);
                if (settings == null)
                {
                    Type settingsType = P0Api.Resolve("DigitalTwin.TwinRuntimeSettings");
                    if (settingsType == null) return "settings-type-missing";
                    settings = Activator.CreateInstance(settingsType);
                    if (f != null) f.SetValue(safety, settings);
                }

                FieldInfo flag = settings.GetType().GetField("RequireStartStateMatchBeforeExecute",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (flag == null) return "flag-missing";
                flag.SetValue(settings, false);
                return "disabled-for-test-instance";
            }
            catch (Exception e)
            {
                return "error:" + e.Message;
            }
        }

        // ================================================================ T2

        [UnityTest]
        public IEnumerator T2_HaltTopic_UniqueBool_OneShot_NoStorm()
        {
            yield return EnsureReady();
            Assert.That(_bridge, Is.Not.Null, "Ros2Bridge not found: " + FullSetupError);
            Assert.That(_probeType, Is.Not.Null, "TwinRosPublishProbe not found");

            // 先确保 halt 发布者已注册（离线也能注册与排队）。
            long haltBefore = _bridge.Long("HaltPublishCount");
            ProbeReset();
            object sent = _bridge.Call("SendHalt");
            long haltAfter = _bridge.Long("HaltPublishCount");
            long commandTotal = Probe("CommandTotal");
            string lastTopic = (string)P0Api.StaticGet(_probeType, "LastTopic") ?? string.Empty;

            object ros = RosConnectionOf(_bridge);
            object haltTopicState = RosTopic(ros, HaltTopicName);
            string haltType = RosTopicType(haltTopicState);
            bool haltIsPublisher = RosTopicIsPublisher(haltTopicState);

            P0Ev.Note("T2 haltSend=" + ResultStatus(sent) + " haltPublishDelta=" + (haltAfter - haltBefore) +
                      " type=" + haltType + " isPublisher=" + haltIsPublisher + " lastTopic=" + lastTopic);

            Assert.That(ResultStatus(sent), Is.EqualTo("SENT"), "SendHalt 必须投递成功");
            Assert.That(haltAfter - haltBefore, Is.EqualTo(1L), "一次 SendHalt 只允许产生一次 halt 发布");
            Assert.That(haltType, Is.EqualTo("std_msgs/Bool"),
                "/dt/cmd/halt 的消息类型必须唯一且为 std_msgs/Bool（实测 " + haltType + "）");
            Assert.That(haltIsPublisher, Is.True, "Ros2Bridge 必须是 /dt/cmd/halt 的发布者");
            Assert.That(commandTotal, Is.EqualTo(1L), "halt 发布只应计一次 command 计数");
            Assert.That(lastTopic, Is.EqualTo(HaltTopicName), "最后一次 command 发布必须是 halt 话题");

            // ④ 无异常重复触发：一次急停 + 多次"复位尝试"只能有 1 次 halt 发布，不能连环刷屏。
            P0Api storm = NewController("P0_StormController");
            ProbeReset();
            long stormHaltBefore = _bridge.Long("HaltPublishCount");
            object halt = storm.Call("EmergencyStop");
            Assert.That(ResultSuccess(halt) || ResultStatus(halt) == "SENT",
                "测试控制器 EmergencyStop 应投递 halt（实测 " + ResultStatus(halt) + "）");

            int clearAttempts = 0;
            int clearedTimes = 0;
            for (int i = 0; i < 5; i++)
            {
                object reasonOut;
                object cleared = storm.CallOut("TryClearEmergencyStop", out reasonOut, (object)null);
                clearAttempts++;
                if (cleared != null && Convert.ToBoolean(cleared)) clearedTimes++;
                yield return null;
            }

            long stormHaltAfter = _bridge.Long("HaltPublishCount");
            long stormDelta = stormHaltAfter - stormHaltBefore;
            long stormCommandPublishes = Probe("CommandTotal");
            string stormRemote = storm.Get("RemoteStop").ToString();

            P0Ev.Note("T2 storm haltDelta=" + stormDelta + " clearAttempts=" + clearAttempts +
                      " cleared=" + clearedTimes + " remote=" + stormRemote +
                      " commandPublishes=" + stormCommandPublishes);
            P0Ev.Add("t2", "{" +
                "\"haltTopic\": " + P0Ev.Q(HaltTopicName) +
                ", \"haltMessageType\": " + P0Ev.Q(haltType) +
                ", \"haltPublisher\": \"DigitalTwin.Ros2Bridge.SendHalt() -> std_msgs/Bool(true)\"" +
                ", \"haltPublishDeltaPerCall\": " + (haltAfter - haltBefore) +
                ", \"lastPublishTopic\": " + P0Ev.Q(lastTopic) +
                ", \"stormHaltPublishes\": " + stormDelta +
                ", \"stormClearAttempts\": " + clearAttempts +
                ", \"stormClearedTimes\": " + clearedTimes +
                ", \"stormRemoteStopState\": " + P0Ev.Q(stormRemote) +
                ", \"note\": \"single publisher per component is NOT required; type uniqueness + one-shot is\"}");

            Assert.That(stormDelta, Is.EqualTo(1L), "一次急停只能产生一次 HALT（实测 " + stormDelta + "）");
            Assert.That(clearedTimes, Is.EqualTo(0), "远端未确认时不得解除闩锁");
            Assert.That(storm.Bool("IsEmergencyStopped"), Is.True, "闩锁必须保持");
        }

        // ================================================================ T2 live

        [UnityTest]
        public IEnumerator T2b_Live_HaltReachesRos2_AndGetsAck()
        {
            if (Environment.GetEnvironmentVariable("P0_LIVE") != "1")
            {
                Assert.Ignore("P0_LIVE!=1：跳过真实 ROS2 环回用例（只跑离线判据）");
            }

            yield return EnsureReady();
            yield return ConnectLink();
            Assert.That(_bridge.Bool("IsLinkConnected"), Is.True,
                "live 用例需要 10002 endpoint；err=" + _bridge.Str("LastError"));

            P0Api live = NewController("P0_LiveController");
            ProbeReset();
            long haltBefore = _bridge.Long("HaltPublishCount");
            object halt = live.Call("EmergencyStop");
            long haltAfter = _bridge.Long("HaltPublishCount");
            P0Ev.Note("T2b halt=" + ResultStatus(halt) + " haltPublish=" + (haltAfter - haltBefore) +
                      " remote=" + live.Get("RemoteStop"));

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 5f && live.Get("RemoteStop").ToString() != "Confirmed")
            {
                yield return null;
            }

            string remote = live.Get("RemoteStop").ToString();
            string detail = live.Str("RemoteStopDetail");
            long ack = live.Long("HaltAckNs");
            P0Ev.Note("T2b remote=" + remote + " detail=" + detail + " ackNs=" + ack);
            P0Ev.Add("t2_live", "{" +
                "\"haltPublishDelta\": " + (haltAfter - haltBefore) +
                ", \"remoteStopState\": " + P0Ev.Q(remote) +
                ", \"remoteStopDetail\": " + P0Ev.Q(detail) +
                ", \"haltAckNs\": " + ack +
                ", \"haltCountInBridge\": " + _bridge.Long("HaltPublishCount") +
                "}");

            Assert.That(haltAfter - haltBefore, Is.EqualTo(1L), "一次急停只能发一次 HALT");
            Assert.That(remote, Is.EqualTo("Confirmed"),
                "adapter 必须回 /dt/status/motion=HALT_REQUESTED（实测 " + remote + " / " + detail + "）");
            object reasonOut;
            object cleared = live.CallOut("TryClearEmergencyStop", out reasonOut, (object)null);
            Assert.That(Convert.ToBoolean(cleared), Is.True, "远端确认后应允许清闩锁：" + reasonOut);
        }

        // ================================================================ T3

        [UnityTest]
        public IEnumerator T3_Latch_BlocksCanSendRealMove_AndEnterExecute()
        {
            yield return EnsureReady();
            P0Api c = NewController("P0_T3Controller");
            c.Set("enableBidirectionalControl", true);
            c.Set("enableRealRobotCommand", true);
            c.Set("enableDryRun", false);
            c.Set("allowExecuteMode", true);

            bool canMoveBefore = c.Bool("CanSendRealMove");
            bool enterBefore = (bool)c.Call("EnterExecute");
            string modeBefore = c.Get("CurrentMode").ToString();

            object halt = c.Call("EmergencyStop");
            bool canMoveAfter = c.Bool("CanSendRealMove");
            bool enterAfter = (bool)c.Call("EnterExecute");
            string modeAfter = c.Get("CurrentMode").ToString();
            object move = c.Call("SendMoveJoint", new float[6], 5f);

            P0Ev.Note("T3 before canMove=" + canMoveBefore + " enterExecute=" + enterBefore + " mode=" + modeBefore);
            P0Ev.Note("T3 after canMove=" + canMoveAfter + " enterExecute=" + enterAfter + " mode=" + modeAfter +
                      " move=" + ResultStatus(move) + " err=" + ResultError(move));
            P0Ev.Add("t3", "{" +
                "\"haltStatus\": " + P0Ev.Q(ResultStatus(halt)) +
                ", \"modeBefore\": " + P0Ev.Q(modeBefore) +
                ", \"canSendRealMoveBefore\": " + P0Ev.B(canMoveBefore) +
                ", \"enterExecuteBefore\": " + P0Ev.B(enterBefore) +
                ", \"canSendRealMoveAfter\": " + P0Ev.B(canMoveAfter) +
                ", \"enterExecuteAfter\": " + P0Ev.B(enterAfter) +
                ", \"modeAfter\": " + P0Ev.Q(modeAfter) +
                ", \"sendMoveJointAfter\": " + P0Ev.Q(ResultStatus(move)) +
                ", \"sendMoveJointError\": " + P0Ev.Q(ResultError(move)) +
                "}");

            Assert.That(canMoveBefore, Is.True, "前置条件：未闩锁且三开关就绪时 CanSendRealMove 应为 true");
            Assert.That(enterBefore, Is.True, "前置条件：未闩锁时应能进入 Execute");
            Assert.That(canMoveAfter, Is.False, "闩锁期间 CanSendRealMove 必须为 false");
            Assert.That(enterAfter, Is.False, "闩锁期间 EnterExecute() 必须为 false");
            Assert.That(modeAfter, Is.EqualTo(modeBefore), "EnterExecute 被拒不得造成模式切换");
            Assert.That(ResultStatus(move), Is.EqualTo("BLOCKED"), "闩锁期间下发必须被 BLOCKED");
            Assert.That(ResultError(move), Does.Contain("Emergency stop"), "阻断原因必须指向急停");
        }

        // ================================================================ T4

        [UnityTest]
        public IEnumerator T4_ClearRequiresRemoteAck_KeepsLatchWhenUnknown()
        {
            yield return EnsureReady();
            P0Api c = NewController("P0_T4Controller");
            object halt = c.Call("EmergencyStop");
            string stateAfterHalt = c.Get("RemoteStop").ToString();

            object reason1;
            object clear1 = c.CallOut("TryClearEmergencyStop", out reason1, (object)null);
            bool latch1 = c.Bool("IsEmergencyStopped");
            bool canClear1 = c.Bool("CanClearEmergencyStop");

            // 远端给的是非停止状态 -> 仍不得清
            c.Call("NotifyRemoteMotionState", "MOVING", DateTime.UtcNow.Ticks);
            object reason2;
            object clear2 = c.CallOut("TryClearEmergencyStop", out reason2, (object)null);

            // 远端给停止回执 -> 允许清
            long ackNs = DateTime.UtcNow.Ticks;
            c.Call("NotifyRemoteMotionState", "HALT_REQUESTED", ackNs);
            string stateAfterAck = c.Get("RemoteStop").ToString();
            object reason3;
            object clear3 = c.CallOut("TryClearEmergencyStop", out reason3, (object)null);
            bool latch3 = c.Bool("IsEmergencyStopped");

            P0Ev.Note("T4 halt=" + ResultStatus(halt) + " state=" + stateAfterHalt +
                      " clear1=" + clear1 + " (" + reason1 + ")");
            P0Ev.Note("T4 afterAck state=" + stateAfterAck + " clear3=" + clear3 + " latch=" + latch3);

            Assert.That(ResultStatus(halt), Is.EqualTo("SENT"), "远端 HALT 必须发出");
            Assert.That(stateAfterHalt, Is.EqualTo("Pending"), "发出 HALT 后应处于 Pending");
            Assert.That(Convert.ToBoolean(clear1), Is.False, "没有远端确认时不得解除闩锁");
            Assert.That(latch1, Is.True, "没有远端确认时闩锁必须保持");
            Assert.That(canClear1, Is.False, "没有远端确认时 CanClearEmergencyStop 必须为 false");
            Assert.That(Convert.ToBoolean(clear2), Is.False, "非停止类远端状态不得解除闩锁");
            Assert.That(stateAfterAck, Is.EqualTo("Confirmed"), "停止回执应被判为 Confirmed");
            Assert.That(Convert.ToBoolean(clear3), Is.True, "远端确认后应允许解除闩锁");
            Assert.That(latch3, Is.False, "清除后闩锁应解除");

            // 远端失败场景：桥被禁用 -> HALT 发不出去 -> 闩锁必须保持
            bool bridgeWasEnabled = _bridge.Bool("enableBridge");
            _bridge.Set("enableBridge", false);
            P0Api c2 = NewController("P0_T4NoBridge");
            object halt2 = c2.Call("EmergencyStop");
            string failedState = c2.Get("RemoteStop").ToString();
            object reason4;
            object clear4 = c2.CallOut("TryClearEmergencyStop", out reason4, (object)null);
            _bridge.Set("enableBridge", bridgeWasEnabled);

            // UI 层：ToggleHalt 在未确认时不得清闩锁，并显示"远端未确认停止"
            P0Api view = NewPanelView("P0_HaltView");
            P0Api c3 = NewController("P0_T4UiController");
            view.Set("controller", c3.O);
            c3.Call("EmergencyStop");
            view.Call("ToggleHalt");
            string uiMsg1 = view.Str("LastHaltMessage");
            bool latchUi = c3.Bool("IsEmergencyStopped");
            c3.Call("NotifyRemoteMotionState", "HALT_REQUESTED", DateTime.UtcNow.Ticks);
            view.Call("ToggleHalt");
            string uiMsg2 = view.Str("LastHaltMessage");
            bool latchUi2 = c3.Bool("IsEmergencyStopped");

            P0Ev.Note("T4 noBridge halt=" + ResultStatus(halt2) + " state=" + failedState +
                      " clear=" + clear4 + " (" + reason4 + ")");
            P0Ev.Note("T4 ui msg1=" + uiMsg1 + " latch=" + latchUi + " | msg2=" + uiMsg2 + " latch=" + latchUi2);
            P0Ev.Add("t4", "{" +
                "\"haltStatus\": " + P0Ev.Q(ResultStatus(halt)) +
                ", \"remoteStateAfterHalt\": " + P0Ev.Q(stateAfterHalt) +
                ", \"clearWithoutAck\": " + P0Ev.B(clear1 != null && Convert.ToBoolean(clear1)) +
                ", \"clearWithoutAckReason\": " + P0Ev.Q(reason1 == null ? string.Empty : reason1.ToString()) +
                ", \"latchKeptWithoutAck\": " + P0Ev.B(latch1) +
                ", \"clearAfterNonStopState\": " + P0Ev.B(clear2 != null && Convert.ToBoolean(clear2)) +
                ", \"remoteStateAfterAck\": " + P0Ev.Q(stateAfterAck) +
                ", \"clearAfterAck\": " + P0Ev.B(clear3 != null && Convert.ToBoolean(clear3)) +
                ", \"noBridgeRemoteState\": " + P0Ev.Q(failedState) +
                ", \"noBridgeClear\": " + P0Ev.B(clear4 != null && Convert.ToBoolean(clear4)) +
                ", \"uiMessageBeforeAck\": " + P0Ev.Q(uiMsg1) +
                ", \"uiLatchBeforeAck\": " + P0Ev.B(latchUi) +
                ", \"uiMessageAfterAck\": " + P0Ev.Q(uiMsg2) +
                ", \"uiLatchAfterAck\": " + P0Ev.B(latchUi2) +
                "}");

            Assert.That(ResultStatus(halt2), Is.EqualTo("BLOCKED"), "桥禁用时 HALT 必须投递失败");
            Assert.That(failedState, Is.EqualTo("Failed"), "投递失败应记 Failed");
            Assert.That(Convert.ToBoolean(clear4), Is.False, "远端未知时不得解锁");
            Assert.That(latchUi, Is.True, "UI 未确认时不得清闩锁");
            Assert.That(uiMsg1, Does.Contain("远端未确认停止"), "UI 必须显示远端未确认停止（实测 " + uiMsg1 + "）");
            Assert.That(latchUi2, Is.False, "远端确认后 UI 点击应能清除闩锁");
            Assert.That(uiMsg2, Does.Contain("确认"), "UI 应显示远端确认（实测 " + uiMsg2 + "）");
        }

        // ================================================================ T5 (10003)

        [UnityTest]
        public IEnumerator T5_PointSession10003_Cancel_NoLossNoDupNoCrossDomainHalt()
        {
            yield return EnsureReady();
            Assert.That(_bridge, Is.Not.Null, "Ros2Bridge not found: " + FullSetupError);

            bool origReal = _controller.Bool("EnableRealRobotCommand");
            _controller.Set("enableRealRobotCommand", false);

            Type sessionType = P0Api.Resolve("DigitalTwin.PointMotionSession");
            Assert.That(sessionType, Is.Not.Null, "PointMotionSession type not found");
            GameObject go = new GameObject("P0_PointSession");
            TempObjects.Add(go);
            P0Api session = P0Api.Wrap(go.AddComponent(sessionType));

            object connect = session.Call("ConnectSimulation");
            Assert.That(connect != null && Convert.ToBoolean(connect), "ConnectSimulation 应成功（真机标志已临时关闭）");

            object sessionRos = RosConnectionOf(session);
            Assert.That(sessionRos, Is.Not.Null, "PointMotionSession 必须有自己的 ROSConnection");
            int sessionPort = Convert.ToInt32(P0Api.FieldOf(sessionRos, "RosPort"));
            string sessionIp = (string)P0Api.FieldOf(sessionRos, "RosIPAddress") ?? string.Empty;
            int bridgePort = Convert.ToInt32(P0Api.FieldOf(_bridge.O, "rosTcpPort"));
            object sessionHaltTopic = RosTopic(sessionRos, HaltTopicName);
            bool isolationCarriesHalt = session.Bool("IsolationChannelCarriesHalt");

            P0Ev.Note("T5 session=" + sessionIp + ":" + sessionPort + " bridgePort=" + bridgePort +
                      " sessionHaltTopic=" + (sessionHaltTopic == null ? "(none)" : RosTopicType(sessionHaltTopic)) +
                      " isolationCarriesHalt=" + isolationCarriesHalt);

            Assert.That(sessionPort, Is.EqualTo(10003), "隔离通道必须是 10003");
            Assert.That(bridgePort, Is.EqualTo(10002), "主停止通道必须是 10002");
            Assert.That(sessionHaltTopic, Is.Null, "10003 通道不得注册 /dt/cmd/halt（同话题第二消息类型）");
            Assert.That(isolationCarriesHalt, Is.False, "隔离通道不得承担停止职责");

            long haltBefore = _bridge.Long("HaltPublishCount");
            ProbeReset();
            session.Call("Cancel", true);
            long haltAfter1 = _bridge.Long("HaltPublishCount");
            long probeAfter1 = Probe("Total");
            string lastTopic1 = (string)P0Api.StaticGet(_probeType, "LastTopic") ?? string.Empty;
            string haltStatus = session.Str("LastHaltStatus");
            bool haltPublished = session.Bool("LastHaltPublished");
            bool sessionEstopped = session.Bool("Estopped");
            bool latchAfter = _controller.Bool("IsEmergencyStopped");

            session.Call("Cancel", true);
            long haltAfter2 = _bridge.Long("HaltPublishCount");
            long probeAfter2 = Probe("Total");
            object sessionHaltTopicAfter = RosTopic(sessionRos, HaltTopicName);

            P0Ev.Note("T5 cancel1 haltDelta=" + (haltAfter1 - haltBefore) + " probeTotal=" + probeAfter1 +
                      " lastTopic=" + lastTopic1 + " status=" + haltStatus +
                      " published=" + haltPublished + " estopped=" + sessionEstopped);
            P0Ev.Note("T5 cancel2 haltDelta=" + (haltAfter2 - haltBefore) + " probeTotal=" + probeAfter2);
            P0Ev.Add("t5", "{" +
                "\"sessionEndpoint\": " + P0Ev.Q(sessionIp + ":" + sessionPort) +
                ", \"bridgeEndpointPort\": " + bridgePort +
                ", \"sessionHaltPublisher\": \"none\"" +
                ", \"isolationChannelCarriesHalt\": " + P0Ev.B(isolationCarriesHalt) +
                ", \"firstCancelHaltDelta\": " + (haltAfter1 - haltBefore) +
                ", \"firstCancelProbeTotal\": " + probeAfter1 +
                ", \"firstCancelLastTopic\": " + P0Ev.Q(lastTopic1) +
                ", \"firstCancelHaltStatus\": " + P0Ev.Q(haltStatus) +
                ", \"firstCancelHaltPublished\": " + P0Ev.B(haltPublished) +
                ", \"secondCancelHaltDelta\": " + (haltAfter2 - haltBefore) +
                ", \"secondCancelProbeTotal\": " + probeAfter2 +
                ", \"controllerLatchAfter\": " + P0Ev.B(latchAfter) +
                ", \"sessionEstopped\": " + P0Ev.B(sessionEstopped) +
                "}");

            Assert.That(haltAfter1 - haltBefore, Is.EqualTo(1L), "一次 Cancel 必须恰好产生一次 HALT（不丢失、不重复）");
            Assert.That(probeAfter1, Is.EqualTo(1L), "Cancel 只允许有 1 次发布（即主通道 Bool halt，无跨域发布）");
            Assert.That(lastTopic1, Is.EqualTo(HaltTopicName), "取消后的发布必须是 /dt/cmd/halt");
            Assert.That(haltPublished, Is.True, "LastHaltPublished 必须为 true");
            Assert.That(haltStatus, Does.Contain("SENT"), "LastHaltStatus 应记录 SENT（实测 " + haltStatus + "）");
            Assert.That(sessionEstopped, Is.True, "点动通道自己的急停标记应置位");
            Assert.That(latchAfter, Is.False,
                "10003 取消不得误清/误置主控制器的闩锁（当前主控制器未急停）");
            Assert.That(haltAfter2 - haltBefore, Is.EqualTo(2L), "第二次 Cancel 恰好再发一次 HALT");
            Assert.That(sessionHaltTopicAfter, Is.Null, "取消后 10003 通道仍不得出现 halt 发布者");

            _controller.Set("enableRealRobotCommand", origReal);
        }
    }
}
