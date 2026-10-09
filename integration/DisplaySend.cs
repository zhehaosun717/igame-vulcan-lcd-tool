using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Web.Script.Serialization;
namespace GpuLcd {
 static class DisplaySend {
  static int Main(string[] args){
   try{
    if(args.Length==0)throw new ArgumentException("progress <0-100> <title> | notify <seconds> <text> | clear | status | json");
    string line;
    if(args[0]=="status")line="widget-status";
    else if(args[0]=="links")line="link-status";
    else{
     DisplayRequest r;
     if(args[0]=="json"){
      string json=Console.In.ReadToEnd();if(json.Length>2048)throw new ArgumentException("JSON too long");
      r=new JavaScriptSerializer().Deserialize<DisplayRequest>(json);
     }else if(args[0]=="progress" && args.Length>=3)r=new DisplayRequest{action="progress",percent=int.Parse(args[1]),title=string.Join(" ",args,2,args.Length-2)};
     else if(args[0]=="notify" && args.Length>=3)r=new DisplayRequest{action="notify",seconds=int.Parse(args[1]),text=string.Join(" ",args,2,args.Length-2)};
     else if(args[0]=="clear" || args[0]=="clear-progress" || args[0]=="clear-notice")r=new DisplayRequest{action=args[0]};
     else throw new ArgumentException("Invalid arguments");
     if(r==null)throw new ArgumentException("Missing request");
     if(r.action=="progress" || r.action=="notify")DisplayJobs.Frame(r);
     line="widget "+new JavaScriptSerializer().Serialize(r);
    }
    using(var p=new NamedPipeClientStream(".","GpuLcdTool",PipeDirection.InOut,PipeOptions.Asynchronous)){
     p.Connect(1000);
     var w=new StreamWriter(p,new UTF8Encoding(false)){AutoFlush=true};
     var rd=new StreamReader(p,Encoding.UTF8);w.WriteLine(line);
     var task=rd.ReadLineAsync();if(!task.Wait(4000))throw new TimeoutException("LCD tool did not reply.");
     string result=task.Result??"ERR empty reply";Console.OutputEncoding=Encoding.UTF8;Console.WriteLine(result);
     return result.StartsWith("OK")?0:1;
    }
   }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
  }
 }
}
