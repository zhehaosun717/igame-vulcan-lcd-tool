using System;
using System.Linq;
using System.Text.RegularExpressions;
namespace GpuLcd {
 public sealed class DisplayRequest {
  public string action="",title="",text="",color="4CA9F5";
  public int percent=0,seconds=10,x=12,y=8,font=2;
 }
 public sealed class DisplayFrame {
  public string Kind,Text,Color; public int X,Y,Font;
  public string Signature {get{return Kind+"|"+Text+"|"+Color+"|"+X+"|"+Y+"|"+Font;}}
 }
 public sealed class DisplayJobs {
  DisplayFrame progress,notice; DateTime progressUntil,noticeUntil;
  public static DisplayFrame Frame(DisplayRequest r){
   if(r.action!="progress" && r.action!="notify")throw new ArgumentException("未知显示操作");
   if(r.x<0 || r.x>479 || r.y<0 || r.y>127 || r.font<0 || r.font>2)throw new ArgumentException("位置或字体超出范围");
   if(!Regex.IsMatch(r.color??"", "^[0-9a-fA-F]{6}$"))throw new ArgumentException("颜色必须是六位十六进制");
   string text;
   if(r.action=="progress"){
    if(r.percent<0 || r.percent>100)throw new ArgumentException("进度必须在 0–100 之间");
    string title=string.IsNullOrWhiteSpace(r.title)?"Progress":r.title;
    if(title.Contains("\n") || title.Contains("\r"))throw new ArgumentException("进度标题只能有一行");
    int n=r.percent/5;
    text=title+"\n["+new string('#',n)+new string('-',20-n)+"] "+r.percent+"%";
   }else{
    if(r.seconds<3 || r.seconds>300)throw new ArgumentException("通知时间必须在 3–300 秒之间");
    text=(r.text??"").Replace("\r\n","\n");
   }
   if(string.IsNullOrWhiteSpace(text))throw new ArgumentException("文字不能为空");
   if(text.Any(c=>c!='\n' && (c<32 || c>126)))throw new ArgumentException("原生接口仅支持英文、数字和 ASCII 符号；不支持中文");
   var lines=text.Split('\n');
   if(lines.Length>3 || lines.Any(x=>x.Length>36))throw new ArgumentException("最多三行，每行最多 36 个字符");
   return new DisplayFrame{Kind=r.action,Text=text,Color=r.color.ToUpperInvariant(),X=r.x,Y=r.y,Font=r.font};
  }
  public void Apply(DisplayRequest r,DateTime now){
   if(r.action=="clear"){progress=notice=null;return;}
   if(r.action=="clear-progress"){progress=null;return;}
   if(r.action=="clear-notice"){notice=null;return;}
   var f=Frame(r); // Validate before mutating an existing display.
   if(r.action=="progress"){progress=f;progressUntil=now.AddMinutes(30);}
   else{notice=f;noticeUntil=now.AddSeconds(r.seconds);}
  }
  public DisplayFrame Current(DateTime now){
   if(notice!=null && now>=noticeUntil)notice=null;
   if(progress!=null && now>=progressUntil)progress=null;
   return notice??progress;
  }
 }
}
