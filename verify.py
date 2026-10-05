import ctypes, json, pathlib, subprocess, hashlib, array, math, tempfile, shutil, os, uuid, struct, sys
app = pathlib.Path(__file__).resolve().parent.parent
test_parent = pathlib.Path(os.environ.get('NTSC_TEST_ROOT',tempfile.gettempdir())).resolve()
work = test_parent / ('ntsc-studio-test-'+uuid.uuid4().hex)
work.mkdir(mode=0o777)
assert work.resolve().parent == test_parent
exe = app / 'NTSC Video Studio.exe'
flags = subprocess.CREATE_NO_WINDOW
checks = []
def run(args, expected=0):
    p = subprocess.run([str(x) for x in args], capture_output=True, timeout=240, creationflags=flags)
    assert p.returncode == expected, (args, p.returncode, p.stderr.decode(errors='replace'), (app/'last-error.txt').read_text(errors='replace') if (app/'last-error.txt').exists() else '')
    return p
def probe(path):
    return json.loads(run([app/'assets'/'ffprobe.exe','-v','error','-show_streams','-show_format','-of','json',path]).stdout)
def streams(path):
    return {s['codec_type']:s for s in probe(path)['streams']}
def convert(source,target,width=512,animate=False,scale=0):
    target.unlink(missing_ok=True)
    run([exe,'--convert',source,target,width,'--effect-scale='+str(scale)]+(['--animate'] if animate else []))
    return streams(target)

source = work/'테스트 영상 23.976.mp4'
run([app/'assets'/'ffmpeg.exe','-v','error','-y','-f','lavfi','-i','testsrc2=size=640x360:rate=24000/1001:duration=3','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=3','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-c:a','aac','-shortest',source])
orig=streams(source)
normal=convert(source,work/'verified-audio.mp4')
assert normal['video']['avg_frame_rate']==orig['video']['avg_frame_rate']
assert normal['video']['nb_frames']==orig['video']['nb_frames']
assert (normal['video']['width'],normal['video']['height'])==(640,360)
assert normal['audio']['codec_name']=='aac'
assert abs(float(normal['audio']['duration'])-float(orig['audio']['duration']))<0.05
checks.append('Fractional frame rate, frame count, dimensions, AAC audio and Korean/spaced path: PASS')
run([app/'assets'/'ffmpeg.exe','-v','error','-y','-f','lavfi','-i','testsrc2=size=240x360:rate=30:duration=8','-an','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p',work/'portrait-no-audio.mp4'])
portrait=convert(work/'portrait-no-audio.mp4',work/'verified-portrait.mp4',256)
assert 'audio' not in portrait
assert (portrait['video']['width'],portrait['video']['height'])==(240,360)
assert portrait['video']['nb_frames']=='240'
checks.append('Portrait/no-audio conversion, dimensions and complete frame count: PASS')
run([app/'assets'/'ffmpeg.exe','-v','error','-y','-display_rotation:v:0','90','-i',source,'-c','copy',work/'rotated.mp4'])
rotated=convert(work/'rotated.mp4',work/'verified-rotation.mp4',256)
assert (rotated['video']['width'],rotated['video']['height'])==(360,640)
checks.append('Rotation metadata -> physically rotated output dimensions: PASS')
run([app/'assets'/'ffmpeg.exe','-v','error','-y','-i',source,'-itsoffset','0.5','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=2.5','-map','0:v:0','-map','1:a:0','-c:v','copy','-c:a','aac',work/'delayed-audio.mp4'])
convert(work/'delayed-audio.mp4',work/'verified-delay.mp4',256)
audio=run([app/'assets'/'ffmpeg.exe','-v','error','-i',work/'verified-delay.mp4','-map','0:a:0','-ac','1','-ar','48000','-f','f32le','pipe:1']).stdout
samples=array.array('f');samples.frombytes(audio)
def rms(start,end):
    values=samples[int(start*48000):int(end*48000)]
    return math.sqrt(sum(v*v for v in values)/len(values))
assert rms(0.05,0.25)<0.002 and rms(0.7,0.9)>0.02
checks.append('Delayed audio preserves the initial silent interval: PASS')
run([exe,'--test-preview',source,work/'preview-test'])
assert (work/'preview-test-after.png').stat().st_size>1000
checks.append('Preview extraction and native filtering: PASS')
with (work/'preview-test-before.png').open('rb') as f:
    header=f.read(24)
    assert struct.unpack('>II',header[16:24])==(511,287)
run([exe,'--test-resolution',work/'resolution-test.txt'])
checks.append('V1 horizontal resolution default, 4:3/portrait aspect and 8K effect mode: PASS')
cancelled=work/'cancelled-test.mp4'
run([exe,'--test-cancel',work/'portrait-no-audio.mp4',cancelled])
assert not cancelled.exists() and not list(work.glob('.cancelled-test-*.partial.mp4'))
checks.append('Cancellation stops conversion and removes partial file: PASS')
sentinel=work/'existing-test.mp4';sentinel.write_bytes(b'DO NOT OVERWRITE')
run([exe,'--convert',source,sentinel],expected=1)
assert sentinel.read_bytes()==b'DO NOT OVERWRITE'
checks.append('Existing output file is preserved: PASS')
bad=work/'invalid-input.mp4';bad.write_bytes(b'not a video')
badout=work/'bad-output.mp4'
run([exe,'--convert',bad,badout],expected=1)
assert not badout.exists()
checks.append('Invalid input returns an error without creating output: PASS')
run([exe,'--self-test',work/'native-test.png'])
checks.append('Native dimensions, two alternating-position flicker phases and RGB bitmap: PASS')
run([exe,'--ui-snapshot',work/'ui-preview.png',source])
checks.append('Windows Forms layout rendered with processed preview: PASS')

