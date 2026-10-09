using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using iGameAPI.LCD.CSharp;
namespace GpuLcd {
 partial class Dev {
  public bool DrawNative(DisplayFrame f){
   var m=new LCD3CustomTextModel{
    ColorMode=0,ColorTimePeriod=0,Font=f.Font,
    FontColor=new iGameAPI.Contracts.LCD.Color{
     R=Convert.ToByte(f.Color.Substring(0,2),16),G=Convert.ToByte(f.Color.Substring(2,2),16),B=Convert.ToByte(f.Color.Substring(4,2),16)},
    MotionMode=0,MotionSpeed=0,X=f.X,Y=f.Y,
    TextLine1="",TextLine2="",TextLine3=""};
   var lines=f.Text.Split('\n');m.TextLine1=lines[0];
   if(lines.Length>1)m.TextLine2=lines[1];if(lines.Length>2)m.TextLine3=lines[2];
   var c=CoreObj();
   return (bool)c.GetType().GetMethod("LCD3_PlayCustomText",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(c,new object[]{m});
  }
  public bool DrawSavedText(LCD3CustomTextModel m){
   var c=CoreObj();
   return (bool)c.GetType().GetMethod("LCD3_PlayCustomText",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(c,new object[]{m});
  }
 }
 partial class MainForm {
  readonly DisplayJobs displayJobs=new DisplayJobs();
  readonly System.Windows.Forms.Timer widgetTimer=new System.Windows.Forms.Timer{Interval=500};
  bool widgetOwnsScreen,widgetWasLive,widgetSavedShow;
  string widgetSavedMovie="",widgetSent="",widgetError="";
  LCD3CustomTextModel widgetSavedText;
  DateTime widgetLastSend;
  Label widgetStatus=new Label();
  PictureBox widgetPreview=new PictureBox();
  DisplayFrame widgetPreviewFrame;
  TabPage TabWidgets(){
   var tab=new TabPage("进度 / 通知"){AutoScroll=true};
   var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16)};
   flow.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(510,0),Text="原生文字显示：英文、数字和常用符号。\r\n通知到期后恢复进度或原画面；进度 30 分钟未更新会自动结束。\r\n最多每两秒发一次变化，不上传文件。",Margin=new Padding(0,0,0,12)});
   var title=new TextBox{Text="Download",Width=200,MaxLength=36};
   var pct=new NumericUpDown{Margin=new Padding(3),Minimum=0,Maximum=100,Value=45,Width=70};
   var progressRow=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MaximumSize=new Size(510,0)};
   progressRow.Controls.Add(new Label{Text="进度标题",AutoSize=true,Padding=new Padding(0,6,0,0)});
   progressRow.Controls.Add(title);progressRow.Controls.Add(pct);progressRow.Controls.Add(new Label{Text="%",AutoSize=true,Padding=new Padding(0,6,0,0)});
   flow.Controls.Add(progressRow);
   var note=new TextBox{Multiline=true,Text="Download complete!\r\nReady to use.",Width=480,Height=48,MaxLength=110,ScrollBars=ScrollBars.Vertical};
   flow.Controls.Add(new Label{Text="通知文字（最多三行）",AutoSize=true,Margin=new Padding(0,12,0,4)});
   flow.Controls.Add(note);
   var seconds=new NumericUpDown{Margin=new Padding(3),Minimum=3,Maximum=300,Value=10,Width=65};
   var x=new NumericUpDown{Margin=new Padding(3),Minimum=0,Maximum=479,Value=12,Width=65};
   var y=new NumericUpDown{Margin=new Padding(3),Minimum=0,Maximum=127,Value=8,Width=65};
   var color=Color.FromArgb(76,169,245);
   var options=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MaximumSize=new Size(510,0)};
   options.Controls.Add(new Label{Text="通知秒数",AutoSize=true});options.Controls.Add(seconds);
   options.Controls.Add(new Label{Text="X",AutoSize=true});options.Controls.Add(x);
   options.Controls.Add(new Label{Text="Y",AutoSize=true});options.Controls.Add(y);
   var colorBtn=new Button{Text="文字颜色",AutoSize=true};
   colorBtn.Click+=(s,e)=>{using(var c=new ColorDialog{Color=color}){if(c.ShowDialog()==DialogResult.OK){color=c.Color;colorBtn.ForeColor=color;}}};
   options.Controls.Add(colorBtn);flow.Controls.Add(options);
   var buttons=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MaximumSize=new Size(510,0)};
   Action<string> send=action=>{
    var request=new DisplayRequest{action=action,title=title.Text,percent=(int)pct.Value,text=note.Text,seconds=(int)seconds.Value,x=(int)x.Value,y=(int)y.Value,color=color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2")};
    string result=WidgetCommand(new JavaScriptSerializer().Serialize(request));if(result.StartsWith("ERR"))Log(result);
   };
   string[] labels={"显示 / 更新进度","显示通知","停止进度","清除通知","恢复原画面"};
   string[] actions={"progress","notify","clear-progress","clear-notice","clear"};
   for(int i=0;i<labels.Length;i++){var a=actions[i];var b=new Button{Text=labels[i],AutoSize=true,MinimumSize=new Size(125,34)};b.Click+=(s,e)=>send(a);buttons.Controls.Add(b);}
   flow.Controls.Add(buttons);
   widgetStatus.AutoSize=true;widgetStatus.MaximumSize=new Size(500,0);widgetStatus.Text="未显示临时文字";
   flow.Controls.Add(widgetStatus);
   flow.Controls.Add(new Label{Text="布局示意（实际字体以屏幕为准）",AutoSize=true,Margin=new Padding(0,10,0,2)});
   widgetPreview.Size=new Size(480,128);widgetPreview.BackColor=Color.FromArgb(13,19,29);
   widgetPreview.Paint+=(s,e)=>{
    var f=widgetPreviewFrame;if(f==null)return;
    using(var font=new Font("Consolas",14,FontStyle.Regular,GraphicsUnit.Pixel))
    using(var brush=new SolidBrush(ColorTranslator.FromHtml("#"+f.Color)))e.Graphics.DrawString(f.Text,font,brush,new PointF(f.X,f.Y));
   };
   flow.Controls.Add(widgetPreview);
   flow.Controls.Add(new Label{Text="也可由脚本发送真实进度。示例见 integration/WIDGETS.md。\r\n助手事件继续接收，临时文字结束后恢复最新助手状态。",AutoSize=true,MaximumSize=new Size(500,0),ForeColor=Color.Gray,Margin=new Padding(0,10,0,0)});
   tab.Controls.Add(flow);return tab;
  }
  void StartWidgets(){widgetTimer.Tick+=(s,e)=>TickWidgets();widgetTimer.Start();}
  string WidgetCommand(string json){
   try{
    if(File.Exists(StorageOps.Flag))return "ERR 屏幕处于存储维护模式";
    if(!connected)return "ERR 屏幕未连接";
    if(json.Length>2048)return "ERR 请求过长";
    var r=new JavaScriptSerializer().Deserialize<DisplayRequest>(json);
    if(r==null)return "ERR 请求为空";
    displayJobs.Apply(r,DateTime.UtcNow);TickWidgets();
    return "OK "+(displayJobs.Current(DateTime.UtcNow)==null?"已清除临时显示":"已接收，最多两秒内更新");
   }catch(Exception ex){return "ERR "+ex.Message;}
  }
  void TickWidgets(){
   if(linkCfg.NightMode)return;
   var frame=displayJobs.Current(DateTime.UtcNow);
   widgetPreviewFrame=frame;widgetPreview.Invalidate();
   if(File.Exists(StorageOps.Flag) || !connected){
    displayJobs.Apply(new DisplayRequest{action="clear"},DateTime.UtcNow);widgetOwnsScreen=false;return;
   }
   if(frame==null){if(widgetOwnsScreen)RestoreWidget();return;}
   if(!widgetOwnsScreen){
    widgetSavedMovie=playing;widgetSavedText=(LCD3CustomTextModel)dev.L3.GetType().GetProperty("CustomTextModel").GetValue(dev.L3,null);
    widgetSavedShow=dev.L3.IsDisplayCustomText;widgetWasLive=dev.Started;
    liveTimer.Stop();dev.Stop();if(widgetWasLive)System.Threading.Thread.Sleep(1200);revertTimer.Stop();widgetOwnsScreen=true;widgetSent="";
   }
   if(frame.Signature==widgetSent)return;
   if((DateTime.UtcNow-widgetLastSend).TotalSeconds<2)return;
   widgetLastSend=DateTime.UtcNow;
   try{
    if(!dev.DrawNative(frame))throw new IOException("厂家库未接受文字指令");
    widgetSent=frame.Signature;widgetError="";playing="";
    widgetStatus.Text=(frame.Kind=="progress"?"进度":"通知")+"：文字指令已发送";
   }catch(Exception ex){widgetError=ex.Message;widgetStatus.Text="显示失败："+widgetError;}
  }
  void RestoreWidget(){
   widgetOwnsScreen=false;widgetSent="";widgetPreviewFrame=null;widgetPreview.Invalidate();
   if(linkCfg.NightMode || File.Exists(StorageOps.Flag) || !connected)return;
   try{
    string state=linkState.Current(DateTime.UtcNow);
    if(state!="idle"){
     linkApplied="restore";lastLcdTry=DateTime.MinValue;TickLinks();
    }else if(widgetSavedShow){
     if(!dev.DrawSavedText(widgetSavedText))throw new IOException("恢复原文字失败");
    }else{
     string movie=string.IsNullOrEmpty(widgetSavedMovie)?cfg.Current:widgetSavedMovie;
     playing="";if(!string.IsNullOrEmpty(movie)){string result=PlayExternal(movie,0);if(!result.StartsWith("OK"))throw new IOException(result);}
     linkApplied="idle";
    }
    if(widgetWasLive){dev.Start();liveTimer.Start();}
    widgetStatus.Text="已恢复原画面 / 当前助手状态";widgetError="";
   }catch(Exception ex){widgetError=ex.Message;widgetStatus.Text="恢复失败："+widgetError;Log(widgetStatus.Text);}
  }
  void StopWidgets(){widgetTimer.Stop();displayJobs.Apply(new DisplayRequest{action="clear"},DateTime.UtcNow);if(widgetOwnsScreen)RestoreWidget();}
 }
}



