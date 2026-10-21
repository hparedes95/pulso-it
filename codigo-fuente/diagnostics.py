COMMON = r'''

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$WarningPreference='SilentlyContinue'
$global:FormatEnumerationLimit=-1
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
if(-not $Horas){$Horas=24}
if(-not $Desde){$Desde=(Get-Date).AddHours(-$Horas)}
if(-not $Hasta){$Hasta=Get-Date}
function WindowText { $Desde.ToString('g')+' — '+$Hasta.ToString('g') }
$script:rows=New-Object System.Collections.Generic.List[object]
function Row($estado,$titulo,$resumen,$evidencia,$siguiente,$codigo='',$datos=$null) {
 $r=[pscustomobject]@{State=$estado;Title=[string]$titulo;Summary=[string]$resumen;Evidence=[string]$evidencia;Next=[string]$siguiente;Code=[string]$codigo;Data=$datos}
 $script:rows.Add($r)
 if($EmitirProgreso){[Console]::WriteLine((ConvertTo-Json -InputObject $r -Depth 8 -Compress));[Console]::Out.Flush()}
}
function Check($nombre,[scriptblock]$accion) {
 try { & $accion }
 catch {
  $motivo='No se pudo completar la consulta.'
  if($_.CategoryInfo.Category -eq 'PermissionDenied' -or $_.Exception -is [UnauthorizedAccessException]){$motivo='Tu cuenta no tiene acceso a esta consulta.'}
  elseif($_.Exception -is [System.Management.Automation.CommandNotFoundException]){$motivo='Este comando o componente no está disponible.'}
  Row 'unknown' $nombre $motivo ($_.Exception.Message+"`nIdentificador: "+$_.FullyQualifiedErrorId) 'Una consulta sin verificar no es una avería confirmada. Revisa el permiso delegado o componente; las demás consultas continúan.'
 }
}
function Details($datos) { ($datos | Format-List * | Out-String -Width 240).Trim() }
function Number($value) { $n=0.0;if($null -eq $value -or -not [double]::TryParse([string]$value,[ref]$n)){return $null};return $n }
function MemoryRow($os,$prefix='') {
 $total=Number $os.TotalVisibleMemorySize;$free=Number $os.FreePhysicalMemory
 if($null -eq $total -or $total -le 0 -or $null -eq $free -or $free -lt 0 -or $free -gt $total){Row 'unknown' ($prefix+'Memoria RAM') 'El proveedor no devolvió una memoria válida.' (Details ($os | Select-Object TotalVisibleMemorySize,FreePhysicalMemory)) 'No se calcula un porcentaje con valores ausentes o incoherentes.';return}
 $use=[math]::Round(100*(1-$free/$total),1)
 $state=if($use -ge 90){'warning'}else{'info'}
 Row $state ($prefix+'Memoria RAM') ("{0}% en uso · {1:N1} GB libres de {2:N1} GB" -f $use,($free/1MB),($total/1MB)) 'Instantánea de memoria física; los valores CIM originales están en KiB.' 'Si coincide con lentitud, revisa los procesos de mayor consumo. La presión de memoria no identifica por sí sola al programa causante.' 'memory.snapshot' @{UsePercent=$use;AvailableMB=($free/1024);TotalMB=($total/1024)}
}
function SpaceRow($disk,$prefix='') {
 $size=Number $disk.Size;$free=Number $disk.FreeSpace
 if($null -eq $size -or $size -le 0 -or $null -eq $free -or $free -lt 0 -or $free -gt $size){Row 'unknown' ($prefix+'Espacio '+$disk.DeviceID) 'No se pudo calcular un espacio válido.' (Details ($disk | Select-Object DeviceID,Size,FreeSpace)) 'No se interpreta un valor ausente como cero libre.';return}
 $pct=[math]::Round(100*$free/$size,1)
 $state=if($pct -lt 5){'critical'}elseif($pct -lt 15){'warning'}else{'good'}
 Row $state ($prefix+'Espacio '+$disk.DeviceID) ("{0}% libre · {1:N1} GB de {2:N1} GB" -f $pct,($free/1GB),($size/1GB)) (Details ($disk | Select-Object DeviceID,VolumeName,FileSystem,Size,FreeSpace)) 'Umbrales orientativos: <5% incidencia, <15% revisar. El tamaño absoluto y el uso del volumen también importan. No se borra ningún archivo.' 'disk.space' @{Identity=[string]$disk.DeviceID;FreePercent=$pct;FreeBytes=$free;TotalBytes=$size}
}
function Events($log,$max=80,$levels=@(1,2)) {
 try { @(Get-WinEvent -FilterHashtable @{LogName=$log;StartTime=$Desde;EndTime=$Hasta;Level=$levels} -MaxEvents $max -ErrorAction Stop) }
 catch {if($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*'){return};throw}
}
function OptionalService($name) {
 try {Get-Service -Name $name -ErrorAction Stop}
 catch {if($_.FullyQualifiedErrorId -like 'NoServiceFoundForGivenName*'){return};throw}
}
function SocketProbe($hostName,$portNumber) {
 $c=New-Object Net.Sockets.TcpClient;$watch=[Diagnostics.Stopwatch]::StartNew()
 try {
  $t=$c.ConnectAsync($hostName,$portNumber)
  if(-not $t.Wait(3000)){return [pscustomobject]@{Status='timeout';Milliseconds=$watch.ElapsedMilliseconds;Message='No se ha completado la conexión TCP en 3 s.'}}
  $t.GetAwaiter().GetResult()
  return [pscustomobject]@{Status='connected';Milliseconds=$watch.ElapsedMilliseconds;Message='Conexión TCP abierta y cerrada sin autenticación.'}
 }catch{
  $cause=$_.Exception.GetBaseException();$state=if($cause -is [Net.Sockets.SocketException] -and $cause.SocketErrorCode -eq [Net.Sockets.SocketError]::AccessDenied){'blocked'}else{'failed'}
  return [pscustomobject]@{Status=$state;Milliseconds=$watch.ElapsedMilliseconds;Message=$cause.Message}
 }finally{$c.Dispose()}
}
function Tcp($hostName,$portNumber,$label) {
 $probe=SocketProbe $hostName $portNumber
 if($probe.Status -eq 'connected'){Row 'good' $label ($hostName+':'+$portNumber+' accesible · '+$probe.Milliseconds+' ms') $probe.Message 'La respuesta no garantiza permisos, certificados, sesión RDP ni funcionamiento de la aplicación.' 'network.tcp' @{Status='connected';Target=$hostName;Port=$portNumber;Milliseconds=$probe.Milliseconds;Identity=($hostName+':'+$portNumber)}}
 elseif($probe.Status -eq 'blocked'){Row 'unknown' $label 'El contexto local no tiene permiso para abrir el socket.' $probe.Message 'La política local o el producto de seguridad pueden bloquear la prueba. No se puede concluir que el destino esté caído.' 'network.tcp' @{Status='blocked';Target=$hostName;Port=$portNumber;Identity=($hostName+':'+$portNumber)}}
 else{Row 'warning' $label ($hostName+':'+$portNumber+' no accesible o sin respuesta.') $probe.Message 'Revisa nombre/IP, puerto correcto, escucha, ruta y firewall. Un rechazo o timeout no identifica el extremo que falla ni demuestra que el equipo esté apagado.' 'network.tcp' @{Status=$probe.Status;Target=$hostName;Port=$portNumber;Identity=($hostName+':'+$portNumber)}}
}
function EventSubject($event) {
 try {
  if(-not $event.PSObject.Methods['ToXml']){return ''}
  [xml]$xml=$event.ToXml();$fields=@{}
  foreach($entry in @($xml.Event.EventData.Data)){if($entry.Name){$fields[[string]$entry.Name]=[string]$entry.'#text'}}
  $names=if($event.ProviderName -eq 'Service Control Manager'){@('ServiceName','param1')}elseif($event.ProviderName -in @('Application Error','Application Hang')){@('AppName','ApplicationName','FaultingApplicationName','param1')}else{@('DeviceName','DiskNumber')}
  foreach($name in $names){if($fields.ContainsKey($name) -and $fields[$name]){$v=$fields[$name] -replace '[\x00-\x1F\x7F]',' ';return $v.Substring(0,[math]::Min(120,$v.Length))}}
 }catch{} # El mensaje y el resto de la consulta siguen disponibles si falta XML.
 return ''
}
function GroupEvents($log,$max=80) {
 $events=@(Events $log $max);$regularCount=$events.Count;$warningLimited=$false
 # Lectura separada y acotada: los avisos repetitivos no desplazan a los errores/críticos.
 if($log -eq 'System'){
  try{
   $warnings=@(Get-WinEvent -FilterHashtable @{LogName=$log;StartTime=$Desde;EndTime=$Hasta;Level=3;ProviderName=@('disk','Microsoft-Windows-Disk','storahci','stornvme','storport','Microsoft-Windows-StorPort','Microsoft-Windows-WHEA-Logger','Microsoft-Windows-DNS-Client');Id=@(51,129,153,17,19,1014)} -MaxEvents 40 -ErrorAction Stop)
   $warningLimited=$warnings.Count -ge 40
   $warnings=@($warnings|Where-Object {($_.ProviderName -in @('disk','Microsoft-Windows-Disk') -and $_.Id -in @(51,153)) -or ($_.ProviderName -in @('storahci','stornvme','storport','Microsoft-Windows-StorPort') -and $_.Id -eq 129) -or ($_.ProviderName -eq 'Microsoft-Windows-WHEA-Logger' -and $_.Id -in @(17,19)) -or ($_.ProviderName -eq 'Microsoft-Windows-DNS-Client' -and $_.Id -eq 1014)})
   $events+= $warnings
  }catch{if($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*'){Row 'unknown' 'Advertencias relevantes de System' 'No se completó la lectura adicional de advertencias.' $_.Exception.Message 'Los errores/críticos obtenidos se conservan; no se afirma cobertura de advertencias.'}}
 }
 if(-not $events.Count){Row 'good' ('Eventos · '+$log) ('Sin errores/críticos visibles entre '+(WindowText)+'.') 'Errores/críticos y, en System, una muestra adicional de avisos concretos de almacenamiento, WHEA y DNS. No incluye Security ni todas las advertencias.' 'Una consulta vacía no certifica la salud del equipo.';return}
 Row 'info' ('Cobertura · '+$log) ($events.Count.ToString()+' eventos leídos · '+(WindowText)+' · máximo '+$max+' errores/críticos y hasta 40 avisos específicos de System') 'Los recuentos por grupo corresponden a esta muestra de eventos más recientes.' 'Si se alcanza el máximo, puede haber eventos anteriores que no se han leído. Ajusta la ventana para acotar la incidencia.' 'event.coverage' @{Identity=$log;Limited=($regularCount -ge $max -or $warningLimited);Count=$events.Count}
 foreach($group in @($events | Group-Object -Property { $key=([string]$_.ProviderName)+'|'+$_.Id;if($_.ProviderName -in @('Application Error','Application Hang','Service Control Manager')){$key+='|'+(EventSubject $_)};$key })){
  $last=$group.Group | Sort-Object TimeCreated -Descending | Select-Object -First 1
  $advice='Relaciona la fecha y el mensaje con el síntoma. El evento por sí solo no identifica la causa.'
  if($last.ProviderName -eq 'Microsoft-Windows-Kernel-Power' -and $last.Id -eq 41){$advice='Windows registró un apagado no limpio. No demuestra una fuente de alimentación averiada. Correlaciona con BugCheck, volcado y contexto del apagado.'}
  elseif($last.ProviderName -match 'WER-SystemErrorReporting|BugCheck' -and $last.Id -eq 1001){$advice='Hay evidencia de un error de parada. Revisa el código y los metadatos del volcado. No se analiza la memoria ni se confirma un controlador culpable.'}
  elseif($last.ProviderName -eq 'Service Control Manager' -and $last.Id -in @(7000,7001,7023,7024,7031,7034)){$advice='Revisa el servicio nombrado, sus dependencias y su código de error; no reinicies servicios sin conocer el impacto.'}
  elseif($last.ProviderName -match 'WHEA-Logger'){$advice='El proveedor ha registrado un error de hardware. Revisa los detalles y la repetición; no identifica por sí solo una pieza que deba sustituirse.'}
  elseif($last.ProviderName -match 'disk|Ntfs|storahci|stornvme|storport'){$advice='Correlaciona con el disco/volumen afectado, los contadores de almacenamiento y los tiempos de respuesta. No ejecutes reparaciones antes de valorar los datos.'}
  $evidence=Details ($group.Group | Sort-Object TimeCreated -Descending | Select-Object -First 3 TimeCreated,Id,LevelDisplayName,ProviderName,Message)
  Row 'warning' ($last.ProviderName+' · ID '+$last.Id) ($group.Count.ToString()+' repeticiones en la muestra · última '+$last.TimeCreated.ToString('g')) $evidence $advice 'event.group' @{Identity=($log+'|'+$last.ProviderName+'|'+$last.Id+'|'+(EventSubject $last));Log=$log;Level=$last.Level;Subject=(EventSubject $last);TimesUtc=@($group.Group|Sort-Object TimeCreated -Descending|Select-Object -First 20|ForEach-Object {$_.TimeCreated.ToUniversalTime().ToString('o')});Provider=[string]$last.ProviderName;Id=$last.Id;LastSeenUtc=$last.TimeCreated.ToUniversalTime().ToString('o');Occurrences=$group.Count;WindowStartUtc=$Desde.ToUniversalTime().ToString('o');WindowEndUtc=$Hasta.ToUniversalTime().ToString('o');Limited=($regularCount -ge $max -or $warningLimited)}
 }
}

'''

