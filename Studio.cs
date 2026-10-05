// NTSC Video Studio — GPL-3.0-or-later. See licenses and README.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NtscStudio {
    sealed class Options {
        public int WorkWidth=512;
        public int EdgeStrength=80,PatternSize=3;
        public int EffectScale=0; // 0: retain the V1-sized effect grid at high resolutions.
        public double[] Values=new double[10];
        public int Scanlines=0;
        public bool UseGpu=false;
        public Options() { Values[7]=0.25;Values[8]=0.30;Values[9]=0.20;Values[4]=0.05; }
    }
    sealed class VideoInfo {
        public int Width,Height,StreamIndex;
        public double Duration,Fps,NominalFps,AudioOffset;
        public string Rate;
        public bool Hdr;
    }
    sealed class FrameTiming {
        public string Rate,ClockRate;
        public double Fps,ClockFps;
        public int Phase(long outputFrame) {
            // Use rational arithmetic before division to avoid phase drift at 59.94 fps.
            string[] output=Rate.Split('/'),clock=ClockRate.Split('/');
            decimal on=decimal.Parse(output[0],CultureInfo.InvariantCulture),od=output.Length>1?decimal.Parse(output[1],CultureInfo.InvariantCulture):1;
            decimal cn=decimal.Parse(clock[0],CultureInfo.InvariantCulture),cd=clock.Length>1?decimal.Parse(clock[1],CultureInfo.InvariantCulture):1;
            return (int)(decimal.Floor(outputFrame*cn*od/(cd*on))%2);
        }
    }
    sealed class NativeFilter : IDisposable {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
        static readonly object loadLock=new object();
        static IntPtr library;
        static void EnsureLibrary() {
            lock(loadLock) {
                if(library!=IntPtr.Zero)return;
                string file=Path.Combine(Engine.Assets,"ntsc.dll");
                if(!File.Exists(file))throw new Exception("assets 폴더의 ntsc.dll이 없습니다. ZIP 전체를 압축 해제해 주세요.");
                library=LoadLibraryEx(file,IntPtr.Zero,0x1100);
                if(library==IntPtr.Zero)throw new Exception("assets의 NTSC 필터를 불러올 수 없습니다. 오류 코드: "+Marshal.GetLastWin32Error());
            }
        }
        [DllImport("ntsc.dll",CallingConvention=CallingConvention.Cdecl)]
        static extern IntPtr ntsc_create(int width,int height,[In] double[] options,int edgeStrength,int scanline,int patternSize,int displayWidth,int displayHeight);
        [DllImport("ntsc.dll",CallingConvention=CallingConvention.Cdecl)]
        static extern int ntsc_process(IntPtr handle,[In] byte[] rgb,int inputBytes,[Out] byte[] dest,int outputBytes,int frame);
        [DllImport("ntsc.dll",CallingConvention=CallingConvention.Cdecl)]
        static extern void ntsc_destroy(IntPtr handle);
        [DllImport("ntsc.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode,ExactSpelling=true)]
        static extern IntPtr ntsc_create_gpu(int width,int height,[In] double[] options,int edgeStrength,int scanline,int patternSize,int displayWidth,int displayHeight,string shader);
        [DllImport("ntsc.dll",CallingConvention=CallingConvention.Cdecl)]
        static extern int ntsc_gpu_available([Out] byte[] name,int capacity);
        [DllImport("ntsc.dll",CallingConvention=CallingConvention.Cdecl)]
        static extern IntPtr ntsc_gpu_error();
        static string GpuError() { return Marshal.PtrToStringAnsi(ntsc_gpu_error())??"GPU processing failed"; }
        public static string GpuName() {
            EnsureLibrary();var name=new byte[512];
            if(ntsc_gpu_available(name,name.Length)==0)throw new Exception("GPU 렌더링을 사용할 수 없습니다. CPU를 선택해 주세요.\n"+GpuError());
            int count=Array.IndexOf(name,(byte)0);return Encoding.UTF8.GetString(name,0,count<0?name.Length:count);
        }
        IntPtr handle;
        bool gpu;
        public int Width,Height;
        public NativeFilter(int width,int height,Options options,int displayWidth=0,int displayHeight=0) {
            EnsureLibrary();
            Width=((width-1)/3+1)*7;bool large=(long)Width*height*6>100000000;
            Height=height*(large?1:2);if(large)Width=Math.Max(2,displayWidth>0?displayWidth:width);
            int dw=Math.Max(2,displayWidth>0?displayWidth:width),dh=Math.Max(2,displayHeight>0?displayHeight:height);
            gpu=options.UseGpu;
            handle=gpu?ntsc_create_gpu(width,height,options.Values,options.EdgeStrength,options.Scanlines,options.PatternSize,dw,dh,Path.Combine(Engine.Assets,"ntsc.hlsl")):
                ntsc_create(width,height,options.Values,options.EdgeStrength,options.Scanlines,options.PatternSize,dw,dh);
            if(handle==IntPtr.Zero) throw new Exception(gpu?"GPU 필터를 초기화할 수 없습니다. CPU를 선택해 주세요.\n"+GpuError():"NTSC 필터를 초기화할 수 없습니다.");
        }
        public byte[] Apply(byte[] input,int frame) {
            var output=new byte[checked(Width*Height*3)];
            Apply(input,output,frame); return output;
        }
        public void Apply(byte[] input,byte[] output,int frame) {
            if(ntsc_process(handle,input,input.Length,output,output.Length,frame)==0)
                throw new Exception(gpu?"GPU 프레임 처리에 실패했습니다.\n"+GpuError():"NTSC 프레임 처리에 실패했습니다.");
        }
        public void Dispose() { if(handle!=IntPtr.Zero){ntsc_destroy(handle);handle=IntPtr.Zero;} }
    }
    static class Engine {
        public static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string Assets=Path.Combine(Root,"assets");
        static string Exe(string name) {
            var path=Path.Combine(Assets,name+".exe");
            if(!File.Exists(path)) throw new Exception("assets 폴더의 "+name+".exe가 없습니다. ZIP 전체를 압축 해제해 주세요.");
            return path;
        }
        public static string CreateVideoOutputPath(string input) {
            string folder=Path.Combine(Root,"vid");
            Directory.CreateDirectory(folder);
            string title=Path.GetFileNameWithoutExtension(input);
            if(string.IsNullOrWhiteSpace(title))title="video";
            string name=title+"_NISC",target=Path.Combine(folder,name+".mp4");
            for(int n=2;File.Exists(target)||Directory.Exists(target);n++)target=Path.Combine(folder,name+"_"+n+".mp4");
            return target;
        }
        public static string Q(string s) { return "\""+s.Replace("\"","\\\"")+"\""; }
        public static Process Start(string exe,string args) {
            var p=new Process();
            p.StartInfo=new ProcessStartInfo(Exe(exe),args) {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,
                RedirectStandardError=true,RedirectStandardInput=true
            };
            p.Start(); return p;
        }
        static double Num(object value,double fallback=0) {
            double d; return double.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,
                CultureInfo.InvariantCulture,out d)&&!double.IsNaN(d)&&!double.IsInfinity(d)?d:fallback;
        }
        static object Get(Dictionary<string,object> d,string k) { object v; return d.TryGetValue(k,out v)?v:null; }
        static double Ratio(string value) {
            if(string.IsNullOrEmpty(value))return 0;
            var parts=value.Replace(':','/').Split('/');
            if(parts.Length!=2)return Num(value);
            double den=Num(parts[1]);return den==0?0:Num(parts[0])/den;
        }
        public static FrameTiming Timing(VideoInfo info,Options options) {
            double clockSource=info.NominalFps>0?info.NominalFps:info.Fps;
            bool fractional=new[]{24,30,60,120,240}.Any(n=>Math.Abs(clockSource-n*1000.0/1001)<0.002);
            string clock=fractional?"60000/1001":"60/1";
            double clockFps=Ratio(clock);
            // Preserve every source frame; at low FPS alternate phases per source frame
            // so sampling a 60 Hz clock cannot freeze the two-phase effect.
            string rate=info.Rate;
            if(info.Fps<clockFps) {clock=rate;clockFps=info.Fps;}
            return new FrameTiming {Rate=rate,Fps=Ratio(rate),ClockRate=clock,ClockFps=clockFps};
        }
        public static VideoInfo Probe(string input) {
            using(var p=Start("ffprobe","-v error -show_streams -show_format -of json "+Q(input))) {
                var err=p.StandardError.ReadToEndAsync();var text=p.StandardOutput.ReadToEnd();p.WaitForExit();
                if(p.ExitCode!=0)throw new Exception("영상을 읽을 수 없습니다.\n"+err.Result);
                var json=new JavaScriptSerializer { MaxJsonLength=8*1024*1024 };
                var data=(Dictionary<string,object>)json.DeserializeObject(text);
                var streams=(object[])Get(data,"streams");
                var v=streams.Cast<Dictionary<string,object>>().FirstOrDefault(x=>Convert.ToString(Get(x,"codec_type"))=="video" &&
                    !(Get(x,"disposition") is Dictionary<string,object> && Num(Get((Dictionary<string,object>)Get(x,"disposition"),"attached_pic"))==1));
                if(v==null)throw new Exception("영상 트랙을 찾을 수 없습니다.");
                string rate=Convert.ToString(Get(v,"avg_frame_rate"));double fps=Ratio(rate);
                if(fps<=0){rate=Convert.ToString(Get(v,"r_frame_rate"));fps=Ratio(rate);}
                if(fps<1||fps>240)throw new Exception("이 영상의 프레임 속도를 처리할 수 없습니다. 1~240 fps 영상이 필요합니다.");
                int w=(int)Num(Get(v,"width")),h=(int)Num(Get(v,"height"));
                double sar=Ratio(Convert.ToString(Get(v,"sample_aspect_ratio")));if(sar<=0)sar=1;
                w=(int)Math.Round(w*sar);
                double rotation=0;
                var tags=Get(v,"tags") as Dictionary<string,object>;if(tags!=null)rotation=Num(Get(tags,"rotate"));
                var side=Get(v,"side_data_list") as object[];
                if(side!=null)foreach(var item in side){var d=item as Dictionary<string,object>;if(d!=null&&Get(d,"rotation")!=null)rotation=Num(Get(d,"rotation"));}
                if(Math.Abs(rotation)%180>45&&Math.Abs(rotation)%180<135){int t=w;w=h;h=t;}
                if(w<2||h<2||w>8192||h>8192)throw new Exception("지원 범위를 벗어난 영상 크기입니다.");
                var fmt=Get(data,"format") as Dictionary<string,object>;
                double duration=Num(Get(v,"duration"));if(duration<=0&&fmt!=null)duration=Num(Get(fmt,"duration"));
                var transfer=Convert.ToString(Get(v,"color_transfer"));
                var audio=streams.Cast<Dictionary<string,object>>().FirstOrDefault(x=>Convert.ToString(Get(x,"codec_type"))=="audio");
                double offset=audio==null?0:Num(Get(audio,"start_time"))-Num(Get(v,"start_time"));
                return new VideoInfo { Width=w,Height=h,StreamIndex=(int)Num(Get(v,"index")),Fps=fps,NominalFps=Ratio(Convert.ToString(Get(v,"r_frame_rate"))),Rate=rate,Duration=duration,
                    AudioOffset=offset,Hdr=transfer=="smpte2084"||transfer=="arib-std-b67" };
            }
        }
        public static double EffectMagnification(Options options) { return options.EffectScale==0?Math.Max(1,options.WorkWidth/512.0):options.EffectScale; }
        public static void WorkingSize(VideoInfo info,Options options,out int w,out int h) {
            int target=(int)Math.Round(options.WorkWidth/EffectMagnification(options));
            w=target-((target-1)%3);
            h=Math.Max(1,(int)Math.Round((double)w*info.Height/info.Width));
            if(w<4||w>8192||h>8192||(long)w*h>33554432)throw new Exception("처리 해상도를 더 낮춰 주세요. 가로·세로 최대 8192, 전체 3355만 픽셀까지 지원합니다.");
        }
        static string DecodeArgs(string input,VideoInfo info,FrameTiming timing,int w,int h,double? time) {
            string seek=time.HasValue?"-ss "+time.Value.ToString("0.######",CultureInfo.InvariantCulture)+" ":"";
            return "-hide_banner -loglevel error -nostdin -threads 1 -filter_threads 1 "+seek+"-i "+Q(input)+
                " -map 0:"+info.StreamIndex+" -an -sn -dn -vf "+Q("scale="+w+":"+h+":flags=area,setsar=1,setpts=PTS-STARTPTS")+
                (time.HasValue?" -frames:v 1":"")+" -fps_mode passthrough -threads 1 -f rawvideo -pix_fmt rgb24 pipe:1";
        }
        static bool ReadFrame(Stream stream,byte[] bytes,CancellationToken cancel) {
            int n=0;
            while(n<bytes.Length) {
                cancel.ThrowIfCancellationRequested();int read=stream.Read(bytes,n,bytes.Length-n);
                if(read==0){if(n==0)return false;throw new Exception("영상 프레임이 중간에 끊겼습니다.");} n+=read;
            }
            return true;
        }
        public static Bitmap BitmapFromRgb(byte[] rgb,int width,int height) {
            var bmp=new Bitmap(width,height,PixelFormat.Format24bppRgb);
            var data=bmp.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
            try {
                var row=new byte[width*3];
                for(int y=0;y<height;y++) {
                    for(int x=0;x<width;x++){int a=(y*width+x)*3;row[x*3]=rgb[a+2];row[x*3+1]=rgb[a+1];row[x*3+2]=rgb[a];}
                    Marshal.Copy(row,0,IntPtr.Add(data.Scan0,y*data.Stride),row.Length);
                }
            } finally { bmp.UnlockBits(data); }
            return bmp;
        }
        public static Bitmap[] Preview(string input,VideoInfo info,Options options,double seconds) {
            if(info.Hdr)throw new Exception("현재 버전은 SDR 영상용입니다. HDR 영상을 SDR로 변환한 뒤 사용해 주세요.");
            int w,h;WorkingSize(info,options,out w,out h);
            var timing=Timing(info,options);
            using(var decoder=Start("ffmpeg",DecodeArgs(input,info,timing,w,h,seconds))) {
                var err=decoder.StandardError.ReadToEndAsync();var rgb=new byte[w*h*3];
                if(!ReadFrame(decoder.StandardOutput.BaseStream,rgb,CancellationToken.None)){decoder.WaitForExit();throw new Exception("이 위치의 프레임을 읽을 수 없습니다.\n"+err.Result);}
                decoder.StandardOutput.BaseStream.CopyTo(Stream.Null);decoder.WaitForExit();
                if(decoder.ExitCode!=0)throw new Exception(err.Result);
                using(var filter=new NativeFilter(w,h,options)) {
                    var pictures=new Bitmap[3];
                    try {
                        using(var original=BitmapFromRgb(rgb,w,h)){int pw=Math.Min(1400,w);pictures[0]=new Bitmap(original,new Size(pw,Math.Max(1,(int)((double)pw*h/w))));}
                        var pixels=new byte[checked(filter.Width*filter.Height*3)];
                        for(int phase=0;phase<2;phase++) {
                            filter.Apply(rgb,pixels,phase);
                            using(var full=BitmapFromRgb(pixels,filter.Width,filter.Height)){int pw=Math.Min(1400,filter.Width);pictures[phase+1]=new Bitmap(full,new Size(pw,Math.Max(1,(int)((double)pw*filter.Height/filter.Width))));}
                        }
                        return pictures;
                    }
                    catch { foreach(var picture in pictures)if(picture!=null)picture.Dispose();throw; }
                }
            }
        }
        static void Kill(Process p) { try{if(p!=null&&!p.HasExited)p.Kill();}catch{} }
        public static long ConvertVideo(string input,string target,VideoInfo info,Options options,CancellationToken cancel,Action<long,double> progress) {
            if(info.Hdr)throw new Exception("현재 버전은 SDR 영상용입니다. HDR 영상을 SDR로 변환한 뒤 사용해 주세요.");
            if(string.Equals(Path.GetFullPath(input),Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))throw new Exception("원본과 다른 저장 경로를 선택해 주세요.");
            if(File.Exists(target))throw new Exception("이미 존재하는 파일입니다. 다른 이름을 선택해 주세요.");
            int w,h;WorkingSize(info,options,out w,out h);
            var timing=Timing(info,options);
            int finalW=(info.Width+1)/2*2,finalH=(info.Height+1)/2*2;
            string partial=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target)),"."+Path.GetFileNameWithoutExtension(target)+"-"+Guid.NewGuid().ToString("N")+".partial.mp4");
            long count=0;bool complete=false;
            try {
                using(var filter=new NativeFilter(w,h,options))
                using(var decoder=Start("ffmpeg",DecodeArgs(input,info,timing,w,h,null))) {
                    var decodeError=decoder.StandardError.ReadToEndAsync();
                    string audioFilter=info.AudioOffset<0?
                        "atrim=start="+(-info.AudioOffset).ToString("0.########",CultureInfo.InvariantCulture)+",asetpts=PTS-STARTPTS":
                        "asetpts=PTS-STARTPTS,adelay="+Math.Round(info.AudioOffset*1000).ToString(CultureInfo.InvariantCulture)+":all=1";
                    string encodingPreset=(long)finalW*finalH>16777216?"-preset ultrafast -tune zerolatency -rc-lookahead 0":"-preset medium -rc-lookahead 8";
                    string encodingThreads=(long)finalW*finalH>16777216?"1":"2";
                    string encodeArgs="-hide_banner -loglevel error -nostdin -y -threads 2 -f rawvideo -pixel_format rgb24 -video_size "+filter.Width+"x"+filter.Height+
                        " -framerate "+timing.Rate+" -i pipe:0 -i "+Q(input)+" -map 0:v:0 -map 1:a:0? -map_metadata 1 -vf "+
                        Q("scale="+finalW+":"+finalH+":flags=lanczos,setsar=1")+
                        " -c:v libx264 "+encodingPreset+" -crf 14 -pix_fmt yuv420p -threads "+encodingThreads+" -filter_threads 1 -c:a aac -b:a 192k -af "+Q(audioFilter)+" -metadata:s:v:0 rotate=0 -movflags +faststart "+Q(partial);
                    using(var encoder=Start("ffmpeg",encodeArgs))
                    using(cancel.Register(()=>{Kill(decoder);Kill(encoder);})) {
                        var encodeError=encoder.StandardError.ReadToEndAsync();
                        var inputFrame=new byte[w*h*3];var outputFrame=new byte[filter.Width*filter.Height*3];
                        try {
                            var watch=Stopwatch.StartNew();long last=0;
                            while(ReadFrame(decoder.StandardOutput.BaseStream,inputFrame,cancel)) {
                                cancel.ThrowIfCancellationRequested();filter.Apply(inputFrame,outputFrame,timing.Phase(count));
                                // Bound individual Windows pipe writes, including 8K frame buffers.
                                for(int offset=0;offset<outputFrame.Length;offset+=1048576) {
                                    cancel.ThrowIfCancellationRequested();encoder.StandardInput.BaseStream.Write(outputFrame,offset,Math.Min(1048576,outputFrame.Length-offset));
                                }
                                count++;
                                if(watch.ElapsedMilliseconds-last>200){progress(count,count/timing.Fps);last=watch.ElapsedMilliseconds;}
                            }
                            encoder.StandardInput.Close();decoder.WaitForExit();encoder.WaitForExit();cancel.ThrowIfCancellationRequested();
                            if(decoder.ExitCode!=0)throw new Exception("영상 읽기 실패:\n"+decodeError.Result);
                            if(encoder.ExitCode!=0)throw new Exception("영상 저장 실패:\n"+encodeError.Result);
                            if(count==0)throw new Exception("처리할 영상 프레임이 없습니다.");
                            File.Move(partial,target);complete=true;progress(count,count/timing.Fps);return count;
                        } catch {
                            Kill(decoder);Kill(encoder);decoder.WaitForExit();encoder.WaitForExit();
                            cancel.ThrowIfCancellationRequested();
                            string detail=encodeError.Result;
                            if(encoder.ExitCode!=0&&!string.IsNullOrWhiteSpace(detail))throw new Exception("영상 저장 실패:\n"+detail);
                            throw;
                        }
                    }
                }
            } finally { if(!complete&&File.Exists(partial))try{File.Delete(partial);}catch{} }
        }
    }

    sealed class MainForm : Form {
        readonly TextBox path=new TextBox { ReadOnly=true,Dock=DockStyle.Fill };
        readonly Button open=new Button { Text="영상 열기",AutoSize=true };
        readonly Button preview=new Button { Text="미리보기",AutoSize=true };
        readonly Button export=new Button { Text="인코딩 시작 · vid에 저장",Dock=DockStyle.Fill,Height=44 };
        readonly Button cancel=new Button { Text="취소",Enabled=false,AutoSize=true };
        readonly Label infoLabel=new Label { Text="MP4 · MOV · MKV · AVI 등의 SDR 영상을 열어 주세요.",AutoSize=true,ForeColor=Color.FromArgb(90,100,110) };
        readonly Label status=new Label { Text="준비됨",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft };
        readonly ProgressBar bar=new ProgressBar { Dock=DockStyle.Fill };
        readonly NumericUpDown seek=new NumericUpDown { DecimalPlaces=1,Increment=1,Width=85,Maximum=360000 };
        readonly ComboBox resolution=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill };
        readonly ComboBox preset=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill };
        readonly ComboBox renderer=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=215 };
        readonly Label gpuStatus=new Label { Dock=DockStyle.Bottom,Height=25,AutoEllipsis=true,ForeColor=Color.FromArgb(75,85,95),Text="NTSC 필터 처리 방식 선택" };
        readonly TrackBar strength=new TrackBar { Minimum=20,Maximum=100,Value=80,TickStyle=TickStyle.None,Dock=DockStyle.Fill,AutoSize=false };
        readonly ComboBox pattern=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=165 };
        readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer { Interval=16 };
        readonly Stopwatch animationClock=new Stopwatch();
        Bitmap[] phases;
        readonly CheckBox scanlines=new CheckBox { Text="스캔라인 추가 (15%)",AutoSize=true };
        readonly PictureBox before=new PictureBox { Dock=DockStyle.Fill,BackColor=Color.FromArgb(22,27,33),SizeMode=PictureBoxSizeMode.Zoom };
        readonly PictureBox after=new PictureBox { Dock=DockStyle.Fill,BackColor=Color.FromArgb(22,27,33),SizeMode=PictureBoxSizeMode.Zoom };
        readonly TrackBar[] sliders=new TrackBar[10];
        readonly int[] visibleIndexes={7,8,9,6,4,1};
        readonly List<Control> edits=new List<Control>();
        VideoInfo video;string input;CancellationTokenSource cancellation;bool busy;
        public MainForm() {
            Text="NTSC Video Studio";ClientSize=new Size(1120,880);MinimumSize=new Size(1000,850);
            Font=new Font("맑은 고딕",9.5f);BackColor=Color.FromArgb(246,247,249);AutoScaleMode=AutoScaleMode.Dpi;
            var root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,67));root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,37));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,26));root.RowStyles.Add(new RowStyle(SizeType.Absolute,32));Controls.Add(root);
            var head=new Panel { Dock=DockStyle.Fill };
            head.Controls.Add(new Label { Text="NTSC Video Studio",Font=new Font("맑은 고딕",21,FontStyle.Bold),AutoSize=true,Location=new Point(0,0) });
            head.Controls.Add(new Label { Text="영상에 아날로그 색 번짐과 경계 무늬를 더하세요.",AutoSize=true,Location=new Point(2,43),ForeColor=Color.FromArgb(75,85,95) });root.Controls.Add(head,0,0);
            var renderPanel=new Panel { Dock=DockStyle.Right,Width=350 };
            var renderRow=new FlowLayoutPanel { Dock=DockStyle.Top,Height=35 };
            renderRow.Controls.Add(new Label { Text="렌더링",AutoSize=true,Padding=new Padding(0,5,8,0) });renderRow.Controls.Add(renderer);
            renderPanel.Controls.Add(renderRow);renderPanel.Controls.Add(gpuStatus);head.Controls.Add(renderPanel);
            renderer.Items.AddRange(new object[]{"CPU · 기본","GPU · Direct3D 11"});renderer.SelectedIndex=0;
            var files=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 };
            files.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));files.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));
            files.Controls.Add(path,0,0);files.Controls.Add(open,1,0);root.Controls.Add(files,0,1);root.Controls.Add(infoLabel,0,2);
            var body=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,325));root.Controls.Add(body,0,3);
            var images=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=5,Padding=new Padding(0,0,15,0) };
            images.RowStyles.Add(new RowStyle(SizeType.Absolute,28));images.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            images.RowStyles.Add(new RowStyle(SizeType.Absolute,28));images.RowStyles.Add(new RowStyle(SizeType.Percent,50));images.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            images.Controls.Add(new Label { Text="원본 · 작업 해상도",AutoSize=true },0,0);images.Controls.Add(before,0,1);
            images.Controls.Add(new Label { Text="NTSC 적용 · 화면 비율 복원",AutoSize=true,Padding=new Padding(0,5,0,0) },0,2);images.Controls.Add(after,0,3);
            var seekPanel=new FlowLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(0,7,0,0) };
            seekPanel.Controls.Add(new Label { Text="위치 (초)",AutoSize=true,Padding=new Padding(0,5,0,0) });seekPanel.Controls.Add(seek);seekPanel.Controls.Add(preview);images.Controls.Add(seekPanel,0,4);body.Controls.Add(images,0,0);
            var settings=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=13,Padding=new Padding(8,0,0,0) };
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute,25));settings.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute,25));settings.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
            for(int i=0;i<6;i++)settings.RowStyles.Add(new RowStyle(SizeType.Absolute,49));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute,55));settings.RowStyles.Add(new RowStyle(SizeType.Absolute,90));settings.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            settings.Controls.Add(new Label { Text="효과 스타일",AutoSize=true },0,0);settings.Controls.Add(preset,0,1);
            preset.Items.AddRange(new object[]{"컴포지트 · 기본","색 번짐 중심","선명한 영상","강한 아날로그 효과","캡처카드 · 예시 참고"});
            settings.Controls.Add(new Label { Text="효과 해상도 · V1 가로 기준",AutoSize=true },0,2);settings.Controls.Add(resolution,0,3);
            resolution.Items.AddRange(new object[]{"256","384","512 · V1 기본","640","768","1024","1536","1920 · Full HD","2560","3840 · 4K","5120","7680 · 8K"});resolution.SelectedIndex=2;
            string[] labels={"색 경계 무늬","밝기 경계의 색","색 번짐","수평 디테일","선명도","채도"};
            for(int i=0;i<10;i++)sliders[i]=new TrackBar { Minimum=-100,Maximum=100,TickStyle=TickStyle.None,SmallChange=5,LargeChange=10,Dock=DockStyle.Fill,AutoSize=false,Height=26 };
            for(int j=0;j<6;j++) {
                var panel=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=2 };
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute,22));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,27));
                int index=visibleIndexes[j];var label=new Label { Text=labels[j]+"   0",AutoSize=true };string name=labels[j];
                sliders[index].ValueChanged+=(s,e)=>{label.Text=name+"   "+sliders[index].Value;};
                panel.Controls.Add(label,0,0);panel.Controls.Add(sliders[index],0,1);settings.Controls.Add(panel,0,j+4);edits.Add(sliders[index]);
            }
            var emphasis=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=2 };emphasis.RowStyles.Add(new RowStyle(SizeType.Absolute,23));emphasis.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            var strengthLabel=new Label { Text="무지개빛·경계 강조 · 80",AutoSize=true };strength.ValueChanged+=(s,e)=>strengthLabel.Text="무지개빛·경계 강조 · "+strength.Value;
            emphasis.Controls.Add(strengthLabel,0,0);emphasis.Controls.Add(strength,0,1);settings.Controls.Add(emphasis,0,10);
            pattern.Items.AddRange(new object[]{"자동 · V1 효과 크기 유지","1× · 확대 없음","2×","3×","4×","6×","8×","12×","16×","24×","32×"});pattern.SelectedIndex=0;
            var checks=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=true };checks.Controls.Add(new Label { Text="전체 효과 확대",AutoSize=true,Padding=new Padding(0,5,0,0) });checks.Controls.Add(pattern);checks.Controls.Add(scanlines);checks.Controls.Add(new Label { Text="무지개빛 · 위치가 오가는 빠른 깜빡임",AutoSize=true });settings.Controls.Add(checks,0,11);
            settings.Controls.Add(export,0,12);body.Controls.Add(settings,1,0);
            root.Controls.Add(bar,0,4);
            var foot=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 };foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));foot.Controls.Add(status,0,0);foot.Controls.Add(cancel,1,0);root.Controls.Add(foot,0,5);
            edits.AddRange(new Control[]{open,preset,resolution,strength,pattern,scanlines,seek,preview,renderer});
            preset.SelectedIndexChanged+=(s,e)=>SetPreset();preset.SelectedIndex=4;
            resolution.SelectedIndexChanged+=(s,e)=>UpdateInfoLabel();
            pattern.SelectedIndexChanged+=(s,e)=>UpdateInfoLabel();
            renderer.SelectedIndexChanged+=(s,e)=>{
                if(renderer.SelectedIndex==1)try{gpuStatus.Text=NativeFilter.GpuName();}catch(Exception ex){renderer.SelectedIndex=0;MessageBox.Show(this,ex.Message,"GPU 렌더링",MessageBoxButtons.OK,MessageBoxIcon.Information);}
                else gpuStatus.Text="CPU · NTSC 필터 처리";
            };
            open.Click+=async(s,e)=>await OpenVideo();preview.Click+=async(s,e)=>await UpdatePreview();export.Click+=async(s,e)=>await ExportVideo();
            cancel.Click+=(s,e)=>{if(cancellation!=null){cancel.Enabled=false;status.Text="취소 중…";cancellation.Cancel();}};
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show(this,"진행 중인 작업이 끝나거나 취소된 뒤 창을 닫아 주세요.","작업 진행 중");}};
            animation.Tick+=(s,e)=>{if(phases!=null){double fps=video==null?60:Engine.Timing(video,ReadOptions()).ClockFps;after.Image=phases[(int)(animationClock.Elapsed.TotalSeconds*fps)%2];}};
            FormClosed+=(s,e)=>{animation.Stop();animation.Dispose();if(before.Image!=null)before.Image.Dispose();after.Image=null;if(phases!=null)foreach(var image in phases)image.Dispose();};
            export.Enabled=false;preview.Enabled=false;
        }
        void SetPreset() {
            foreach(var slider in sliders)slider.Value=0;
            switch(preset.SelectedIndex) {
                case 1: sliders[4].Value=20;sliders[6].Value=20;sliders[7].Value=-75;sliders[8].Value=-75;break;
                case 2: sliders[4].Value=20;sliders[6].Value=70;sliders[7].Value=-75;sliders[8].Value=-75;sliders[9].Value=-100;break;
                case 3: sliders[7].Value=60;sliders[8].Value=45;sliders[9].Value=40;sliders[6].Value=-25;break;
                case 4: sliders[7].Value=25;sliders[8].Value=30;sliders[9].Value=20;sliders[4].Value=5;break;
            }
        }
        Options ReadOptions() { return new Options {WorkWidth=new[]{256,384,512,640,768,1024,1536,1920,2560,3840,5120,7680}[resolution.SelectedIndex],Values=sliders.Select(x=>x.Value/100.0).ToArray(),EdgeStrength=strength.Value,EffectScale=new[]{0,1,2,3,4,6,8,12,16,24,32}[pattern.SelectedIndex],Scanlines=scanlines.Checked?15:0,UseGpu=renderer.SelectedIndex==1}; }
        void UpdateInfoLabel() {
            if(video==null)return;
            var timing=Engine.Timing(video,ReadOptions());
            int workWidth,workHeight;try{Engine.WorkingSize(video,ReadOptions(),out workWidth,out workHeight);}catch(Exception ex){infoLabel.Text=ex.Message;return;}
            infoLabel.Text=video.Width+" × "+video.Height+"  ·  "+video.Fps.ToString("0.###")+" fps 원본 → "+timing.Fps.ToString("0.###")+
                " fps 저장  ·  가로 "+workWidth+" 처리 · 전체 "+Engine.EffectMagnification(ReadOptions()).ToString("0.##")+"×  ·  "+(video.Duration>0?TimeSpan.FromSeconds(video.Duration).ToString(@"hh\:mm\:ss"):"길이 미상");
        }
        void SetBusy(bool value,bool converting=false) {
            busy=value;foreach(var c in edits)c.Enabled=!value;preview.Enabled=!value&&video!=null;export.Enabled=!value&&video!=null;cancel.Enabled=value&&converting;
            bar.Style=value&&!converting?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous;
            if(value)animation.Stop();else if(phases!=null)animation.Start();
        }
        async Task OpenVideo() {
            using(var dialog=new OpenFileDialog { Filter="영상 파일|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.mpg;*.mpeg;*.mts;*.m2ts|모든 파일|*.*" }) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                SetBusy(true);status.Text="영상 정보를 읽는 중…";
                try {
                    var candidate=await Task.Run(()=>Engine.Probe(dialog.FileName));
                    if(candidate.Hdr)throw new Exception("현재 버전은 SDR 영상용입니다. HDR 영상을 SDR로 변환한 뒤 사용해 주세요.");
                    video=candidate;input=dialog.FileName;path.Text=input;
                    UpdateInfoLabel();
                    seek.Maximum=(decimal)Math.Max(0,video.Duration>0?video.Duration-1/video.Fps:360000);seek.Value=0;status.Text="영상 준비 완료";
                } catch(Exception ex){ShowError(ex);}finally{SetBusy(false);}
            }
            if(video!=null)await UpdatePreview();
        }
        async Task UpdatePreview() {
            if(video==null||busy)return;var settings=ReadOptions();double time=(double)seek.Value;
            SetBusy(true);status.Text="효과 미리보기를 만드는 중…";
            try {
                var pictures=await Task.Run(()=>Engine.Preview(input,video,settings,time));
                SetPictures(pictures);
                status.Text="무지개빛 오가며 깜빡임 재생 중 · 설정 변경 후 미리보기를 눌러 주세요.";
            }catch(Exception ex){ShowError(ex);}finally{SetBusy(false);}
        }
        void SetPictures(Bitmap[] pictures) {
            animation.Stop();after.Image=null;if(before.Image!=null)before.Image.Dispose();if(phases!=null)foreach(var image in phases)image.Dispose();
            before.Image=pictures[0];phases=new Bitmap[2];
            int displayW=Math.Min(1400,pictures[0].Width*2),displayH=Math.Max(1,(int)Math.Round((double)displayW*pictures[0].Height/pictures[0].Width));
            for(int i=0;i<2;i++)using(var filtered=pictures[i+1]) {
                var display=new Bitmap(displayW,displayH);
                using(var g=Graphics.FromImage(display)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(filtered,0,0,display.Width,display.Height);}phases[i]=display;
            }
            after.Image=phases[0];animationClock.Restart();animation.Start();
        }
        public void LoadSnapshotVideo(string file,bool gpu=false) {
            renderer.SelectedIndex=gpu?1:0;
            input=file;video=Engine.Probe(file);path.Text=Path.GetFileName(file);
            UpdateInfoLabel();
            SetPictures(Engine.Preview(file,video,ReadOptions(),0));SetBusy(false);status.Text="무지개빛 오가며 깜빡임 재생 중 · 설정 변경 후 미리보기를 눌러 주세요.";
        }
        async Task ExportVideo() {
            if(video==null||busy)return;
                var settings=ReadOptions();cancellation=new CancellationTokenSource();SetBusy(true,true);bar.Value=0;status.Text="변환을 시작하는 중…";
                try {
                    int w,h;Engine.WorkingSize(video,settings,out w,out h);
                    string target=Engine.CreateVideoOutputPath(input);
                    long frames=await Task.Run(()=>Engine.ConvertVideo(input,target,video,settings,cancellation.Token,(count,seconds)=>{
                        if(!IsDisposed)BeginInvoke(new Action(()=>{bar.Value=video.Duration>0?(int)Math.Min(99,seconds/video.Duration*100):0;status.Text=count+" 프레임 처리 · "+TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");}));
                    }));
                    bar.Value=100;status.Text="저장 완료 · "+frames+" 프레임";
                    MessageBox.Show(this,"영상 저장을 완료했습니다.\n\n"+target,"완료",MessageBoxButtons.OK,MessageBoxIcon.Information);
                }catch(OperationCanceledException){bar.Value=0;status.Text="취소됨 · 미완성 파일은 제거했습니다.";}
                catch(Exception ex){bar.Value=0;ShowError(ex);}
                finally{cancellation.Dispose();cancellation=null;SetBusy(false);}
        }
        void ShowError(Exception ex){status.Text="작업을 완료하지 못했습니다.";MessageBox.Show(this,ex.Message,"확인",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    static class Program {
        static void SelectRenderer(string[] args,Options options) {
            string value=args.FirstOrDefault(x=>x.StartsWith("--renderer="));
            if(value==null)return;
            if(value=="--renderer=gpu")options.UseGpu=true;
            else if(value=="--renderer=cpu")options.UseGpu=false;
            else throw new Exception("Renderer must be cpu or gpu.");
        }
        [STAThread] static int Main(string[] args) {
            try {
                if(args.Length>0&&args[0]=="--export") {
                    if(args.Length<2)throw new Exception("--export INPUT");
                    var options=new Options();SelectRenderer(args,options);var info=Engine.Probe(args[1]);
                    int w,h;Engine.WorkingSize(info,options,out w,out h);
                    string target=Engine.CreateVideoOutputPath(args[1]);
                    Engine.ConvertVideo(args[1],target,info,options,CancellationToken.None,(n,s)=>{});return 0;
                }
                if(args.Length>0&&args[0]=="--convert") {
                    if(args.Length<3)throw new Exception("--convert INPUT OUTPUT [WIDTH: 64..8192]");
                    var options=new Options();
                    SelectRenderer(args,options);
                    if(args.Length>3&&args[3]!="--animate") {
                        options.WorkWidth=int.Parse(args[3]);
                    }
                    if(options.WorkWidth<64||options.WorkWidth>8192)throw new Exception("Width must be between 64 and 8192.");
                    var scaleArg=args.FirstOrDefault(x=>x.StartsWith("--effect-scale="));
                    if(scaleArg!=null){options.EffectScale=int.Parse(scaleArg.Substring(15));if(options.EffectScale<0||options.EffectScale>32)throw new Exception("Effect scale must be 0(auto) or 1..32.");}
                    var info=Engine.Probe(args[1]);long count=Engine.ConvertVideo(args[1],args[2],info,options,CancellationToken.None,(n,s)=>{});
                    var timing=Engine.Timing(info,options);
                    File.WriteAllText(args[2]+".result.txt","frames="+count+"\nfps="+timing.Rate+"\ninput_fps="+info.Rate+"\nphase_clock="+timing.ClockRate+"\nrenderer="+(options.UseGpu?"gpu":"cpu"),Encoding.UTF8);return 0;
                }
                if(args.Length>0&&args[0]=="--self-test") {
                    var opts=new Options();int w=256,h=24;var rgb=new byte[w*h*3];
                    for(int i=0;i<rgb.Length;i++)rgb[i]=(byte)(i%251);
                    using(var filter=new NativeFilter(w,h,opts)) {
                        var a=filter.Apply(rgb,0);var b=filter.Apply(rgb,1);
                        var repeated=filter.Apply(rgb,2);
                        if(filter.Width!=602||filter.Height!=48||a.SequenceEqual(b)||!a.SequenceEqual(repeated))throw new Exception("Two-phase flicker test failed");
                        using(var bmp=Engine.BitmapFromRgb(a,602,48))bmp.Save(args[1],ImageFormat.Png);
                    }
                    File.WriteAllText(args[1]+".txt","native filter, alternating-position rainbow flicker and bitmap conversion: PASS");return 0;
                }
                if(args.Length>0&&args[0]=="--test-timing") {
                    var animate=new Options();
                    var thirty=Engine.Timing(new VideoInfo {Rate="30/1",Fps=30},animate);
                    var fractional=Engine.Timing(new VideoInfo {Rate="30000/1001",Fps=30000.0/1001},animate);
                    var fast=Engine.Timing(new VideoInfo {Rate="120/1",Fps=120},animate);
                    var still=Engine.Timing(new VideoInfo {Rate="30/1",Fps=30},new Options());
                    var capture=Engine.Timing(new VideoInfo {Rate="45794160/766999",Fps=45794160.0/766999,NominalFps=2997.0/50},animate);
                    if(capture.ClockRate!="45794160/766999"||capture.Rate!="45794160/766999")throw new Exception("Capture average timing preservation failed");
                    if(thirty.Rate!="30/1"||fractional.Rate!="30000/1001"||still.Rate!="30/1")throw new Exception("Frame rate preservation failed");
                    for(int i=0;i<10000;i++) {
                        if(thirty.Phase(i)!=i%2||fractional.Phase(i)!=i%2||fast.Phase(i)!=(i/2)%2)throw new Exception("Phase clock drift");
                    }
                    File.WriteAllText(args[1],"30->30; 29.97->29.97; 120 fps timed phases; source-rate flicker; 10000 frames without drift: PASS");return 0;
                }
                if(args.Length>0&&args[0]=="--test-resolution") {
                    int w,h;
                    Engine.WorkingSize(new VideoInfo {Width=1920,Height=1080},new Options(),out w,out h);
                    if(w!=511||h!=287)throw new Exception("V1 widescreen default failed");
                    Engine.WorkingSize(new VideoInfo {Width=1440,Height=1080},new Options(),out w,out h);
                    if(w!=511||h!=383)throw new Exception("V1 4:3 aspect failed");
                    Engine.WorkingSize(new VideoInfo {Width=1080,Height=1920},new Options(),out w,out h);
                    if(w!=511||Math.Abs((double)w/h-1080.0/1920)>0.006)throw new Exception("Portrait aspect failed");
                    Engine.WorkingSize(new VideoInfo {Width=7680,Height=4320},new Options {WorkWidth=7680,EffectScale=1},out w,out h);
                    if(w!=7678||h!=4319)throw new Exception("8K processing failed");
                    Engine.WorkingSize(new VideoInfo {Width=7680,Height=4320},new Options {WorkWidth=7680},out w,out h);
                    if(w!=511||h!=287)throw new Exception("Automatic whole-effect magnification failed");
                    Engine.WorkingSize(new VideoInfo {Width=1920,Height=1080},new Options {WorkWidth=1920,EffectScale=4},out w,out h);
                    if(w!=478||h!=269)throw new Exception("Manual whole-effect magnification failed");
                    bool rejected=false;try{Engine.WorkingSize(new VideoInfo {Width=1080,Height=1920},new Options {WorkWidth=7680,EffectScale=1},out w,out h);}catch{rejected=true;}
                    if(!rejected)throw new Exception("Oversized portrait guard failed");
                    File.WriteAllText(args[1],"V1 width default, aspect ratio, 8K effect resolution and oversized guard: PASS");return 0;
                }
                if(args.Length>0&&args[0]=="--test-preview") {
                    var settings=new Options();SelectRenderer(args,settings);
                    var pictures=Engine.Preview(args[1],Engine.Probe(args[1]),settings,0);
                    using(pictures[0])pictures[0].Save(args[2]+"-before.png",ImageFormat.Png);
                    using(pictures[1])pictures[1].Save(args[2]+"-after.png",ImageFormat.Png);
                    pictures[2].Dispose();
                    return 0;
                }
                if(args.Length>0&&args[0]=="--test-cancel") {
                    using(var token=new CancellationTokenSource()) {
                        var info=Engine.Probe(args[1]);token.CancelAfter(200);var settings=new Options();SelectRenderer(args,settings);
                        try{Engine.ConvertVideo(args[1],args[2],info,settings,token.Token,(n,s)=>{});throw new Exception("Cancellation did not occur");}
                        catch(OperationCanceledException){if(File.Exists(args[2]))throw new Exception("Cancelled output was committed");return 0;}
                    }
                }
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                if(args.Length>0&&args[0]=="--ui-snapshot") {
                    using(var form=new MainForm()){
                        form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;
                        form.Show();Application.DoEvents();if(args.Length>2)form.LoadSnapshotVideo(args[2],args.Contains("--renderer=gpu"));form.PerformLayout();Application.DoEvents();
                        using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.Save(args[1],ImageFormat.Png);}form.Close();
                    }return 0;
                }
                Application.Run(new MainForm());return 0;
            } catch(Exception ex) {
                if(args.Length>0){File.WriteAllText(Path.Combine(Engine.Root,"last-error.txt"),ex.ToString());return 1;}
                MessageBox.Show(ex.Message,"NTSC Video Studio",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
            }
        }
    }
}
