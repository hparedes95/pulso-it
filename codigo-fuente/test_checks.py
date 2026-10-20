from diagnostics import packaged, COMMON, CHECKS
from pathlib import Path
import subprocess,base64,json,sys
scripts=packaged()
mock=r'''
function Get-CimInstance {
 param($ClassName,$Filter,$OperationTimeoutSec,$CimSession,$Namespace)
 switch($ClassName){
 'Win32_OperatingSystem' {[pscustomobject]@{Caption='Windows simulado';Version='10';BuildNumber='123';LastBootUpTime=(Get-Date).AddDays(-1);TotalVisibleMemorySize=$(if($CaseMemoryMissing){$null}else{8MB});FreePhysicalMemory=4MB;CSName='SIMULADO';ProductType=1}}
 'Win32_ComputerSystem' {[pscustomobject]@{Manufacturer='Ejemplo';Model='Equipo simulado';Domain='prueba';TotalPhysicalMemory=8GB}}
 'Win32_Processor' {[pscustomobject]@{Name='CPU simulada';LoadPercentage=12;NumberOfCores=4;NumberOfLogicalProcessors=8}}
 'Win32_PerfFormattedData_PerfOS_Processor' {if(-not $CaseCPU){$CaseCPU=@(10,15,20)};$v=$CaseCPU[$global:cpuIndex % $CaseCPU.Count];$global:cpuIndex++;[pscustomobject]@{Name='_Total';PercentProcessorTime=$v}}
 'Win32_LogicalDisk' {[pscustomobject]@{DeviceID='C:';VolumeName='Simulado';Size=100GB;FreeSpace=$(if($CaseSpaceMissing){$null}else{3GB});FileSystem='NTFS'}}
 'Win32_Service' {[pscustomobject]@{Name='MockService';DisplayName='Servicio simulado';State='Stopped';StartMode='Auto';ExitCode=$(if($null -ne $CaseServiceCode){$CaseServiceCode}elseif($CaseServiceError){1067}else{0})}}
 'Win32_PageFileUsage' {[pscustomobject]@{Name='C:\pagefile.sys';AllocatedBaseSize=1000;CurrentUsage=$(if($CasePagingHigh){950}else{100});PeakUsage=500}}
 'Win32_PnPEntity' {}
 'Win32_QuickFixEngineering' {[pscustomobject]@{HotFixID='KB000000';Description='Simulada';InstalledOn=(Get-Date)}}
 'Win32_Volume' {[pscustomobject]@{DriveLetter='C';DirtyBitSet=$false}}
 'AntiVirusProduct' {[pscustomobject]@{displayName='Antivirus simulado';timestamp='2026'}}
 }
}
function Get-Process {[pscustomobject]@{ProcessName='Simulado';Id=123;WorkingSet64=1GB;CPU=1;StartTime=[datetime]'2026-10-01'}}
function Get-NetAdapter {param([switch]$IncludeHidden) [pscustomobject]@{Name='Ethernet';ifIndex=1;InterfaceDescription='Simulado';Status=$(if($CaseDisconnected){'Disconnected'}else{'Up'});LinkSpeed='1 Gbps'}}
function Get-NetIPAddress {[pscustomobject]@{InterfaceAlias='Ethernet';InterfaceIndex=1;IPAddress='169.254.1.2';PrefixLength=16;AddressFamily='IPv4';AddressState='Preferred'}}
function Get-NetRoute {[pscustomobject]@{DestinationPrefix='0.0.0.0/0';NextHop='10.0.0.1';InterfaceIndex=1;InterfaceAlias='Ethernet';RouteMetric=10}}
function Get-DnsClientServerAddress {[pscustomobject]@{InterfaceAlias='Ethernet';InterfaceIndex=1;AddressFamily='IPv4';ServerAddresses=@('10.0.0.1','10.0.0.2')}}
function Get-NetIPInterface {[pscustomobject]@{InterfaceIndex=1;InterfaceAlias='Ethernet';Dhcp='Enabled';ConnectionState='Connected'}}
function Get-NetConnectionProfile {[pscustomobject]@{InterfaceAlias='Ethernet';NetworkCategory='Private';IPv4Connectivity='LocalNetwork'}}
function Get-NetAdapterStatistics {[pscustomobject]@{Name='Ethernet';ReceivedPacketErrors=0;OutboundPacketErrors=0;ReceivedDiscardedPackets=0;OutboundDiscardedPackets=0}}
function Get-DnsClient {[pscustomobject]@{InterfaceAlias='Ethernet';ConnectionSpecificSuffix='ejemplo.local';RegisterThisConnectionsAddress=$true}}
function Get-DnsClientGlobalSetting {[pscustomobject]@{SuffixSearchList=@('ejemplo.local');UseDevolution=$true;DevolutionLevel=2}}
function Get-Content {param($LiteralPath) '# Hosts simulado sin coincidencias'}
function Get-ItemProperty {param($Path,$Name) [pscustomobject]@{fDenyTSConnections=$(if($CaseRdpDisabled){1}else{0});PortNumber=3389;UserAuthentication=1;SecurityLayer=2;DisplayName='Software simulado';DisplayVersion='1';Publisher='Prueba';InstallDate='20261019';ProxyEnable=0}}
function Get-NetTCPConnection {param($State) if(-not $CaseNoListen){[pscustomobject]@{LocalAddress='0.0.0.0';LocalPort=3389;OwningProcess=123}}}
function Get-Service {param($Name) [pscustomobject]@{Name=$Name;Status='Running';StartType='Automatic'}}
function Get-WinEvent {param($FilterHashtable,$MaxEvents,$ErrorAction) [pscustomobject]@{ProviderName='Proveedor simulado';Id=1;TimeCreated=(Get-Date);LevelDisplayName='Error';Message='Evento simulado con tildes: conexión. No es una incidencia real.'}}
function Get-MpComputerStatus {[pscustomobject]@{AMServiceEnabled=$true;AntivirusEnabled=$true;RealTimeProtectionEnabled=$true;AMRunningMode='Normal';QuickScanEndTime=(Get-Date);FullScanEndTime=(Get-Date);AntivirusSignatureLastUpdated=(Get-Date);AntivirusSignatureVersion='1.2.3';AntivirusSignatureAge=0}}
function Get-MpThreat {}
function Get-PhysicalDisk {[pscustomobject]@{FriendlyName='SSD simulado';MediaType='SSD';HealthStatus='Healthy';OperationalStatus=@('OK');Size=100GB}}
function Get-StorageReliabilityCounter {param($PhysicalDisk) if($CaseMissingReliability){[pscustomobject]@{}}else{[pscustomobject]@{Temperature=30;TemperatureMax=80;Wear=$(if($null -ne $CaseWear){$CaseWear}else{5});PowerOnHours=100;ReadErrorsUncorrected=0;WriteErrorsUncorrected=0}}}
function Get-Volume {[pscustomobject]@{DriveLetter='C';FileSystemLabel='Simulado';FileSystem='NTFS';HealthStatus='Healthy';OperationalStatus=@('OK');Size=100GB;SizeRemaining=3GB}}
function Get-VpnConnection {param([switch]$AllUserConnection) [pscustomobject]@{Name='VPN simulada';ServerAddress='vpn.ejemplo';TunnelType='Ikev2';ConnectionStatus='Disconnected';SplitTunneling=$true}}
function Get-AppxPackage {[pscustomobject]@{Name='Aplicación simulada';Version='1';Publisher='Prueba'}}
function Get-Printer {[pscustomobject]@{Name='Impresora simulada';DriverName='Prueba';PortName='IP_10.0.0.3';PrinterStatus='Offline';Shared=$false}}
function Get-PrintJob {param($PrinterName) [pscustomobject]@{ID=1;PrinterName=$PrinterName;JobStatus=$(if($CaseOldJob){'Normal'}else{'Error'});SubmittedTime=$(if($CaseOldJob){(Get-Date).AddHours(-2)}else{Get-Date});Size=100}}
function Get-PrinterPort {[pscustomobject]@{Name='Puerto simulado';PrinterHostAddress='10.0.0.3';PortNumber=9100}}
function Get-ScheduledTask {[pscustomobject]@{TaskName='Tarea simulada';TaskPath='\Microsoft\Windows\EnterpriseMgmt\Mock\';State='Ready'}}
function Get-ScheduledTaskInfo {param($TaskName,$TaskPath) [pscustomobject]@{LastRunTime=(Get-Date);LastTaskResult=$(if($null -ne $CaseTaskResult){$CaseTaskResult}else{0});NextRunTime=(Get-Date).AddHours(1);NumberOfMissedRuns=0}}
function Get-ChildItem {param($Path,$LiteralPath,$Filter,[switch]$File) [pscustomobject]@{Name='Log simulado';Length=123;LastWriteTime=(Get-Date)}}
function Test-Path {param($Path,$LiteralPath) $true}
function New-CimSessionOption {param($Protocol) 'SIMULADO'}
function New-CimSession {param($ComputerName,$SessionOption,$OperationTimeoutSec) 'SIMULADO'}
function Remove-CimSession {param($CimSession,$ErrorAction) $global:closed=$true}
'''

