// Offline harness: compile this file with Chapter4Recorder.cs only, then run via Mono.
// Stubs deliberately exclude Unity Editor, ROS, networking and all motion interfaces.
using System;
using System.IO;
using System.Threading;
using DigitalTwin;
namespace UnityEngine
{
    public class Object { }
    public class MonoBehaviour : Object { }
    public class DisallowMultipleComponent : Attribute { }
    public class Transform { public string name; public Transform parent; }
    public static class Time { public static int frameCount; public static float unscaledDeltaTime; }
    public static class JsonUtility
    {
        public static bool FailModel;
        public static Action<object> BeforeSerialize;
        public static string ToJson(object value, bool pretty)
        {
            BeforeSerialize?.Invoke(value);
            if(FailModel && value is RobotModelController) throw new IOException("Injected model serialization failure");
            return "{}";
        }
    }
}
namespace DigitalTwin
{
    public static class SystemClock { public static long NowNs() => System.Diagnostics.Stopwatch.GetTimestamp()*100; }
    public sealed class RobotModelController : UnityEngine.Object
    {
        public sealed class Body { public UnityEngine.Transform transform; }
        public sealed class Rotation { public string ToString(string format) => "0,0,0,1"; }
        public sealed class Joint { public Body body; public string name; public int sign; public float offsetDeg,minDeg,maxDeg; public bool hasZeroLocalRotation; public Rotation zeroLocalRotation; }
        public Joint[] Joints=new Joint[0];
        public void ReadMeasuredDisplayDegrees(float[] actual, bool[] valid) { }
    }
    public sealed class RobotStateFrame
    {
        public bool HasSourceIdentity=true,RawEnqueued=true;
        public string RunId,SourceId="test",SourceSessionId="session";
        public ulong SourceSequenceId=1;
        public long SequenceId=1,UnityReceiveTimestampNs,UnityApplyTimestampNs;
        public double[] JointPositionRad=new double[6];
        public float[] ModelTargetDisplayDeg;
        public RobotStateFrame Clone() => (RobotStateFrame)MemberwiseClone();
    }
}
public static class RecorderRegression
{
    static void Require(bool condition, string message) { if(!condition) throw new Exception(message); }
    static void ExpectFailure(Action action) { bool caught=false; try { action(); } catch(Exception) { caught=true; } Require(caught,"Expected failure"); }
    public static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"chapter4_recorder_regression_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var recorder=new Chapter4Recorder();
            Func<string,Chapter4Recorder.Profile> profile=id=>new Chapter4Recorder.Profile{runId=id,outputDirectory=root,applyHz=30};
            recorder.Begin(profile("normal"),null);
            recorder.Record(new RobotStateFrame{RunId="normal"},"receive");
            recorder.End(); recorder.End();
            Require(recorder.IsFlushed && File.ReadAllLines(Path.Combine(root,"normal","samples.csv")).Length==2,"Normal drain/idempotent End");
            Console.WriteLine("PASS normal drain and repeated End");
            ExpectFailure(()=>recorder.Begin(profile("normal"),null));
            Require(recorder.IsFlushed,"Invalid replacement changed completed run");
            Console.WriteLine("PASS existing run cannot be overwritten");
            UnityEngine.JsonUtility.FailModel=true;
            ExpectFailure(()=>recorder.Begin(profile("failed_begin"),new RobotModelController()));
            Require(!recorder.IsRecording && !recorder.HasPendingWriter && !recorder.IsFlushed,"Begin failure left running writer");
            Require(File.Exists(Path.Combine(root,"failed_begin","quality.json")),"Begin failure missing quality");
            UnityEngine.JsonUtility.FailModel=false;
            Console.WriteLine("PASS Begin failure closes and preserves incomplete run");
            UnityEngine.JsonUtility.BeforeSerialize=value=> { var p=value as Chapter4Recorder.Profile; if(p!=null && p.runId=="writer_failure") Directory.CreateDirectory(Path.Combine(root,p.runId,"samples.csv")); };
            recorder.Begin(profile("writer_failure"),null);
            recorder.Record(new RobotStateFrame{RunId="writer_failure"},"receive");
            for(int i=0;i<100 && recorder.IsRecording;i++) Thread.Sleep(10);
            recorder.End();
            Require(!recorder.IsFlushed && !recorder.HasPendingWriter && !string.IsNullOrEmpty(recorder.LastError),"Writer failure reported successful");
            UnityEngine.JsonUtility.BeforeSerialize=null;
            Console.WriteLine("PASS writer failure is incomplete");
            recorder.Begin(profile("after_failure"),null);
            recorder.Record(new RobotStateFrame{RunId="after_failure"},"receive");
            recorder.End();
            string rows=File.ReadAllText(Path.Combine(root,"after_failure","samples.csv"));
            Require(recorder.IsFlushed && !rows.Contains("writer_failure") && rows.Contains("after_failure"),"Old queued data mixed into new run");
            Console.WriteLine("PASS new run after failure contains only new identity");
            recorder.Begin(profile("configuration_failure"),null);
            recorder.Fail(new IOException("Injected configuration failure"));
            Require(!recorder.IsFlushed && !recorder.IsRecording && !recorder.HasPendingWriter,"Configuration failure left successful/running recorder");
            Console.WriteLine("PASS post-Begin configuration failure seals incomplete");
            Console.WriteLine("6 recorder lifecycle cases passed; no Editor or robot interfaces used.");
            return 0;
        }
        finally { Directory.Delete(root,true); }
    }
}
