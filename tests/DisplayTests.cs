using System;
namespace GpuLcd{
 static class DisplayTests{
  static int n;
  static void Check(bool ok){if(!ok)throw new Exception("Assertion "+n);n++;}
  static void Reject(Action f){try{f();}catch(ArgumentException){n++;return;}throw new Exception("Expected validation failure");}
  static void Main(){
   var t=DateTime.UtcNow;var s=new DisplayJobs();
   Check(s.Current(t)==null);
   s.Apply(new DisplayRequest{action="progress",title="Build",percent=45},t);
   Check(s.Current(t).Text.Contains("45%"));
   s.Apply(new DisplayRequest{action="notify",text="Ready",seconds=3},t);
   Check(s.Current(t).Kind=="notify");
   s.Apply(new DisplayRequest{action="progress",title="Build",percent=90},t.AddSeconds(1));
   Check(s.Current(t.AddSeconds(1)).Text=="Ready");
   Check(s.Current(t.AddSeconds(3)).Text.Contains("90%"));
   Reject(()=>s.Apply(new DisplayRequest{action="notify",text="中文"},t));
   Check(s.Current(t.AddSeconds(4)).Kind=="progress");
   Reject(()=>DisplayJobs.Frame(new DisplayRequest{action="progress",percent=101}));
   Reject(()=>DisplayJobs.Frame(new DisplayRequest{action="notify",text="x",seconds=0}));
   Reject(()=>DisplayJobs.Frame(new DisplayRequest{action="notify",text=new string('a',37)}));
   Reject(()=>DisplayJobs.Frame(new DisplayRequest{action="notify",text="a\nb\nc\nd"}));
   Reject(()=>DisplayJobs.Frame(new DisplayRequest{action="notify",text="a",color="bad"}));
   Check(s.Current(t.AddMinutes(31))==null);
   s.Apply(new DisplayRequest{action="progress",percent=100},t);
   s.Apply(new DisplayRequest{action="notify",text="Done"},t);
   s.Apply(new DisplayRequest{action="clear-progress"},t);Check(s.Current(t).Kind=="notify");
   s.Apply(new DisplayRequest{action="clear-notice"},t);Check(s.Current(t)==null);
   Console.WriteLine(n+" display validation, expiry, and restoration assertions passed.");
  }
 }
}
