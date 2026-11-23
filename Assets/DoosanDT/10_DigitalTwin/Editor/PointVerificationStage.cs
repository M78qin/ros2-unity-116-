using System;
using System.IO;
using System.Reflection;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;
using DigitalTwin;
using Newtonsoft.Json.Linq;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace DigitalTwin.EditorTools
{
    [InitializeOnLoad]
    public static class PointVerificationStage
    {
        private const string Root = @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\codex_2026-11-09\prelive90";
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        static PointVerificationStage()
        {
            if (SessionState.GetBool("Codex.RealReplayVerify", false)) {
                typeof(TwinUiFix1Stage).GetField("Widths", Static).SetValue(null, new[] {1920});
            }
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("Codex.RealReplayVerify",false)) {
                    SessionState.SetBool("Codex.RealReplayVerify",false); FullHd();
                }
            };
            EditorApplication.update += TickPoint;
        }
        [MenuItem("DigitalTwin/Codex/2 Verify Point Simulation")]
        public static void Point()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first");
            string outDir=Root+"/unity_point_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(outDir);
            SessionState.SetString("Codex.Point.Out",outDir);
            SessionState.SetInt("Codex.Point.Step",0);
            SessionState.SetInt("Codex.Point.Mode",0);
            SessionState.SetBool("Codex.Point.Armed",true);
            SessionState.SetFloat("Codex.Point.Start",(float)EditorApplication.timeSinceStartup);
            FullHd(); EditorApplication.isPlaying=true;
        }
        static double _nextPoint;
        static void TickPoint()
        {
            if(SessionState.GetBool("Codex.Drag.Armed",false)) { TickDrag(); return; }
            if(!SessionState.GetBool("Codex.Point.Armed",false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if(EditorApplication.timeSinceStartup<_nextPoint) return;
            _nextPoint=EditorApplication.timeSinceStartup+0.15;
            string dir=SessionState.GetString("Codex.Point.Out","");
            int step=SessionState.GetInt("Codex.Point.Step",0),mode=SessionState.GetInt("Codex.Point.Mode",0);
            try {
                if(EditorApplication.timeSinceStartup-SessionState.GetFloat("Codex.Point.Start",0)>130) throw new Exception("point verification deadline");
                var surface=TwinUiSurfaceHost.Instance;
                if(surface==null||surface.Page==null) return;
                if(step==0) {
                    string closed;TwinUiTestIsolation.DisableXrAffordances(out closed);
                    File.AppendAllText(dir+"/events.jsonl",new JObject { ["event"]="boot",["closed_xr"]=closed,["width"]=Screen.width,["height"]=Screen.height }.ToString(Newtonsoft.Json.Formatting.None)+"\n");
                    var win=UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
                    if(win!=null) win.ApplyLayout(new Rect(8,8,1904,1064),false,false);
                    surface.SetTab(TwinUiWorkspaceRegistry.Control,false);
                    SessionState.SetInt("Codex.Point.Step",1); return;
                }
                var widget=surface.Find("command") as TwinUiWidgetCommand;
                if(widget==null||widget.Session==null) return;
                var s=widget.Session;
                if(step==1) { Click(widget,"ConnectSim"); if(s.State=="BLOCKED") throw new Exception(s.Reason); SessionState.SetInt("Codex.Point.Step",2); return; }
                if(step==2) {
                    if(!s.Connected) return;
                    Click(widget,"Mode"+mode);
                    double[] target=mode==0?s.CurrentJoints:s.CurrentTcp;
                    target=(double[])target.Clone();target[0]+=mode==0?0.1:1.0;
                    for(int i=0;i<6;i++) widget.View.Find("Target"+i).GetComponent<TMP_InputField>().text=target[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                    var edited=widget.View.Find("Target5").GetComponent<TMP_InputField>();
                    edited.onEndEdit.Invoke(edited.text);
                    SessionState.SetString("Codex.Point.Expected",new JArray(target).ToString(Newtonsoft.Json.Formatting.None));
                    // Product preview has no remote side effects; its visual scope is recorded separately.
                    Click(widget,"Preview");
                    File.AppendAllText(dir+"/events.jsonl",new JObject { ["event"]="preview",["mode"]=mode,["state"]=s.State,["reason"]=s.Reason }.ToString(Newtonsoft.Json.Formatting.None)+"\n");
                    Click(widget,"Check"); if(s.State=="BLOCKED") throw new Exception(s.Reason);
                    SessionState.SetInt("Codex.Point.Step",3);return;
                }
                if(step==3 && s.State=="PLANNED") {
                    Click(widget,"Authorize"); Click(widget,"Confirm"); if(s.State=="BLOCKED") throw new Exception(s.Reason);
                    SessionState.SetInt("Codex.Point.Step",4);return;
                }
                if(step==4 && s.State=="CONFIRMED") {
                    Click(widget,"Execute"); if(s.State=="BLOCKED") throw new Exception(s.Reason);
                    SessionState.SetInt("Codex.Point.Step",5);return;
                }
                if(step==5 && s.State=="REACHED") {
                    widget.Refresh();
                    var expected=JArray.Parse(SessionState.GetString("Codex.Point.Expected","")).ToObject<double[]>();
                    for(int i=0;i<6;i++) if(Math.Abs(expected[i]-s.Target[i])>1e-8) throw new Exception("screen target mismatch at "+i);
                    File.AppendAllText(dir+"/events.jsonl",new JObject { ["event"]="simulated_reached",["mode"]=mode,["state"]=s.State,["digest"]=s.Digest,["target"]=new JArray(s.Target),["reason"]=s.Reason }.ToString(Newtonsoft.Json.Formatting.None)+"\n");
                    SessionState.SetInt("Codex.Point.Step",6);_nextPoint=EditorApplication.timeSinceStartup+0.6;return;
                }
                if(step==6) {
                    foreach(var module in surface.Modules) if(module.View!=null && module.View.gameObject.activeInHierarchy) module.Refresh();
                    ScreenCapture.CaptureScreenshot(dir+"/mode_"+mode+"_1920x1080.png");
                    SessionState.SetInt("Codex.Point.Step",7);_nextPoint=EditorApplication.timeSinceStartup+0.8;return;
                }
                if(step==7) {
                    if(mode<2) {SessionState.SetInt("Codex.Point.Mode",mode+1);SessionState.SetInt("Codex.Point.Step",2);return;}
                    bool blocked=!s.Execute();
                    if(!blocked) throw new Exception("duplicate execute was not blocked");
                    File.WriteAllText(dir+"/result.json",new JObject { ["passed"]=true,["modes"]=3,["interaction"]="production Button pointerClick + TMP_InputField text events",["duplicate_execute_blocked"]=blocked,["source"]="SIMULATED",["preview_path_verified"]=false,["real_hardware_enabled"]=false }.ToString());
                    s.Cancel();SessionState.SetBool("Codex.Point.Armed",false);EditorApplication.isPlaying=false;return;
                }
                if(s.State=="FAILED"||s.State=="REJECTED"||s.State=="BLOCKED") throw new Exception(s.State+": "+s.Reason);
            } catch(Exception e) {
                File.WriteAllText(dir+"/result.json",new JObject { ["passed"]=false,["error"]=e.ToString(),["step"]=step,["mode"]=mode }.ToString());
                SessionState.SetBool("Codex.Point.Armed",false);
                UnityEngine.Object.FindObjectOfType<PointMotionSession>()?.Cancel();
                EditorApplication.isPlaying=false;
            }
        }
        [MenuItem("DigitalTwin/Codex/3 Verify Local IK Drag")]
        public static void Drag()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first");
            string dir=Root+"/ik_drag_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");Directory.CreateDirectory(dir);
            SessionState.SetString("Codex.Drag.Out",dir);SessionState.SetInt("Codex.Drag.Step",0);
            SessionState.SetFloat("Codex.Drag.Start",(float)EditorApplication.timeSinceStartup);
            SessionState.SetBool("Codex.Drag.Armed",true);FullHd();EditorApplication.isPlaying=true;
        }
        static void TickDrag()
        {
            if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            string dir=SessionState.GetString("Codex.Drag.Out","");
            try {
                double elapsed=EditorApplication.timeSinceStartup-SessionState.GetFloat("Codex.Drag.Start",0);
                if(elapsed>35) throw new Exception("drag test timeout");
                if(elapsed<3) return;
                int phase=SessionState.GetInt("Codex.Drag.Step",0);
                if(phase==1) {
                    if(elapsed<SessionState.GetFloat("Codex.Drag.CaptureAt",0)) return;
                    ScreenCapture.CaptureScreenshot(dir+"/ik_target_1920x1080.png");
                    SessionState.SetInt("Codex.Drag.Step",2);SessionState.SetFloat("Codex.Drag.CaptureAt",(float)elapsed+1);return;
                }
                if(phase==2) {
                    if(elapsed<SessionState.GetFloat("Codex.Drag.CaptureAt",0)) return;
                    UnityEngine.Object.FindObjectOfType<DigitalTwin.Interaction.TwinPlayIkHandle>()?.CancelForTest();
                    SessionState.SetBool("Codex.Drag.Armed",false);EditorApplication.isPlaying=false;return;
                }
                string closed;TwinUiTestIsolation.DisableXrAffordances(out closed);
                var handle=UnityEngine.Object.FindObjectOfType<DigitalTwin.Interaction.TwinPlayIkHandle>();
                var ik=UnityEngine.Object.FindObjectOfType<RobotIkController>();
                if(handle==null || ik==null) throw new Exception("existing IK/handle missing");
                if(handle.AllowRobotDispatch) throw new Exception("handle remote dispatch must be disabled");
                handle.ResetToCurrentForTest();
                if(!handle.BeginDragForTest()) throw new Exception("handle could not initialize");
                int dispatch=handle.DispatchCalls;
                Vector3 start=handle.TargetPosition;
                for(int i=1;i<=10;i++) handle.DragToForTest(start+Vector3.right*(0.001f*i));
                handle.EndDragForTest();
                float moved=Vector3.Distance(start,handle.TargetPosition);
                bool ok=handle.HasSolution && handle.JointLimitsOk && handle.DispatchCalls==dispatch
                    && Math.Abs(moved-0.01f)<0.001f && handle.PositionErrorMeters<0.002f;
                var surface=TwinUiSurfaceHost.Instance;
                surface.SetTab(TwinUiWorkspaceRegistry.Control,false);
                var widget=surface.Find("command") as TwinUiWidgetCommand;
                if(widget==null) throw new Exception("control widget missing");
                Click(widget,"Mode0"); Click(widget,"LoadIk");
                float[] solved=ik.GetLastSolutionDegreesCopy();
                for(int i=0;i<6;i++) ok &= Math.Abs(widget.Session.Target[i]-solved[i])<1e-4;
                File.WriteAllText(dir+"/result.json",new JObject { ["passed"]=ok,["scope"]="existing handle test API -> Unity IK -> production read-IK button -> joint point target",
                    ["moved_m"]=moved,["position_error_m"]=handle.PositionErrorMeters,["has_solution"]=handle.HasSolution,
                    ["joint_limits_ok"]=handle.JointLimitsOk,["dispatch_delta"]=handle.DispatchCalls-dispatch,
                    ["target_deg"]=new JArray(widget.Session.Target),["source"]="LOCAL_IK_MODEL",["physical_accuracy_verified"]=false }.ToString());
                SessionState.SetInt("Codex.Drag.Step",1);SessionState.SetFloat("Codex.Drag.CaptureAt",(float)elapsed+0.6f);
            } catch(Exception e) {
                File.WriteAllText(dir+"/result.json",new JObject { ["passed"]=false,["error"]=e.ToString() }.ToString());
                SessionState.SetBool("Codex.Drag.Armed",false);EditorApplication.isPlaying=false;
            }
        }
        private static void Click(TwinUiWidgetCommand widget,string name)
        {
            widget.Refresh();
            var t=widget.View.Find(name);
            if(t==null) throw new Exception("UI button missing: "+name);
            var button=t.GetComponent<Button>();
            if(button==null||!button.interactable) throw new Exception("UI button blocked: "+name);
            var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
            ExecuteEvents.Execute<IPointerClickHandler>(t.gameObject,data,ExecuteEvents.pointerClickHandler);
        }
        [MenuItem("DigitalTwin/Codex/1 Verify Real Replay")]
        public static void Replay()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before verification");
            SessionState.SetBool("Codex.RealReplayVerify",true);
            var t = typeof(TwinUiFix1Stage);
            t.GetField("Widths",Static).SetValue(null,new[]{1920});
            t.GetField("BatchRoot",Static).SetValue(null,Root+"/ui_review");
            t.GetMethod("RunCore",Static).Invoke(null,null);
        }
        [MenuItem("DigitalTwin/Codex/0 Full HD 1920x1080")]
        public static void FullHd()
        {
            var helper=typeof(TwinUiShotWidthStage);
            object group=helper.GetMethod("Group",Static).Invoke(null,null);
            var type=group.GetType();
            var get=type.GetMethod("GetGameViewSize");
            int count=(int)type.GetMethod("GetTotalCount").Invoke(group,null);
            var remove=type.GetMethod("RemoveCustomSize");
            for(int i=count-1;i>=0;i--) {
                object size=get.Invoke(group,new object[]{i});
                string name=(string)helper.GetMethod("SizeText",Static).Invoke(null,new[]{size});
                if(name!=null && name.StartsWith("A9 ")) remove.Invoke(group,new object[]{i});
            }
            count=(int)type.GetMethod("GetTotalCount").Invoke(group,null);
            int selected=-1;
            for(int i=0;i<count;i++) {
                object size=get.Invoke(group,new object[]{i});
                int w=(int)helper.GetMethod("SizeInt",Static).Invoke(null,new[]{size,(object)"width"});
                int h=(int)helper.GetMethod("SizeInt",Static).Invoke(null,new[]{size,(object)"height"});
                if(w==1920 && h==1080) { selected=i; break; }
            }
            if(selected<0) selected=(int)helper.GetMethod("AddCustom",Static).Invoke(null,new[]{group,(object)1920,1080,"Full HD"});
            helper.GetMethod("ApplySize",Static).Invoke(null,new object[]{selected});
            Directory.CreateDirectory(Root);
            File.WriteAllText(Root+"/resolution.json",JsonUtility.ToJson(new SizeProof { selected=selected,utc=DateTime.UtcNow.ToString("O"),width=1920,height=1080 }));
        }
        [Serializable] private class SizeProof {public int selected,width,height;public string utc;}
    }
}
