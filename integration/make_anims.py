from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import math
out=Path(__file__).parent/'anims';out.mkdir(exist_ok=True)
S=2;W,H=480,128;bg=(13,19,29)
font=ImageFont.truetype('C:/Windows/Fonts/msyhbd.ttc',30*S)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',15*S)
for state,title,sub,col in [('work','Codex 正在工作','处理任务中',(76,169,245)),('ask','Codex 等待操作','请查看任务窗口',(242,178,66)),('done','Codex 本轮结束','请查看结果与待办',(93,207,153))]:
 frames=[]
 for f in range(24):
  im=Image.new('RGB',(W*S,H*S),bg);d=ImageDraw.Draw(im);k=.55+.45*math.sin(f/24*math.tau)**2
  c=tuple(int(x*k) for x in col)
  d.rounded_rectangle((6*S,6*S,473*S,121*S),radius=12*S,outline=c,width=2*S)
  if state=='work':
   d.arc((24*S,24*S,102*S,102*S),start=f*15,end=f*15+260,fill=col,width=5*S)
   d.line([(51*S,51*S),(40*S,64*S),(51*S,77*S)],fill=col,width=3*S)
   d.line([(75*S,51*S),(86*S,64*S),(75*S,77*S)],fill=col,width=3*S)
  elif state=='ask':
   d.rounded_rectangle((48*S,30*S,60*S,72*S),radius=4*S,fill=c);d.ellipse((48*S,82*S,60*S,94*S),fill=c)
  else:
   d.line([(28*S,65*S),(51*S,85*S),(91*S,42*S)],fill=col,width=7*S)
  d.text((125*S,25*S),title,font=font,fill=(233,239,246));d.text((127*S,77*S),sub,font=small,fill=(153,170,191))
  frames.append(im.resize((W,H),Image.Resampling.LANCZOS))
 path=out/('cx'+state+'.gif');frames[0].save(path,save_all=True,append_images=frames[1:],duration=80,loop=0,optimize=False,disposal=2)
 print(path)
frames[0].save(out/'preview.png')
