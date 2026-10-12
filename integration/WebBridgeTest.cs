using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
namespace GpuLcd {
 // 实机测试：新界面桥的命令层 (MainForm.WebCall)。验证 读命令不碰串口、写命令的拒绝条件、参数校验、放行路径。
 static class WebBridgeTest {
  const BindingFlags BF=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
  static MainForm f; static int fail=0;
  static object G(string n){return typeof(MainForm).GetField(n,BF).GetValue(f);}
  static void S(string n,object v){typeof(MainForm).GetField(n,BF).SetValue(f,v);}
  static void Check(string w,bool ok,string d){Console.WriteLine((ok?"PASS  ":"FAIL  ")+w+"  -> "+d);if(!ok)fail++;}
  static object Call(string cmd,Dictionary<string,object> a,out string err){
   var m=typeof(MainForm).GetMethod("WebCall",BF);var args=new object[]{cmd,a,null};
   object r=m.Invoke(f,args);err=(string)args[2];return r;}
  static Dictionary<string,object> D(params object[] kv){var d=new Dictionary<string,object>();for(int i=0;i<kv.Length;i+=2)d[(string)kv[i]]=kv[i+1];return d;}
  static string J(object o){return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(o);}
  [STAThread] static int Main(){Vendor.Setup();return Run();}
  [MethodImpl(MethodImplOptions.NoInlining)] static int Run(){
   Application.EnableVisualStyles();
   f=new MainForm(false){Preview=true};
   f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-10000,-10000);f.Show();Application.DoEvents();
   string err;
   try{
    if(File.Exists(StorageOps.Flag))throw new Exception("maintenance lock");
    typeof(MainForm).GetMethod("Connect",BF).Invoke(f,null);
    Check("connected",(bool)G("connected"),"");if(!(bool)G("connected"))return 2;
    var lc=G("linkCfg");var nm=lc.GetType().GetField("NightMode");
    bool realNight=(bool)nm.GetValue(lc);Console.WriteLine("real NightMode (from integration.json) = "+realNight);

    { var c0=G("cfg"); string cur0=(string)c0.GetType().GetField("Current").GetValue(c0);
      if(cur0=="ccask.savi"){Console.WriteLine("ABORT: settings.json Current is already ccask.savi (the upload test overwrites it and refuses to overwrite the in-use file). Restore settings first.");return 4;} }
    // ---- 读命令 ----
    var st=Call("getState",null,out err);Console.WriteLine("getState = "+J(st));
    Check("getState ok",err==null&&J(st).Contains("\"connected\":true")&&J(st).Contains("RTX 3070 Ti"),J(st));
    Check("getState firmware numeric",J(st).Contains("\"firmware\":122"),"");
    var se=Call("getSettings",null,out err);Console.WriteLine("getSettings = "+J(se));
    Check("getSettings ok",err==null&&J(se).Contains("brightness"),"");
    var fl=Call("listFiles",null,out err);Console.WriteLine("listFiles = "+J(fl));
    Check("listFiles has sizes",err==null&&J(fl).Contains("img2.savi")&&!J(fl).Contains("\"size\":0"),"");
    var lg=Call("getLog",D("limit",5),out err);Check("getLog limited to <=5",err==null&&((System.Collections.IList)lg).Count<=5,((System.Collections.IList)lg).Count+" lines");
    Call("nope",null,out err);Check("unknown cmd rejected",err!=null&&err.Contains("未知命令"),err);

    // ---- 熄灯中：写命令必须被拒绝 ----
    nm.SetValue(lc,true);
    Call("setBasic",D("brightness",5),out err);Check("setBasic refused in night mode",err!=null&&err.Contains("恢复灯光"),err);
    Call("playFile",D("name","img2.savi"),out err);Check("playFile refused in night mode",err!=null&&err.Contains("恢复灯光"),err);

    // ---- 非熄灯：参数校验（这些都在写串口之前被拒绝）----
    nm.SetValue(lc,false);
    string[][] bad={new[]{"brightness","11"},new[]{"brightness","-1"},new[]{"opacity","101"},new[]{"dir","4"}};
    foreach(var b in bad){Call("setBasic",D(b[0],int.Parse(b[1])),out err);Check("setBasic rejects "+b[0]+"="+b[1],err!=null&&err.Contains("必须是"),err);}
    Call("setBasic",D("enabled","yes"),out err);Check("setBasic rejects non-bool enabled",err!=null,err);
    Call("setBasic",D("brightness","abc"),out err);Check("setBasic rejects non-numeric",err!=null,err);
    Call("playFile",D("name","nonexist.savi"),out err);Check("playFile rejects file not on screen",err!=null&&err.Contains("屏幕上没有"),err);
    Call("playFile",D("name","../../x"),out err);Check("playFile rejects path-like name",err!=null,err);
    Call("playFile",null,out err);Check("playFile rejects missing name",err!=null,err);

    // ---- 上传中：写命令必须被拒绝 ----
    S("uploading",true);
    Call("setBasic",D("brightness",5),out err);Check("setBasic refused while uploading",err=="上传进行中",err);
    Call("playFile",D("name","img2.savi"),out err);Check("playFile refused while uploading",err=="上传进行中",err);
    st=Call("getState",null,out err);Check("getState still works while uploading, reports uploading=true",err==null&&J(st).Contains("\"uploading\":true"),"");
    S("uploading",false);

    // ---- 放行路径：写入与当前设置相同的值（真实写一次串口）----
    var cfg=G("cfg");int bri=(int)cfg.GetType().GetField("Bri").GetValue(cfg),opa=(int)cfg.GetType().GetField("Opa").GetValue(cfg),dir=(int)cfg.GetType().GetField("Dir").GetValue(cfg);bool en=(bool)cfg.GetType().GetField("En").GetValue(cfg);
    var r=Call("setBasic",D("brightness",bri,"opacity",opa,"dir",dir,"enabled",en),out err);
    Check("setBasic accepted (same values as saved settings)",err==null,err??J(r));
    se=Call("getSettings",null,out err);Check("settings unchanged after same-value setBasic",J(se).Contains("\"brightness\":"+bri)&&J(se).Contains("\"opacity\":"+opa),J(se));
    string cur=(string)cfg.GetType().GetField("Current").GetValue(cfg);
    r=Call("playFile",D("name",cur),out err);
    Check("playFile accepted for current default '"+cur+"'",err==null,err??J(r));
    Check("playing == current after playFile",(string)G("playing")==cur,(string)G("playing"));
    // ================= 阶段 2：上传命令 =================
    Console.WriteLine("---- upload commands ----");
    string gif=@"C:\Users\Sun_Local\tools\GpuLcd\anims\ccask.gif";
    var sp=Call("getSpace",null,out err);Console.WriteLine("getSpace = "+J(sp));
    Check("getSpace known with sane numbers",err==null&&J(sp).Contains("\"known\":true")&&!J(sp).Contains("\"total\":0"),J(sp));
    Func<string,Dictionary<string,object>> U=(name)=>D("token",(string)G("uploadToken"),"name",name,"fit","stretch","mode","movie");
    S("uploadToken","");S("uploadPath","");
    Call("startUpload",D("token","","name","ccask.savi","fit","stretch","mode","movie"),out err);Check("startUpload rejects empty token",err!=null&&err.Contains("令牌"),err);
    S("uploadToken","tok-1");S("uploadPath",gif);
    Call("startUpload",D("token","WRONG","name","ccask.savi","fit","stretch","mode","movie"),out err);Check("startUpload rejects wrong token",err!=null&&err.Contains("令牌"),err);
    foreach(var bn in new[]{"bad name.savi","toolongname1.savi","x.gif","../a.savi","a/b.savi",""}){
     Call("startUpload",D("token","tok-1","name",bn,"fit","stretch","mode","movie"),out err);Check("startUpload rejects name '"+bn+"'",err!=null&&err.Contains("文件名"),err);}
    Call("startUpload",D("token","tok-1","name","ccask.savi","fit","zoom","mode","movie"),out err);Check("startUpload rejects bad fit",err!=null&&err.Contains("适配"),err);
    Call("startUpload",D("token","tok-1","name","ccask.savi","fit","stretch","mode","both"),out err);Check("startUpload rejects bad mode",err!=null&&err.Contains("用途"),err);
    string curName=(string)cfg.GetType().GetField("Current").GetValue(cfg);
    Call("startUpload",D("token","tok-1","name",curName,"fit","stretch","mode","movie"),out err);Check("startUpload refuses to overwrite current default '"+curName+"'",err!=null&&err.Contains("正在使用"),err);
    S("playing","ccwork.savi");
    Call("startUpload",D("token","tok-1","name","ccwork.savi","fit","stretch","mode","movie"),out err);Check("startUpload refuses to overwrite currently playing file",err!=null&&err.Contains("正在使用"),err);
    S("playing",curName);
    // 熄灯 / 上传中 / 令牌文件被删
    nm.SetValue(lc,true);Call("startUpload",D("token","tok-1","name","ccask.savi","fit","stretch","mode","movie"),out err);Check("startUpload refused in night mode",err!=null&&err.Contains("恢复灯光"),err);nm.SetValue(lc,false);
    S("uploading",true);Call("startUpload",D("token","tok-1","name","ccask.savi","fit","stretch","mode","movie"),out err);Check("startUpload refused while uploading",err=="上传进行中",err);S("uploading",false);
    S("uploadPath",@"C:\no\such\file.gif");Call("startUpload",D("token","tok-1","name","ccask.savi","fit","stretch","mode","movie"),out err);Check("startUpload refuses vanished file",err!=null&&err.Contains("不存在"),err);
    Check("token revoked after vanished file",(string)G("uploadToken")=="",(string)G("uploadToken"));

    // 用户在确认框里点"否"：不上传，令牌保留
    S("uploadToken","tok-2");S("uploadPath",gif);string seen=null;
    S("confirmHook",new Func<string,bool>(t=>{seen=t;return false;}));
    var rc=Call("startUpload",D("token","tok-2","name","ccask.savi","fit","stretch","mode","movie"),out err);
    Check("declined confirm -> started:false cancelled",err==null&&J(rc).Contains("\"started\":false")&&J(rc).Contains("cancelled"),err??J(rc));
    Check("declined confirm did not start upload",!(bool)G("uploading"),"");
    Check("confirm text mentions overwrite / not cancellable / power-cycle",seen!=null&&seen.Contains("覆盖同名文件")&&seen.Contains("不可取消")&&seen.Contains("30 秒"),seen==null?"(no text)":seen.Replace("\r\n"," | "));
    Check("declined confirm keeps token (user may retry)",(string)G("uploadToken")=="tok-2","");

    // 确认框期间状态变了（熄灯）：确认后必须重新检查并拒绝
    S("confirmHook",new Func<string,bool>(t=>{nm.SetValue(lc,true);return true;}));
    Call("startUpload",D("token","tok-2","name","ccask.savi","fit","stretch","mode","movie"),out err);
    Check("state change during confirm -> refused after confirm",err!=null&&err.Contains("恢复灯光"),err);
    Check("...and upload did not start",!(bool)G("uploading"),"");
    nm.SetValue(lc,false);

    // 真正上传：覆盖屏幕上的 ccask.savi（非当前默认，与原图转换内容等价）
    bool done=false,doneOk=false;string doneMsg="";
    S("uploadDoneHook",new Action<bool,string,string>((ok,n,m)=>{done=true;doneOk=ok;doneMsg=m;}));
    S("confirmHook",new Func<string,bool>(t=>true));
    rc=Call("startUpload",D("token","tok-2","name","ccask.savi","fit","stretch","mode","movie"),out err);
    Check("confirmed -> started:true",err==null&&J(rc).Contains("\"started\":true"),err??J(rc));
    Check("uploading flag set on return",(bool)G("uploading"),"");
    Check("token is single-use (consumed)",(string)G("uploadToken")=="","");
    int maxPct=0,blocked=0,probes=0;var sw=System.Diagnostics.Stopwatch.StartNew();
    // 只有上传真的在进行时才连发写入；上传没启动就不要对真实设备乱写
    while(!done&&(bool)G("uploading")&&sw.Elapsed.TotalSeconds<120){
     probes++;
     Call("setBasic",D("brightness",3),out err);if(err=="上传进行中")blocked++;
     Call("playFile",D("name",curName),out err);if(err=="上传进行中")blocked++;
     Call("startUpload",D("token","tok-2","name","ccask.savi","fit","stretch","mode","movie"),out err);if(err=="上传进行中")blocked++;
     maxPct=Math.Max(maxPct,((Dev)G("dev")).UploadPercent);
     var t0=System.Diagnostics.Stopwatch.StartNew();while(t0.ElapsedMilliseconds<150){Application.DoEvents();System.Threading.Thread.Sleep(10);}
    }
    Check("uploadDone fired",done,"after "+sw.Elapsed.TotalSeconds.ToString("0.0")+"s");
    Check("upload succeeded",doneOk,doneMsg);
    Check("progress reached 100",maxPct>=100,"max="+maxPct);
    Check("every write attempt during upload was refused ("+probes+" rounds)",blocked==probes*3,"blocked="+blocked+" expected="+probes*3);
    Check("uploading cleared",!(bool)G("uploading"),"");
    var tmp=Call("listFiles",null,out err);Console.WriteLine("listFiles after = "+J(tmp));
    Check("ccask.savi on screen matches converted local file",J(tmp).Contains("\"name\":\"ccask.savi\",\"size\":"+new FileInfo(Path.Combine(Paths.Work,"out","ccask.savi")).Length),"local="+new FileInfo(Path.Combine(Paths.Work,"out","ccask.savi")).Length);
    Call("startUpload",D("token","tok-2","name","ccask.savi","fit","stretch","mode","movie"),out err);Check("token cannot be reused after upload",err!=null&&err.Contains("令牌"),err);
    S("confirmHook",null);S("uploadDoneHook",null);

    // ================= 阶段 3：其余页面 =================
    Console.WriteLine("---- phase 3 commands ----");
    nm.SetValue(lc,false);   // 临时按"未熄灯"测写路径；真实熄灯状态 realNight 在末尾还原
    var mo=Call("getModes",null,out err);Console.WriteLine("getModes = "+J(mo));
    Check("getModes shape",err==null&&J(mo).Contains("\"available\"")&&J(mo).Contains("GPU 温度")&&J(mo).Contains("\"selected\"")&&J(mo).Contains("canUseMovie"),"");
    foreach(var badSel in new object[]{new object[]{},new object[]{99},new object[]{-1},new object[]{"x"}}){Call("setModes",D("selected",badSel,"interval",10,"auto",true),out err);Check("setModes rejects selected="+J(badSel),err!=null,err);}
    Call("setModes",D("selected",new object[]{1},"interval",0,"auto",true),out err);Check("setModes rejects interval 0",err!=null&&err.Contains("1-120"),err);
    Call("setModes",D("selected",new object[]{1},"interval",121,"auto",true),out err);Check("setModes rejects interval 121",err!=null&&err.Contains("1-120"),err);
    Call("setModes",D("selected",new object[]{1},"interval",10,"auto","yes"),out err);Check("setModes rejects non-bool auto",err!=null,err);
    S("uploading",true);Call("setModes",D("selected",new object[]{1},"interval",10,"auto",true),out err);Check("setModes refused while uploading",err=="上传进行中",err);S("uploading",false);
    var cl=(CheckedListBox)G("clb");int cnt0=0;for(int i=0;i<cl.Items.Count;i++)if(cl.GetItemChecked(i))cnt0++;var numS=(NumericUpDown)G("numSec");var chkA=(CheckBox)G("chkAuto");
    var curSel=new List<object>();for(int i=0;i<cl.Items.Count;i++)if(cl.GetItemChecked(i))curSel.Add(i);
    Check("setModes accepted with current selection",Call("setModes",D("selected",curSel.ToArray(),"interval",(int)numS.Value,"auto",chkA.Checked),out err)!=null&&err==null,err);

    var tx=Call("getText",null,out err);Console.WriteLine("getText = "+J(tx));Check("getText shape",err==null&&J(tx).Contains("fontSize")&&J(tx).Contains("color"),"");
    Call("setText",D("text","你好","fontSize",0,"color","FF0000","show",false),out err);Check("setText rejects non-ASCII",err!=null&&err.Contains("ASCII"),err);
    Call("setText",D("text",new string('a',65),"fontSize",0,"color","FF0000","show",false),out err);Check("setText rejects >64 chars",err!=null&&err.Contains("64"),err);
    Call("setText",D("text","ok","fontSize",10,"color","FF0000","show",false),out err);Check("setText rejects fontSize 10",err!=null&&err.Contains("0-9"),err);
    Call("setText",D("text","ok","fontSize",0,"color","red","show",false),out err);Check("setText rejects bad color",err!=null&&err.Contains("RRGGBB"),err);
    Call("setText",D("text","ok","fontSize",0,"color","FF0000"),out err);Check("setText rejects missing show",err!=null,err);
    var cfgT=G("cfg");string tx0=(string)cfgT.GetType().GetField("Text").GetValue(cfgT);int fs0=(int)cfgT.GetType().GetField("FontSize").GetValue(cfgT);string tc0=(string)cfgT.GetType().GetField("TextColor").GetValue(cfgT);bool sh0=(bool)cfgT.GetType().GetField("ShowText").GetValue(cfgT);
    Check("setText accepted with current values",Call("setText",D("text",tx0??"","fontSize",fs0,"color",string.IsNullOrEmpty(tc0)?"FF0000":tc0,"show",sh0),out err)!=null&&err==null,err);

    var lv=Call("getLive",null,out err);Console.WriteLine("getLive = "+J(lv));Check("getLive shape",err==null&&J(lv).Contains("\"on\":false")&&J(lv).Contains("autostart"),"");
    Call("setLive",D("on","yes"),out err);Check("setLive rejects non-bool",err!=null,err);
    S("uploading",true);Call("setLive",D("on",true),out err);Check("setLive refused while uploading",err=="上传进行中",err);S("uploading",false);
    bool as0=J(lv).Contains("\"autostart\":true");
    Call("setAutostart",D("on",1),out err);Check("setAutostart rejects non-bool",err!=null,err);
    Check("setAutostart same value accepted (registry unchanged)",Call("setAutostart",D("on",as0),out err)!=null&&err==null,err);

    nm.SetValue(lc,realNight);   // setLinks 会保存配置文件（含熄灯标志），所以测试期间保持真实值
    var li=Call("getLinks",null,out err);Console.WriteLine("getLinks = "+J(li));Check("getLinks shape",err==null&&J(li).Contains("idleOffsetMs")&&J(li).Contains("sessions")&&J(li).Contains("statusText"),"");
    Call("setLinks",D("idleOffsetMs",6000),out err);Check("setLinks rejects offset 6000",err!=null&&err.Contains("5000"),err);
    Call("setLinks",D("idleRatePercent",94),out err);Check("setLinks rejects rate 94",err!=null&&err.Contains("95"),err);
    Call("setLinks",D("idleRatePercent","abc"),out err);Check("setLinks rejects non-numeric rate",err!=null,err);
    Call("setLinks",D("codex","yes"),out err);Check("setLinks rejects non-bool codex",err!=null&&err.Contains("布尔"),err);
    bool cx0=J(li).Contains("\"codex\":true");
    Call("setLinks",D("codex",!cx0),out err);li=Call("getLinks",null,out err);Check("setLinks toggles codex",J(li).Contains("\"codex\":"+(!cx0?"true":"false")),J(li));
    Call("setLinks",D("codex",cx0),out err);li=Call("getLinks",null,out err);Check("setLinks restores codex",J(li).Contains("\"codex\":"+(cx0?"true":"false")),"");
    Check("getLinks has dot matrix status",J(li).Contains("\"dotMatrixText\""),J(li));
    Call("setLinks",D("dotMatrix","yes"),out err);Check("setLinks rejects non-bool dotMatrix",err!=null&&err.Contains("布尔"),err);
    bool dm0=J(li).Contains("\"dotMatrix\":true");
    Call("setLinks",D("dotMatrix",!dm0),out err);li=Call("getLinks",null,out err);Check("setLinks toggles dotMatrix",J(li).Contains("\"dotMatrix\":"+(!dm0?"true":"false")),J(li));
    Call("setLinks",D("dotMatrix",dm0),out err);li=Call("getLinks",null,out err);Check("setLinks restores dotMatrix",J(li).Contains("\"dotMatrix\":"+(dm0?"true":"false")),"");
    nm.SetValue(lc,false);
    Call("linkDemo",D("state","exploding"),out err);Check("linkDemo rejects bad state",err!=null&&err.Contains("状态"),err);
    S("uploading",true);Call("linkDemo",D("state","working"),out err);Check("linkDemo refused while uploading",err=="上传进行中",err);Call("idleSync",null,out err);Check("idleSync refused while uploading",err=="上传进行中",err);Call("sendWidget",D("action","notify","seconds",5,"text","X"),out err);Check("sendWidget refused while uploading",err=="上传进行中",err);S("uploading",false);
    nm.SetValue(lc,true);Call("linkDemo",D("state","working"),out err);Check("linkDemo refused in night mode",err!=null&&err.Contains("恢复灯光"),err);Call("sendWidget",D("action","clear"),out err);Check("sendWidget refused in night mode",err!=null&&err.Contains("恢复灯光"),err);nm.SetValue(lc,false);
    Call("sendWidget",D("action","progress","percent",150,"title","x"),out err);Console.WriteLine("sendWidget percent=150 -> "+err);
    var wsx=Call("getWidgetStatus",null,out err);Check("getWidgetStatus shape",err==null&&J(wsx).Contains("active")&&J(wsx).Contains("sent"),J(wsx));
    var wr2=Call("sendWidget",D("action","notify","seconds",4,"text","PHASE3 OK"),out err);Check("sendWidget notify accepted",err==null&&J(wr2).Contains("message"),err??J(wr2));
    var wd=System.Diagnostics.Stopwatch.StartNew();while(wd.Elapsed.TotalSeconds<6&&string.IsNullOrEmpty((string)G("widgetSent"))){typeof(MainForm).GetMethod("TickWidgets",BF).Invoke(f,null);var t1=System.Diagnostics.Stopwatch.StartNew();while(t1.ElapsedMilliseconds<300){Application.DoEvents();System.Threading.Thread.Sleep(10);}}
    Check("widget frame reached screen",!string.IsNullOrEmpty((string)G("widgetSent"))&&(string)G("widgetError")=="","sent="+G("widgetSent"));
    wsx=Call("getWidgetStatus",null,out err);Check("getWidgetStatus active during notify",J(wsx).Contains("\"active\":true"),J(wsx));
    Call("sendWidget",D("action","clear"),out err);var wd2=System.Diagnostics.Stopwatch.StartNew();while(wd2.ElapsedMilliseconds<800){Application.DoEvents();System.Threading.Thread.Sleep(10);}
    Check("widget cleared and screen restored",!(bool)G("widgetOwnsScreen"),"err="+G("widgetError"));

    Call("setNight",D("on","maybe"),out err);Check("setNight rejects non-bool",err!=null,err);
    S("uploading",true);Call("setNight",D("on",true),out err);Check("setNight refused while uploading",err!=null&&err.Contains("上传"),err);S("uploading",false);
    var op=Call("readOpening",null,out err);Check("readOpening returns mode",err==null&&J(op).Contains("\"mode\":"),err??J(op));
    S("uploading",true);Call("readOpening",null,out err);Check("readOpening refused while uploading",err=="上传进行中",err);Call("previewOpening",null,out err);Check("previewOpening refused while uploading",err=="上传进行中",err);Call("openStorageManager",null,out err);Check("openStorageManager refused while uploading",err=="上传进行中",err);S("uploading",false);
    int asked=0;S("confirmHook",new Func<string,bool>(t=>{asked++;return false;}));
    var po=Call("previewOpening",null,out err);Check("previewOpening cancelled -> started:false (nothing written)",err==null&&J(po).Contains("\"started\":false")&&asked==1,err??J(po));
    var os=Call("openStorageManager",null,out err);Check("openStorageManager cancelled -> started:false (app NOT closed)",err==null&&J(os).Contains("\"started\":false")&&asked==2&&!f.IsDisposed&&!File.Exists(StorageOps.Flag),err??J(os));
    S("confirmHook",null);
    nm.SetValue(lc,realNight);   // 还原真实熄灯标志
    Call("nope2",null,out err);Check("unknown cmd still rejected",err!=null&&err.Contains("未知命令"),err);

    Console.WriteLine(fail==0?"ALL PASS":"FAILURES: "+fail);
    return fail==0?0:1;
   }catch(Exception e){Console.WriteLine("EXCEPTION "+e);return 3;}
   finally{try{S("reallyExit",true);f.Close();}catch{}}
  }
 }
}
