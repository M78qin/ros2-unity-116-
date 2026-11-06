using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;

namespace DigitalTwin
{
    // Dedicated source-identity contract. Existing paper logs remain backward compatible.
    [DisallowMultipleComponent]
    public sealed class Chapter4Recorder : MonoBehaviour
    {
        [Serializable] public sealed class Profile
        {
            public string runId;
            public string outputDirectory;
            public int applyHz = 60;
            public string configSha256;
            public string sourceMode;
        }
        [Serializable] private sealed class Quality
        {
            public string run_id;
            public long written_rows;
            public long dropped_rows;
            public string writer_error;
            public bool flushed;
            public string measurement_phase = "model and render rows sampled in LateUpdate";
        }
        private readonly ConcurrentQueue<Tuple<int,string>> queue = new ConcurrentQueue<Tuple<int,string>>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private Thread worker;
        private volatile bool running;
        private volatile string error = "";
        private long dropped, written;
        private string directory, runId;
        private RobotModelController model;
        private RobotStateFrame active;
        private long lastRenderNs;
        private System.Diagnostics.Process process;
        private long resourceAtNs;
        private double cpuAtMs, cpuPercent;
        private long workingSet;
        public string RunId => runId ?? string.Empty;
        public bool HasPendingWriter => worker != null;
        public bool IsRecording => running && string.IsNullOrEmpty(error);
        public string RecordingDirectory => directory ?? string.Empty;
        public string LastError => error;
        public bool IsFlushed { get; private set; }

