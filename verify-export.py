"""0.0.4 export choices; tests NVENC on hardware when available."""
import json, os, pathlib, shutil, subprocess, tempfile, uuid
app=pathlib.Path(__file__).resolve().parent.parent
parent=pathlib.Path(os.environ.get('NTSC_TEST_ROOT',tempfile.gettempdir())).resolve()
work=parent/('ntsc-export-test-'+uuid.uuid4().hex);work.mkdir(mode=0o777)
exe=app/'NTSC Video Studio.exe';ff=app/'assets/ffmpeg.exe'
def run(args,expected=0):
    p=subprocess.run([str(a) for a in args],capture_output=True,timeout=90,creationflags=subprocess.CREATE_NO_WINDOW)
    assert p.returncode==expected,(args,p.returncode,p.stderr.decode(errors='replace'),(app/'last-error.txt').read_text(encoding='utf-8-sig',errors='replace') if (app/'last-error.txt').exists() else '')
    return p
def streams(path):
    data=json.loads(run([app/'assets/ffprobe.exe','-v','error','-show_streams','-of','json',path]).stdout)
    return {s['codec_type']:s for s in data['streams']}
source=work/'source.mp4'
run([ff,'-v','error','-f','lavfi','-i','testsrc2=size=960x540:rate=30:duration=2','-f','lavfi','-i','sine=frequency=440:duration=2','-c:v','libx264','-threads','1','-preset','ultrafast','-c:a','aac','-shortest',source])
checks=[]
for fps,rate in [('source','30/1'),('23.976','24000/1001'),('24','24/1'),('25','25/1'),('29.97','30000/1001'),('30','30/1'),('50','50/1'),('60','60/1'),('59.94','60000/1001'),('120','120/1')]:
    out=work/('fps-'+fps+'.mp4');run([exe,'--convert',source,out,'--fps='+fps,'--max-edge=854','--quality=balanced'])
    result=streams(out);v=result['video'];assert v['avg_frame_rate']==rate
    assert (v['width'],v['height'])==(854,480)
    count=int(v['nb_frames']);original=streams(source)['video'];n,d=map(int,rate.split('/'))
    assert abs(count-float(original['duration'])*n/d)<=1.01
    assert abs(float(v['duration'])-float(original['duration']))<=d/n+0.002
    assert result['audio']['codec_name']=='aac'
    assert abs(float(result['audio']['duration'])-float(streams(source)['audio']['duration']))<0.05
checks.append('Every GUI FPS choice (source/23.976/24/25/29.97/30/50/59.94/60/120), 480p bounds, frame counts, playback duration and AAC audio: PASS')
portrait=work/'portrait.mp4';out=work/'portrait-small.mp4'
run([ff,'-v','error','-f','lavfi','-i','testsrc2=size=360x640:rate=30:duration=0.5','-c:v','libx264','-threads','1','-preset','ultrafast',portrait])
run([exe,'--convert',portrait,out,256,'--max-edge=320'])
v=streams(out)['video'];assert (v['width'],v['height'])==(180,320) and v['nb_frames']==streams(portrait)['video']['nb_frames']
checks.append('Portrait output scaling preserves aspect ratio and original frame count: PASS')
sizes=[]
for quality in ('best','high','balanced','small'):
    out=work/(quality+'.mp4');run([exe,'--convert',source,out,'--quality='+quality])
    assert (streams(out)['video']['width'],streams(out)['video']['height'])==(960,540)
    sizes.append(out.stat().st_size)
assert sizes[0]>sizes[1]>sizes[2]>sizes[3],sizes
checks.append('All four compression levels produce progressively smaller files on the same moving scene: PASS '+str(sizes))
out=work/'gpu-effects.mp4';run([exe,'--convert',source,out,'--renderer=gpu','--encoder=cpu','--fps=60','--max-edge=1280'])
assert streams(out)['video']['avg_frame_rate']=='60/1' and streams(out)['video']['width']==960
checks.append('GPU NTSC + CPU H264 encoding, explicit 60 FPS and no unwanted upscaling: PASS')
still=work/'still.mp4';out=work/'still-60.mp4'
run([ff,'-v','error','-f','lavfi','-i','smptebars=size=320x180:rate=30:duration=1','-c:v','libx264','-threads','1','-preset','ultrafast',still])
run([exe,'--convert',still,out,256,'--fps=60'])
raw=run([ff,'-v','error','-i',out,'-f','rawvideo','-pix_fmt','rgb24','pipe:1']).stdout
size=320*180*3;frames=[raw[i:i+size] for i in range(0,len(raw),size)]
def diff(a,b):return sum(abs(x-y) for x,y in zip(a,b))/size
assert abs(len(frames)-60)<=1 and diff(frames[10],frames[11])>max(.1,diff(frames[10],frames[12])*3),(len(frames),diff(frames[10],frames[11]),diff(frames[10],frames[12]))
checks.append('Explicit 30->60 FPS: duplicated source frames receive independent alternating NTSC phases: PASS')
out=work/'nvenc.mp4'
p=subprocess.run([str(exe),'--convert',str(source),str(out),'--encoder=nvenc','--fps=60'],capture_output=True,timeout=90,creationflags=subprocess.CREATE_NO_WINDOW)
if p.returncode==0:
    assert streams(out)['video']['avg_frame_rate']=='60/1'
    assert 'encoder=nvenc' in pathlib.Path(str(out)+'.result.txt').read_text(encoding='utf-8-sig')
    checks.append('Actual NVIDIA NVENC encoding with explicit 60 FPS: PASS')
else:
    error=(app/'last-error.txt').read_text(encoding='utf-8-sig')
    assert p.returncode==1 and 'NVIDIA NVENC' in error and not out.exists() and not list(work.glob('*.partial.mp4')),error
    assert any(x in error for x in ('Cannot load nvcuda','Cannot load nvEncodeAPI','No NVENC capable','minimum required Nvidia driver','Driver does not support')),error
    checks.append('NVIDIA hardware encoding: NOT VERIFIED on this machine (NVENC GPU/driver unavailable). Explicit preflight error; no partial output or silent CPU substitution: PASS')
    (work/'nvenc-diagnostic.txt').write_text(error,encoding='utf-8')
for option in ('--fps=0','--fps=241','--encoder=bad','--quality=0'):
    out=work/'invalid.mp4';run([exe,'--convert',source,out,option],expected=1);assert not out.exists()
checks.append('Invalid export options rejected without completed output: PASS')
run([exe,'--export-settings-snapshot',parent/'export-settings-004.png',source,'--encoder=nvenc','--fps=60','--max-edge=854'])
run([exe,'--ui-snapshot',parent/'ui-preview.png',source,'--renderer=gpu'])
text='\nVersion 0.0.4 export validation\n'+'\n'.join(checks)+'\n';print(text)
with (app/'VALIDATION.txt').open('a',encoding='utf-8') as f:f.write(text)
assert work.resolve().parent==parent and work.name.startswith('ntsc-export-test-')
shutil.rmtree(work);(app/'last-error.txt').unlink(missing_ok=True)
