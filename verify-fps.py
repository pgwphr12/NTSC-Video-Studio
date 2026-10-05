"""Additional VFR frame-count and low-FPS GPU flicker checks."""
import json, os, pathlib, shutil, subprocess, tempfile, uuid
app=pathlib.Path(__file__).resolve().parent.parent
parent=pathlib.Path(os.environ.get('NTSC_TEST_ROOT',tempfile.gettempdir())).resolve()
work=parent/('ntsc-fps-test-'+uuid.uuid4().hex);work.mkdir(mode=0o777)
def run(args):
    p=subprocess.run([str(a) for a in args],capture_output=True,timeout=90,creationflags=subprocess.CREATE_NO_WINDOW)
    assert p.returncode==0,(args,p.stderr.decode(errors='replace'))
    return p.stdout
def video(path):
    return json.loads(run([app/'assets/ffprobe.exe','-v','error','-select_streams','v:0','-show_streams','-of','json',path]))['streams'][0]
ff=app/'assets/ffmpeg.exe';exe=app/'NTSC Video Studio.exe'
source=work/'variable.mp4';out=work/'variable-filtered.mp4'
run([ff,'-v','error','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=2','-vf',r"select=not(eq(mod(n\,5)\,1))",'-fps_mode','vfr','-c:v','libx264','-threads','1','-preset','ultrafast',source])
original=video(source)
assert original['r_frame_rate']!=original['avg_frame_rate']
run([exe,'--convert',source,out,256])
result=video(out)
assert result['nb_frames']==original['nb_frames']
assert abs(float(result['duration'])-float(original['duration']))<0.001
assert result['avg_frame_rate']==original['avg_frame_rate']
checks=['VFR: every decoded frame retained at source average FPS; individual timestamps converted to CFR: PASS']
source=work/'static.mp4';out=work/'static-gpu.mp4'
run([ff,'-v','error','-f','lavfi','-i','smptebars=size=320x180:rate=30:duration=1','-c:v','libx264','-threads','1','-preset','ultrafast',source])
run([exe,'--convert',source,out,256,'--renderer=gpu'])
assert video(out)['nb_frames']==video(source)['nb_frames'] and video(out)['avg_frame_rate']=='30/1'
raw=run([ff,'-v','error','-i',out,'-pix_fmt','rgb24','-f','rawvideo','pipe:1'])
size=320*180*3;frames=[raw[i:i+size] for i in range(0,len(raw),size)]
def diff(a,b):return sum(abs(x-y) for x,y in zip(a,b))/size
adjacent=sum(diff(frames[i],frames[i+1]) for i in range(10,20))/10
repeat=sum(diff(frames[i],frames[i+2]) for i in range(10,20))/10
assert adjacent>max(0.1,repeat*3),(adjacent,repeat)
checks.append(f'30 FPS GPU static-input flicker after H264: adjacent RGB delta {adjacent:.3f}, two-frame repeat {repeat:.3f}: PASS')
run([exe,'--ui-snapshot',parent/'ui-preview.png',source,'--renderer=gpu'])
text='\nOriginal FPS additional validation\n'+'\n'.join(checks)+'\n';print(text)
with (app/'VALIDATION.txt').open('a',encoding='utf-8') as f:f.write(text)
assert work.resolve().parent==parent and work.name.startswith('ntsc-fps-test-')
shutil.rmtree(work)