ps='C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe'
def run(script):
 bootstrap="[Console]::InputEncoding=New-Object System.Text.UTF8Encoding($false);$body=[Console]::In.ReadToEnd(); & ([scriptblock]::Create($body))"
 r=subprocess.run([ps,'-NoProfile','-NonInteractive','-EncodedCommand',base64.b64encode(bootstrap.encode('utf-16le')).decode()],input=script.encode('utf-8'),capture_output=True,timeout=35)
 if r.returncode:raise RuntimeError(r.stderr.decode('utf-8',errors='replace')[:1500])
 return json.loads(r.stdout.decode('utf-8-sig'))

for name,code in scripts.items():
 setup="$Destino='';$Puerto=443;$Servidor='';"
 data=run(setup+mock+code)
 unknown=[x for x in data if x['State']=='unknown']
 if unknown:raise RuntimeError((name,unknown))
 assert len(data)>0
 assert all(x['Title'] and x['Summary'] and x['Next'] for x in data)
 print(name,':',len(data),'resultados simulados correctos')

denials='\n'.join('function '+n+" { throw [System.UnauthorizedAccessException]::new('Permiso denegado simulado') }" for n in ['Get-CimInstance','Get-Process','Get-NetAdapter','Get-NetIPAddress','Get-NetRoute','Get-DnsClientServerAddress','Get-NetIPInterface','Get-NetConnectionProfile','Get-NetAdapterStatistics','Get-DnsClient','Get-DnsClientGlobalSetting','Get-Content','Get-ItemProperty','Get-NetTCPConnection','Get-Service','Get-WinEvent','Get-MpComputerStatus','Get-MpThreat','Get-PhysicalDisk','Get-StorageReliabilityCounter','Get-Volume','Get-VpnConnection','Get-AppxPackage','Get-Printer','Get-PrintJob','Get-PrinterPort','Get-ScheduledTask','Get-ScheduledTaskInfo','Get-ChildItem','Test-Path'])
for name,code in scripts.items():
 data=run("$Destino='';$Puerto=443;$Servidor='';"+denials+code)
 assert data and all(x['State']=='unknown' for x in data),(name,data)
print('12 secciones con permisos denegados: correcto')
data=run("$Destino='';$Puerto=443;$Servidor='servidor-simulado';"+mock+scripts['servidores'])
assert not any(x['State']=='unknown' for x in data)
print('HealthCheck remoto con sesión simulada: correcto')

# Parseo del AST y lista de comandos permitidos en consultas embebidas.
for name,code in scripts.items():
 escaped=code.replace("'","''")
 audit=r'''$t=$null;$e=$null;$a=[System.Management.Automation.Language.Parser]::ParseInput('''+"'"+escaped+"'"+r''',[ref]$t,[ref]$e);if($e.Count){throw ($e|Out-String)};$c=@($a.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst]},$true)|ForEach-Object {$_.GetCommandName()});$bad=@($c|Where-Object {$_ -match '^(Set-(?!StrictMode)|New-Item|Add-Content|Out-File|Export-|Start-|Stop-|Restart-|Enable-|Disable-|Install-|Uninstall-|Invoke-Expression|Remove-(?!CimSession))'});if($bad.Count){throw ($bad -join ',')};ConvertTo-Json -InputObject @($c) -Compress'''
 run(audit)
print('Sintaxis 5.1 y auditoría de comandos de las 12 secciones: correcto')
