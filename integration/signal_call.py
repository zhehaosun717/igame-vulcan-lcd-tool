import pathlib,json,subprocess,threading,queue,sys,time
root=pathlib.Path(__file__).parent
versions=sorted((pathlib.Path.home()/'AppData/Local/VortxEngine').glob('app-*/Signal-x64/SignalRgbMcp.exe'),key=lambda p:p.stat().st_mtime,reverse=True)
p=subprocess.Popen([str(versions[0])],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,encoding='utf-8',creationflags=0x08000000)
q=queue.Queue()
def reader():
 for line in p.stdout:
  try:q.put(json.loads(line))
  except ValueError:pass
threading.Thread(target=reader,daemon=True).start()
def send(v):p.stdin.write(json.dumps(v)+'\n');p.stdin.flush()
def req(i,m,params):
 send({'jsonrpc':'2.0','id':i,'method':m,'params':params});end=time.monotonic()+8
 while time.monotonic()<end:
  try:v=q.get(timeout=.2)
  except queue.Empty:continue
  if v.get('id')==i:return v
 return {'timeout':m}
try:
 req(1,'initialize',{'protocolVersion':'2024-11-05','capabilities':{},'clientInfo':{'name':'GpuLcdTest','version':'1'}})
 send({'jsonrpc':'2.0','method':'notifications/initialized'})
 args=json.loads(sys.argv[2]) if len(sys.argv)>2 else {}
 v=req(2,'tools/call',{'name':sys.argv[1],'arguments':args})
 (root/('signal-'+sys.argv[1]+'.json')).write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(v,ensure_ascii=False)[:14000])
finally:
 p.terminate()
 try:p.wait(timeout=2)
 except subprocess.TimeoutExpired:p.kill()