        public static string ValidateProfile(Profile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.runId) ||
                profile.runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                profile.runId == "." || profile.runId == ".." ||
                string.IsNullOrWhiteSpace(profile.outputDirectory) || !Path.IsPathRooted(profile.outputDirectory))
                throw new ArgumentException("Chapter4 requires a simple runId and an absolute Windows outputDirectory.");
            if (profile.applyHz != 30 && profile.applyHz != 60)
                throw new ArgumentException("Chapter4 applyHz must be 30 or 60.");
            string result = Path.Combine(profile.outputDirectory, profile.runId);
            if (Directory.Exists(result)) throw new IOException("Run directory already exists: " + result);
            return result;
        }

        public void Begin(Profile profile, RobotModelController robot)
        {
            if (running || worker!=null) throw new InvalidOperationException("End the previous run first.");
            string nextDirectory = ValidateProfile(profile);
            directory = nextDirectory;
            runId = profile.runId; model = robot; active = null; lastRenderNs = 0;
            error = ""; dropped = 0; written = 0;
            IsFlushed = false;
            try
            {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"profile.json"), JsonUtility.ToJson(profile,true));
            if (robot != null) File.WriteAllText(Path.Combine(directory,"model_config.json"),JsonUtility.ToJson(robot,true));
            if(robot!=null)
            {
                var mapping=new List<string>{"joint_index,joint_name,body_path,sign,offset_deg,min_deg,max_deg,zero_captured,zero_quaternion"};
                for(int i=0;i<robot.Joints.Length;i++)
                {
                    var joint=robot.Joints[i]; if(joint==null) continue;
                    string bodyPath="";
                    for(var t=joint.body==null ? null : joint.body.transform;t!=null;t=t.parent) bodyPath=t.name+"/"+bodyPath;
                    mapping.Add(string.Join(",",Csv(i+1),Csv(joint.name),Csv(bodyPath),Csv(joint.sign),Csv(joint.offsetDeg),
                        Csv(joint.minDeg),Csv(joint.maxDeg),Csv(joint.hasZeroLocalRotation),Csv(joint.zeroLocalRotation.ToString("F9"))));
                }
                File.WriteAllLines(Path.Combine(directory,"model_mapping.csv"),mapping);
            }
            process=System.Diagnostics.Process.GetCurrentProcess();
            resourceAtNs=SystemClock.NowNs(); cpuAtMs=process.TotalProcessorTime.TotalMilliseconds;
            workingSet=process.WorkingSet64; cpuPercent=0;
            running = true;
            worker = new Thread(WriteLoop) { IsBackground = true, Name = "Chapter4 CSV" };
            worker.Start();
            }
            catch(Exception e)
            {
                // Keep partial files as evidence, but do not leave a live writer or
                // report successful flushing when initialization was incomplete.
                Fail(e);
                throw;
            }
        }

        public void Fail(Exception failure)
        {
            error = failure.ToString();
            IsFlushed = false;
            End();
        }

        public void SaveConfiguration(string name, UnityEngine.Object configuration)
        {
            if(running && configuration!=null)
                File.WriteAllText(Path.Combine(directory,name+".json"),JsonUtility.ToJson(configuration,true));
        }

        private static string Csv(object value)
        {
            if (value == null) return "";
            string s = value is IFormattable f ? f.ToString(null,CultureInfo.InvariantCulture) : value.ToString();
            return "\"" + s.Replace("\"","\"\"") + "\"";
        }
        private void Enqueue(int file, string line)
        {
            if (!running || !string.IsNullOrEmpty(error) || queue.Count >= 8192)
            { Interlocked.Increment(ref dropped); return; }
            queue.Enqueue(Tuple.Create(file,line)); wake.Set();
        }
        private static string Header()
        {
            var names = new List<string>{"event","run_id","source_id","source_session_id","source_seq",
                "local_receive_seq","unity_receive_ns","unity_event_ns","raw_enqueued","status"};
            for(int j=1;j<=6;j++) names.Add("input_j"+j+"_rad");
            for(int j=1;j<=6;j++) names.Add("target_j"+j+"_display_deg");
            for(int j=1;j<=6;j++) names.Add("actual_j"+j+"_display_deg");
            for(int j=1;j<=6;j++) names.Add("actual_j"+j+"_valid");
            return string.Join(",",names);
        }
        public void Record(RobotStateFrame frame, string kind, string status = "accepted")
        {
            if (frame == null || !frame.HasSourceIdentity) return;
            var values = new List<object>{kind,frame.RunId,frame.SourceId,frame.SourceSessionId,frame.SourceSequenceId,
                frame.SequenceId,frame.UnityReceiveTimestampNs,kind=="apply" ? frame.UnityApplyTimestampNs : SystemClock.NowNs(),frame.RawEnqueued,status};
            for(int j=0;j<6;j++) values.Add(frame.JointPositionRad != null && j<frame.JointPositionRad.Length ? (object)frame.JointPositionRad[j] : null);
            for(int j=0;j<6;j++) values.Add(frame.ModelTargetDisplayDeg != null && j<frame.ModelTargetDisplayDeg.Length ? (object)frame.ModelTargetDisplayDeg[j] : null);
            float[] actual = new float[6]; bool[] valid = new bool[6];
            if(kind=="model" && model!=null) model.ReadMeasuredDisplayDegrees(actual,valid);
            for(int j=0;j<6;j++) values.Add(valid[j] ? (object)actual[j] : null);
            for(int j=0;j<6;j++) values.Add(valid[j]);
            Enqueue(0,string.Join(",",values.ConvertAll(Csv)));
        }
        public void Applied(RobotStateFrame frame)
        {
            if(frame==null || frame.RunId!=runId) return;
            active=frame.Clone(); Record(frame,"apply");
        }
        private void LateUpdate()
        {
            if(!running) return;
            long now=SystemClock.NowNs();
            if(now-resourceAtNs>=1000000000L)
            {
                process.Refresh();
                double cpu=process.TotalProcessorTime.TotalMilliseconds;
                cpuPercent=100.0*(cpu-cpuAtMs)/((now-resourceAtNs)/1e6);
                workingSet=process.WorkingSet64;cpuAtMs=cpu;resourceAtNs=now;
            }
            Enqueue(1,string.Join(",",Csv(runId),Csv(Time.frameCount),Csv(now),
                Csv(lastRenderNs>0 ? (object)((now-lastRenderNs)/1e6) : null),Csv(Time.unscaledDeltaTime*1000.0),
                Csv(workingSet),Csv(cpuPercent)));
            lastRenderNs=now;
            if(active!=null) Record(active,"model");
        }
        private void WriteLoop()
        {
            try
            {
                using(var samples=new StreamWriter(Path.Combine(directory,"samples.csv")))
                using(var render=new StreamWriter(Path.Combine(directory,"render_frames.csv")))
                {
                    samples.WriteLine(Header());
                    render.WriteLine("run_id,frame_count,unity_monotonic_ns,frame_interval_ms,unscaled_delta_ms,process_working_set_bytes,process_cpu_percent_one_core");
                    while(running || !queue.IsEmpty)
                    {
                        while(queue.TryDequeue(out var row))
                        { (row.Item1==0 ? samples : render).WriteLine(row.Item2); Interlocked.Increment(ref written); }
                        samples.Flush(); render.Flush();
                        if(running) wake.WaitOne(250);
                    }
                }
            }
            catch(Exception e) { error=e.ToString(); }
        }
        public void End()
        {
            if(worker==null && !running && (directory==null || IsFlushed)) return;
            running=false; wake.Set();
            bool joined=worker==null || (worker.ThreadState & ThreadState.Unstarted)!=0 || worker.Join(10000);
            if(!joined) error="Writer did not flush within 10 seconds; run is incomplete.";
            // A terminated writer may have failed. Never label such a run as flushed,
            // and never carry its unwritten rows into the next run.
            if(joined)
            {
                while(queue.TryDequeue(out _)) Interlocked.Increment(ref dropped);
                worker=null;
            }
            IsFlushed=joined && string.IsNullOrEmpty(error);
            try
            {
                File.WriteAllText(Path.Combine(directory,"quality.json"),JsonUtility.ToJson(new Quality{
                    run_id=runId,written_rows=written,dropped_rows=dropped,writer_error=error,flushed=IsFlushed},true));
            }
            catch(Exception e) { error=e.ToString(); IsFlushed=false; }
            process?.Dispose(); process=null;
        }
        private void OnDisable() { End(); }
        private void OnApplicationQuit() { End(); }
    }
}
