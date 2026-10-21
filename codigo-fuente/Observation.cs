using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PulsoIT {
    internal static class PipeText {
        public static void Utf8(){Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});Console.SetError(new System.IO.StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)){AutoFlush=true});}
    }
    public sealed class ProcessMeasurement {public string Name;public int Id;public long StartTicks;public double? CpuPercent;public double MemoryMB;}
    public sealed class PerformanceSample {
        public double Elapsed;
        public Dictionary<string,double?> Metrics=new Dictionary<string,double?>();
        public List<ProcessMeasurement> Processes=new List<ProcessMeasurement>();
        public List<string> Notes=new List<string>();
        public double? Get(string key){double? value;return Metrics.TryGetValue(key,out value)&&value.HasValue&&!double.IsNaN(value.Value)&&!double.IsInfinity(value.Value)&&value>=0?value:null;}
    }
    public sealed class ObservationSession {
        public DateTime Started=DateTime.Now,Ended=DateTime.Now;
        public int RequestedSeconds;
        public bool Complete;
        public List<PerformanceSample> Samples=new List<PerformanceSample>();
        public List<string> Notes=new List<string>();
        public List<double> Values(string key){return Samples.Select(s=>s.Get(key)).Where(v=>v.HasValue).Select(v=>v.Value).ToList();}
        public string Stat(string key,string label,string unit){var v=Values(key);return label+": "+(v.Count==0?"sin verificar":("media "+v.Average().ToString("N1")+unit+", máximo "+v.Max().ToString("N1")+unit+" ("+v.Count+" muestras)"));}
        public string TopCpuSummary(){var top=Samples.SelectMany(s=>s.Processes).Where(p=>p.CpuPercent.HasValue).GroupBy(p=>p.Id+"|"+p.StartTicks).Select(g=>new{Name=g.First().Name,Id=g.First().Id,Average=g.Average(p=>p.CpuPercent.Value),Count=g.Count()}).Where(p=>p.Count>=3).OrderByDescending(p=>p.Average).FirstOrDefault();return top==null?"Procesos: cobertura insuficiente.":"Proceso con mayor media visible: "+top.Name+" (PID "+top.Id+"), "+top.Average.ToString("N1")+"% de CPU; no confirma que sea la causa.";}
        public string ShortSummary(){return Started.ToString("T")+" · "+Samples.Count+" muestras · "+(Complete?"intervalo terminado":"intervalo parcial")+Environment.NewLine+Stat("cpu","CPU","%")+Environment.NewLine+Stat("commit","Compromiso de memoria","%")+Environment.NewLine+Stat("available","RAM disponible"," MB")+Environment.NewLine+Stat("paging","Páginas escritas","/s")+Environment.NewLine+Stat("readMs","Latencia de lectura"," ms")+Environment.NewLine+Stat("writeMs","Latencia de escritura"," ms")+Environment.NewLine+Stat("queue","Cola de disco","")+Environment.NewLine+Stat("netErrors","Errores nuevos de red","/intervalo")+Environment.NewLine+TopCpuSummary()+(Notes.Count==0?"":Environment.NewLine+"Limitaciones: "+string.Join("; ",Notes.Distinct()));}
    }
    internal sealed class PdhCounters:IDisposable {
        [StructLayout(LayoutKind.Explicit,Size=16)]struct Formatted {[FieldOffset(0)]public uint Status;[FieldOffset(8)]public double Value;}
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhOpenQuery(string source,IntPtr user,out IntPtr query);
        [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhAddEnglishCounterW")]static extern uint Add(IntPtr query,string path,IntPtr user,out IntPtr counter);
        [DllImport("pdh.dll")]static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]static extern uint PdhGetFormattedCounterValue(IntPtr counter,uint format,out uint type,out Formatted value);
        [DllImport("pdh.dll")]static extern uint PdhCloseQuery(IntPtr query);
        IntPtr query;readonly Dictionary<string,IntPtr> handles=new Dictionary<string,IntPtr>();
        public readonly List<string> Notes=new List<string>();
        public PdhCounters(){
            uint code=PdhOpenQuery(null,IntPtr.Zero,out query);if(code!=0){Notes.Add("Contadores de rendimiento no disponibles (0x"+code.ToString("X8")+").");return;}
            var paths=new Dictionary<string,string>{{"cpu",@"\Processor(_Total)\% Processor Time"},{"commit",@"\Memory\% Committed Bytes In Use"},{"available",@"\Memory\Available MBytes"},{"paging",@"\Memory\Pages Output/sec"},{"readMs",@"\PhysicalDisk(_Total)\Avg. Disk sec/Read"},{"writeMs",@"\PhysicalDisk(_Total)\Avg. Disk sec/Write"},{"queue",@"\PhysicalDisk(_Total)\Current Disk Queue Length"},{"io",@"\PhysicalDisk(_Total)\Disk Transfers/sec"}};
            foreach(var pair in paths){IntPtr h;code=Add(query,pair.Value,IntPtr.Zero,out h);if(code==0)handles[pair.Key]=h;else Notes.Add(pair.Key+": contador ausente o sin acceso (0x"+code.ToString("X8")+").");}
            if(handles.Count>0)PdhCollectQueryData(query); // Los contadores de tasa necesitan dos lecturas.
        }
        public Dictionary<string,double?> Read(){
            var values=new Dictionary<string,double?>();if(query==IntPtr.Zero)return values;
            if(handles.Count==0)return values;
            uint status=PdhCollectQueryData(query);if(status!=0){string note="No se pudo recoger la muestra de rendimiento (0x"+status.ToString("X8")+").";if(!Notes.Contains(note))Notes.Add(note);foreach(var key in handles.Keys)values[key]=null;return values;}
            foreach(var pair in handles){uint type;Formatted value;uint result=PdhGetFormattedCounterValue(pair.Value,0x8200,out type,out value);double? v=result==0&&(value.Status==0||value.Status==1)&&!double.IsNaN(value.Value)&&!double.IsInfinity(value.Value)&&value.Value>=0?(double?)value.Value:null;
                if((pair.Key=="cpu"||pair.Key=="commit")&&v>100.5)v=null;
                if((pair.Key=="cpu"||pair.Key=="commit")&&v.HasValue)v=Math.Min(v.Value,100);
                if((pair.Key=="readMs"||pair.Key=="writeMs")&&v.HasValue)v*=1000;
                values[pair.Key]=v;
                if(!v.HasValue){string note=pair.Key+": muestra sin verificar (0x"+(result!=0?result:value.Status).ToString("X8")+").";if(!Notes.Contains(note))Notes.Add(note);}
            }
            return values;
        }
        public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;}}
    }
    internal sealed class ProcessPoint {public string Name;public int Id;public long StartTicks;public double Cpu,Memory,Captured;}
    internal sealed class NativeFallback {
        [StructLayout(LayoutKind.Sequential)]struct PerformanceInfo {
            public uint Size;public UIntPtr CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize;
            public uint HandleCount,ProcessCount,ThreadCount;
        }
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
        [DllImport("kernel32.dll")]static extern ushort GetActiveProcessorGroupCount();
        [DllImport("psapi.dll",SetLastError=true)]static extern bool GetPerformanceInfo(out PerformanceInfo info,uint size);
        long oldIdle,oldKernel,oldUser;bool hasCpu;
        public NativeFallback(){hasCpu=GetSystemTimes(out oldIdle,out oldKernel,out oldUser);}
        internal static double? CpuDelta(long idle0,long kernel0,long user0,long idle1,long kernel1,long user1){
            if(idle1<idle0||kernel1<kernel0||user1<user0)return null;
            double elapsed=(double)(kernel1-kernel0)+(user1-user0),idle=idle1-idle0;if(elapsed<=0||idle>elapsed)return null;
            return Math.Max(0,Math.Min(100,100*(elapsed-idle)/elapsed));
        }
        public void Fill(PerformanceSample sample){
            long idle,kernel,user;bool ok=GetSystemTimes(out idle,out kernel,out user);
            if(!sample.Get("cpu").HasValue&&ok&&hasCpu&&GetActiveProcessorGroupCount()==1){sample.Metrics["cpu"]=CpuDelta(oldIdle,oldKernel,oldUser,idle,kernel,user);if(sample.Get("cpu").HasValue)sample.Notes.Add("CPU: API nativa GetSystemTimes, usada al faltar el contador PDH.");}
            else if(!sample.Get("cpu").HasValue&&GetActiveProcessorGroupCount()>1)sample.Notes.Add("CPU sin verificar: la fuente alternativa no cubre todos los grupos de procesadores.");
            oldIdle=idle;oldKernel=kernel;oldUser=user;hasCpu=ok;
            if(!sample.Get("commit").HasValue||!sample.Get("available").HasValue){PerformanceInfo info;if(GetPerformanceInfo(out info,(uint)Marshal.SizeOf(typeof(PerformanceInfo)))){
                double committed=info.CommitTotal.ToUInt64(),limit=info.CommitLimit.ToUInt64(),available=info.PhysicalAvailable.ToUInt64(),total=info.PhysicalTotal.ToUInt64(),page=info.PageSize.ToUInt64();
                if(!sample.Get("commit").HasValue&&limit>0&&committed<=limit){sample.Metrics["commit"]=100*committed/limit;sample.Notes.Add("Compromiso: API nativa GetPerformanceInfo, usada al faltar el contador PDH.");}
                if(!sample.Get("available").HasValue&&page>0&&total>0&&available<=total){sample.Metrics["available"]=available*page/1048576.0;sample.Notes.Add("RAM disponible: API nativa GetPerformanceInfo, usada al faltar el contador PDH.");}
            }}
        }
    }
    public static class ObservationCollector {
        static Dictionary<int,ProcessPoint> Processes(Stopwatch watch,out int skipped){
            var result=new Dictionary<int,ProcessPoint>();skipped=0;
            Process[] processes;try{processes=Process.GetProcesses();}catch(System.ComponentModel.Win32Exception){skipped++;return result;}catch(System.Security.SecurityException){skipped++;return result;}
            foreach(var p in processes)using(p){try{result[p.Id]=new ProcessPoint{Name=p.ProcessName,Id=p.Id,StartTicks=p.StartTime.ToUniversalTime().Ticks,Cpu=p.TotalProcessorTime.TotalSeconds,Memory=p.WorkingSet64/1048576.0,Captured=watch.Elapsed.TotalSeconds};}catch(InvalidOperationException){skipped++;}catch(System.ComponentModel.Win32Exception){skipped++;}catch(NotSupportedException){skipped++;}catch(System.Security.SecurityException){skipped++;}}
            return result;
        }
        static Dictionary<string,long[]> Network(out int skipped){
            var result=new Dictionary<string,long[]>();skipped=0;
            NetworkInterface[] interfaces;try{interfaces=NetworkInterface.GetAllNetworkInterfaces();}catch(NetworkInformationException){skipped++;return result;}catch(System.Security.SecurityException){skipped++;return result;}
            foreach(var n in interfaces){try{if(n.OperationalStatus!=OperationalStatus.Up||!n.Supports(NetworkInterfaceComponent.IPv4))continue;var s=n.GetIPv4Statistics();result[n.Id]=new[]{s.IncomingPacketsWithErrors+s.OutgoingPacketsWithErrors,s.IncomingPacketsDiscarded+s.OutgoingPacketsDiscarded};}catch(NetworkInformationException){skipped++;}catch(System.Security.SecurityException){skipped++;}}
            return result;
        }
        internal static List<ProcessMeasurement> ProcessDelta(Dictionary<int,ProcessPoint> before,Dictionary<int,ProcessPoint> after){
            var result=new List<ProcessMeasurement>();foreach(var p in after.Values){ProcessPoint old;double? use=null;if(before.TryGetValue(p.Id,out old)&&p.StartTicks==old.StartTicks&&p.Captured>old.Captured&&p.Cpu>=old.Cpu)use=Math.Min(100,100*(p.Cpu-old.Cpu)/(p.Captured-old.Captured)/Environment.ProcessorCount);result.Add(new ProcessMeasurement{Name=p.Name,Id=p.Id,StartTicks=p.StartTicks,CpuPercent=use,MemoryMB=p.Memory});}
            return result.OrderByDescending(p=>p.CpuPercent??-1).Take(8).Concat(result.OrderByDescending(p=>p.MemoryMB).Take(8)).GroupBy(p=>p.Id).Select(g=>g.First()).ToList();
        }
        public static int Worker(int seconds,bool synthetic){
            PipeText.Utf8();var serializer=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};var clock=Stopwatch.StartNew();
            if(synthetic){for(int i=1;i<=seconds;i++){Thread.Sleep(100);Console.WriteLine(serializer.Serialize(Synthetic(i)));Console.Out.Flush();}return 0;}
            using(var pdh=new PdhCounters()){
                var fallback=new NativeFallback();int skipped;var before=Processes(clock,out skipped);int netSkipped;var netBefore=Network(out netSkipped);int sequence=1;
                while(clock.Elapsed.TotalSeconds<seconds){
                    int delay=Math.Max(1,(int)((sequence-clock.Elapsed.TotalSeconds)*1000));Thread.Sleep(delay);sequence=(int)Math.Floor(clock.Elapsed.TotalSeconds)+1;
                    var sample=new PerformanceSample{Elapsed=clock.Elapsed.TotalSeconds,Metrics=pdh.Read()};sample.Notes.AddRange(pdh.Notes);fallback.Fill(sample);
                    var after=Processes(clock,out skipped);sample.Processes=ProcessDelta(before,after);before=after;if(skipped>0)sample.Notes.Add("No se pudo leer CPU/inicio de "+skipped+" procesos.");
                    var netAfter=Network(out netSkipped);long errors=0,drops=0;int pairs=0;bool reset=false;
                    foreach(var item in netAfter){long[] old;if(netBefore.TryGetValue(item.Key,out old)){if(item.Value[0]>=old[0]&&item.Value[1]>=old[1]){errors+=item.Value[0]-old[0];drops+=item.Value[1]-old[1];pairs++;}else reset=true;}}
                    sample.Metrics["netErrors"]=pairs>0?(double?)errors:null;sample.Metrics["netDrops"]=pairs>0?(double?)drops:null;netBefore=netAfter;
                    if(reset)sample.Notes.Add("Se reinició algún contador de red; su intervalo no se suma.");if(netSkipped>0)sample.Notes.Add("Parte de los contadores IPv4 no pudo leerse.");
                    if(pairs==0)sample.Notes.Add("Sin pares de contadores válidos en interfaces IPv4 activas.");
                    if(!sample.Metrics.Values.Any(v=>v.HasValue))sample.Notes.Add("Sin métricas válidas. Comprueba permisos o disponibilidad de contadores.");
                    Console.WriteLine(serializer.Serialize(sample));Console.Out.Flush();
                }
            }
            return 0;
        }
        public static PerformanceSample Synthetic(int i){return new PerformanceSample{Elapsed=i,Metrics=new Dictionary<string,double?>{{"cpu",96},{"commit",94},{"available",400},{"paging",120},{"readMs",80},{"writeMs",65},{"queue",3},{"io",40},{"netErrors",0},{"netDrops",0}},Processes=new List<ProcessMeasurement>{new ProcessMeasurement{Name="Proceso simulado",Id=123,CpuPercent=70,MemoryMB=2200}}};}
    }
    public static class ObservationRunner {
        public static ObservationSession Run(int seconds,CancellationToken token,Action<PerformanceSample> publish,bool synthetic=false){
            var session=new ObservationSession{RequestedSeconds=seconds};var serializer=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};object gate=new object();string error="";
            using(var p=new Process()){
                p.StartInfo=new ProcessStartInfo(Application.ExecutablePath,(synthetic?"--observe-test-worker ":"--observe-worker ")+seconds){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                p.OutputDataReceived+=(s,e)=>{if(string.IsNullOrWhiteSpace(e.Data))return;try{var point=serializer.Deserialize<PerformanceSample>(e.Data);if(point==null||point.Metrics==null||point.Elapsed<0)throw new FormatException("Muestra inválida.");lock(gate)session.Samples.Add(point);if(publish!=null)publish(point);}catch(Exception ex){lock(gate)error="Una muestra no pudo leerse: "+ex.Message;}};
                p.ErrorDataReceived+=(s,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))lock(gate)error=e.Data.Substring(0,Math.Min(2000,e.Data.Length));};
                p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();var watch=Stopwatch.StartNew();
                try{while(!p.WaitForExit(100)){if(token.IsCancellationRequested)break;if(watch.Elapsed.TotalSeconds>seconds+20){error="El proveedor tardó demasiado; se conserva el intervalo parcial.";break;}}
                    if(!p.HasExited){p.Kill();p.WaitForExit(2000);}if(p.HasExited)p.WaitForExit();
                    session.Complete=!token.IsCancellationRequested&&p.ExitCode==0&&error.Length==0;
                }finally{try{if(!p.HasExited){p.Kill();p.WaitForExit(2000);}}catch(InvalidOperationException){}session.Ended=DateTime.Now;}
                lock(gate){session.Notes=session.Samples.SelectMany(s=>s.Notes??new List<string>()).Distinct().ToList();if(error.Length>0)session.Notes.Add(error);if(session.Samples.Count==0)session.Notes.Add("El proveedor no devolvió muestras.");}
            }
            return session;
        }
    }
    public sealed class TrendView:Control {
        public int Duration=30;
        public readonly List<PerformanceSample> Samples=new List<PerformanceSample>();
        public TrendView(){DoubleBuffered=true;BackColor=Color.White;MinimumSize=new Size(400,280);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            string[][] groups={new[]{"cpu","CPU","%"},new[]{"commit","Memoria comprometida","%"},new[]{"available","RAM disponible","MB"},new[]{"readMs","Latencia de disco (lectura/escritura)","ms","writeMs"},new[]{"paging","Páginas escritas a paginación","/s"},new[]{"netErrors","Red IPv4: errores / descartes","/intervalo","netDrops"}};
            for(int i=0;i<groups.Length;i++){
                var r=new Rectangle(i%2*Width/2+8,i/2*Height/3+6,Width/2-16,Height/3-12);var plot=new Rectangle(r.Left+42,r.Top+28,Math.Max(10,r.Width-52),Math.Max(10,r.Height-48));
                e.Graphics.DrawRectangle(Pens.LightGray,plot);var keys=groups[i].Skip(3).PrependCompat(groups[i][0]).ToArray();var values=Samples.SelectMany(s=>keys.Select(k=>s.Get(k))).Where(v=>v.HasValue).Select(v=>v.Value).ToList();double max=groups[i][2]=="%"?100:Math.Max(1,values.Count==0?1:values.Max()*1.1);double duration=Math.Max(Duration,Samples.Count==0?Duration:Samples.Max(s=>s.Elapsed));
                string latest=Samples.Count==0||!Samples.Last().Get(groups[i][0]).HasValue?"sin datos":Samples.Last().Get(groups[i][0]).Value.ToString("N1")+groups[i][2];
                TextRenderer.DrawText(e.Graphics,groups[i][1]+" · "+latest,Font,new Rectangle(r.Left,r.Top,r.Width,25),ForeColor,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics,max.ToString("N0"),Font,new Point(r.Left,plot.Top),Color.Gray);TextRenderer.DrawText(e.Graphics,"0",Font,new Point(r.Left,plot.Bottom-14),Color.Gray);
                for(int series=0;series<keys.Length;series++){PointF? previous=null;using(var pen=new Pen(series==0?Color.FromArgb(0,135,123):Color.FromArgb(214,129,30),2))foreach(var s in Samples){double? v=s.Get(keys[series]);if(!v.HasValue){previous=null;continue;}var p=new PointF((float)(plot.Left+Math.Min(1,s.Elapsed/duration)*plot.Width),(float)(plot.Bottom-Math.Min(1,v.Value/max)*plot.Height));if(previous.HasValue)e.Graphics.DrawLine(pen,previous.Value,p);previous=p;}}
                TextRenderer.DrawText(e.Graphics,"0 s",Font,new Point(plot.Left,plot.Bottom+2),Color.Gray);TextRenderer.DrawText(e.Graphics,duration.ToString("N0")+" s",Font,new Point(plot.Right-36,plot.Bottom+2),Color.Gray);
            }
        }
    }
    static class EnumerableCompat {public static IEnumerable<T> PrependCompat<T>(this IEnumerable<T> source,T value){yield return value;foreach(var item in source)yield return item;}}
    public sealed class ObservationForm:Form {
        readonly int seconds;readonly TrendView chart=new TrendView();readonly TextBox details=new TextBox();readonly Label status=new Label();readonly Button stop=new Button();readonly ConcurrentQueue<PerformanceSample> queue=new ConcurrentQueue<PerformanceSample>();
        CancellationTokenSource cancel=new CancellationTokenSource();bool running,wantClose;public ObservationSession Session;
        public ObservationForm(int seconds,bool preview=false){
            this.seconds=seconds;chart.Duration=seconds;Text="Observar mientras reproduces el problema · "+seconds+" s";ClientSize=new Size(1000,730);MinimumSize=new Size(880,640);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=4};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,65));root.RowStyles.Add(new RowStyle(SizeType.Percent,35));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
            root.Controls.Add(new Label{Text="Reproduce el fallo durante la observación. No se ejecutan cargas de estrés, pruebas de red ni reparaciones.\r\nVerde: primera serie. Naranja: escritura de disco o descartes de red. Huecos: datos sin verificar.",AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10)},0,0);
            chart.Dock=DockStyle.Fill;root.Controls.Add(chart,0,1);details.Multiline=true;details.ReadOnly=true;details.MaxLength=0;details.ScrollBars=ScrollBars.Vertical;details.Dock=DockStyle.Fill;root.Controls.Add(details,0,2);
            var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};stop.Text="Detener y conservar";stop.AutoSize=true;stop.Padding=new Padding(8,4,8,4);status.AutoSize=true;status.Margin=new Padding(12,8,0,0);footer.Controls.Add(stop);footer.Controls.Add(status);root.Controls.Add(footer,0,3);
            stop.Click+=(s,e)=>{if(running){cancel.Cancel();stop.Enabled=false;}else Close();};FormClosing+=(s,e)=>{if(running){e.Cancel=true;wantClose=true;cancel.Cancel();}};FormClosed+=(s,e)=>cancel.Dispose();
            if(preview){for(int i=1;i<=seconds;i++)chart.Samples.Add(ObservationCollector.Synthetic(i));Session=new ObservationSession{RequestedSeconds=seconds,Complete=true,Samples=chart.Samples.ToList()};details.Text="DATOS DE DEMOSTRACIÓN · no corresponden al PC."+Environment.NewLine+Session.ShortSummary();status.Text="Vista previa con datos simulados";stop.Text="Volver";}else Shown+=(s,e)=>Start();
        }
        void Drain(){PerformanceSample sample;bool changed=false;while(queue.TryDequeue(out sample)){chart.Samples.Add(sample);changed=true;}if(changed){chart.Invalidate();var last=chart.Samples.Last();status.Text=last.Elapsed.ToString("N0")+" / "+seconds+" s · "+chart.Samples.Count+" muestras";details.Text="Cola de disco: "+(last.Get("queue").HasValue?last.Get("queue").Value.ToString("N1"):"sin verificar")+" · Actividad: "+(last.Get("io").HasValue?last.Get("io").Value.ToString("N1")+" operaciones/s":"sin verificar")+Environment.NewLine+"Procesos visibles (CPU % / memoria residente MB):"+Environment.NewLine+string.Join(Environment.NewLine,last.Processes.OrderByDescending(p=>p.CpuPercent??-1).Select(p=>p.Name+" · PID "+p.Id+" · "+(p.CpuPercent.HasValue?p.CpuPercent.Value.ToString("N1")+"%":"CPU sin verificar")+" · "+p.MemoryMB.ToString("N0")+" MB"));}}
        async void Start(){running=true;var timer=new System.Windows.Forms.Timer{Interval=250};timer.Tick+=(s,e)=>Drain();timer.Start();try{Session=await Task.Run(()=>ObservationRunner.Run(seconds,cancel.Token,p=>queue.Enqueue(p)));Drain();details.Text=Session.ShortSummary();status.Text=Session.Complete?"Observación terminada · datos solo en memoria":"Observación parcial · datos conservados";}catch(Exception ex){Session=new ObservationSession{RequestedSeconds=seconds,Samples=chart.Samples.ToList(),Ended=DateTime.Now};Session.Notes.Add(ex.Message);details.Text=Session.ShortSummary();status.Text="No se completó toda la observación.";}finally{running=false;timer.Stop();timer.Dispose();stop.Enabled=true;stop.Text="Volver al diagnóstico";if(wantClose)Close();}}
    }
}
