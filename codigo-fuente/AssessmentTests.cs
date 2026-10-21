using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PulsoIT {
    static class AssessmentTests {
        static int count;static DateTime now;
        static void Check(bool yes,string name){if(!yes)throw new InvalidOperationException("Diagnóstico 2.1: "+name);count++;}
        static Finding Fact(string section,string code,string state="info"){return new Finding(section,state,"Prueba "+code,"Dato simulado","Original simulado","Revisar sin modificar"){Code=code,Time=now.AddSeconds(-1),IncidentKey="now"};}
        static Finding Event(string provider,int id,int minutesAgo){var f=Fact("eventos","event.group","warning");f.Data["Provider"]=provider;f.Data["Id"]=id;f.Data["LastSeenUtc"]=now.AddMinutes(-minutesAgo).ToUniversalTime().ToString("o");f.Data["Occurrences"]=1;return f;}
        static List<Finding> Normal(){return Catalog.Sections.Select(s=>Fact(s.Id,"context")).ToList();}
        static Dictionary<string,DateTime> Completed(){return Catalog.Sections.ToDictionary(s=>s.Id,s=>now.AddMilliseconds(-100));}
        static ObservationSession NormalObservation(){var s=new ObservationSession{Started=now.AddSeconds(-31),Ended=now.AddSeconds(-1),RequestedSeconds=30,Complete=true};for(int i=1;i<=30;i++){var p=ObservationCollector.Synthetic(i);p.Metrics["cpu"]=20;p.Metrics["commit"]=40;p.Metrics["available"]=8000;p.Metrics["paging"]=0;p.Metrics["readMs"]=3;p.Metrics["writeMs"]=3;s.Samples.Add(p);}return s;}
        static DiagnosticAssessment Build(IEnumerable<Finding> f,ObservationSession s=null,IncidentContext c=null,bool running=false){return Assessment.Build(f,s,c??new IncidentContext(),now,Completed(),Catalog.Sections.Select(x=>x.Id),running);}
        public static void Run(){
            now=DateTime.Now;
            var good=Build(Normal(),NormalObservation());Check(good.State=="good"&&good.CanReassure&&good.Cases.Count==0,"sin incidencias con cobertura completa");Check(good.Report.Contains("no certifica")&&good.Report.Contains("12/12"),"conclusión normal con alcance explícito");
            var empty=Build(new Finding[0]);Check(empty.State=="pending"&&empty.Reviewed==0&&!empty.CanReassure,"sin consultas no equivale a equipo sano");
            var partial=Build(new[]{Fact("software","inventory")},NormalObservation());Check(!partial.CanReassure&&partial.State=="pending","inventario solo no permite concluir salud");
            Check(!Assessment.Build(Normal(),NormalObservation(),new IncidentContext(),now,Completed(),Catalog.Sections.Select(x=>x.Id),false,new[]{"discos"}).CanReassure,"fallo de transporte mantiene la sección parcial aunque haya resultados");
            var pending=Normal();pending.Add(Fact("discos","disk.health","unknown"));Check(Build(pending,NormalObservation()).State=="pending","acceso denegado impide conclusión verde");
            Check(!Build(Normal()).CanReassure,"sin observar rendimiento no se certifica cobertura completa");
            var noMetrics=NormalObservation();foreach(var s in noMetrics.Samples)s.Metrics["cpu"]=null;Check(!Build(Normal(),noMetrics).CanReassure,"métricas ausentes impiden conclusión tranquilizadora completa");
            var cancelled=NormalObservation();cancelled.Complete=false;Check(!Build(Normal(),cancelled).CanReassure,"observación interrumpida no se presenta como completa");
            Check(Build(Normal(),NormalObservation(),null,true).State=="running","consulta en curso nunca presenta veredicto final");
            var old=Fact("discos","disk.space","critical");old.Time=now.AddHours(-1);Check(Build(new[]{old}).Cases.Count==0,"resultado antiguo excluido");
            old.Time=now.AddMinutes(1);Check(Build(new[]{old}).Cases.Count==0,"resultado futuro excluido");
            old.Time=now.AddSeconds(-1);old.ScopeKey="server:otro";Check(Build(new[]{old}).Cases.Count==0&&Build(new[]{old}).Reviewed==0,"servidor remoto no contamina el PC");
            old.ScopeKey="local:otra-maquina";Check(Build(new[]{old}).Cases.Count==0,"otro equipo con prefijo local tampoco se mezcla");
            old.ScopeKey="local:"+Environment.MachineName;old.IncidentKey="otra-incidencia";Check(Build(new[]{old}).Cases.Count==0,"otro contexto excluido");
            var stop=Event("Microsoft-Windows-WER-SystemErrorReporting",1001,5);var reboot=Event("Microsoft-Windows-Kernel-Power",41,4);var pair=Build(new[]{stop,reboot});Check(pair.Cases.Count==1&&pair.Cases[0].Id=="restart.stop"&&pair.Cases[0].Evidence.Count==2,"dos registros del reinicio se agrupan en un episodio");
            var apart=Event("Microsoft-Windows-Kernel-Power",41,40);Check(Build(new[]{stop,apart}).Cases.Count==2,"episodios separados no se correlacionan");
            var foreign=Event("MiAplicacion",1001,5);Check(PlainLanguage.EventKind(foreign)==""&&!Build(new[]{foreign}).Cases.Any(x=>x.Id=="restart.stop"),"ID sin proveedor correcto no es BugCheck");
            var xml=Event("Application Error",1000,2);xml.Data["Subject"]="editor.exe";Check(PlainLanguage.Detail(xml).Contains("editor.exe")&&PlainLanguage.Explain(xml).Category=="application","explicación de aplicación con identidad");
            var whea=Event("Microsoft-Windows-WHEA-Logger",19,3);Check(Build(new[]{stop,reboot,whea}).Cases[0].Conclusion.Contains("error de parada")&&Build(new[]{stop,reboot,whea}).Cases[0].Confidence.Contains("hardware"),"WHEA próximo aporta una pista, no pieza culpable");
            Check(PlainLanguage.Explain(whea).Impact.Contains("corregidos"),"WHEA corregido no se interpreta automáticamente como hardware roto");
            var ancient=Event("Application Error",1000,120);var history=Normal();history.Add(ancient);Check(Build(history,NormalObservation()).Cases.Count==0&&Build(history,NormalObservation()).Historical==1,"registro antiguo se presenta como antecedente");
            Check(Build(history,NormalObservation()).Report.Contains("ANTECEDENTES"),"antecedentes visibles, no ocultados");
            var past=new IncidentContext{IsNow=false,IncidentLocal=now.AddHours(-2),MarginMinutes=15};ancient.IncidentKey=past.EvidenceKey;Check(Build(new[]{ancient},null,past).Cases.Count==1&&!Build(new[]{ancient},NormalObservation(),past).CanReassure,"evento pasado sí se analiza cuando se indica su hora");
            var future=Event("Application Error",1000,-20);var futureList=Normal();futureList.Add(future);Check(!Build(futureList,NormalObservation()).CanReassure&&Build(futureList).Report.Contains("fecha futura"),"reloj incoherente se declara pendiente");
            var limit=Fact("eventos","event.coverage");limit.Data["Limited"]=true;var limited=Normal();limited.Add(limit);Check(!Build(limited,NormalObservation()).CanReassure,"límite de muestra impide cobertura completa");
            var d=Fact("red","network.dns","warning");d.Data["Target"]="servidor";var t=Fact("red","network.tcp","good");t.Data["Target"]="servidor";var dnsTcp=Build(new[]{d,t});Check(dnsTcp.Cases.Count==1&&dnsTcp.Cases[0].Id.StartsWith("network.dns-alternate"),"DNS fallido con TCP abierto no se presenta como equipo caído");
            t.Data["Target"]="otro";Check(!Build(new[]{d,t}).Cases.Any(x=>x.Id.StartsWith("network.dns-alternate")),"destinos distintos no se correlacionan");
            d.State="good";t.State="warning";t.Data["Target"]="servidor";Check(Build(new[]{d,t}).Cases[0].Title.Contains("puerto"),"resolución correcta con puerto fallido");
            t.State="unknown";Check(Build(new[]{d,t}).Cases.Count==0,"prueba TCP sin permiso no se convierte en fallo de red");
            var printing=new[]{Fact("impresoras","printer.spooler","warning"),Fact("impresoras","printer.queue","warning"),Fact("impresoras","printer.state","warning")};Check(Build(printing).Cases.Count==1&&Build(printing).Cases[0].Evidence.Count==3,"incidencias de impresión agrupadas");
            var service=Fact("servicios","service.errors","warning");service.Data["Names"]=new[]{"ServicioUno","Nombre visible"};var scm=Event("Service Control Manager",7031,2);scm.Data["Subject"]="Nombre visible";Check(Build(new[]{service,scm}).Cases.Count==1&&Build(new[]{service,scm}).Cases[0].Id=="service.correlated","servicio exacto enlaza estado y registro");
            scm.Data["Subject"]="Otro servicio";Check(!Build(new[]{service,scm}).Cases.Any(c=>c.Id=="service.correlated"),"servicios diferentes no se mezclan");
            var vpn=Fact("vpn","vpn.events","warning");vpn.Data["TimesUtc"]=new[]{now.AddHours(-3).ToUniversalTime().ToString("o")};Check(Build(new[]{vpn}).Cases.Count==0&&Build(new[]{vpn}).Historical==1,"intento VPN antiguo no se declara fallo actual");
            var rdp=new[]{Fact("rdp","rdp.listen","warning"),Fact("rdp","rdp.service","warning")};Check(Build(rdp).Cases.Count==1&&Build(rdp).Cases[0].Impact.Contains("entrar en este PC"),"RDP local no se atribuye a un servidor remoto");
            var diskError=Event("disk",7,1);Check(Build(new[]{diskError,foreign}).Cases[0].Id=="storage.integrity","integridad prioritaria sobre registro sin regla");
            var threat=Fact("defender","security.active","critical");Check(Build(new[]{threat}).State=="critical","amenaza activa prioritaria");
            var load=NormalObservation();foreach(var p in load.Samples){p.Metrics["commit"]=96;p.Metrics["paging"]=100;p.Metrics["readMs"]=90;p.Metrics["writeMs"]=90;}Check(Build(Normal(),load).Cases.Any(x=>x.Id=="performance.memory-io"),"memoria y disco se relacionan con muestras simultáneas");
            var alternating=NormalObservation();for(int i=0;i<30;i++){var p=alternating.Samples[i];p.Metrics["commit"]=96;p.Metrics["available"]=400;p.Metrics["paging"]=i<15?100:0;p.Metrics["readMs"]=i>=15?90:3;p.Metrics["writeMs"]=i>=15?90:3;}Check(!Build(Normal(),alternating).Cases.Any(x=>x.Id=="performance.memory-io"),"síntomas en momentos distintos no prueban el mismo mecanismo");
            Check(PlainLanguage.Explain(Fact("defender","security.active")).Impact.Contains("No es un análisis"),"sin amenazas visibles no significa equipo libre de malware");
            Check(PlainLanguage.Explain(Fact("servicios","service.errors")).Impact.Contains("1077"),"servicio nunca iniciado se explica como contexto");
            Check(PlainLanguage.Text("a\r\nb\0c").IndexOf('\0')<0,"texto de registros sin caracteres de control");
            Check(Math.Abs(NativeFallback.CpuDelta(100,200,100,150,270,130).Value-50)<0.001,"CPU alternativa descuenta el tiempo inactivo incluido en kernel");
            Check(!NativeFallback.CpuDelta(0,0,0,200,100,0).HasValue&&!NativeFallback.CpuDelta(0,0,0,0,0,0).HasValue,"deltas nativos inválidos no se convierten en CPU normal");
            foreach(var section in Catalog.Sections){var f=Fact(section.Id,"sin.regla","warning");Check(Build(new[]{f}).Cases.Count==1,"aviso sin regla nunca se pierde: "+section.Id);}
            // Dos grupos con antecedentes próximos y hechos actuales separados no son el mismo episodio.
            stop.Data["TimesUtc"]=new[]{now.AddHours(-4).ToUniversalTime().ToString("o"),now.AddMinutes(-2).ToUniversalTime().ToString("o")};apart.Data["TimesUtc"]=new[]{now.AddHours(-4).AddMinutes(1).ToUniversalTime().ToString("o"),now.AddMinutes(-40).ToUniversalTime().ToString("o")};Check(!Assessment.Near(stop,apart,new IncidentContext(),now),"no correlacionar por una coincidencia antigua dentro de grupos");
            var scripts=Catalog.Sections.ToDictionary(x=>x.Id,x=>(object)"[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false);[Console]::WriteLine('{\"State\":\"info\",\"Title\":\"Consulta simulada\",\"Summary\":\"Sin avisos simulados\",\"Evidence\":\"Simulado\",\"Next\":\"Ninguno\"}');");
            bool observationCalled=false;
            bool passed=false,started=false;var watch=System.Diagnostics.Stopwatch.StartNew();
            using(var form=new MainForm(new Engine(scripts),token=>{observationCalled=true;now=DateTime.Now;return NormalObservation();}))using(var timer=new System.Windows.Forms.Timer{Interval=50}){
                form.ShowInTaskbar=false;form.Opacity=0;
                form.Shown+=(sender,args)=>form.BeginInvoke(new Action(()=>{started=true;form.RunCompleteForTest();}));
                timer.Tick+=(sender,args)=>{if(started&&!form.DiagnosticBusy){passed=observationCalled&&form.LastAssessment.State=="good"&&form.LastAssessment.Reviewed==12;timer.Stop();form.Close();}else if(watch.Elapsed.TotalSeconds>25)form.CancelForTest();};
                timer.Start();Application.Run(form);
            }
            Check(passed,"recorrido completo ejecuta las doce secciones y observación y termina con conclusión final");
            Console.Error.WriteLine("Diagnóstico 2.1 correcto: "+count+" escenarios de conclusión, cobertura, correlación y explicación.");
        }
    }
}
