"""Validate real D3D11 hardware rendering against the CPU Blargg path and encoded outputs."""
import ctypes, pathlib, subprocess, tempfile, os, uuid, shutil, json
app=pathlib.Path(__file__).resolve().parent.parent
parent=pathlib.Path(os.environ.get('NTSC_TEST_ROOT',tempfile.gettempdir())).resolve()
work=parent/('ntsc-gpu-test-'+uuid.uuid4().hex);work.mkdir(mode=0o777)
assets=app/'assets';exe=app/'NTSC Video Studio.exe';checks=[]
def run(args,expected=0):
    p=subprocess.run([str(a) for a in args],capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW,timeout=120)
    error=(app/'last-error.txt').read_text(encoding='utf-8-sig',errors='replace') if (app/'last-error.txt').exists() else ''
    assert p.returncode==expected,(args,p.returncode,p.stderr.decode(errors='replace'),error)
    return p.stdout
lib=ctypes.CDLL(str(assets/'ntsc.dll'))
lib.ntsc_gpu_available.argtypes=[ctypes.c_void_p,ctypes.c_int];lib.ntsc_gpu_error.restype=ctypes.c_char_p
lib.ntsc_create.argtypes=[ctypes.c_int,ctypes.c_int,ctypes.POINTER(ctypes.c_double),ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_int];lib.ntsc_create.restype=ctypes.c_void_p
lib.ntsc_create_gpu.argtypes=lib.ntsc_create.argtypes+[ctypes.c_wchar_p];lib.ntsc_create_gpu.restype=ctypes.c_void_p
lib.ntsc_process.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_int,ctypes.c_void_p,ctypes.c_int,ctypes.c_int];lib.ntsc_process.restype=ctypes.c_int
lib.ntsc_destroy.argtypes=[ctypes.c_void_p]
name=ctypes.create_string_buffer(512)
if not lib.ntsc_gpu_available(name,512):
    print('Hardware GPU unavailable; explicit error:',lib.ntsc_gpu_error().decode(errors='replace'))
    shutil.rmtree(work)
    raise SystemExit(0)
checks.append('Hardware GPU: '+name.value.decode('utf-8'))
settings=(ctypes.c_double*10)();settings[7]=.25;settings[8]=.30;settings[9]=.20;settings[4]=.05
for w,h,scan in [(4,3,0),(64,32,15),(511,287,0),(1918,1079,15),(3838,2159,15),(7678,32,0)]:
    settings=(ctypes.c_double*10)();settings[7]=.25;settings[8]=.30;settings[9]=.20;settings[4]=.05
    if w==64:
        settings[1]=.4;settings[4]=.65;settings[6]=-.25;settings[7]=-.5;settings[8]=.7;settings[9]=-.6
    outw=((w-1)//3+1)*7;large=outw*h*6>100_000_000
    emit=w if large else outw;rows=1 if large else 2;size=emit*h*rows*3
    args=(w,h,settings,80,scan,3,w,h)
    cpu=lib.ntsc_create(*args);gpu=lib.ntsc_create_gpu(*args,str(assets/'ntsc.hlsl'))
    assert cpu and gpu,lib.ntsc_gpu_error().decode(errors='replace')
    # Nonuniform fixture covers all 8-bit input values and kernel/chunk boundaries.
    pattern=bytes((n*37+n//7)%256 for n in range(65536))
    data=(pattern*((w*h*3+len(pattern)-1)//len(pattern)))[:w*h*3]
    rgb=ctypes.create_string_buffer(data,len(data));a=(ctypes.c_ubyte*size)();b=(ctypes.c_ubyte*size)()
    try:
        phases=(0,1) if w<3000 else (1,)
        maximum=0;total=0
        for phase in phases:
            assert lib.ntsc_process(cpu,rgb,len(data),a,size,phase)
            assert lib.ntsc_process(gpu,rgb,len(data),b,size,phase),lib.ntsc_gpu_error().decode(errors='replace')
            delta=0;peak=0
            for x,y in zip(a,b):d=abs(x-y);delta+=d;peak=max(peak,d)
            assert peak<=2 and delta/size<.1,(w,h,phase,peak,delta/size)
            maximum=max(maximum,peak);total+=delta
        checks.append(f'CPU/GPU raw comparison {w}x{h}, scanlines {scan}, maximum RGB delta {maximum}, mean {total/(size*len(phases)):.6f}: PASS')
    finally:
        lib.ntsc_destroy(cpu);lib.ntsc_destroy(gpu)
    del data,rgb,a,b
source=work/'GPU 영상 테스트.mp4'
run([assets/'ffmpeg.exe','-v','error','-y','-f','lavfi','-i','testsrc2=size=640x360:rate=30:duration=1','-f','lavfi','-i','sine=frequency=440:duration=1','-c:v','libx264','-threads','1','-preset','ultrafast','-c:a','aac','-shortest',source])
for mode in ('cpu','gpu'):
    out=work/(mode+'.mp4')
    run([exe,'--convert',source,out,512,'--renderer='+mode])
    assert 'renderer='+mode in pathlib.Path(str(out)+'.result.txt').read_text(encoding='utf-8-sig')
    data=json.loads(run([assets/'ffprobe.exe','-v','error','-show_streams','-of','json',out]))
    streams={s['codec_type']:s for s in data['streams']}
    assert streams['video']['avg_frame_rate']=='30/1' and streams['audio']['codec_name']=='aac'
    original=json.loads(run([assets/'ffprobe.exe','-v','error','-select_streams','v:0','-show_streams','-of','json',source]))['streams'][0]
    assert streams['video']['nb_frames']==original['nb_frames']
    assert abs(float(streams['video']['duration'])-float(original['duration']))<0.001
    assert (streams['video']['width'],streams['video']['height'])==(640,360)
run([exe,'--test-preview',source,work/'gpu-preview','--renderer=gpu'])
assert (work/'gpu-preview-after.png').exists()
run([exe,'--ui-snapshot',work/'gpu-ui.png',source,'--renderer=gpu'])
checks.append('GPU preview, UI selection and CPU/GPU encoded 30->30 fps outputs with original dimensions/audio: PASS')
long=work/'cancel.mp4'
run([assets/'ffmpeg.exe','-v','error','-y','-f','lavfi','-i','testsrc2=size=640x360:rate=30:duration=8','-c:v','libx264','-threads','1','-preset','ultrafast',long])
run([exe,'--test-cancel',long,work/'cancelled.mp4','--renderer=gpu'])
assert not (work/'cancelled.mp4').exists() and not list(work.glob('*.partial.mp4'))
checks.append('GPU conversion cancellation cleans the partial file: PASS')
# Native failure must be explicit instead of silently rendering on the CPU.
assert not lib.ntsc_create_gpu(64,32,settings,80,0,3,64,32,str(work/'missing.hlsl'))
assert lib.ntsc_gpu_error()
checks.append('Missing GPU shader returns an explicit error with no CPU substitution: PASS')
text='\nGPU renderer validation\n'+'\n'.join(checks)+'\n'
print(text)
with (app/'VALIDATION.txt').open('a',encoding='utf-8') as f:f.write(text)
assert work.resolve().parent==parent and work.name.startswith('ntsc-gpu-test-')
shutil.rmtree(work)
(app/'last-error.txt').unlink(missing_ok=True)