CHECKS = {
'pc': r'''

Check 'Windows y memoria' {
 $os=Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 8
 MemoryRow $os
 Row 'info' 'Windows y último arranque' ($os.Caption+' · '+$os.Version) (Details ($os | Select-Object Caption,Version,BuildNumber,LastBootUpTime,OSArchitecture,TotalVirtualMemorySize,FreeVirtualMemory)) 'La versión no demuestra que todas las actualizaciones estén instaladas. La memoria virtual incluye paginación.'
}
Check 'Hardware' {
 $cs=Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 8
 Row 'info' 'Equipo y pertenencia' ($cs.Manufacturer+' '+$cs.Model) (Details ($cs | Select-Object Manufacturer,Model,Domain,PartOfDomain,TotalPhysicalMemory,NumberOfLogicalProcessors)) 'Confirma el equipo, la memoria instalada y la pertenencia a dominio. No se comprueba la confianza con el dominio.'
}
Check 'CPU con varias muestras' {
 $samples=@();$source='Contador de rendimiento CIM · 3 muestras separadas 0,5 s'
 for($i=0;$i -lt 3;$i++){
  $cpu=Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'" -OperationTimeoutSec 6
  $value=Number $cpu.PercentProcessorTime
  if($null -ne $value -and $value -ge 0 -and $value -le 100){$samples+=$value}
  if($i -lt 2){[Threading.Thread]::Sleep(500)}
 }
 if($samples.Count -ne 3){Row 'unknown' 'Carga de CPU' 'No se obtuvieron las 3 muestras válidas.' (Details $samples) 'No se interpreta una muestra ausente como 0%. El proveedor puede no estar disponible.'}
 else {$avg=[math]::Round(($samples|Measure-Object -Average).Average,1);$high=@($samples|Where-Object {$_ -ge 90}).Count
  Row $(if($high -eq 3){'warning'}else{'info'}) 'Carga de CPU' ('Media '+$avg+'% · muestras: '+($samples -join ', ')) $source 'Solo se avisa si las 3 muestras llegan al 90%. Es un intervalo corto; repite durante la lentitud y correlaciona con los procesos.' 'cpu.snapshot' @{Average=$avg;Samples=@($samples);HighCount=$high}
 }
}
Check 'Procesos por memoria' {
 $p=@(Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 15 ProcessName,Id,@{n='MemoriaMB';e={[math]::Round($_.WorkingSet64/1MB,1)}})
 Row 'info' 'Procesos con mayor memoria' ($p.Count.ToString()+' procesos por memoria residente') (Details $p) 'La memoria residente no equivale a una fuga de memoria. No se finaliza ningún proceso.'
}
Check 'Uso del archivo de paginación' {
 $files=@(Get-CimInstance Win32_PageFileUsage -OperationTimeoutSec 8|Select-Object Name,AllocatedBaseSize,CurrentUsage,PeakUsage)
 if(-not $files.Count){Row 'info' 'Archivo de paginación' 'No se encontraron archivos de paginación en este proveedor.' '' 'Puede ser una configuración intencionada. No se crea ni se cambia el archivo de paginación.'}
 else{
  $allocated=($files|Measure-Object AllocatedBaseSize -Sum).Sum;$used=($files|Measure-Object CurrentUsage -Sum).Sum
  if($allocated -gt 0){$pct=[math]::Round(100*$used/$allocated,1);Row 'info' 'Archivo de paginación' ($pct.ToString()+'% del espacio asignado en uso') (Details $files) 'Los tamaños están en MB. El porcentaje ocupado del archivo no equivale al compromiso de memoria ni demuestra lentitud. Usa la observación para correlacionar compromiso, paginación y latencia. No se recomienda deshabilitar la paginación.'}
  else{Row 'unknown' 'Archivo de paginación' 'No se devolvió un tamaño asignado válido.' (Details $files) 'No se calcula un porcentaje con un denominador ausente o cero.'}
 }
}
Check 'Procesos por CPU en intervalo' {
 $before=@{};$skipped=0
 foreach($p in @(Get-Process)){try{if($null -ne $p.CPU){$before[$p.Id]=@([double]$p.CPU,$p.StartTime.ToUniversalTime().Ticks)}}catch{$skipped++}}
 $clock=[Diagnostics.Stopwatch]::StartNew();[Threading.Thread]::Sleep(1000)
 $cpus=[Environment]::ProcessorCount;$values=@();$seconds=$clock.Elapsed.TotalSeconds
 foreach($p in @(Get-Process)){try{
  if($before.ContainsKey($p.Id) -and $null -ne $p.CPU -and $before[$p.Id][1] -eq $p.StartTime.ToUniversalTime().Ticks){
   $delta=[double]$p.CPU-$before[$p.Id][0]
   if($delta -ge 0){$values+=[pscustomobject]@{Proceso=$p.ProcessName;PID=$p.Id;CPU_Porcentaje=[math]::Round([math]::Min(100,100*$delta/$seconds/$cpus),1)}}
  }
 }catch{$skipped++}}
 if(-not $values.Count){Row 'unknown' 'Procesos por CPU' 'No se pudieron comparar procesos en el intervalo.' ('Lecturas omitidas: '+$skipped) 'Los permisos o procesos que terminan pueden reducir la cobertura.'}
 else{Row 'info' 'Procesos por CPU' ('Top 10 · intervalo '+[math]::Round($seconds,1)+' s · lecturas omitidas '+$skipped) (Details ($values | Sort-Object CPU_Porcentaje -Descending | Select-Object -First 10)) 'Porcentaje normalizado por procesadores lógicos visibles. Se comprueba la hora de inicio para evitar confundir un PID reutilizado. Es orientativo.' 'cpu.processes' @{Count=$values.Count}}
}
Check 'Dispositivos con incidencias' {
 $d=@(Get-CimInstance Win32_PnPEntity -Filter 'ConfigManagerErrorCode <> 0' -OperationTimeoutSec 8 | Select-Object Name,PNPClass,ConfigManagerErrorCode,Status)
 $bad=@($d|Where-Object {$_.ConfigManagerErrorCode -ne 22})
 Row $(if($bad.Count){'warning'}else{'info'}) 'Estado de dispositivos' ($bad.Count.ToString()+' incidencias · '+@($d|Where-Object ConfigManagerErrorCode -eq 22).Count+' deshabilitados') (Details $d) 'Código 22 significa deshabilitado y puede ser intencionado. Revisa el código en el Administrador de dispositivos; no se cambia ningún controlador.' 'device.errors' @{Count=$bad.Count;Disabled=@($d|Where-Object ConfigManagerErrorCode -eq 22).Count}
}
Check 'Indicadores de reinicio pendiente' {
 $cbs=Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'
 $wu=Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
 Row 'info' 'Indicadores de reinicio' ('CBS: '+$cbs+' · Windows Update: '+$wu) 'Son indicadores parciales del registro, no una orden de reiniciar. No se leen nombres de archivos pendientes.' 'Si coincide con una actualización o instalación incompleta, acuerda el reinicio con el usuario. No se reinicia automáticamente.' 'printer.spooler' @{State=[string]$s.Status}
}
Check 'Actualizaciones registradas' {
 $q=@(Get-CimInstance Win32_QuickFixEngineering -OperationTimeoutSec 8 | Sort-Object InstalledOn -Descending | Select-Object -First 10 HotFixID,Description,InstalledOn)
 Row 'info' 'Últimas actualizaciones registradas' ($q.Count.ToString()+' entradas recientes del proveedor QFE') (Details $q) 'Este inventario no cubre todas las actualizaciones y no confirma que Windows esté al día. No se busca ni instala nada en línea.'
}

''',
'red': r'''

$script:activeIndexes=@()
Check 'Adaptadores' {
 $a=@(Get-NetAdapter);$up=@($a|Where-Object Status -eq 'Up');$script:activeIndexes=@($up|ForEach-Object {$_.ifIndex})
 if(-not $a.Count){Row 'unknown' 'Adaptadores' 'No se encontraron adaptadores visibles.' '' 'Revisa el proveedor y los controladores.'}
 else{Row $(if($up.Count){'info'}else{'warning'}) 'Adaptadores de red' ($up.Count.ToString()+' activos de '+$a.Count) (Details ($a|Select-Object Name,ifIndex,InterfaceDescription,Status,LinkSpeed)) 'Un adaptador desconectado puede ser normal. Un enlace activo no garantiza salida a Internet.'}
}
Check 'Direcciones y APIPA' {
 $ip=@(Get-NetIPAddress | Where-Object {$_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -ne '::1'})
 Row 'info' 'Direcciones IP' ($ip.Count.ToString()+' direcciones fuera de loopback') (Details ($ip|Select-Object InterfaceAlias,InterfaceIndex,IPAddress,PrefixLength,AddressFamily,AddressState)) 'Revisa dirección, máscara y estado. IPv6 link-local es normal y no implica fallo.'
 $apipa=@($ip|Where-Object {$_.IPAddress -like '169.254.*' -and $_.InterfaceIndex -in $script:activeIndexes})
 if($apipa.Count){Row 'warning' 'APIPA en una interfaz activa' 'Hay IPv4 de autoconfiguración en un enlace activo.' (Details ($apipa|Select-Object InterfaceAlias,IPAddress,AddressState)) 'Comprueba DHCP o la red esperada. No se avisa por APIPA en adaptadores desconectados.' 'network.apipa' @{Count=$apipa.Count;Identity='active'}}
 $usable=@($ip|Where-Object {$_.InterfaceIndex -in $script:activeIndexes -and $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -notlike 'fe80:*' -and $_.AddressState -eq 'Preferred'})
 if($script:activeIndexes.Count -and -not $usable.Count){Row 'warning' 'IP utilizable en enlaces activos' 'No se vio una dirección preferida fuera de autoconfiguración/link-local.' '' 'Comprueba DHCP, dirección estática y duplicidades. No se solicita una renovación de IP.' 'network.address' @{Count=$usable.Count}}
}
Check 'Rutas' {
 $r=@(Get-NetRoute)
 Row 'info' 'Tabla de rutas' ($r.Count.ToString()+' rutas locales') (Details ($r|Select-Object DestinationPrefix,NextHop,InterfaceIndex,InterfaceAlias,RouteMetric)) 'Revisa ruta por defecto y rutas específicas/VPN; un equipo de una red aislada puede no necesitar ruta por defecto.' 'network.routes' @{Identity='routes';Routes=(@($r|Sort-Object InterfaceIndex,DestinationPrefix,NextHop|ForEach-Object {$_.InterfaceIndex.ToString()+':'+$_.DestinationPrefix+'>'+ $_.NextHop}) -join ';')}
 $default=@($r|Where-Object {$_.DestinationPrefix -in @('0.0.0.0/0','::/0') -and $_.InterfaceIndex -in $script:activeIndexes})
 if($script:activeIndexes.Count -and -not $default.Count){Row 'info' 'Ruta por defecto' 'No se ve una ruta por defecto en las interfaces activas.' '' 'Puede explicar acceso limitado fuera de la subred, pero puede ser intencionado en una red aislada.'}
}
Check 'DNS configurado' {
 $d=@(Get-DnsClientServerAddress)
 Row 'info' 'Servidores DNS' 'Configuración por interfaz.' (Details ($d|Select-Object InterfaceAlias,InterfaceIndex,AddressFamily,ServerAddresses)) 'Comprueba los DNS corporativos. Solo se consulta un nombre si indicas un destino.' 'network.dns.config' @{Identity='configured';Addresses=(@($d|Sort-Object InterfaceIndex,AddressFamily|ForEach-Object {($_.InterfaceIndex.ToString())+':'+($_.ServerAddresses -join ',')}) -join ';')}
 if($script:activeIndexes.Count -and -not @($d|Where-Object {$_.InterfaceIndex -in $script:activeIndexes -and $_.ServerAddresses.Count -gt 0}).Count){Row 'warning' 'DNS en enlaces activos' 'No se ven servidores DNS en las interfaces activas.' '' 'Puede afectar al acceso por nombre; no demuestra que falle una conexión por IP.' 'network.dns.missing' @{Count=0}}
}
Check 'DHCP y perfil' {
 Row 'info' 'DHCP por interfaz' 'Configuración de DHCP local.' (Details (Get-NetIPInterface | Where-Object InterfaceIndex -in $script:activeIndexes | Select-Object InterfaceAlias,AddressFamily,Dhcp,ConnectionState,InterfaceMetric)) 'DHCP deshabilitado puede indicar una IP estática válida.'
}
Check 'Perfil de red' {Row 'info' 'Perfil de red' 'Perfiles reconocidos por Windows.' (Details (Get-NetConnectionProfile | Select-Object InterfaceAlias,NetworkCategory,IPv4Connectivity,IPv6Connectivity)) 'Las etiquetas de Windows no sustituyen una prueba del recurso corporativo.'}
Check 'Contadores de red' {Row 'info' 'Errores y descartes de interfaces' 'Contadores acumulados del adaptador.' (Details (Get-NetAdapterStatistics | Select-Object Name,ReceivedPacketErrors,OutboundPacketErrors,ReceivedDiscardedPackets,OutboundDiscardedPackets)) 'Un contador histórico no indica un fallo activo. Compara dos consultas antes de concluir pérdida de paquetes.'}
Check 'Proxy del usuario' {
 $p=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
 $server=[string]$p.ProxyServer;$pac=[string]$p.AutoConfigURL
 $server=$server -replace '(?i)(https?://)?[^\s;/@]+:[^\s;/@]+@','$1[credenciales ocultas]@'
 $pac=$pac -replace '(?i)(https?://)[^/@]+@','$1[credenciales ocultas]@'
 $pacUri=$null;if([Uri]::TryCreate($pac,[UriKind]::Absolute,[ref]$pacUri)){$pac=$pacUri.GetLeftPart([UriPartial]::Path)}
 Row 'info' 'Proxy del usuario actual' ('Proxy habilitado: '+$p.ProxyEnable) ('Servidor: '+$server+"`nPAC: "+$pac) 'No es la configuración WinHTTP ni la de todas las aplicaciones. No se descarga el archivo PAC ni se cambia el proxy.'
}
if($Destino){
 Check 'DNS del destino' {
  $literal=$null
  if([Net.IPAddress]::TryParse($Destino,[ref]$literal)){Row 'info' 'Destino por IP' 'Se omitió DNS porque el destino es una IP literal.' $Destino 'No es necesario que una IP tenga registro PTR para probar su puerto TCP.'}
  else{try{$resolverClock=[Diagnostics.Stopwatch]::StartNew();$d=@(Resolve-DnsName -Name $Destino -DnsOnly -QuickTimeout);if(-not $d.Count){throw 'El resolvedor no devolvió registros.'};Row 'good' 'Resolución DNS' ($Destino+' resuelto · '+$resolverClock.ElapsedMilliseconds+' ms') (Details ($d|Select-Object Name,Type,IPAddress,NameHost)) 'Comprueba que la IP resuelta es la esperada. Esta duración mide la consulta explícita, no todas las rutas de resolución de cada aplicación.' 'network.dns' @{Status='resolved';Target=$Destino;Identity=$Destino;Milliseconds=$resolverClock.ElapsedMilliseconds}}catch{Row 'warning' 'Resolución DNS' ('No se pudo resolver '+$Destino) $_.Exception.Message 'Revisa el nombre, los DNS y la VPN. La prueba TCP se intentará por separado.' 'network.dns' @{Status='failed';Target=$Destino;Identity=$Destino}}}
 }

 Check 'Hosts y sufijos del destino' {
  $hostsPath=Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
  $entries=@();$literal=$null
  if(-not [Net.IPAddress]::TryParse($Destino,[ref]$literal)){
   foreach($line in @(Get-Content -LiteralPath $hostsPath)){
    $plain=($line -split '#',2)[0].Trim();if(-not $plain){continue};$parts=@($plain -split '\s+')
    if($parts.Count -gt 1 -and @($parts|Select-Object -Skip 1|Where-Object {$_ -ieq $Destino}).Count){$entries+=[pscustomobject]@{Direccion=$parts[0];Nombre=$Destino}}
   }
  }
  Row 'info' 'Coincidencias del destino en hosts' ($entries.Count.ToString()+' coincidencias para '+$Destino) (Details $entries) 'Solo se muestran las coincidencias del nombre indicado. Una entrada puede ser intencionada; no se modifica hosts.' 'network.hosts' @{Identity=$Destino;Matches=$entries.Count}
  Row 'info' 'Sufijos y orden de resolución' 'Configuración del cliente DNS.' (Details (Get-DnsClientGlobalSetting|Select-Object SuffixSearchList,UseDevolution,DevolutionLevel)) 'Un nombre corto puede resolverse de forma distinta con VPN, sufijos o entradas hosts. La consulta explícita DNS no reproduce todas las rutas de resolución de cada aplicación.'
 }

 Check 'TCP del destino' {Tcp $Destino $Puerto 'Conexión TCP al destino'}
}

''',
'rdp': r'''

$script:rdpEnabled=$null;$script:rdpPort=$null
Check 'Función RDP local' {
 $c=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections
 if($null -eq $c.fDenyTSConnections){throw 'No se devolvió fDenyTSConnections.'}
 $script:rdpEnabled=$c.fDenyTSConnections -eq 0
 Row 'info' 'Escritorio remoto local' $(if($script:rdpEnabled){'La función está habilitada.'}else{'La función está deshabilitada.'}) ('fDenyTSConnections: '+$c.fDenyTSConnections) 'Tener RDP deshabilitado puede ser la política prevista, no una avería. Windows Home no ofrece el mismo servidor RDP que Pro.' 'rdp.config' @{Enabled=$script:rdpEnabled}
}
Check 'Puerto y NLA' {
 $p=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp'
 if($p.PortNumber -gt 0 -and $p.PortNumber -le 65535){$script:rdpPort=[int]$p.PortNumber}
 Row 'info' 'Puerto y autenticación' ('Puerto: '+$p.PortNumber+' · NLA: '+$p.UserAuthentication) (Details ($p|Select-Object PortNumber,UserAuthentication,SecurityLayer)) 'La configuración no prueba el acceso efectivo, la pertenencia a grupos ni las políticas de inicio de sesión.'
}
Check 'Escucha RDP' {
 if($null -eq $script:rdpPort){Row 'unknown' 'Escucha RDP local' 'No se pudo determinar el puerto local.' '' 'No se presupone que sea 3389 si la consulta del registro falló.'}
 else{$l=@(Get-NetTCPConnection -State Listen|Where-Object LocalPort -eq $script:rdpPort);Row $(if($l.Count){'good'}elseif($script:rdpEnabled -eq $true){'warning'}else{'info'}) 'Escucha RDP local' ($l.Count.ToString()+' escuchas en el puerto '+$script:rdpPort) (Details ($l|Select-Object LocalAddress,LocalPort,OwningProcess)) 'La ausencia de escucha solo se avisa si RDP está habilitado. No se valida el firewall efectivo ni UDP.' 'rdp.listen' @{Enabled=$script:rdpEnabled;Count=$l.Count;Port=$script:rdpPort}}
}
Check 'Servicio RDP' {$s=OptionalService 'TermService';if($null -eq $s){Row 'info' 'TermService' 'El servicio no está instalado.' '' 'Comprueba la edición de Windows.'}else{Row $(if($script:rdpEnabled -eq $true -and $s.Status -ne 'Running'){'warning'}else{'info'}) 'TermService' ([string]$s.Status) (Details ($s|Select-Object Name,Status,StartType)) 'Se avisa por un servicio detenido solo si la función local está habilitada.' 'rdp.service' @{State=[string]$s.Status;Enabled=$script:rdpEnabled}}}
if($Destino){Check 'TCP RDP remoto' {Tcp $Destino $Puerto 'Puerto RDP del destino'}}

''',
'eventos': r'''

foreach($log in @('System','Application')){Check ('Eventos de '+$log) {GroupEvents $log 100}}
Check 'Metadatos de volcados' {
 $path=Join-Path $env:SystemRoot 'Minidump'
 if(Test-Path -LiteralPath $path){$f=@(Get-ChildItem -LiteralPath $path -Filter '*.dmp' -File|Sort-Object LastWriteTime -Descending|Select-Object -First 8 Name,Length,LastWriteTime);Row 'info' 'Volcados de error de parada' ($f.Count.ToString()+' archivos recientes visibles') (Details $f) 'Solo se leen nombres, tamaños y fechas. No se abre el contenido de memoria ni se atribuye el fallo a un controlador.'}
 else{Row 'info' 'Volcados de error de parada' 'No se encontró la carpeta Minidump.' '' 'La ausencia de archivos no descarta un error de parada; puede haber otra configuración o limpieza previa.'}
}

''',
'defender': r'''

Check 'Microsoft Defender' {
 $s=Get-MpComputerStatus
 if($null -eq $s.AntivirusEnabled -or $null -eq $s.RealTimeProtectionEnabled){Row 'unknown' 'Protección de Defender' 'No se devolvieron los estados de protección.' (Details ($s|Select-Object AMRunningMode,AMServiceEnabled)) 'No se interpretan campos vacíos como protección desactivada.'}
 else{$passive=([string]$s.AMRunningMode -match 'Passive|EDR');$state=if($passive){'info'}elseif(-not $s.AntivirusEnabled -or -not $s.RealTimeProtectionEnabled){'warning'}else{'good'}
  Row $state 'Protección de Defender' ('Antivirus: '+$s.AntivirusEnabled+' · tiempo real: '+$s.RealTimeProtectionEnabled+' · modo: '+$s.AMRunningMode) (Details ($s|Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AMRunningMode,QuickScanEndTime,FullScanEndTime)) 'El modo pasivo puede ser intencionado si existe otro producto. No se inicia ningún análisis.' 'security.status' @{Mode=[string]$s.AMRunningMode;Antivirus=$s.AntivirusEnabled;RealTime=$s.RealTimeProtectionEnabled}
 }
 $stamp=$s.AntivirusSignatureLastUpdated
 if($null -eq $stamp -or [string]::IsNullOrWhiteSpace([string]$s.AntivirusSignatureVersion)){Row 'unknown' 'Firmas de Defender' 'No se obtuvo fecha o versión de firmas.' '' 'Comprueba el producto de seguridad asignado.'}
 else{$age=((Get-Date)-[datetime]$stamp).TotalHours;$state=if($age -lt -1){'unknown'}elseif($age -gt 48 -and [string]$s.AMRunningMode -notmatch 'Passive|EDR'){'warning'}else{'info'};Row $state 'Firmas de Defender' ('Última actualización: '+$stamp) (Details ($s|Select-Object AntivirusSignatureVersion,AntivirusSignatureLastUpdated,AntivirusSignatureAge)) 'Aviso orientativo tras 48 h; una fecha futura puede indicar reloj incorrecto. No se actualiza automáticamente.' 'security.signatures' @{AgeHours=$age;Mode=[string]$s.AMRunningMode}}
}
Check 'Productos antivirus registrados' {
 $os=Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 8
 if($os.ProductType -gt 1){Row 'info' 'Centro de seguridad' 'Inventario SecurityCenter2 omitido en Windows Server.' '' 'El registro de antivirus de cliente no es equivalente en Server.'}
 else{$av=@(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -OperationTimeoutSec 8|Select-Object displayName,timestamp);Row 'info' 'Antivirus registrados' ($av.Count.ToString()+' productos registrados') (Details $av) 'El registro no prueba que el producto esté activo o actualizado. No se descodifica productState con suposiciones no documentadas.'}
}
Check 'Amenazas activas en Defender' {
 $t=@(Get-MpThreat|Where-Object IsActive -eq $true|Select-Object ThreatID,ThreatName,IsActive,DidThreatExecute,SeverityID)
 Row $(if($t.Count){'critical'}else{'info'}) 'Amenazas activas comunicadas por Defender' ($t.Count.ToString()+' amenazas activas visibles') (Details $t) 'Solo se consulta el estado existente; no es un análisis antivirus ni sustituye la consola de seguridad. No se muestran rutas de archivos afectados.' 'security.active' @{Count=$t.Count}
}

''',
'discos': r'''

Check 'Espacio en discos' {$d=@(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' -OperationTimeoutSec 8);if(-not $d.Count){Row 'unknown' 'Unidades locales' 'No se obtuvieron unidades fijas.' '' 'Revisa el proveedor.'};foreach($v in $d){SpaceRow $v}}
Check 'Discos físicos' {
 $disks=@(Get-PhysicalDisk)
 if(-not $disks.Count){Row 'unknown' 'Discos físicos' 'El proveedor no devolvió discos.' '' 'No se puede evaluar su estado físico.'}
 foreach($d in $disks){$h=[string]$d.HealthStatus;$state=if($h -eq 'Healthy'){'good'}elseif($h -eq 'Unknown' -or -not $h){'unknown'}else{'warning'};Row $state ('Disco físico · '+$d.FriendlyName) $h (Details ($d|Select-Object FriendlyName,MediaType,HealthStatus,OperationalStatus,Size)) 'Depende del proveedor. No sustituye una prueba física o de superficie.' 'disk.health' @{Identity=[string]$d.FriendlyName;Health=$h}}
}
Check 'Fiabilidad del almacenamiento' {
 foreach($disk in @(Get-PhysicalDisk)) {Check ('Fiabilidad · '+$disk.FriendlyName) {
  $c=Get-StorageReliabilityCounter -PhysicalDisk $disk
  $fields=@('Temperature','TemperatureMax','Wear','ReadErrorsUncorrected','WriteErrorsUncorrected','PowerOnHours')
  $present=@($fields|Where-Object {$null -ne $c.$_})
  if(-not $present.Count){Row 'unknown' ('Fiabilidad · '+$disk.FriendlyName) 'El dispositivo no ha comunicado contadores utilizables.' '' 'No se interpreta como desgaste 0% ni como ausencia de errores.'}
  else{
   $error=($null -ne $c.ReadErrorsUncorrected -and $c.ReadErrorsUncorrected -gt 0) -or ($null -ne $c.WriteErrorsUncorrected -and $c.WriteErrorsUncorrected -gt 0)
   $wear=Number $c.Wear;$hot=$null -ne $c.Temperature -and $null -ne $c.TemperatureMax -and $c.TemperatureMax -gt 0 -and $c.Temperature -gt $c.TemperatureMax
   $state=if($null -ne $wear -and $wear -ge 100){'critical'}elseif($error -or $hot -or ($null -ne $wear -and $wear -ge 90)){'warning'}else{'info'}
   Row $state ('Fiabilidad · '+$disk.FriendlyName) ($present.Count.ToString()+' contadores disponibles del dispositivo') (Details ($c|Select-Object Temperature,TemperatureMax,Wear,PowerOnHours,ReadErrorsUncorrected,WriteErrorsUncorrected)) 'Wear es desgaste consumido: 100% alcanza el límite estimado; 90% es un aviso orientativo. Los errores pueden ser históricos. La temperatura solo se compara con el máximo que informa el propio dispositivo. No se resetean contadores.' 'disk.reliability' @{Identity=([string]$disk.DeviceId+':'+$disk.FriendlyName);Wear=$wear;Temperature=$c.Temperature;TemperatureMax=$c.TemperatureMax;ReadErrors=$c.ReadErrorsUncorrected;WriteErrors=$c.WriteErrorsUncorrected}
  }
 }}
}
Check 'Volúmenes' {
 foreach($v in @(Get-Volume)){$h=[string]$v.HealthStatus;$state=if($h -and $h -notin @('Healthy','Unknown')){'warning'}elseif($h -eq 'Unknown' -or -not $h){'unknown'}else{'info'};Row $state ('Volumen '+$v.DriveLetter+' '+$v.FileSystemLabel) ('Salud comunicada: '+$h) (Details ($v|Select-Object DriveLetter,FileSystemLabel,FileSystem,HealthStatus,OperationalStatus,Size,SizeRemaining)) 'Un volumen sin letra puede ser de recuperación. No se monta ni se repara.' 'disk.volume' @{Identity=([string]$v.DriveLetter+':'+$v.FileSystemLabel);Health=$h}}
}
Check 'Volúmenes marcados dirty' {
 $v=@(Get-CimInstance Win32_Volume -OperationTimeoutSec 8|Where-Object DirtyBitSet -eq $true|Select-Object DriveLetter,Label,FileSystem,DirtyBitSet)
 Row $(if($v.Count){'warning'}else{'info'}) 'Marca dirty del sistema de archivos' ($v.Count.ToString()+' volúmenes con marca activa') (Details $v) 'La marca indica que Windows considera necesaria una comprobación; no confirma por sí sola daño físico. No se ejecuta chkdsk ni reparación.' 'disk.dirty' @{Count=$v.Count}
}

''',
'vpn': r'''

Check 'VPN del usuario' {
 $v=@(Get-VpnConnection)
 if(-not $v.Count){Row 'info' 'VPN del usuario' 'No hay perfiles VPN de Windows para este usuario.' '' 'Los clientes de terceros pueden no aparecer aquí.'}
 foreach($p in $v){Row 'info' $p.Name ([string]$p.ConnectionStatus) (Details ($p | Select-Object Name,ServerAddress,TunnelType,ConnectionStatus,SplitTunneling)) 'Un perfil desconectado puede ser normal. No se conecta ni desconecta la VPN.'}
}
Check 'VPN compartidas' {
 $v=@(Get-VpnConnection -AllUserConnection)
 Row 'info' 'VPN de todos los usuarios' ($v.Count.ToString()+' perfiles de Windows') (Details ($v | Select-Object Name,ServerAddress,TunnelType,ConnectionStatus,SplitTunneling)) 'La cuenta puede no tener acceso a todos los perfiles.'
}
Check 'Rutas y adaptadores de VPN' {
 Row 'info' 'Adaptadores (incluidos ocultos)' 'Revisa los adaptadores del cliente VPN.' (Details (Get-NetAdapter -IncludeHidden | Select-Object Name,InterfaceDescription,Status)) 'Una interfaz virtual desconectada puede ser normal.'
 Row 'info' 'Rutas' 'Rutas locales, incluidas las de VPN.' (Details (Get-NetRoute | Select-Object DestinationPrefix,NextHop,InterfaceAlias)) 'Comprueba si existe una ruta hacia el recurso corporativo.'
}
Check 'Eventos VPN' {
 try {$ev=@(Get-WinEvent -FilterHashtable @{LogName='Application';ProviderName='RasClient';StartTime=$Desde;EndTime=$Hasta;Level=@(1,2,3)} -MaxEvents 20 -ErrorAction Stop)}
 catch {if($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*'){$ev=@()}else{throw}}
 Row $(if($ev.Count){'warning'}else{'info'}) 'Eventos RasClient' ($ev.Count.ToString()+' eventos entre '+(WindowText)+' (máximo 20)') (Details ($ev | Select-Object TimeCreated,Id,LevelDisplayName,Message)) 'Solo cubre el cliente VPN integrado. Un evento antiguo no demuestra que la VPN esté fallando ahora.' 'vpn.events' @{Count=$ev.Count;TimesUtc=@($ev|ForEach-Object {$_.TimeCreated.ToUniversalTime().ToString('o')});Limited=($ev.Count -ge 20)}
}

Check 'Servicio de acceso remoto' {$s=OptionalService 'RasMan';Row 'info' 'RasMan' $(if($s){[string]$s.Status}else{'No instalado'}) (Details ($s|Select-Object Name,Status,StartType)) 'Un servicio detenido puede usar inicio bajo demanda y no es necesariamente un error.'}
Check 'Sufijos DNS de conexiones' {Row 'info' 'DNS de las interfaces' 'Sufijos por conexión, útiles para recursos de VPN.' (Details (Get-DnsClient|Select-Object InterfaceAlias,ConnectionSpecificSuffix,RegisterThisConnectionsAddress)) 'Comprueba el sufijo esperado para resolver nombres corporativos. No se registra ni cambia DNS.'}

''',
'software': r'''

foreach($p in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')){Check $p {
 if(-not (Test-Path $p)){Row 'info' 'Origen de inventario ausente' $p 'No se encontró esta ubicación del registro.' 'Esto no indica un problema; puede no haber aplicaciones registradas en ese origen.'}
 else {$s=@(Get-ItemProperty $p | Where-Object DisplayName | Select-Object DisplayName,DisplayVersion,Publisher,InstallDate | Sort-Object DisplayName);Row 'info' 'Software registrado' ($s.Count.ToString()+' entradas · '+$p) (Details $s) 'Inventario parcial. No ejecuta Win32_Product, instaladores ni reparaciones. Puede haber duplicados y no cubre portables.'}
}}
Check 'Appx del usuario' {
 $a=@(Get-AppxPackage | Select-Object Name,Version,Publisher | Sort-Object Name)
 Row 'info' 'Paquetes Appx' ($a.Count.ToString()+' paquetes del usuario actual') (Details $a) 'No incluye todos los perfiles ni demuestra que una aplicación funcione.'
}

''',
'servicios': r'''

Check 'Servicios' {
 $s=@(Get-CimInstance Win32_Service -OperationTimeoutSec 8|Select-Object Name,DisplayName,State,StartMode,ExitCode,ServiceSpecificExitCode)
 if(-not $s.Count){Row 'unknown' 'Servicios' 'No se obtuvieron servicios.' '' 'No se presenta una lista vacía como correcta.'}
 else{
  $stopped=@($s|Where-Object {$_.StartMode -eq 'Auto' -and $_.State -ne 'Running'})
  $withErrors=@($stopped|Where-Object {$null -ne $_.ExitCode -and $_.ExitCode -notin @(0,1077)})
  Row $(if($withErrors.Count){'warning'}else{'info'}) 'Automáticos detenidos' ($stopped.Count.ToString()+' detenidos · '+$withErrors.Count+' con código de error para revisar') (Details $stopped) 'Un servicio automático detenido puede usar inicio diferido o desencadenadores. El código 1077 significa que no se intentó iniciarlo desde el arranque y se muestra como contexto. Correlaciona los demás códigos con Service Control Manager.' 'service.errors' @{Count=$withErrors.Count;Stopped=$stopped.Count;Names=@($withErrors|ForEach-Object {$_.Name;$_.DisplayName})}
  Row 'info' 'Todos los servicios' ($s.Count.ToString()+' servicios visibles') (Details ($s|Sort-Object Name)) 'No se inician, detienen ni reinician servicios.'
 }
}

''',
'impresoras': r'''

Check 'Impresoras instaladas' {
 $p=@(Get-Printer)
 if(-not $p.Count){Row 'info' 'Impresoras' 'No se encontraron impresoras en este contexto.' '' 'Una impresora de otro usuario puede no ser visible para tu cuenta.'}
 foreach($i in $p){
  $e=if([string]$i.PrinterStatus -match 'Error|Offline|PaperOut|PaperJam|DoorOpen|NotAvailable|UserIntervention'){'warning'}else{'info'}
  Row $e $i.Name ([string]$i.PrinterStatus) (Details ($i | Select-Object Name,DriverName,PortName,PrinterStatus,Shared)) 'El estado depende del controlador y puede estar desactualizado. No se imprime ninguna prueba.' 'printer.state' @{Identity=[string]$i.Name;State=[string]$i.PrinterStatus}
  Check ('Cola '+$i.Name) {
   $j=@(Get-PrintJob -PrinterName $i.Name | Select-Object ID,PrinterName,JobStatus,SubmittedTime,Size)
   $fallos=@($j | Where-Object { [string]$_.JobStatus -match 'Error|Offline|PaperOut|Blocked|UserIntervention' -or ($null -ne $_.SubmittedTime -and [datetime]$_.SubmittedTime -lt (Get-Date).AddHours(-1)) })
   Row $(if($fallos.Count){'warning'}else{'info'}) ('Cola · '+$i.Name) ($j.Count.ToString()+' trabajos pendientes') (Details $j) 'Se avisa por errores de cola o trabajos de más de una hora. Un trabajo antiguo puede estar pausado intencionadamente. No se muestran nombres de documentos ni se eliminan trabajos.' 'printer.queue' @{Identity=[string]$i.Name;Printer=[string]$i.Name;Count=$j.Count;ToReview=$fallos.Count}
  }
 }
}
Check 'Puertos de impresión' {Row 'info' 'Puertos' 'Puertos y direcciones de las impresoras.' (Details (Get-PrinterPort | Select-Object Name,PrinterHostAddress,PortNumber)) 'Comprueba si la dirección coincide con la impresora afectada.'}
Check 'Cola de impresión' {$s=Get-Service Spooler;Row $(if($s.Status -eq 'Running'){'good'}else{'warning'}) 'Spooler' ([string]$s.Status) (Details ($s | Select-Object Name,Status,StartType)) 'Si está detenido, puede afectar a la impresión. No se reinicia automáticamente.' 'printer.spooler' @{State=[string]$s.Status}}

Check 'Eventos de impresión' {GroupEvents 'Microsoft-Windows-PrintService/Admin' 30}

''',
'intune': r'''

Check 'Intune Management Extension' {
 $s=OptionalService 'IntuneManagementExtension'
 if(-not $s){Row 'info' 'Servicio IME' 'No está instalado en este equipo.' '' 'IME no es obligatorio para todos los escenarios MDM. Su ausencia no es por sí sola una incidencia.'}
 else{Row 'info' 'Servicio IME' ([string]$s.Status) (Details ($s|Select-Object Name,Status,StartType)) 'Su estado no demuestra inscripción, sincronización correcta ni cumplimiento.'}
}
Check 'Tareas de gestión' {
 $tasks=@(Get-ScheduledTask|Where-Object TaskPath -like '\Microsoft\Windows\EnterpriseMgmt\*')
 Row 'info' 'Tareas EnterpriseMgmt' ($tasks.Count.ToString()+' tareas visibles') (Details ($tasks|Select-Object TaskName,TaskPath,State)) 'No se ejecuta ninguna tarea. Se consultan los resultados de hasta 15 tareas para limitar la duración.'
 foreach($t in @($tasks|Select-Object -First 15)){Check ('Última ejecución · '+$t.TaskName) {
  $i=Get-ScheduledTaskInfo -TaskName $t.TaskName -TaskPath $t.TaskPath
  $never=$null -eq $i.LastRunTime -or $i.LastRunTime.Year -lt 2000
  $code=$i.LastTaskResult;$normal=@(0,267008,267009,267010,267011,267013)
  $state=if(-not $never -and $null -ne $code -and $code -notin $normal){'warning'}else{'info'}
  Row $state ('Tarea · '+$t.TaskName) $(if($never){'Sin ejecución anterior válida.'}else{'Última ejecución '+$i.LastRunTime+' · código '+$code}) (Details ($i|Select-Object LastRunTime,LastTaskResult,NextRunTime,NumberOfMissedRuns)) 'No todos los códigos son errores: lista para ejecutar, en curso, deshabilitada o nunca ejecutada son estados del programador. Un resultado antiguo no demuestra un fallo actual.' 'mdm.task' @{Identity=($t.TaskPath+$t.TaskName);Result=$code;Never=$never;LastRunUtc=$(if(-not $never){$i.LastRunTime.ToUniversalTime().ToString('o')}else{''})}
 }}
}
Check 'Eventos MDM' {GroupEvents 'Microsoft-Windows-DeviceManagement-Enterprise-Diagnostics-Provider/Admin' 40}
Check 'Metadatos de logs IME' {
 $p=Join-Path $env:ProgramData 'Microsoft\IntuneManagementExtension\Logs'
 if(-not(Test-Path -LiteralPath $p)){Row 'info' 'Archivos de registro IME' 'La carpeta de logs no existe.' '' 'Puede ser normal si IME no está instalado.'}
 else{$f=@(Get-ChildItem -LiteralPath $p -File|Select-Object Name,Length,LastWriteTime);Row 'info' 'Archivos de registro IME' ($f.Count.ToString()+' archivos visibles') (Details $f) 'Solo metadatos. No se leen contenidos, tokens, claves ni certificados; una fecha antigua no confirma falta de sincronización.'}
}

''',
'servidores': r'''

$target=if($Servidor){$Servidor}else{'LOCAL'}
$session=$null;$argsCim=@{OperationTimeoutSec=8}
try{
 if($Servidor){$session=New-CimSession -ComputerName $Servidor -SessionOption (New-CimSessionOption -Protocol Wsman) -OperationTimeoutSec 8;$argsCim.CimSession=$session}
 Check ($target+' · sistema') {$o=Get-CimInstance @argsCim -ClassName Win32_OperatingSystem;MemoryRow $o ($target+' · ');Row 'info' ($target+' · sistema') ($o.Caption+' · '+$o.Version) (Details ($o|Select-Object CSName,Caption,Version,LastBootUpTime)) 'No comprueba aplicaciones, backups, clúster ni AD.'}
 Check ($target+' · CPU') {$v=@(Get-CimInstance @argsCim -ClassName Win32_Processor|Select-Object Name,LoadPercentage);Row 'info' ($target+' · CPU') 'Muestra puntual remota, sin semáforo de saturación.' (Details $v) 'Una muestra no representa una carga sostenida.'}
 Check ($target+' · discos') {$d=@(Get-CimInstance @argsCim -ClassName Win32_LogicalDisk -Filter 'DriveType=3');if(-not $d.Count){throw 'No se obtuvieron discos.'};foreach($v in $d){SpaceRow $v ($target+' · ')}}
 Check ($target+' · servicios') {
  $s=@(Get-CimInstance @argsCim -ClassName Win32_Service|Where-Object {$_.StartMode -eq 'Auto' -and $_.State -ne 'Running'}|Select-Object Name,DisplayName,State,StartMode,ExitCode)
  $errors=@($s|Where-Object {$null -ne $_.ExitCode -and $_.ExitCode -notin @(0,1077)})
  Row $(if($errors.Count){'warning'}else{'info'}) ($target+' · automáticos detenidos') ($s.Count.ToString()+' detenidos · '+$errors.Count+' con código de error') (Details $s) 'Inicio diferido o desencadenadores pueden explicar un servicio detenido. No se reinicia ninguno.'
 }
}catch{Row 'unknown' ($target+' · sesión CIM') 'No se pudo completar la conexión.' $_.Exception.Message 'Requiere acceso delegado y WSMan ya configurado. No se cambian WinRM, TrustedHosts ni firewall.'}
finally{if($session){Remove-CimSession $session -ErrorAction SilentlyContinue}}

''',
}

def packaged():
    return {k: COMMON + '\n' + v + \
        "\nif(-not $EmitirProgreso){ConvertTo-Json -InputObject @($script:rows.ToArray()) -Depth 8 -Compress}\n" for k,v in CHECKS.items()}
