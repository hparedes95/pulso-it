using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PulsoIT {
    public sealed class Hypothesis {
        public string Title,Confidence,Evidence,Missing,Next;
        public int Priority;
    }
    public static class Triage {
        static bool IsProblem(Finding f){return f.State=="warning"||f.State=="critical";}
        public static List<Hypothesis> Build(IEnumerable<Finding> source,ObservationSession observation,IncidentContext context,DateTime now,int limit=3){
            var findings=source.Where(f=>now>=f.Time&&now-f.Time<TimeSpan.FromMinutes(10)&&f.ScopeKey.StartsWith("local:",StringComparison.Ordinal)&&(f.IncidentKey==""||f.IncidentKey==context.EvidenceKey)).ToList();
            var result=new List<Hypothesis>();
            Action<string,string,string,string,string,int> add=(title,confidence,evidence,missing,next,priority)=>result.Add(new Hypothesis{Title=title,Confidence=confidence,Evidence=evidence,Missing=missing,Next=next,Priority=priority});
            bool live=context.IsNow&&observation!=null&&now>=observation.Ended&&now-observation.Ended<TimeSpan.FromMinutes(10)&&observation.Samples.Count>=10;
            if(live){
                var cpu=observation.Values("cpu");if(cpu.Count>=10&&cpu.Count(x=>x>=90)>=cpu.Count*.7)add("CPU ocupada durante la observación","Evidencia consistente",observation.Stat("cpu","CPU","%")+". "+observation.TopCpuSummary(),"Este intervalo es corto y puede incluir el coste de la propia medición. No identifica una aplicación defectuosa.","Reproduce el síntoma y comprueba si el mismo proceso mantiene la carga en una segunda observación.",90);
                var commit=observation.Values("commit");var paging=observation.Values("paging");var available=observation.Values("available");
                bool highCommit=commit.Count>=10&&commit.Count(x=>x>=90)>=commit.Count*.7;
                bool activePaging=paging.Count>=10&&paging.Count(x=>x>10)>=paging.Count*.5;
                bool lowAvailable=available.Count>=10&&available.Count(x=>x<1024)>=available.Count*.7;
                if(highCommit&&(activePaging||lowAvailable))add("Posible presión de memoria","Evidencia consistente",observation.Stat("commit","Memoria comprometida","%")+"; "+observation.Stat("available","Disponible","MB")+"; "+observation.Stat("paging","Páginas escritas","/s"),"Los umbrales son orientativos. La paginación también puede responder a la gestión normal de memoria.","Revisa los procesos de mayor memoria y repite con la misma carga. No deshabilites el archivo de paginación.",95);
                else if(highCommit||lowAvailable)add("Memoria ajustada; falta confirmar el impacto","Evidencia moderada",observation.Stat("commit","Memoria comprometida","%")+"; "+observation.Stat("available","Disponible","MB"),"No se ha establecido que la paginación o el disco estén causando la lentitud.","Repite mientras se reproduce el fallo y observa paginación y latencia del disco.",55);
                var active=observation.Samples.Where(s=>s.Get("io").HasValue&&s.Get("io")>0).ToList();
                var latency=active.Where(s=>s.Get("readMs").HasValue||s.Get("writeMs").HasValue).ToList();
                if(latency.Count>=10&&latency.Count(s=>(s.Get("readMs")??0)>=50||(s.Get("writeMs")??0)>=50)>=latency.Count*.7)add("Almacenamiento con respuesta lenta durante la actividad","Evidencia moderada",observation.Stat("readMs","Lectura","ms")+"; "+observation.Stat("writeMs","Escritura","ms")+"; "+observation.Stat("queue","Cola",""),"El valor de 50 ms es una orientación, no un límite universal. La media agregada puede ocultar diferencias entre discos.","Comprueba si coincide con el síntoma, paginación, carga de la aplicación y eventos de almacenamiento. No implica por sí solo daño físico.",85);
                var errors=observation.Values("netErrors");if(errors.Any(x=>x>0))add("Se han incrementado errores de red durante la observación","Evidencia moderada",observation.Stat("netErrors","Errores nuevos","/intervalo"),"No determina si el origen es controlador, interfaz o red. Los descartes se muestran aparte porque pueden ser normales.","Repite y comprueba si aumentan en la interfaz utilizada durante el fallo.",70);
            }
            var apipa=findings.FirstOrDefault(f=>f.Code=="network.apipa"&&IsProblem(f));if(apipa!=null)add("Configuración IPv4 a revisar en una interfaz activa","Evidencia moderada",apipa.Summary,"APIPA no prueba que DHCP sea la única causa ni que esa interfaz sea la utilizada por la aplicación.","Comprueba el adaptador usado, el segmento de red y si se esperaba DHCP o IP estática.",85);
            var tcp=findings.LastOrDefault(f=>f.Code=="network.tcp");var dns=findings.LastOrDefault(f=>f.Code=="network.dns"&&(tcp==null||string.Equals(f.MetaText("Target"),tcp.MetaText("Target"),StringComparison.OrdinalIgnoreCase)));var https=findings.LastOrDefault(f=>f.Code=="network.https");
            if(tcp!=null&&IsProblem(tcp))add(dns!=null&&dns.State=="good"?"El nombre resuelve; falta completar la conexión al servicio":"La conexión al recurso no se ha completado","Evidencia moderada",tcp.Summary+(dns==null?"":". "+dns.Summary),"Un rechazo o timeout no identifica qué equipo o filtro lo provoca. Una prueba sin permiso no entra en esta hipótesis.","Contrasta host, puerto y escucha del servicio. Si afecta a varios equipos, comprueba primero el recurso o la red compartida.",80);
            else if(dns!=null&&IsProblem(dns))add("Resolución del nombre a revisar","Evidencia moderada",dns.Summary,"No determina si el problema está en el cliente, servidor DNS o trayecto.","Comprueba los DNS corporativos, la VPN, los sufijos y las coincidencias en hosts.",75);
            if(https!=null&&IsProblem(https))add("La validación HTTPS o la respuesta del servicio necesita revisión","Evidencia moderada",https.Summary,"Un certificado no válido o un fallo HTTP no demuestra un fallo de la conectividad IP. No se han enviado credenciales.","Comprueba nombre, reloj, cadena/caducidad del certificado y configuración de la aplicación. No omitas la validación del certificado.",90);
            var reboot=findings.FirstOrDefault(f=>f.Code=="event.group"&&f.MetaText("Provider")=="Microsoft-Windows-Kernel-Power"&&f.MetaText("Id")=="41");
            if(reboot!=null)add("Consta un apagado no limpio; causa aún sin confirmar","Evidencia moderada",reboot.Summary,"El evento 41 no identifica una fuente de alimentación defectuosa. Hay que correlacionar errores de parada, hardware y contexto.","Sitúa la hora exacta, revisa BugCheck/WHEA y metadatos del volcado. Un análisis de memoria requiere herramientas específicas.",context.Symptom==Catalog.Symptoms[1]?100:65);
            var app=findings.FirstOrDefault(f=>f.Code=="event.group"&&((f.MetaText("Provider")=="Application Error"&&f.MetaText("Id")=="1000")||(f.MetaText("Provider")=="Application Hang"&&f.MetaText("Id")=="1002")));
            if(app!=null)add("Constan errores o bloqueos de aplicación en la ventana consultada","Evidencia moderada",app.Summary,"No confirma una regresión ni identifica por sí solo el módulo responsable. El evento debe coincidir con la aplicación y la hora del síntoma.","Abre el detalle del evento y contrasta aplicación, módulo y cambios recientes. No reinstales software automáticamente.",context.Symptom==Catalog.Symptoms[0]?85:60);
            var queues=findings.Where(f=>f.Code=="printer.queue"&&IsProblem(f)).ToList();if(queues.Any())add("Hay trabajos de impresión que requieren revisión","Evidencia moderada",string.Join("; ",queues.Select(f=>f.Summary)),"Un trabajo antiguo puede estar pausado intencionadamente y el estado del controlador puede estar desactualizado.","Comprueba la impresora afectada, el trabajo más antiguo y el puerto. No borres la cola sin valorar el impacto.",context.Symptom==Catalog.Symptoms[3]?100:60);
            var lowSpace=findings.FirstOrDefault(f=>f.Code=="disk.space"&&IsProblem(f));if(lowSpace!=null)add("Poco espacio disponible en un volumen","Evidencia consistente",lowSpace.Summary,"No demuestra lentitud del almacenamiento ni daño físico. Puede ser secundario al síntoma.","Comprueba si es el volumen del sistema o de la aplicación afectada y revisa su uso antes de eliminar datos.",65);
            var reliability=findings.FirstOrDefault(f=>f.Code=="disk.reliability"&&IsProblem(f));if(reliability!=null)add("El dispositivo comunica contadores de fiabilidad que requieren revisión","Evidencia moderada",reliability.Title+" · "+reliability.Summary,"El proveedor y el modelo condicionan los datos; los errores acumulados pueden ser históricos. No demuestra que explique esta incidencia.","Revisa los valores, la repetición y el estado de las copias con el responsable antes de intervenir sobre el almacenamiento.",reliability.State=="critical"?105:65);
            var threat=findings.FirstOrDefault(f=>f.Code=="security.active"&&f.State=="critical");if(threat!=null)add("Defender comunica amenazas activas","Evidencia consistente",threat.Summary,"La app lee el estado comunicado; no ha ejecutado un análisis ni establecido que explique todos los síntomas.","Traslada la evidencia al responsable de seguridad y sigue el procedimiento corporativo.",110);
            return result.OrderByDescending(h=>h.Priority).Take(limit).ToList();
        }
        public static string Report(IEnumerable<Finding> source,ObservationSession observation,IncidentContext context,DateTime now){
            var all=source.ToList();var hypotheses=Build(all,observation,context,now);var text=new StringBuilder();
            text.AppendLine("INTERPRETACIÓN DE LA INCIDENCIA · hipótesis, no diagnóstico definitivo").AppendLine().AppendLine(context.Describe()).AppendLine();
            text.AppendLine(all.Count(f=>f.State=="warning"||f.State=="critical")+" observaciones para revisar · "+all.Count(f=>f.State=="unknown")+" pendientes de comprobar.");
            if(all.Any(f=>now-f.Time>=TimeSpan.FromMinutes(10)))text.AppendLine("Hay resultados de más de 10 minutos: se excluyen de las hipótesis hasta repetir la consulta.");
            if(!context.IsNow)text.AppendLine("Los valores de rendimiento actuales no reconstruyen el estado del PC a la hora de una incidencia pasada.");
            if(context.Affected=="Varios equipos o usuarios")text.AppendLine("El alcance es compartido: contrasta servicio, red y cambios comunes antes de atribuirlo a este PC.");
            if(context.Change!="No se sabe"&&context.Change!="Ninguno conocido")text.AppendLine("Cambio declarado: conviene contrastar su fecha con el inicio del síntoma; coincidencia no significa causalidad.");
            if(hypotheses.Count==0)text.AppendLine().AppendLine("No hay evidencia suficiente para priorizar una causa. Revisa los pendientes, concreta el momento y observa el rendimiento mientras reproduces el problema.");
            int i=0;foreach(var h in hypotheses){text.AppendLine().AppendLine((++i)+". "+h.Title).AppendLine(h.Confidence).AppendLine("A favor: "+h.Evidence).AppendLine("Falta confirmar: "+h.Missing).AppendLine("Siguiente paso: "+h.Next);}
            if(observation!=null)text.AppendLine().AppendLine("OBSERVACIÓN EN MEMORIA").AppendLine(observation.ShortSummary());
            return text.ToString();
        }
    }
}
