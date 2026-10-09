using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace GpuLcd {
 sealed class SignalReadClient : IDisposable {
  Process process; int nextId;
  readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=1024*1024};
  void Send(object data){process.StandardInput.WriteLine(json.Serialize(data));process.StandardInput.Flush();}
  Dictionary<string,object> Request(string method,object parameters){
   int id=++nextId;Send(new {jsonrpc="2.0",id=id,method=method,@params=parameters});
   var end=DateTime.UtcNow.AddSeconds(3);
   while(DateTime.UtcNow<end){
    var line=process.StandardOutput.ReadLineAsync();
    if(!line.Wait(Math.Max(1,(int)(end-DateTime.UtcNow).TotalMilliseconds)))throw new TimeoutException("SignalRGB MCP 超时");
    if(line.Result==null)throw new IOException("SignalRGB MCP 已退出");
    var result=json.DeserializeObject(line.Result) as Dictionary<string,object>;
    if(result!=null && result.ContainsKey("id") && Convert.ToInt32(result["id"])==id)return result;
   }
   throw new TimeoutException("SignalRGB MCP 超时");
  }
  void Connect(){
   if(process!=null && !process.HasExited)return;
   var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VortxEngine");
   var exe=Directory.GetDirectories(root,"app-*").OrderByDescending(Directory.GetLastWriteTimeUtc)
    .Select(d=>Path.Combine(d,"Signal-x64","SignalRgbMcp.exe")).FirstOrDefault(File.Exists);
   if(exe==null)throw new FileNotFoundException("找不到 SignalRGB MCP");
   process=Process.Start(new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8});
   process.ErrorDataReceived+=(s,e)=>{};process.BeginErrorReadLine();
   Request("initialize",new{protocolVersion="2024-11-05",capabilities=new{},clientInfo=new{name="GpuLcdTool",version="1"}});
   Send(new{jsonrpc="2.0",method="notifications/initialized"});
  }
  public string Read(string name,object arguments){
   try{
    Connect();var response=Request("tools/call",new{name=name,arguments=arguments});
    object value;if(!response.TryGetValue("result",out value))throw new IOException("MCP 返回错误");
    var result=value as Dictionary<string,object>;
    if(result==null || !result.TryGetValue("content",out value))return "";
    var list=value as object[];if(list==null)return "";
    return string.Join("\n",list.Select(x=>x as Dictionary<string,object>).Where(x=>x!=null && x.ContainsKey("text")).Select(x=>Convert.ToString(x["text"])));
   }catch{Dispose();throw;}
  }
  public void Dispose(){var p=process;process=null;if(p!=null){try{if(!p.HasExited)p.Kill();}catch{}try{p.Dispose();}catch{}}}
 }
 partial class MainForm {
  // Compatibility name retained; no HTTP listener is opened.
  SignalReadClient signalBridge;
  string sharedState="idle"; bool signalOn;
  DateTime signalAckTime,lastSignalRead;
  string signalAckState="",lastCanvasMarker="",lastSignalPreview="Auto";
  bool signalReadBusy;
  void StartSignalBridge(){
   if(!linkCfg.Signal || signalReadBusy || (DateTime.UtcNow-lastSignalRead).TotalSeconds<3)return;
   if(signalBridge==null)signalBridge=new SignalReadClient();
   var reader=signalBridge;signalReadBusy=true;lastSignalRead=DateTime.UtcNow;
   Task.Run(()=>{
    string status="",settings="",logs="",error="";
    try{
     status=reader.Read("read_status",new{});
     if(status.Contains("Current effect: CodexLCD,")){
      settings=reader.Read("read_effectsettings",new{});
      logs=reader.Read("debug_canvaslogs",new{limit=12});
     }
    }catch(Exception ex){error=ex.Message;}
    try{BeginInvoke(new Action(()=>{
     signalReadBusy=false;if(!linkCfg.Signal)return;
     if(error!=""){signalStatus.Text="SignalRGB：MCP "+error;return;}
     if(!status.Contains("Current effect: CodexLCD,")){signalStatus.Text="SignalRGB：请选中 CodexLCD 灯效";return;}
     var match=System.Text.RegularExpressions.Regex.Match(settings,@"property: previewState,.*?value: ([A-Za-z]+),");
     if(match.Success && match.Groups[1].Value!=lastSignalPreview){
      lastSignalPreview=match.Groups[1].Value;
      string preview=lastSignalPreview.ToLowerInvariant();
      if(preview=="working" || preview=="waiting" || preview=="done" || preview=="idle"){
       Log("SignalRGB 反向预览："+preview);demoState=preview;demoUntil=DateTime.UtcNow.AddSeconds(8);TickLinks();
      }
     }
     var markers=System.Text.RegularExpressions.Regex.Matches(logs,@"GpuLcdState=(idle|working|waiting|done);seq=\d+");
     if(markers.Count>0){
      var marker=markers[markers.Count-1];
      if(marker.Value!=lastCanvasMarker){lastCanvasMarker=marker.Value;signalAckState=marker.Groups[1].Value;signalAckTime=DateTime.UtcNow;}
      signalStatus.Text="SignalRGB：灯效脚本已确认 "+signalAckState;
     }
    }));}catch{}
   });
  }
 }
}

