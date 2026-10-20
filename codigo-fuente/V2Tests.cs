using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PulsoIT {
    static class V2Tests {
        static int count;
        static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException("Prueba 2.0: "+message);count++;}
        static ObservationSession Healthy(){var s=new ObservationSession{Started=DateTime.Now.AddSeconds(-30),Ended=DateTime.Now,RequestedSeconds=30,Complete=true};for(int i=1;i<=30;i++){var p=ObservationCollector.Synthetic(i);p.Metrics["cpu"]=10;p.Metrics["commit"]=40;p.Metrics["available"]=8192;p.Metrics["paging"]=0;p.Metrics["readMs"]=2;p.Metrics["writeMs"]=2;p.Metrics["queue"]=0;p.Metrics["io"]=20;s.Samples.Add(p);}return s;}
        static List<Hypothesis> Build(ObservationSession s){return Triage.Build(new Finding[0],s,new IncidentContext(),DateTime.Now);}
        static Finding Fact(string code,string state){return new Finding("red",state,"Prueba","Resumen","Evidencia","Revisar"){Code=code};}
        public static void Run(){
            var healthy=Healthy();Assert(Build(healthy).Count==0,"un intervalo normal no debe inventar una causa");
            healthy.Samples[0].Metrics["cpu"]=99;Assert(!Build(healthy).Any(h=>h.Title.StartsWith("CPU")),"un pico aislado no es carga sostenida");
            var high=Healthy();foreach(var p in high.Samples)p.Metrics["cpu"]=96;Assert(Build(high).Any(h=>h.Title.StartsWith("CPU")),"CPU sostenida");
            var missing=Healthy();foreach(var p in missing.Samples)foreach(var key in p.Metrics.Keys.ToList())p.Metrics[key]=null;Assert(Build(missing).Count==0&&missing.Stat("cpu","CPU","%").Contains("sin verificar"),"métricas ausentes no equivalen a cero");
            var memory=Healthy();foreach(var p in memory.Samples){p.Metrics["commit"]=96;p.Metrics["paging"]=200;p.Metrics["available"]=250;}Assert(Build(memory).Any(h=>h.Title=="Posible presión de memoria"&&h.Confidence.Contains("consistente")),"correlación de compromiso, disponible y paginación");
            foreach(var p in memory.Samples){p.Metrics["commit"]=40;p.Metrics["paging"]=0;}Assert(Build(memory).Any(h=>h.Confidence.Contains("moderada"))&&!Build(memory).Any(h=>h.Title=="Posible presión de memoria"),"memoria disponible sola necesita más evidencia");
            var disk=Healthy();foreach(var p in disk.Samples){p.Metrics["readMs"]=100;p.Metrics["writeMs"]=100;p.Metrics["io"]=0;}Assert(!Build(disk).Any(h=>h.Title.Contains("Almacenamiento")),"latencia sin actividad no se diagnostica como cuello de botella");
            foreach(var p in disk.Samples)p.Metrics["io"]=20;Assert(Build(disk).Any(h=>h.Title.Contains("Almacenamiento")),"latencia durante actividad");
            high.Ended=DateTime.Now.AddHours(-1);Assert(Build(high).Count==0,"observaciones antiguas no sustentan hipótesis actuales");
            high.Ended=DateTime.Now;Assert(Triage.Build(new Finding[0],high,new IncidentContext{IsNow=false},DateTime.Now).Count==0,"medidas actuales no reconstruyen una incidencia pasada");
            var blocked=Fact("network.tcp","unknown");blocked.Data["Target"]="servidor";Assert(Triage.Build(new[]{blocked},null,new IncidentContext(),DateTime.Now).Count==0,"bloqueo de permisos no es fallo de red");
            var dns=Fact("network.dns","good");dns.Data["Target"]="a";var tcp=Fact("network.tcp","warning");tcp.Data["Target"]="b";Assert(!Triage.Build(new[]{dns,tcp},null,new IncidentContext(),DateTime.Now).Any(h=>h.Title.Contains("nombre resuelve")),"no correlacionar destinos distintos");
            var space=Fact("disk.space","critical");space.Time=DateTime.Now.AddHours(-1);Assert(Triage.Build(new[]{space},null,new IncidentContext(),DateTime.Now).Count==0,"resultado antiguo excluido");space.Time=DateTime.Now;space.ScopeKey="server:otro";Assert(Triage.Build(new[]{space},null,new IncidentContext(),DateTime.Now).Count==0,"un servidor remoto no explica la lentitud del PC local");
            space.ScopeKey="local:"+Environment.MachineName;space.IncidentKey="otro-momento";Assert(Triage.Build(new[]{space},null,new IncidentContext(),DateTime.Now).Count==0,"ventana de incidencia distinta excluida");
            var old=Fact("network.dns.config","info");old.Data["Addresses"]="antes";var reference=MemoryReference.Capture(new[]{old},null);old.Data["Addresses"]="mutado";Assert(reference.Findings[0].MetaText("Addresses")=="antes","referencia independiente de las consultas nuevas");
            var newer=old.Copy();newer.Time=reference.Captured.AddSeconds(1);newer.Data["Addresses"]="después";Assert(reference.Compare(new[]{newer},null).Contains("CAMBIO"),"comparación de valores");newer.ScopeKey="local:otro";Assert(reference.Compare(new[]{newer},null).Contains("0 comprobaciones comparables"),"no comparar equipos distintos");
            Assert(reference.Compare(reference.Findings,null).Contains("Vuelve a consultar"),"no presentar consultas previas como nuevas");
            newer=old.Copy();newer.Time=reference.Captured.AddSeconds(1);newer.IncidentKey="otra-incidencia";Assert(!reference.Compare(new[]{newer},null).Contains("YA NO CONSTA"),"no declarar ausentes resultados de otra ventana de incidencia");
            foreach(int code in new[]{401,403,404,405})Assert(HttpsProbe.Classify("ejemplo",443,"response",code,"").State=="info","HTTP "+code+" no significa host caído");
            Assert(HttpsProbe.Classify("ejemplo",443,"response",503,"").State=="warning","HTTP 503");Assert(HttpsProbe.Classify("ejemplo",443,"tls",null,"").State=="warning","validación TLS");Assert(HttpsProbe.Classify("ejemplo",443,"blocked",null,"").State=="unknown","HTTPS bloqueado");
            var before=new Dictionary<int,ProcessPoint>{{1,new ProcessPoint{Id=1,StartTicks=10,Captured=1,Cpu=1}}};var after=new Dictionary<int,ProcessPoint>{{1,new ProcessPoint{Id=1,StartTicks=20,Captured=2,Cpu=2,Name="Reutilizado"}}};Assert(!ObservationCollector.ProcessDelta(before,after)[0].CpuPercent.HasValue,"PID reutilizado");
            after[1].StartTicks=10;after[1].Cpu=1+Environment.ProcessorCount*.5;Assert(Math.Abs(ObservationCollector.ProcessDelta(before,after)[0].CpuPercent.Value-50)<.001,"normalización de CPU por proceso");
            var context=new IncidentContext{IsNow=false,IncidentLocal=new DateTime(2026,10,19,10,0,0,DateTimeKind.Local),MarginMinutes=15};var command=new Engine().Command("eventos","",443,"",24,context);Assert(command.Contains(context.FromUtc.Value.ToString("o"))&&command.Contains(context.ToUtc.Value.ToString("o")),"ventana de consulta con zona horaria");
            var observed=ObservationRunner.Run(2,CancellationToken.None,null,true);Assert(observed.Complete&&observed.Samples.Count==2&&observed.Values("cpu").Count==2,"proceso de observación, transporte UTF-8 y métricas");
            using(var cancel=new CancellationTokenSource()){cancel.CancelAfter(800);var watch=System.Diagnostics.Stopwatch.StartNew();var partial=ObservationRunner.Run(60,cancel.Token,null,true);Assert(!partial.Complete&&watch.Elapsed.TotalSeconds<5,"cancelación del proceso de observación");}
            Console.Error.WriteLine("Pruebas 2.0 correctas: "+count+" aserciones de hipótesis, comparación, red, contexto y observación.");
        }
    }
}
