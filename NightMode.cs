using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace GpuLcd {
    partial class Dev {
        public int ReadScreenBrightness() {
            var c=CoreObj();var t=c.GetType().Assembly.GetType("iGameAPI.LCD.CSharp.LCD3Setting");
            object[] a={Activator.CreateInstance(t)};
            if(!(bool)c.GetType().GetMethod("LCD3_GetSetting",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(c,a))throw new IOException("读取屏幕亮度失败");
            return (int)t.GetProperty("Brightness").GetValue(a[0],null);
        }
        public void ApplyNightScreen(int opacity,int brightness,bool enabled,int dir) {
            ApplyBasic(opacity,brightness,enabled,dir);
            int expected=enabled?brightness:0;
            System.Threading.Thread.Sleep(150);
            if(ReadScreenBrightness()==expected)return;
            // The SDK discards I2C write failures. Retry the same settings packet over USB.
            var c=CoreObj();var flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            var t=c.GetType().Assembly.GetType("iGameAPI.LCD.CSharp.LCD3Setting");var setting=Activator.CreateInstance(t);
            t.GetProperty("Brightness").SetValue(setting,expected,null);t.GetProperty("BackgroundOpacity").SetValue(setting,opacity,null);
            var payload=(byte[])c.GetType().GetMethod("CreateSettingData",flags).Invoke(c,new object[]{setting});
            var packet=(byte[])c.GetType().GetMethod("CreateOtherData",flags).Invoke(c,new object[]{57630,payload});
            lock(c) {
                if(!(bool)c.GetType().GetMethod("WriteDataByCOM",flags).Invoke(c,new object[]{packet}))throw new IOException("USB 亮度指令发送失败");
            }
            System.Threading.Thread.Sleep(200);
            int actual=ReadScreenBrightness();
            if(actual!=expected)throw new IOException("屏幕亮度未生效：期望 "+expected+"，读回 "+actual);
        }
    }
    partial class MainForm {
        readonly Button nightButton=new Button();
        readonly Label nightLabel=new Label();
        ToolStripMenuItem nightMenu;
        bool signalWakePending=true;
        string nightScreenResult="";
        void AddNightControls(TableLayoutPanel grid) {
            nightButton.Size=new Size(330,40);nightButton.MinimumSize=new Size(330,40);nightButton.Margin=new Padding(0,0,0,8);
            nightButton.Click+=(s,e)=>Log(SetNightMode(!linkCfg.NightMode));grid.Controls.Add(nightButton,0,0);grid.SetColumnSpan(nightButton,3);
            nightLabel.AutoSize=true;nightLabel.Dock=DockStyle.Fill;nightLabel.Margin=new Padding(0,0,0,8);
            grid.Controls.Add(nightLabel,0,1);grid.SetColumnSpan(nightLabel,3);
        }
        void UpdateNightUi() {
            string title=linkCfg.NightMode?"恢复灯光和屏幕":"一键关闭灯光和屏幕";
            nightButton.Text=title;if(nightMenu!=null)nightMenu.Text=title;
            nightLabel.Text=linkCfg.NightMode?"睡眠熄灯已开启；任务通知不会点亮设备。\r\n"+nightScreenResult:"睡觉时一键熄灯，再次点击恢复原设置。";
        }
        string SetNightMode(bool on) {
            if(uploading || !btnUpload.Enabled)return "ERR 上传进行中，请上传结束后再切换熄灯";
            if(File.Exists(StorageOps.Flag))return "ERR 屏幕处于存储维护模式";
            linkCfg.NightMode=on;SaveLinks();demoState="";demoUntil=DateTime.MinValue;
            debounce.Stop();revertTimer.Stop();liveTimer.Stop();
            signalApplied="";if(!on)signalWakePending=true;
            string error="";
            try {
                if(connected){
                    bool running=dev.Started;dev.Stop();if(running)System.Threading.Thread.Sleep(1200);
                    dev.ApplyNightScreen(cfg.Opa,cfg.Bri,on?false:cfg.En,cfg.Dir);
                    nightScreenResult=on?"屏幕关闭指令已发送。":"";
                }else nightScreenResult="屏幕未连接；仍尝试切换 SignalRGB 灯光。";
            }catch(Exception ex){error=ex.Message;nightScreenResult="屏幕操作失败："+error;}
            UpdateNightUi();
            if(!on && connected){
                playing="";widgetSent="";linkApplied="restore";lastLcdTry=DateTime.MinValue;
                TickWidgets();
                if(cfg.Live && !widgetOwnsScreen)try{dev.Start();liveTimer.Start();}catch(Exception ex){error=ex.Message;Log(error);}
            }
            TickLinks();
            return (error==""?"OK ":"ERR ")+(on?"睡眠熄灯已开启":"已恢复灯光和屏幕")+(error==""?"":"；"+error);
        }
        string NightStatus() {
            object hardware=null;
            if(connected && !File.Exists(StorageOps.Flag)) {
                hardware=Quiet(()=>{
                    var c=StorageOps.Core(dev);var t=c.GetType().Assembly.GetType("iGameAPI.LCD.CSharp.LCD3Setting");
                    object[] a={Activator.CreateInstance(t)};
                    bool ok=(bool)c.GetType().GetMethod("LCD3_GetSetting",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(c,a);
                    if(!ok)return (object)new {read=false};
                    var fields=new Dictionary<string,object>();
                    foreach(var p in t.GetProperties())if(p.CanRead && p.GetIndexParameters().Length==0 && (p.PropertyType.IsPrimitive || p.PropertyType==typeof(string)))fields[p.Name]=p.GetValue(a[0],null);
                    return (object)fields;
                });
            }
            return "OK "+new JavaScriptSerializer().Serialize(new {night=linkCfg.NightMode,screen=hardware,signalEvent=signalApplied,normalEnabled=cfg.En,normalBrightness=cfg.Bri});
        }
    }
}
