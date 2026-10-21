using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PulsoIT {
    public sealed class DiagnosticCase {
        public string Id,Title,Conclusion,Confidence,Impact,Next,Verify;
        public int Priority;
        public readonly List<string> Evidence=new List<string>();
    }
    public sealed class DiagnosticAssessment {
        public string State,Headline,Summary,Report;
        public int Reviewed,Complete,Pending,Unreviewed,Historical,Excluded;
        public bool CanReassure;
        public readonly List<DiagnosticCase> Cases=new List<DiagnosticCase>();
    }
    public static class Assessment {
        public static bool Local(Finding f){string key="local:"+Environment.MachineName;return string.Equals(f.ScopeKey,key,StringComparison.OrdinalIgnoreCase)||(f.ScopeKey??"").StartsWith(key+"|",StringComparison.OrdinalIgnoreCase);}
        public static bool Eligible(Finding f,IncidentContext context,DateTime now){return Local(f)&&f.Time<=now&&now-f.Time<TimeSpan.FromMinutes(10)&&(f.IncidentKey==context.EvidenceKey||(context.IsNow&&string.IsNullOrEmpty(f.IncidentKey)));}
        public static List<DateTime> Times(Finding f){
            var result=new List<DateTime>();object values;
            if(f.Data!=null&&f.Data.TryGetValue("TimesUtc",out values)&&values is IEnumerable&&!(values is string))foreach(object value in (IEnumerable)values){DateTime time;if(DateTime.TryParse(Convert.ToString(value),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time))result.Add(time.ToUniversalTime());}
            if(result.Count==0){DateTime last;if(DateTime.TryParse(f.MetaText("LastSeenUtc"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out last))result.Add(last.ToUniversalTime());}
            return result;
        }
        public static bool EventRelevant(Finding f,IncidentContext context,DateTime now){
            var times=Times(f);if(times.Count==0)return true; // Fecha desconocida: no descartar silenciosamente.
            return context.IsNow?times.Any(t=>t<=now.ToUniversalTime()&&now.ToUniversalTime()-t<=TimeSpan.FromHours(1)):times.Any(t=>t>=context.FromUtc.Value&&t<=context.ToUtc.Value);
        }
        public static bool Near(Finding a,Finding b,IncidentContext context,DateTime now){
            if(!Eligible(a,context,now)||!Eligible(b,context,now))return false;
            Func<DateTime,bool> current=t=>context.IsNow?t<=now.ToUniversalTime()&&now.ToUniversalTime()-t<=TimeSpan.FromHours(1):t>=context.FromUtc.Value&&t<=context.ToUtc.Value;
            var at=Times(a).Where(current);var bt=Times(b).Where(current);return at.Any(x=>bt.Any(y=>Math.Abs((x-y).TotalMinutes)<=5));
        }
        public static bool ObservationUsable(ObservationSession s,IncidentContext context,DateTime now){return context.IsNow&&s!=null&&s.Ended<=now&&now-s.Ended<TimeSpan.FromMinutes(10)&&s.Samples.Count>=10;}
        static bool FullPerformance(ObservationSession s,IncidentContext context,DateTime now){return ObservationUsable(s,context,now)&&s.Complete&&new[]{"cpu","commit","available","paging","readMs","writeMs","io"}.All(k=>s.Values(k).Count>=10);}
        static DiagnosticCase Add(DiagnosticAssessment a,string id,string title,string conclusion,string confidence,string impact,string next,string verify,int priority,IEnumerable<Finding> evidence=null){
            var item=new DiagnosticCase{Id=id,Title=title,Conclusion=conclusion,Confidence=confidence,Impact=impact,Next=next,Verify=verify,Priority=priority};
            if(evidence!=null)foreach(var f in evidence)item.Evidence.Add(PlainLanguage.Text(f.Title,120)+": "+PlainLanguage.Text(f.Summary,260));a.Cases.Add(item);return item;
        }
        public static DiagnosticAssessment Build(IEnumerable<Finding> source,ObservationSession observation,IncidentContext context,DateTime now,IDictionary<string,DateTime> completed=null,IEnumerable<string> requested=null,bool running=false,IEnumerable<string> incomplete=null){
            context=context??new IncidentContext();var all=source.ToList();var facts=all.Where(f=>Eligible(f,context,now)).ToList();
            var a=new DiagnosticAssessment();a.Excluded=all.Count-facts.Count;
            var events=facts.Where(f=>f.Code=="event.group"||f.Code=="vpn.events").ToList();var activeEvents=events.Where(f=>EventRelevant(f,context,now)).ToList();a.Historical=events.Count-activeEvents.Count;
            var historical=new HashSet<Finding>(events.Except(activeEvents));var problems=facts.Where(f=>PlainLanguage.Problem(f)&&!historical.Contains(f)).ToList();
            bool timestampProblem=events.Any(f=>Times(f).Any(t=>t>now.ToUniversalTime().AddMinutes(1)));
            var data=facts.Where(f=>f.Code!="observation.summary").ToList();a.Pending=facts.Count(f=>f.State=="unknown")+(timestampProblem?1:0);
            var reviewed=data.Select(f=>f.Section).Distinct().ToList();a.Reviewed=reviewed.Count;a.Unreviewed=Catalog.Sections.Length-a.Reviewed;
            var incompleteIds=new HashSet<string>(incomplete??new string[0]);
            var complete=new HashSet<string>(reviewed.Where(id=>!incompleteIds.Contains(id)&&completed!=null&&completed.ContainsKey(id)&&completed[id]<=now&&now-completed[id]<TimeSpan.FromMinutes(10)&&!data.Any(f=>f.Section==id&&f.State=="unknown")));
            a.Complete=complete.Count;var handled=new HashSet<Finding>();
            var stops=activeEvents.Where(f=>PlainLanguage.EventKind(f)=="stop").ToList();var reboots=activeEvents.Where(f=>PlainLanguage.EventKind(f)=="restart").ToList();var hardware=activeEvents.Where(f=>PlainLanguage.EventKind(f)=="hardware").ToList();
            if(stops.Count>0){var linked=reboots.Where(r=>stops.Any(s=>Near(r,s,context,now))).ToList();var linkedHardware=hardware.Where(h=>stops.Any(s=>Near(h,s,context,now))).ToList();
                var evidence=stops.Concat(linked).Concat(linkedHardware).ToList();
                Add(a,"restart.stop",linked.Count>0?"El reinicio coincide con un error de parada":"Windows registró un error de parada",linked.Count>0?"Los registros apuntan a un episodio de error de parada; el apagado no limpio describe su consecuencia y no demuestra un fallo eléctrico.":"Hay evidencia de una parada del sistema que requiere investigar el código y el volcado.",linkedHardware.Count>0?"Error de parada observado; posible relación con hardware/firmware":"Error de parada observado; origen aún no determinado","Puede explicar una pantalla azul o un reinicio."+(linkedHardware.Count>0?" WHEA aparece a menos de cinco minutos; esa proximidad no identifica una pieza averiada.":""),"Comprueba código de parada, hora y metadatos del volcado; deriva el análisis del volcado si hace falta.","Contrasta el mismo episodio con el usuario. Las horas son las de registro, no necesariamente el inicio exacto del fallo.",98,evidence);foreach(var f in evidence)handled.Add(f);
            }
            var integrity=activeEvents.Where(f=>PlainLanguage.EventKind(f)=="storage-integrity").ToList();if(integrity.Count>0){Add(a,"storage.integrity","Hay una señal de integridad en el almacenamiento","Windows ha registrado un error de bloque o de estructura de archivos; conviene darle prioridad.","Condición registrada; componente culpable sin confirmar","Puede afectar a la lectura/escritura o a los datos. No demuestra que todos los discos estén dañados.","Identifica el volumen/dispositivo del mensaje y comprueba las copias antes de valorar una intervención.","Contrasta con el proveedor de almacenamiento y otros errores del mismo dispositivo; no se ejecuta reparación.",110,integrity);foreach(var f in integrity)handled.Add(f);}
            if(ObservationUsable(observation,context,now)){
                var h=Triage.Build(new Finding[0],observation,context,now,10);var memory=h.FirstOrDefault(x=>x.Title=="Posible presión de memoria");var disk=h.FirstOrDefault(x=>x.Title.StartsWith("Almacenamiento",StringComparison.Ordinal));
                int paired=observation.Samples.Count(s=>s.Get("commit")>=90&&s.Get("paging")>10&&s.Get("io")>0&&((s.Get("readMs")??0)>=50||(s.Get("writeMs")??0)>=50));
                if(memory!=null&&disk!=null&&paired>=10){var c=Add(a,"performance.memory-io","La presión de memoria coincide con esperas del disco","Durante "+paired+" muestras del mismo intervalo coinciden compromiso alto, escritura de paginación y latencia de disco.","Correlación temporal; dirección de la causa sin confirmar","Es compatible con pausas al trabajar. No permite concluir que ampliar RAM o sustituir el disco resolverá el fallo.","Reproduce la misma tarea y revisa los procesos de mayor memoria; contrasta si bajan las esperas al disminuir la carga.","Repite con una carga comparable. No deshabilites la paginación.",96);c.Evidence.Add(memory.Evidence);c.Evidence.Add(disk.Evidence);}
                foreach(var hypothesis in h){if(paired>=10&&memory!=null&&disk!=null&&(hypothesis==memory||hypothesis==disk))continue;var c=Add(a,"performance."+a.Cases.Count,hypothesis.Title,hypothesis.Evidence,hypothesis.Confidence,hypothesis.Missing,hypothesis.Next,"Repite mientras se reproduce el síntoma; compara intervalos de la misma carga.",hypothesis.Priority);c.Evidence.Add("Observación: "+observation.Started.ToString("T")+" a "+observation.Ended.ToString("T")+" · "+observation.Samples.Count+" muestras.");}
                if(h.Any(x=>x.Title.Contains("memoria")||x.Title.Contains("Memoria")))foreach(var f in problems.Where(f=>f.Code=="memory.snapshot"))handled.Add(f);
                if(h.Any(x=>x.Title.StartsWith("CPU",StringComparison.Ordinal)))foreach(var f in problems.Where(f=>f.Code=="cpu.snapshot"))handled.Add(f);
                var delay=activeEvents.Where(f=>PlainLanguage.EventKind(f)=="storage-delay"&&Times(f).Any(t=>t>=observation.Started.ToUniversalTime().AddMinutes(-5)&&t<=observation.Ended.ToUniversalTime().AddMinutes(5))).ToList();
                if(disk!=null&&delay.Count>0){var c=Add(a,"storage.temporal","Las esperas de disco coinciden con registros de almacenamiento","Se han observado latencias repetidas y registros próximos al intervalo medido.","Coincidencia de señales; unidad y causa sin confirmar","Refuerza la prioridad de revisar almacenamiento. La medición es agregada: los eventos podrían referirse a otro disco.","Identifica el disco/volumen del evento y verifica si recibe la carga durante la lentitud.","No unas dispositivos distintos ni atribuyas automáticamente el problema a daño físico.",97,delay);c.Evidence.Add(disk.Evidence);foreach(var f in delay)handled.Add(f);}
            }
            var dns=facts.Where(f=>f.Code=="network.dns").GroupBy(f=>f.MetaText("Target"),StringComparer.OrdinalIgnoreCase);
            foreach(var group in dns){var d=group.Last();var connections=facts.Where(f=>f.Code=="network.tcp"&&string.Equals(f.MetaText("Target"),group.Key,StringComparison.OrdinalIgnoreCase)).ToList();
                if(PlainLanguage.Problem(d)&&connections.Any(f=>f.State=="good")){var evidence=new[]{d}.Concat(connections.Where(f=>f.State=="good")).ToList();Add(a,"network.dns-alternate."+group.Key,"La consulta DNS falla, pero el puerto sí responde","Para "+PlainLanguage.Text(group.Key,100)+" la prueba DNS explícita falló, aunque una conexión TCP al mismo nombre funcionó.","Resultados distintos observados; acceso total no confirmado","Puede haber diferencias entre DNS explícito, caché, hosts o rutas de resolución. No corresponde afirmar que el destino esté caído.","Revisa la IP esperada, las coincidencias en hosts, DNS, VPN y sufijos.","Repite las dos pruebas con el mismo nombre y puerto.",78,evidence);foreach(var f in evidence)handled.Add(f);}
                else foreach(var tcp in connections.Where(PlainLanguage.Problem)){var x=PlainLanguage.Explain(tcp);Add(a,"network.port."+tcp.MetaText("Identity"),d.State=="good"?"El nombre responde, pero el puerto no":"No se pudo completar el acceso al recurso",d.State=="good"?"DNS resolvió el nombre y TCP no completó la conexión al puerto indicado.":"Han fallado la consulta de nombre y la conexión al recurso; no se ha aislado el punto que falla.","Fallo observado; origen aún no aislado",x.Impact,x.Next,"Contrasta el mismo destino y puerto desde otro equipo autorizado; confirma la escucha del servicio.",82,new[]{d,tcp});handled.Add(d);handled.Add(tcp);}
            }
            var print=problems.Where(f=>new[]{"printer.queue","printer.state","printer.spooler"}.Contains(f.Code)).ToList();if(print.Count>0){bool stopped=print.Any(f=>f.Code=="printer.spooler");Add(a,"printing",stopped?"El servicio local de impresión está detenido":"Hay señales de un problema de impresión",stopped?"Spooler no está en ejecución; esto puede impedir procesar los trabajos locales.":"Hay estados de impresora o trabajos pendientes que requieren revisión.",stopped?"Estado observado; falta confirmar el impacto":"Indicios de impresión; falta confirmar el impacto","No explica por sí solo si una impresora física está averiada ni si un trabajo antiguo está pausado a propósito.",stopped?"Comprueba por qué se detuvo Spooler y sus eventos antes de decidir un reinicio.":"Identifica impresora, trabajo más antiguo, estado físico y puerto.","Verifica la misma impresora y operación con el usuario; no se borran trabajos ni se imprime una prueba.",stopped?88:70,print);foreach(var f in print)handled.Add(f);}
            var rdp=problems.Where(f=>new[]{"rdp.listen","rdp.service"}.Contains(f.Code)).ToList();if(rdp.Count>0){Add(a,"rdp.local","El servicio RDP local no está preparado para recibir la conexión","RDP está habilitado y no se cumple una condición local de escucha o servicio.","Estado local observado","Afecta a entrar en este PC; no demuestra por qué este PC no puede entrar en otro servidor.","Contrasta servicio, puerto configurado, edición y políticas con el responsable.","No cambies firewall o políticas de acceso basándote solo en esta comprobación.",80,rdp);foreach(var f in rdp)handled.Add(f);}
            foreach(var service in problems.Where(f=>f.Code=="service.errors")){
                object raw;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if(service.Data!=null&&service.Data.TryGetValue("Names",out raw)&&raw is IEnumerable&&!(raw is string))foreach(object name in (IEnumerable)raw)names.Add(Convert.ToString(name));
                var matches=activeEvents.Where(f=>PlainLanguage.EventKind(f)=="service"&&f.MetaText("Subject").Length>0&&names.Contains(f.MetaText("Subject"))).ToList();
                if(matches.Count==0)continue;
                var evidence=new[]{service}.Concat(matches).ToList();Add(a,"service.correlated","Un servicio con error tiene también registros de fallo","Coincide el nombre del servicio en su estado actual y en los registros de Service Control Manager.","Dos fuentes sobre el mismo servicio; origen sin confirmar","Puede explicar la función que depende de ese servicio. La coincidencia de nombre no demuestra que todos los eventos sean el mismo episodio.","Revisa código de salida, dependencia y detalle del servicio indicado antes de decidir un reinicio.","Confirma que la hora y la función afectada corresponden al síntoma del usuario.",83,evidence);foreach(var f in evidence)handled.Add(f);
            }
            // Todo aviso actual queda representado, aunque no exista una regla causal específica.
            foreach(var group in problems.Where(f=>!handled.Contains(f)).GroupBy(f=>f.Code=="event.group"?"event:"+PlainLanguage.EventKind(f)+":"+f.MetaText("Provider")+":"+f.MetaText("Subject"):string.IsNullOrEmpty(f.Code)?f.Section+":"+f.Title:f.Code)){
                var list=group.ToList();var first=list.OrderByDescending(f=>f.State=="critical").First();var x=PlainLanguage.Explain(first);
                int priority=first.State=="critical"?105:first.Code=="security.status"?90:first.Code=="disk.reliability"?86:65;
                Add(a,"finding."+group.Key,x.Title,x.Meaning,x.Known?"Condición observada; relación con el síntoma por confirmar":"Registro pendiente de interpretación específica",x.Impact,x.Next,x.Check,priority,list);
            }
            a.Cases.Sort((x,y)=>y.Priority.CompareTo(x.Priority));
            var expected=new HashSet<string>(requested??Catalog.Quick);int missingRequested=expected.Count(id=>!complete.Contains(id));
            bool limited=facts.Any(f=>f.MetaText("Limited").Equals("True",StringComparison.OrdinalIgnoreCase));bool perf=FullPerformance(observation,context,now);
            bool core=Catalog.Quick.All(complete.Contains);a.CanReassure=!running&&context.IsNow&&a.Cases.Count==0&&a.Pending==0&&core&&missingRequested==0&&perf&&!limited;
            if(running){a.State="running";a.Headline="Diagnóstico en curso";}
            else if(a.Cases.Count>0){a.State=a.Cases.Any(c=>c.Priority>=100)?"critical":"warning";a.Headline=a.State=="critical"?"Hay hallazgos que requieren atención prioritaria":"Hay hallazgos que conviene revisar";}
            else if(a.Reviewed==0){a.State="pending";a.Headline="Todavía no hay datos suficientes para valorar este PC";}
            else if(a.Pending>0||missingRequested>0||(context.IsNow&&!perf)){a.State="pending";a.Headline="No se detectan incidencias; el diagnóstico está incompleto";}
            else{a.State=a.CanReassure?"good":"info";a.Headline=context.IsNow?"No se han detectado incidencias en lo revisado":"No se han encontrado indicios suficientes en la ventana consultada";}
            a.Summary=a.Cases.Count+" grupos de hallazgos · "+a.Reviewed+"/12 secciones con datos locales actuales · "+a.Pending+" comprobaciones sin verificar";
            var text=new StringBuilder();text.AppendLine(a.Headline.ToUpperInvariant()).AppendLine(a.Summary).AppendLine();
            if(running)text.AppendLine("Conclusión provisional: las consultas siguen en curso.");
            if(a.CanReassure)text.AppendLine("Las comprobaciones locales básicas y la observación no han mostrado sus condiciones de aviso. No se aprecia una incidencia con esta cobertura; esto no certifica ausencia de fallos intermitentes ni el funcionamiento de todas las aplicaciones.");
            else if(a.Cases.Count==0&&a.Reviewed>0)text.AppendLine("No se ha encontrado una avería concluyente con los datos disponibles. La cobertura siguiente indica qué falta para poder tranquilizarte con más fundamento.");
            if(a.Cases.Count>0){var top=a.Cases[0];text.AppendLine("POR DÓNDE EMPEZAR").AppendLine(top.Title+". "+top.Next).AppendLine();}
            int index=0;foreach(var item in a.Cases){text.AppendLine((++index)+". "+item.Title).AppendLine("Conclusión: "+item.Conclusion).AppendLine("Solidez: "+item.Confidence).AppendLine("Efecto y límites: "+item.Impact);foreach(var ev in item.Evidence.Take(4))text.AppendLine("  • "+ev);if(item.Evidence.Count>4)text.AppendLine("  • "+(item.Evidence.Count-4)+" observaciones adicionales agrupadas; consulta la tabla.");text.AppendLine("Siguiente paso: "+item.Next).AppendLine("Para confirmarlo: "+item.Verify).AppendLine();}
            text.AppendLine("QUÉ SE HA PODIDO COMPROBAR");
            foreach(var section in Catalog.Sections){var rows=data.Where(f=>f.Section==section.Id).ToList();string state=rows.Count==0?"No revisado con datos locales actuales":!complete.Contains(section.Id)?"Parcial o pendiente":rows.Any(f=>problems.Contains(f))||(section.Id=="pc"&&a.Cases.Any(c=>c.Id.StartsWith("performance.",StringComparison.Ordinal)))?"Hallazgos explicados arriba":rows.Any(historical.Contains)?"Sin avisos actuales; hay antecedentes en los registros":"Sin condiciones de aviso en las consultas realizadas";text.AppendLine("• "+section.Name+": "+state+".");}
            text.AppendLine().AppendLine("LÍMITES QUE IMPORTAN PARA ESTA CONCLUSIÓN");
            if(observation!=null&&context.IsNow){text.AppendLine("• Rendimiento: "+string.Join("; ",new[]{new[]{"cpu","CPU"},new[]{"commit","compromiso"},new[]{"available","RAM disponible"},new[]{"paging","paginación"},new[]{"readMs","lectura de disco"},new[]{"writeMs","escritura de disco"},new[]{"io","actividad de disco"}}.Select(pair=>pair[1]+": "+observation.Values(pair[0]).Count+" muestras válidas"))+".");}
            if(!perf&&context.IsNow)text.AppendLine("• Falta observación completa con al menos diez muestras válidas de CPU, memoria, paginación y disco. Usa Diagnóstico completo u Observar 30/60 s; las métricas no disponibles siguen pendientes.");
            if(a.Pending>0)foreach(var f in facts.Where(f=>f.State=="unknown").Take(6))text.AppendLine("• Sin verificar: "+PlainLanguage.Text(f.Title,100)+" — "+PlainLanguage.Text(f.Summary,180));
            if(a.Unreviewed>0)text.AppendLine("• "+a.Unreviewed+" secciones aún no aportan datos locales actuales. El botón Diagnóstico completo amplía la revisión.");
            if(limited)text.AppendLine("• Se ha alcanzado un límite de lectura de eventos. Puede haber registros relevantes fuera de la muestra.");
            if(timestampProblem)text.AppendLine("• Hay registros con fecha futura: comprueba reloj/zona horaria antes de relacionar los episodios o dar el diagnóstico por completo.");
            if(a.Excluded>0)text.AppendLine("• "+a.Excluded+" observaciones antiguas, de otro equipo o de otro contexto se han excluido de la conclusión local.");
            if(!context.IsNow)text.AppendLine("• La medición actual no reconstruye el rendimiento a la hora de una incidencia pasada.");
            if(context.Affected=="Varios equipos o usuarios")text.AppendLine("• El alcance declarado es compartido: contrasta servicio, red y cambios comunes antes de intervenir en este PC.");
            if(!facts.Any(f=>f.Code=="network.tcp"))text.AppendLine("• No se ha probado el puerto del recurso afectado. Una configuración local normal no demuestra acceso a Internet ni a una aplicación corporativa.");
            text.AppendLine("• No se certifican ausencia de malware, estado del portal Intune, copias, autenticación o integridad física completa del hardware.");
            if(a.Historical>0){text.AppendLine().AppendLine("ANTECEDENTES: NO PRUEBAN UN FALLO ACTIVO");foreach(var f in historical.Take(6))text.AppendLine("• "+PlainLanguage.Explain(f).Title+": "+PlainLanguage.Text(f.Summary));text.AppendLine("En modo actual se consideran antecedentes los eventos de hace más de una hora. Si el fallo ocurrió entonces, selecciona su hora en Contexto de incidencia.");}
            text.AppendLine().AppendLine("CONTEXTO APORTADO").AppendLine(context.Describe());
            if(context.Change!="No se sabe"&&context.Change!="Ninguno conocido")text.AppendLine("Un cambio declarado es una pista, no evidencia de causalidad.");
            text.AppendLine().AppendLine("Todo se interpreta mediante reglas locales explícitas. No se envían datos a una IA o servicio externo ni se ejecutan reparaciones.");
            a.Report=text.ToString();return a;
        }
    }
}
