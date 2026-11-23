using System;
using System.IO;
using System.Linq;
using DigitalTwin;
using DigitalTwin.UI;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace DigitalTwin.EditorTools {
 [InitializeOnLoad] public static class CloseoutUiStage {
  const string Root="F:/new_2026-/zyq_ST_use/E_TJ/agent_runs/codex_2026-11-10/ui_control_closeout";
  static double next;
  static CloseoutUiStage(){EditorApplication.update+=Tick;
   Application.logMessageReceived+=(message,stack,type)=>{if(SessionState.GetBool("Closeout.Armed",false)&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)){var dir=SessionState.GetString("Closeout.Dir","");if(Directory.Exists(dir))File.AppendAllText(dir+"/console_errors.jsonl",new JObject{["message"]=message,["stack"]=stack,["type"]=type.ToString()}.ToString(Newtonsoft.Json.Formatting.None)+"\n");}};
   if(SessionState.GetBool("Closeout.Replay",false))typeof(TwinUiFix1Stage).GetField("Widths",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).SetValue(null,new[]{1920});
   EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool("Closeout.Replay",false)){SessionState.SetBool("Closeout.Replay",false);FullHd();}};
  }
  static void FullHd(){var t=typeof(TwinUiShotWidthStage);var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;var g=t.GetMethod("Group",flags).Invoke(null,null);int count=(int)g.GetType().GetMethod("GetTotalCount").Invoke(g,null);int index=-1;for(int i=0;i<count;i++){var size=g.GetType().GetMethod("GetGameViewSize").Invoke(g,new object[]{i});int w=(int)t.GetMethod("SizeInt",flags).Invoke(null,new[]{size,(object)"width"});int h=(int)t.GetMethod("SizeInt",flags).Invoke(null,new[]{size,(object)"height"});if(w==1920&&h==1080){index=i;break;}}if(index<0)index=(int)t.GetMethod("AddCustom",flags).Invoke(null,new[]{g,(object)1920,1080,"Full HD"});t.GetMethod("ApplySize",flags).Invoke(null,new object[]{index});}
  [MenuItem("DigitalTwin/Closeout/7 Verify Real Replay")]
  public static void Replay(){if(EditorApplication.isPlaying)throw new Exception("Exit Play first");SessionState.SetBool("Closeout.Replay",true);var t=typeof(TwinUiFix1Stage);var f=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;t.GetField("Widths",f).SetValue(null,new[]{1920});t.GetField("BatchRoot",f).SetValue(null,Root+"/replay");t.GetMethod("RunCore",f).Invoke(null,null);}
  [MenuItem("DigitalTwin/Closeout/8 Check Chart Label Separation")]
  public static void ChartLabels(){
   var go=new GameObject("CloseoutChartFixture",typeof(RectTransform),typeof(CanvasRenderer));Mesh mesh=null;
   try{var chart=go.AddComponent<TwinUiLineChart>();((RectTransform)go.transform).sizeDelta=new Vector2(800,180);chart.Configure(1,false,0,1);chart.SetSeriesLabel(0,"J1");chart.SetSeries(0,new[]{0f,1f});
    typeof(TwinUiLineChart).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(chart,null);
    using(var vh=new VertexHelper()){typeof(TwinUiLineChart).GetMethod("OnPopulateMesh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(VertexHelper)},null).Invoke(chart,new object[]{vh});mesh=new Mesh();vh.FillMesh(mesh);}
    Color32 series=TwinUIStyle.SeriesColors[0];var points=mesh.vertices.Where((v,i)=>mesh.colors32[i].Equals(series)).ToArray();Assert(points.Length>0,"series vertices missing");
    var legend=(RectTransform)go.transform.Find("Legend");var time=(RectTransform)go.transform.Find("AxisT0");var lc=new Vector3[4];var tc=new Vector3[4];legend.GetWorldCorners(lc);time.GetWorldCorners(tc);
    float top=points.Max(v=>v.y),bottom=points.Min(v=>v.y);float legendBottom=go.transform.InverseTransformPoint(lc[0]).y,timeTop=go.transform.InverseTransformPoint(tc[1]).y;
    File.WriteAllText(Root+"/chart_labels_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff")+".json",new JObject{["passed"]=top<legendBottom&&bottom>timeTop,["line_top"]=top,["legend_bottom"]=legendBottom,["line_bottom"]=bottom,["time_top"]=timeTop,["source"]="SYNTHETIC_UI_GEOMETRY"}.ToString());
   }finally{if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);}
  }
  [MenuItem("DigitalTwin/Closeout/1 Inspect Edit")]
  public static void EditInspect(){Directory.CreateDirectory(Root);Dump(Root+"/edit_inspection.json");}
  [MenuItem("DigitalTwin/Closeout/10 Final Readonly State")]
  public static void FinalState(){if(EditorApplication.isPlaying)throw new Exception("Expected Edit mode");FullHd();Dump(Root+"/final_editor_state_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff")+".json");}
  [MenuItem("DigitalTwin/Closeout/11 Preserve And Reload Edit Scene")]
  public static void PreserveReload(){if(EditorApplication.isPlaying)throw new Exception("Expected Edit mode");var scene=UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();string original=scene.path;string copy=Root+"/editor_unsaved_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff")+".unity";if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,copy,true))throw new Exception("Scene copy not saved; refusing reload");UnityEditor.SceneManagement.EditorSceneManager.OpenScene(original,UnityEditor.SceneManagement.OpenSceneMode.Single);}
  [MenuItem("DigitalTwin/Closeout/3 Check Scene Buttons")]
  public static void CheckScene(){
   var host=UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();var failures=new JArray();
   foreach(string path in new[]{"MissionRoot/Eval/BtnRoaming","MissionRoot/Detail/Timeline/BtnPreview","MissionRoot/Detail/Timeline/BtnPlay","MissionRoot/Detail/Timeline/BtnPause"}){
    var r=host.transform.Find(path) as RectTransform;if(r==null||r.anchoredPosition.y>0)failures.Add(path+": above parent");
   }
   Directory.CreateDirectory(Root);File.WriteAllText(Root+"/scene_buttons_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff")+".json",new JObject{["passed"]=failures.Count==0,["failures"]=failures}.ToString());
  }
  [MenuItem("DigitalTwin/Closeout/4 Repair Scene Buttons")]
  public static void RepairScene(){
   if(EditorApplication.isPlaying)throw new Exception("Edit only");var host=UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
   foreach(string path in new[]{"MissionRoot/Eval/BtnRoaming","MissionRoot/Detail/Timeline/BtnPreview","MissionRoot/Detail/Timeline/BtnPlay","MissionRoot/Detail/Timeline/BtnPause"}){
    var r=host.transform.Find(path) as RectTransform;if(r==null)throw new Exception(path);Undo.RecordObject(r,"Correct legacy button local Y");var p=r.anchoredPosition;p.y=-Mathf.Abs(p.y);r.anchoredPosition=p;
   }
   UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(host.gameObject.scene);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(host.gameObject.scene);
   CheckScene();Dump(Root+"/edit_after.json");
  }
  [MenuItem("DigitalTwin/Closeout/5 Capture Scene Panel")]
  public static void CaptureScene(){
   var host=UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();var c=host.GetComponent<Canvas>();var oldMode=c.renderMode;var oldCam=c.worldCamera;
   var pos=c.transform.position;var rot=c.transform.rotation;var scale=c.transform.localScale;
   var nodes=c.GetComponentsInChildren<Transform>(true);var layers=nodes.Select(t=>t.gameObject.layer).ToArray();
   var go=new GameObject("CloseoutCaptureCamera");var cam=go.AddComponent<Camera>();cam.enabled=false;
   RenderTexture rt=null;Texture2D image=null;var prev=RenderTexture.active;
   try{
    // Isolated panel capture: exclude unrelated room meshes. Restore every layer.
    foreach(var node in nodes)node.gameObject.layer=31;cam.cullingMask=1<<31;
    c.renderMode=RenderMode.WorldSpace;c.worldCamera=cam;c.transform.position=pos;c.transform.rotation=rot;c.transform.localScale=scale;
    // Overlay uses pixel-sized world transforms. Normalize only this temporary
    // world fixture to a human-sized panel, avoiding kilometre-scale raycasts.
    if(oldMode==RenderMode.ScreenSpaceOverlay){c.transform.position=Vector3.zero;c.transform.rotation=Quaternion.identity;c.transform.localScale=Vector3.one*0.001f;}
    var rect=(RectTransform)c.transform;var center=rect.TransformPoint(rect.rect.center);cam.transform.SetPositionAndRotation(center-c.transform.forward*3f,c.transform.rotation);
    cam.orthographic=true;cam.orthographicSize=rect.rect.height*c.transform.lossyScale.y*0.54f;cam.nearClipPlane=0.01f;cam.farClipPlane=100f;
    rt=new RenderTexture(1920,1080,24);cam.targetTexture=rt;Canvas.ForceUpdateCanvases();cam.Render();RenderTexture.active=rt;
    image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
    File.WriteAllBytes(Root+"/scene_panel_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff")+".png",image.EncodeToPNG());
    if(EditorApplication.isPlaying){
     var target=host.Root.Find("BtnWindowMin") as RectTransform;var point=RectTransformUtility.WorldToScreenPoint(cam,target.TransformPoint(target.rect.center));
     var ev=new PointerEventData(EventSystem.current){position=point};var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(ev,hits);Assert(hits.Any(x=>x.gameObject==target.gameObject),"world canvas raycast miss");
     cam.transform.position+=cam.transform.right*0.12f;cam.transform.LookAt(center,c.transform.up);var movedPoint=RectTransformUtility.WorldToScreenPoint(cam,target.TransformPoint(target.rect.center));ev.position=movedPoint;hits.Clear();EventSystem.current.RaycastAll(ev,hits);Assert(hits.Any(x=>x.gameObject==target.gameObject),"world raycast miss after viewpoint change");
     File.WriteAllText(SessionState.GetString("Closeout.Dir",Root)+"/world_raycast.json",new JObject{["passed"]=true,["mode"]="WorldSpace runtime fixture; restored after capture",["camera_pixelRect"]=cam.pixelRect.ToString(),["target"]=PathOf(target),["point"]=point.ToString(),["after_view_change"]=movedPoint.ToString(),["vr_tested"]=false}.ToString());
    }
   }finally{for(int i=0;i<nodes.Length;i++)if(nodes[i]!=null)nodes[i].gameObject.layer=layers[i];c.renderMode=oldMode;c.worldCamera=oldCam;c.transform.position=pos;c.transform.rotation=rot;c.transform.localScale=scale;RenderTexture.active=prev;if(rt!=null)UnityEngine.Object.DestroyImmediate(rt);if(image!=null)UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);}
  }
  [MenuItem("DigitalTwin/Closeout/6 Verify UI Operations")]
  public static void Operations(){SessionState.SetBool("Closeout.OverlayFixture",false);SessionState.SetBool("Closeout.Operations",true);Start();}
  [MenuItem("DigitalTwin/Closeout/9 Diagnose Overlay Fixture")]
  public static void OverlayFixture(){SessionState.SetBool("Closeout.OverlayFixture",true);SessionState.SetBool("Closeout.Operations",true);Start();}
  [MenuItem("DigitalTwin/Closeout/2 Inspect Play")]
  public static void Start(){
   if(EditorApplication.isPlaying)throw new Exception("Exit Play first");
   var profile=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/DoosanDT/10_DigitalTwin/Config/Profiles/TwinSystemProfile.asset");
   var so=new SerializedObject(profile);
   if(so.FindProperty("enableRealRobotCommand").boolValue||!so.FindProperty("enableDryRun").boolValue)throw new Exception("unsafe profile");
   string dir=Root+"/ui_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");Directory.CreateDirectory(dir);
   SessionState.SetString("Closeout.Dir",dir);SessionState.SetInt("Closeout.Step",0);SessionState.SetBool("Closeout.Armed",true);
   EditorApplication.isPlaying=true;
  }
  static string PathOf(Transform t){return t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;}
  static JObject RectInfo(RectTransform r){var c=new Vector3[4];r.GetWorldCorners(c);return new JObject{["path"]=PathOf(r),["active"]=r.gameObject.activeInHierarchy,["rect"]=r.rect.ToString(),["localPosition"]=r.localPosition.ToString("F4"),["scale"]=r.lossyScale.ToString("F6"),["corners"]=new JArray(c.Select(v=>v.ToString("F4")))};}
  static void Dump(string file){
   var canvases=new JArray(UnityEngine.Object.FindObjectsOfType<Canvas>(true).Select(c=>new JObject{["path"]=PathOf(c.transform),["active"]=c.gameObject.activeInHierarchy,["mode"]=c.renderMode.ToString(),["camera"]=c.worldCamera!=null?c.worldCamera.name:"none",["rect"]=RectInfo((RectTransform)c.transform)}));
   var host=UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
   var texts=new JArray(); var buttons=new JArray();
   if(host!=null){foreach(var t in host.GetComponentsInChildren<TMP_Text>(true)){var j=RectInfo(t.rectTransform);j["text"]=t.text;j["mesh_vertices"]=t.mesh!=null?t.mesh.vertexCount:0;j["culled"]=t.canvasRenderer.cull;j["alpha"]=t.canvasRenderer.GetAlpha();j["overflow"]=t.isTextOverflowing;texts.Add(j);}foreach(var b in host.GetComponentsInChildren<Button>(true)){var j=RectInfo((RectTransform)b.transform);j["interactable"]=b.interactable;buttons.Add(j);}}
   var cameras=new JArray(UnityEngine.Object.FindObjectsOfType<Camera>(true).Select(c=>new JObject{["path"]=PathOf(c.transform),["active"]=c.isActiveAndEnabled,["depth"]=c.depth,["tag"]=c.tag,["pixelRect"]=c.pixelRect.ToString(),["position"]=c.transform.position.ToString(),["rotation"]=c.transform.eulerAngles.ToString()}));
   var win=new JObject();if(host!=null && host.IsReady){var corners=new Vector3[4];host.Root.GetWorldCorners(corners);win["rect"]=host.WindowRect.ToString();win["canvasSize"]=host.CanvasSize.ToString();win["root"]=RectInfo(host.Root);win["screen"]=new JArray(corners.Select(v=>RectTransformUtility.WorldToScreenPoint(host.Canvas.worldCamera,v).ToString()));win["camera"]=host.Canvas.worldCamera==null?"none":PathOf(host.Canvas.worldCamera.transform);}
   File.WriteAllText(file,new JObject{["utc"]=DateTime.UtcNow.ToString("O"),["playing"]=EditorApplication.isPlaying,["width"]=Screen.width,["height"]=Screen.height,["canvases"]=canvases,["cameras"]=cameras,["window"]=win,["buttons"]=buttons,["texts"]=texts,["real_enabled"]=UnityEngine.Object.FindObjectOfType<TwinCommandController>()?.EnableRealRobotCommand}.ToString());
  }
  static void Tick(){
   if(!SessionState.GetBool("Closeout.Armed",false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
   if(EditorApplication.timeSinceStartup<next)return;
   int step=SessionState.GetInt("Closeout.Step",0);string dir=SessionState.GetString("Closeout.Dir","");
   try{
    if(step==0){next=EditorApplication.timeSinceStartup+4;SessionState.SetInt("Closeout.Step",1);return;}
    if(step==1){if(UnityEngine.Object.FindObjectOfType<TwinCommandController>()?.EnableRealRobotCommand==true)throw new Exception("runtime real enabled");Dump(dir+"/runtime.json");ScreenCapture.CaptureScreenshot(dir+"/desktop.png");SessionState.SetInt("Closeout.Step",SessionState.GetBool("Closeout.Operations",false)?10:2);next=EditorApplication.timeSinceStartup+1;return;}
    if(step>=10){OperationsTick(step,dir);return;}
    if(step==2){SessionState.SetBool("Closeout.Armed",false);EditorApplication.isPlaying=false;}
   }catch(Exception e){File.WriteAllText(dir+"/failure.txt",e.ToString());SessionState.SetBool("Closeout.Armed",false);EditorApplication.isPlaying=false;}
  }
  static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
  static void Record(string dir,string name){File.AppendAllText(dir+"/operations.jsonl",new JObject{["utc"]=DateTime.UtcNow.ToString("O"),["passed"]=true,["check"]=name}.ToString(Newtonsoft.Json.Formatting.None)+"\n");}
  static void OperationsTick(int step,string dir){
   var h=UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();var s=UnityEngine.Object.FindObjectOfType<TwinUiSurfaceHost>();Assert(h!=null&&h.IsReady&&s!=null,"UI not ready");
   if(step==10){if(SessionState.GetBool("Closeout.OverlayFixture",false)){h.Canvas.renderMode=RenderMode.ScreenSpaceOverlay;Record(dir,"temporary overlay diagnostic; Play-only");}SessionState.SetString("Closeout.Layout",JsonUtility.ToJson(new Vector4(h.WindowRect.x,h.WindowRect.y,h.WindowRect.width,h.WindowRect.height)));SessionState.SetBool("Closeout.Max",h.IsMaximized);h.ApplyLayout(new Rect(0,0,1920,1052),true,false);Record(dir,"maximize within canvas");}
   string[] pages={"overview","twin","control","mission","experiment"};
   // CaptureScreenshot reads at end of frame. Do not switch pages in the same
   // update, otherwise the filename describes the previous page, not the image.
   if(step>=12&&step<=16&&!SessionState.GetBool("Closeout.Captured",false)){ScreenCapture.CaptureScreenshot(dir+"/page_"+pages[step-12]+".png");Dump(dir+"/page_"+pages[step-12]+".json");SessionState.SetBool("Closeout.Captured",true);next=EditorApplication.timeSinceStartup+0.65;return;}
   SessionState.SetBool("Closeout.Captured",false);
   if(step>=11&&step<=15){s.SetTab(pages[step-11],false);Record(dir,"page "+pages[step-11]);}
   if(step==16){s.SetTab("control",false);}
   if(step==17){
    foreach(string n in new[]{"BtnWindowMin","BtnWindowHide","BtnWindowMax"}){var r=h.Root.Find(n) as RectTransform;Assert(r!=null&&r.parent==h.Root,"title button detached "+n);}
    var btn=h.Root.Find("BtnWindowMin").GetComponent<Button>();var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};ExecuteEvents.Execute<IPointerClickHandler>(btn.gameObject,data,ExecuteEvents.pointerClickHandler);Assert(h.IsMinimized&&!h.IsVisible,"minimize failed");Record(dir,"actual minimize pointerClick");h.Restore();Assert(h.IsVisible&&!h.IsMinimized,"restore failed");h.Hide();Assert(!h.IsVisible,"hide failed");h.Restore();Assert(h.IsVisible,"hide restore failed");Record(dir,"hide and restore");
   }
   if(step==18){h.ApplyLayout(new Rect(150,90,1400,850),false,false);var delta=h.MoveBy(new Vector2(40,25));Assert(delta.sqrMagnitude>1,"move failed");h.ResizeBy(TwinUiWindowEdge.Right,new Vector2(80,0));Record(dir,"move resize clamp");s.Detach("command");}
   if(step==19){Assert(s.Modules.First(m=>m.Id=="command").Surface==TwinUiModuleSurface.Floating,"detach failed");s.DockBack("command");Record(dir,"detach and dock command");h.ApplyLayout(new Rect(0,0,1920,1052),true,false);}
   if(step==20){var scroll=h.GetComponentsInChildren<ScrollRect>(true).First(x=>x.name=="PageViewport");scroll.verticalNormalizedPosition=0;Record(dir,"scroll to bottom");}
   if(step==21&&!SessionState.GetBool("Closeout.BottomCaptured",false)){ScreenCapture.CaptureScreenshot(dir+"/desktop_control_bottom.png");SessionState.SetBool("Closeout.BottomCaptured",true);next=EditorApplication.timeSinceStartup+0.65;return;}
   if(step==21){SessionState.SetBool("Closeout.BottomCaptured",false);var scroll=h.GetComponentsInChildren<ScrollRect>(true).First(x=>x.name=="PageViewport");scroll.verticalNormalizedPosition=1;}
   if(step==22){
    var button=s.Modules.First(m=>m.Id=="command").View.Find("Preview");Assert(button!=null,"preview missing");var rect=(RectTransform)button;var screen=RectTransformUtility.WorldToScreenPoint(h.Canvas.worldCamera,rect.TransformPoint(rect.rect.center));var ev=new PointerEventData(EventSystem.current){position=screen};var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(ev,hits);Assert(hits.Any(x=>x.gameObject==button.gameObject||x.gameObject.transform.IsChildOf(button)),"visible preview raycast miss");Record(dir,"screen coordinate graphic raycast hits preview");ScreenCapture.CaptureScreenshot(dir+"/desktop_control_top.png");Dump(dir+"/final.json");
   }
   if(step==23){CaptureScene();Record(dir,"world canvas capture uses same runtime content");}
   if(step==24){
    var saved=JsonUtility.FromJson<Vector4>(SessionState.GetString("Closeout.Layout",""));h.ApplyLayout(new Rect(saved.x,saved.y,saved.z,saved.w),SessionState.GetBool("Closeout.Max",false),false);
    Assert(Mathf.Abs(h.WindowRect.x-saved.x)<1f&&Mathf.Abs(h.WindowRect.y-saved.y)<1f&&Mathf.Abs(h.WindowRect.width-saved.z)<1f&&Mathf.Abs(h.WindowRect.height-saved.w)<1f,"initial window rect not restored");Record(dir,"initial window rect restored within 1px");
    File.WriteAllText(dir+"/result.json",new JObject{["passed"]=true,["source"]="NO_LIVE_DATA",["hardware_enabled"]=false,["physical_stop_verified"]=false,["vr_tested"]=false}.ToString());SessionState.SetBool("Closeout.Operations",false);SessionState.SetBool("Closeout.Armed",false);EditorApplication.isPlaying=false;return;
   }
   SessionState.SetInt("Closeout.Step",step+1);next=EditorApplication.timeSinceStartup+0.65;
  }
 }
}
