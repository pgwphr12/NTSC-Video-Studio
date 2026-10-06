"""Validate the portable layout and automatic vid export from an unrelated working directory."""
import pathlib, subprocess, os, tempfile, uuid, shutil, json, hashlib, ctypes
app=pathlib.Path(__file__).resolve().parent.parent
parent=pathlib.Path(os.environ.get('NTSC_TEST_ROOT',tempfile.gettempdir())).resolve()
work=parent/('ntsc-distribution-test-'+uuid.uuid4().hex);work.mkdir(mode=0o777)
portable=work/'한글 배포 폴더';portable.mkdir(mode=0o777)
assets=portable/'assets';assets.mkdir(mode=0o777)
foreign=work/'unrelated cwd';foreign.mkdir(mode=0o777)
for name in ('NTSC Video Studio.exe','NTSC Video Studio.exe.config'):shutil.copyfile(app/name,portable/name)
for name in ('ffmpeg.exe','ffprobe.exe','ntsc.dll','ntsc.hlsl'):shutil.copyfile(app/'assets'/name,assets/name)
# A same-named bogus DLL in cwd must never replace the packaged DLL.
(foreign/'ntsc.dll').write_bytes(b'Not a native library')
exe=portable/'NTSC Video Studio.exe';checks=[]
def run(args,expected=0):
    p=subprocess.run([str(a) for a in args],cwd=foreign,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW,timeout=90)
    error=(portable/'last-error.txt').read_text(encoding='utf-8-sig',errors='replace') if (portable/'last-error.txt').exists() else ''
    assert p.returncode==expected,(args,p.returncode,p.stderr.decode(errors='replace'),error)
    return p.stdout
source=work/'영상 제목 한글.mp4'
run([assets/'ffmpeg.exe','-v','error','-y','-threads','1','-filter_threads','1','-f','lavfi','-i','testsrc2=size=640x360:rate=30:duration=2','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=2','-c:v','libx264','-threads','1','-preset','ultrafast','-c:a','aac','-shortest',source])
assert not (portable/'vid').exists()
run([exe,'--self-test',work/'native.png'])
run([exe,'--ui-snapshot',work/'ui.png',source])
assert not (portable/'vid').exists() and not (foreign/'vid').exists()
checks.append('Assets EXEs/DLL load from an unrelated cwd and a Korean/spaced portable path; opening/preview creates no vid folder: PASS')
run([exe,'--export',source])
out=portable/'vid'/'영상 제목 한글_NTSC.mp4'
assert out.exists() and not (foreign/'vid').exists() and not (work/'vid').exists()
data=json.loads(run([assets/'ffprobe.exe','-v','error','-show_streams','-of','json',out]))
streams={s['codec_type']:s for s in data['streams']}
assert streams['video']['width']==640 and streams['video']['height']==360
assert streams['video']['avg_frame_rate']=='30/1' and streams['audio']['codec_name']=='aac'
checks.append('Encoding creates executable-adjacent vid and exact title_NTSC.mp4; dimensions, 30->30 fps and AAC audio preserved: PASS')
digest=hashlib.sha256(out.read_bytes()).digest()
run([exe,'--export',source])
assert (portable/'vid'/'영상 제목 한글_NTSC_2.mp4').exists()
assert hashlib.sha256(out.read_bytes()).digest()==digest
assert sorted(p.suffix for p in (portable/'vid').iterdir())==['.mp4','.mp4']
checks.append('Repeated export selects title_NTSC_2.mp4, leaves the existing video intact and creates no sidecar in vid: PASS')
lib=ctypes.CDLL(str(assets/'ntsc.dll'));lib.ntsc_gpu_available.argtypes=[ctypes.c_void_p,ctypes.c_int]
available=lib.ntsc_gpu_available(None,0)
release=ctypes.WinDLL('kernel32').FreeLibrary;release.argtypes=[ctypes.c_void_p];release.restype=ctypes.c_int
assert release(lib._handle);del lib
if available:
    run([exe,'--export',source,'--renderer=gpu'])
    assert (portable/'vid'/'영상 제목 한글_NTSC_3.mp4').exists()
    checks.append('GPU automatic export loads the shader from assets in a Korean/spaced portable path and saves title_NTSC_3.mp4: PASS')
long=work/'cancel-source.mp4'
run([assets/'ffmpeg.exe','-v','error','-y','-f','lavfi','-i','testsrc2=size=640x360:rate=30:duration=8','-c:v','libx264','-threads','1','-preset','ultrafast',long])
cancelled=portable/'vid'/'cancel_NTSC.mp4'
run([exe,'--test-cancel',long,cancelled])
assert not cancelled.exists() and not list((portable/'vid').glob('*.partial.mp4'))
checks.append('Cancellation removes partial video from vid without removing completed videos: PASS')
assert not any((portable/name).exists() for name in ('ffmpeg.exe','ffprobe.exe','ntsc.dll'))
checks.append('Runtime binaries are confined to assets: PASS')
text='\nPortable release validation\n'+'\n'.join(checks)+'\n'
print(text)
with (app/'VALIDATION.txt').open('a',encoding='utf-8') as f:f.write(text)
assert work.resolve().parent==parent and work.name.startswith('ntsc-distribution-test-')
shutil.rmtree(work)
