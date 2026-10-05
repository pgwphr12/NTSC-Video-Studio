"""Check a whole-effect upscale against a low-resolution reference, after encoding."""
import os, pathlib, subprocess, tempfile, uuid, shutil, json
app=pathlib.Path(__file__).resolve().parent.parent
parent=pathlib.Path(os.environ.get('NTSC_TEST_ROOT',tempfile.gettempdir())).resolve()
work=parent/('ntsc-whole-test-'+uuid.uuid4().hex);work.mkdir(mode=0o777)
def run(args):
    p=subprocess.run([str(a) for a in args],capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW,timeout=90)
    assert p.returncode==0,(args,p.stderr.decode(errors='replace'))
    return p.stdout
low=work/'low.mkv';high=work/'high.mkv'
run([app/'assets'/'ffmpeg.exe','-v','error','-y','-f','lavfi','-i','color=c=0x808080:s=512x288:r=60:d=0.1,drawbox=x=256:y=0:w=256:h=288:color=0xC0C0C0:t=fill','-c:v','ffv1','-threads','1',low])
run([app/'assets'/'ffmpeg.exe','-v','error','-y','-threads','1','-filter_threads','1','-i',low,'-vf','scale=1920:1080:flags=neighbor','-c:v','ffv1','-threads','1',high])
outputs=[]
for source,width in [(low,512),(high,1920)]:
    out=work/('filtered-'+str(width)+'.mp4');outputs.append(out)
    run([app/'NTSC Video Studio.exe','--convert',source,out,width])
    video=json.loads(run([app/'assets'/'ffprobe.exe','-v','error','-show_streams','-of','json',out]))['streams'][0]
    assert video['width']==width and video['avg_frame_rate']=='60/1'
def pixels(out):return run([app/'assets'/'ffmpeg.exe','-v','error','-threads','1','-filter_threads','1','-i',out,'-vf','scale=512:288:flags=lanczos','-frames:v','3','-f','rawvideo','-pix_fmt','rgb24','pipe:1'])
lo,hi=map(pixels,outputs);size=512*288*3
mean=sum(abs(a-b) for a,b in zip(lo[:size],hi[:size]))/size
assert mean<2.0,mean
adjacent=sum(abs(a-b) for a,b in zip(hi[:size],hi[size:2*size]))/size
repeat=sum(abs(a-b) for a,b in zip(hi[:size],hi[2*size:3*size]))/size
colors=[max(hi[i:i+3])-min(hi[i:i+3]) for i in range(0,size,3)]
assert max(colors)>45 and adjacent>max(.15,repeat*3),(max(colors),adjacent,repeat)
result=f'Whole effect: low 512x288 vs HD 1920x1080 normalized RGB difference {mean:.3f}; HD rainbow peak chroma {max(colors)}, alternating difference {adjacent:.3f}, repeat {repeat:.3f}: PASS'
with (app/'VALIDATION.txt').open('a',encoding='utf-8') as f:f.write(result+'\n')
print(result)
demo=os.environ.get('NTSC_DEMO_OUTPUT')
if demo:
    run([app/'assets'/'ffmpeg.exe','-v','error','-y','-stream_loop','19','-i',outputs[1],'-t','2','-vf','crop=768:768:576:128,scale=768:768:flags=neighbor','-c:v','libx264','-threads','1','-crf','14',demo])
assert work.resolve().parent==parent and work.name.startswith('ntsc-whole-test-')
shutil.rmtree(work)
