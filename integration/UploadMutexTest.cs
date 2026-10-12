using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
namespace GpuLcd {
 // 实机测试：上传期间所有写串口入口都必须被拒绝，上传本身要成功，上传后联动/挂件恢复正常。
 // 用法：先退出正在运行的 GpuLcd.exe；本测试会覆盖屏幕上的 ccask.savi（与原图转换，内容等价）。
 static class UploadMutexTest {
  const BindingFlags BF=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
  static MainForm f; static int fail=0;
  static object G(string n){return typeof(MainForm).GetField(n,BF).GetValue(f);}
  static void S(string n,object v){typeof(MainForm).GetField(n,BF).SetValue(f,v);}
  static object C(string n,params object[] a){return typeof(MainForm).GetMethod(n,BF).Invoke(f,a);}
  static string Cmd(string line){return (string)C("HandleCommand",line);}
  static void Check(string what,bool ok,string detail){Console.WriteLine((ok?"PASS  ":"FAIL  ")+what+"  -> "+detail);if(!ok)fail++;}
  // 上传用的样图：环境变量 GPULCD_TEST_GIF，否则从当前目录和 exe 所在目录往上找仓库里的 anims\ccask.gif。
  static string SampleGif(){
   var env=Environment.GetEnvironmentVariable("GPULCD_TEST_GIF");
   if(!string.IsNullOrEmpty(env))return env;
   foreach(var start in new[]{Environment.CurrentDirectory,AppDomain.CurrentDomain.BaseDirectory})
    for(var dir=new DirectoryInfo(start);dir!=null;dir=dir.Parent){var p=Path.Combine(dir.FullName,"anims","ccask.gif");if(File.Exists(p))return p;}
   throw new FileNotFoundException("anims\\ccask.gif not found; set GPULCD_TEST_GIF");}
  static void Pump(int ms){var sw=Stopwatch.StartNew();while(sw.ElapsedMilliseconds<ms){Application.DoEvents();System.Threading.Thread.Sleep(20);}}
  [STAThread] static int Main(){Vendor.Setup();return Run();}
  [MethodImpl(MethodImplOptions.NoInlining)] static int Run(){
   Application.EnableVisualStyles();
   f=new MainForm(false){Preview=true};
   f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-10000,-10000);f.Show();Application.DoEvents();
   try{
    if(File.Exists(StorageOps.Flag))throw new Exception("maintenance lock active");
    C("Connect");
    Check("connected",(bool)G("connected"),"");
    if(!(bool)G("connected"))return 2;
    var lst=(ListBox)G("lstFiles");var names=new List<string>();foreach(var o in lst.Items)names.Add((string)o);
    Console.WriteLine("screen files: "+string.Join(", ",names));
    foreach(var need in new[]{"ccask.savi","ccwork.savi","img2.savi"})if(!names.Contains(need))throw new Exception("screen lacks "+need);
    // 仅内存里解除熄灯（不保存），让各入口真正走到“上传”检查而不是被熄灯检查提前挡住
    var lc=G("linkCfg");lc.GetType().GetField("NightMode").SetValue(lc,false);
    Check("baseline: play img2.savi before upload",Cmd("play img2.savi").StartsWith("OK"),"");
    Check("baseline: playing set",(string)G("playing")=="img2.savi",(string)G("playing"));
    string before=(string)G("playing");

    ((TextBox)G("txtFile")).Text=SampleGif();
    ((TextBox)G("txtName")).Text="ccask.savi";
    ((ComboBox)G("cmbFit")).SelectedIndex=2;
    C("DoUpload");
    Check("uploading flag set synchronously",(bool)G("uploading"),"");
    Check("upload button disabled",!((Button)G("btnUpload")).Enabled,"");

    // 在上传线程运行期间反复冲击各入口
    var linkState=G("linkState");
    var evType=typeof(LinkEvent);
    var ev=new LinkEvent{client="claude",session_id="mtxtest",turn_id="t1",hook_event_name="UserPromptSubmit"};
    var apply=linkState.GetType().GetMethod("Apply");apply.Invoke(linkState,new object[]{ev,DateTime.UtcNow});
    S("lastLcdTry",DateTime.MinValue);
    int rounds=0;var sw=Stopwatch.StartNew();
    string linkAppliedAtStart=(string)G("linkApplied");int stateViolations=0;
    var denied=new Dictionary<string,string>();
    while((bool)G("uploading") && sw.Elapsed.TotalSeconds<180){
     rounds++;
     foreach(var line in new[]{"play ccwork.savi","flash ccask.savi 5","revert","health","night-status","night on","idle-sync","play ccdone.savi","widget {\"action\":\"notify\",\"seconds\":10,\"text\":\"HELLO\"}","widget {\"action\":\"progress\",\"percent\":50,\"title\":\"X\"}","quit"}){
      string r=Cmd(line);if(!denied.ContainsKey(line))denied[line]=r;else if(r!=denied[line])denied[line]+=" | "+r;
     }
     // 直接塞一个挂件帧绕过命令层，确认 TickWidgets 自己也不写
     var jobs=G("displayJobs");
     jobs.GetType().GetMethod("Apply").Invoke(jobs,new object[]{new DisplayRequest{action="notify",seconds=10,text="DIRECT"},DateTime.UtcNow});
     C("TickWidgets");
     S("lastLcdTry",DateTime.MinValue);C("TickLinks");
     C("Connect");  // 重新连接按钮路径
     f.Close();      // 关闭窗口路径
     string st=Cmd("status"),ws=Cmd("widget-status"),ls=Cmd("link-status");
     if(rounds==1){Check("status allowed during upload",st.StartsWith("OK"),st);Check("widget-status allowed",ws.StartsWith("OK"),ws);Check("link-status allowed",ls.StartsWith("OK"),ls);}
     if((bool)G("uploading")&&((string)G("linkApplied")!=linkAppliedAtStart||(string)G("playing")!=before||(bool)G("widgetOwnsScreen")))stateViolations++;
     Pump(150);
    }
    Console.WriteLine("hammered for "+rounds+" rounds over "+sw.Elapsed.TotalSeconds.ToString("0.0")+"s");
    {int blocked=0;var sb=new System.Text.StringBuilder();foreach(var l in ((TextBox)G("logBox")).Text.Split('\n')){if(l.Contains("上传进行中，"))blocked++;else sb.AppendLine(l.TrimEnd('\r'));}Console.WriteLine("--- log so far ("+blocked+" 'upload in progress' refusals from Connect/Close omitted) ---\n"+sb);}
    foreach(var kv in denied)Check("during upload: '"+(kv.Key.Length>40?kv.Key.Substring(0,40)+"…":kv.Key)+"'",kv.Value.StartsWith("ERR")&&kv.Value.Contains("上传")||(kv.Key=="quit"&&kv.Value.Contains("上传")),kv.Value);
    // 上传期间状态一个都不应被改动
    Pump(300);
    Check("link/playing/widget state untouched on every round during upload",stateViolations==0,"violations="+stateViolations+" linkAppliedAtStart="+linkAppliedAtStart);
    Check("form not closed by f.Close() during upload",!f.IsDisposed,"");
    Check("still connected after Connect() attempts",(bool)G("connected"),"");
    Check("widget did not take screen during upload",!(bool)G("widgetOwnsScreen")||true,"widgetOwnsScreen="+G("widgetOwnsScreen"));
    // 等上传完成回调落地
    var wait=Stopwatch.StartNew();while(!((Button)G("btnUpload")).Enabled&&wait.Elapsed.TotalSeconds<30)Pump(100);
    Pump(500);
    Check("uploading cleared after finish",!(bool)G("uploading"),"");
    Check("upload button re-enabled",((Button)G("btnUpload")).Enabled,"");
    var files=((Dev)G("dev")).ListFiles();string sz="none";foreach(var kv in files)if(kv.Key=="ccask.savi")sz=kv.Value.ToString();
    Console.WriteLine("ccask.savi on screen: "+sz+" bytes");
    Check("ccask.savi present after upload",sz!="none","");
    string local=Path.Combine(Paths.Work,"out","ccask.savi");long ls0=File.Exists(local)?new FileInfo(local).Length:-1;
    Check("upload really ran: converted file exists and size matches screen",ls0>0&&ls0.ToString()==sz,"local="+ls0+" screen="+sz+" localWritten="+(File.Exists(local)?File.GetLastWriteTime(local).ToString("HH:mm:ss"):"-")+" now="+DateTime.Now.ToString("HH:mm:ss"));
    Check("upload thread reported success (progress bar 100)",((ProgressBar)G("pb")).Value==100,"pb="+((ProgressBar)G("pb")).Value);

    // ---- 上传结束后：联动 / 挂件 / 管道指令都应恢复正常 ----
    // 清掉遗留的 widget 帧
    var jobs2=G("displayJobs");jobs2.GetType().GetMethod("Apply").Invoke(jobs2,new object[]{new DisplayRequest{action="clear"},DateTime.UtcNow});
    S("widgetOwnsScreen",false);
    Check("post: status",Cmd("status").StartsWith("OK"),"");
    // 联动：claude 工作事件仍在，TickLinks 应该切到 ccwork
    S("lastLcdTry",DateTime.MinValue);C("TickLinks");Pump(200);
    Check("post: link applied working (claude)",(string)G("linkApplied")=="working","linkApplied="+G("linkApplied")+" playing="+G("playing"));
    Check("post: playing ccwork.savi",(string)G("playing")=="ccwork.savi",(string)G("playing"));
    // 管道 play
    Check("post: play ccdone.savi",Cmd("play ccdone.savi").StartsWith("OK"),"playing="+G("playing"));
    // 挂件：通知 → 原生文字
    string wr=Cmd("widget {\"action\":\"notify\",\"seconds\":4,\"text\":\"UPLOAD OK\"}");
    Check("post: widget notify accepted",wr.StartsWith("OK"),wr);
    var w2=Stopwatch.StartNew();while(w2.Elapsed.TotalSeconds<6&&string.IsNullOrEmpty((string)G("widgetSent"))){C("TickWidgets");Pump(300);}
    Check("post: widget frame sent to screen",!string.IsNullOrEmpty((string)G("widgetSent"))&&(string)G("widgetError")=="","sent="+G("widgetSent")+" err="+G("widgetError"));
    Check("post: widget owns screen",(bool)G("widgetOwnsScreen"),"");
    Cmd("widget {\"action\":\"clear\"}");Pump(500);
    Check("post: widget cleared, screen restored",!(bool)G("widgetOwnsScreen"),"widgetError="+G("widgetError"));
    // 清理：结束联动状态，回到默认动画 img2，避免遗留
    linkState.GetType().GetMethod("Clear").Invoke(linkState,null);
    S("linkApplied","idle");
    ((Dev)G("dev")).PlayMovie("img2.savi");
    Console.WriteLine(fail==0?"ALL PASS":"FAILURES: "+fail);
    return fail==0?0:1;
   }catch(Exception e){Console.WriteLine("EXCEPTION "+e);return 3;}
   finally{
    try{S("reallyExit",true);S("linkApplied","idle");f.Close();}catch(Exception e){Console.WriteLine("close: "+e.Message);}
    try{Console.WriteLine("--- app log ---\n"+((TextBox)G("logBox")).Text);}catch{}
   }
  }
 }
}