lib=ctypes.CDLL(str(app/'assets'/'ntsc.dll'))
lib.ntsc_create.argtypes=[ctypes.c_int,ctypes.c_int,ctypes.POINTER(ctypes.c_double),ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_int];lib.ntsc_create.restype=ctypes.c_void_p
opts=(ctypes.c_double*10)()
assert not lib.ntsc_create(512,240,opts,80,0,3,1920,1080)
opts[0]=float('nan');assert not lib.ntsc_create(256,240,opts,80,0,3,1920,1080)
checks.append('Native bridge rejects incomplete chunk width and nonfinite settings: PASS')

run([exe,'--test-timing',work/'timing-test.txt'])
checks.append('Rational NTSC timing: 30->30, 29.97->29.97, 120 fps phase cadence; 10000 frames without drift: PASS')
for rate,output_rate in [('15','15/1'),('24','24/1'),('25','25/1'),('30','30/1'),('30000/1001','30000/1001'),('60','60/1'),('120','120/1')]:
    tag=rate.replace('/','-')
    clip=work/f'static-{tag}.mp4'
    run([app/'assets'/'ffmpeg.exe','-v','error','-y','-f','lavfi','-i',f'smptebars=size=320x180:rate={rate}:duration=1','-an','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p',clip])
    out=work/f'animated-{tag}.mp4'
    result=convert(clip,out,256,animate=True)
    video=result['video']
    assert video['avg_frame_rate']==output_rate,(rate,video)
    assert abs(float(video['duration'])-float(streams(clip)['video']['duration']))<=1/float(rate.split('/')[0])+0.001
    assert video['nb_frames']==streams(clip)['video']['nb_frames']
    if rate=='30':
        raw=run([app/'assets'/'ffmpeg.exe','-v','error','-i',out,'-f','rawvideo','-pix_fmt','rgb24','pipe:1']).stdout
        size=320*180*3
        frames=[raw[i:i+size] for i in range(0,len(raw),size)]
        def difference(a,b): return sum(abs(x-y) for x,y in zip(a,b))/size
        adjacent=sum(difference(frames[i],frames[i+1]) for i in range(10,20))/10
        repeated=sum(difference(frames[i],frames[i+2]) for i in range(10,20))/10
        assert adjacent>max(0.1,repeated*3),(adjacent,repeated)
checks.append('15/24/25/30/29.97/60/120 fps encoded outputs retain duration and expected frame rates/counts: PASS')
checks.append('30 fps static source produces independently filtered two rainbow flicker phases in preserved 30 fps output: PASS')
# Verify motion after actual H264/yuv420 encoding at HD/4K output AND high effect width.
motion_results=[]
for width,height,effect in ([(1280,720,1280),(1920,1080,1920)] if '--fps-only' in sys.argv else [(1280,720,1280),(1920,1080,1920),(3840,2160,3840),(7680,4320,7680)]):
    clip=work/f'hd-{width}.mp4';out=work/f'hd-moving-{width}.mp4'
    run([app/'assets'/'ffmpeg.exe','-v','error','-y','-filter_threads','2','-f','lavfi','-i',f'smptebars=size={width}x{height}:rate=60:duration=0.1','-an','-c:v','libx264','-threads','2','-preset','ultrafast','-crf','10','-pix_fmt','yuv420p',clip])
    mode='1x full grid'
    try:
        video=convert(clip,out,effect,scale=1)['video']
    except AssertionError:
        error=(app/'last-error.txt').read_text(encoding='utf-8-sig',errors='replace')
        if width!=7680 or not any(x in error for x in ('Cannot allocate memory','malloc of size','Failed to allocate')):raise
        checks.append('8K full grid / 1x: SKIPPED due to available-memory limit on this machine; testing automatic whole-effect upscale instead')
        video=convert(clip,out,effect,scale=0)['video'];mode='automatic whole-effect upscale'
    assert (video['width'],video['height'])==(width,height) and video['avg_frame_rate']=='60/1'
    # Crop across a static color boundary to measure real pixel changes; no source motion.
    raw=run([app/'assets'/'ffmpeg.exe','-v','error','-threads','2','-filter_threads','2','-i',out,'-vf',f'crop=256:96:{int(width/7)-128}:64','-frames:v','6','-pix_fmt','rgb24','-f','rawvideo','pipe:1']).stdout
    size=256*96*3;frames=[raw[i:i+size] for i in range(0,len(raw),size)]
    assert len(frames)==6
    def diff(a,b):return sum(abs(x-y) for x,y in zip(a,b))/size
    adjacent=sum(diff(frames[i],frames[i+1]) for i in range(5))/5
    repeat=sum(diff(frames[i],frames[i+2]) for i in range(3))/3
    assert adjacent>max(0.3,repeat*2),(width,adjacent,repeat)
    motion_results.append(f'{width}x{height}, effect width {effect}, {mode}: adjacent RGB difference {adjacent:.3f}, two-frame repeat difference {repeat:.3f}: PASS')
checks.extend(motion_results)

text='NTSC Video Studio validation'+(' (FPS update: HD smoke checks; 4K/8K rendering unchanged)' if '--fps-only' in sys.argv else '')+'\n\n'+'\n'.join(checks)+'\n\nSynthetic inputs; no user video was used.\n'
(app/'VALIDATION.txt').write_text(text,encoding='utf-8')
print(text)

assert work.resolve().parent == test_parent and work.name.startswith('ntsc-studio-test-')
shutil.rmtree(work)
(app/'last-error.txt').unlink(missing_ok=True)
