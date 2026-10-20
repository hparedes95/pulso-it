"""Regresiones sobre escenarios de soporte, sin proveedores reales de Windows."""
from diagnostics import COMMON, CHECKS
from pathlib import Path
import ast, subprocess, base64, json, socket

# Comparte los proveedores simulados; no ejecuta la batería base al importarla.
tree=ast.parse(Path(__file__).with_name('test_checks.py').read_text(encoding='utf-8'))
mock=next(ast.literal_eval(n.value) for n in tree.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='mock' for t in n.targets))
ps='C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe'
def scenario(section,options='',overrides='',dest='',port=443):
 script=f"$EmitirProgreso=$false;$Horas=24;$Destino='{dest}';$Puerto={port};$Servidor='';"+mock+options+COMMON+overrides+CHECKS[section]+"\nConvertTo-Json -InputObject @($script:rows.ToArray()) -Depth 8 -Compress"
 bootstrap="[Console]::InputEncoding=New-Object Text.UTF8Encoding($false);$b=[Console]::In.ReadToEnd();& ([scriptblock]::Create($b))"
 result=subprocess.run([ps,'-NoProfile','-NonInteractive','-EncodedCommand',base64.b64encode(bootstrap.encode('utf-16le')).decode()],input=script.encode(),capture_output=True,timeout=35)
 if result.returncode:raise RuntimeError(result.stderr.decode(errors='replace')[:1000])
 return json.loads(result.stdout.decode('utf-8-sig'))
def row(rows,title):
 return next(r for r in rows if title in r['Title'])
def expect(label,section,options,title,state,overrides=''):
 rows=scenario(section,options,overrides)
 actual=row(rows,title)
 assert actual['State']==state,(label,actual)
 print('OK:',label,flush=True)

def run_regressions():
    expect('RAM ausente no se interpreta como 0%','pc','$CaseMemoryMissing=$true;','Memoria RAM','unknown')
    expect('Espacio ausente no se interpreta como disco lleno','discos','$CaseSpaceMissing=$true;','Espacio C:','unknown')
    expect('Pico aislado de CPU no alerta','pc','$CaseCPU=@(10,99,10);','Carga de CPU','info')
    expect('CPU alta en las tres muestras avisa','pc','$CaseCPU=@(95,96,97);','Carga de CPU','warning')
    expect('APIPA en enlace activo avisa','red','','APIPA en una interfaz activa','warning')
    r=scenario('red','$CaseDisconnected=$true;')
    assert not any('APIPA' in x['Title'] and x['State']=='warning' for x in r)
    print('OK: APIPA desconectada no alerta',flush=True)
    expect('RDP deshabilitado sin escucha no alerta','rdp','$CaseRdpDisabled=$true;$CaseNoListen=$true;','Escucha RDP local','info')
    expect('RDP habilitado sin escucha avisa','rdp','$CaseNoListen=$true;','Escucha RDP local','warning')
    expect('Servicio automático detenido sin código no alerta','servicios','','Automáticos detenidos','info')
    expect('Servicio detenido con código de error avisa','servicios','$CaseServiceError=$true;','Automáticos detenidos','warning')
    expect('Contadores ausentes no se presentan como sanos','discos','$CaseMissingReliability=$true;','Fiabilidad ·','unknown')
    expect('Desgaste 100% tiene prioridad','discos','$CaseWear=100;','Fiabilidad ·','critical')
    expect('Desgaste 90% avisa','discos','$CaseWear=90;','Fiabilidad ·','warning')
    expect('Desgaste 0% válido no alerta','discos','$CaseWear=0;','Fiabilidad ·','info')
    expect('Tarea en curso no se considera fallo','intune','$CaseTaskResult=267009;','Tarea ·','info')
    expect('Tarea nunca ejecutada no se considera fallo','intune','$CaseTaskResult=267011;','Tarea ·','info')
    expect('Resultado de tarea con error avisa','intune','$CaseTaskResult=5;','Tarea ·','warning')
    expect('Trabajo de impresión antiguo avisa','impresoras','$CaseOldJob=$true;','Cola ·','warning')
    missing=r'''function Get-Service {param($Name,$ErrorAction) Write-Error -Message 'No instalado' -ErrorId NoServiceFoundForGivenName -Category ObjectNotFound -ErrorAction Stop}'''
    expect('IME no instalado es contexto, no avería','intune','','Servicio IME','info',missing)
    nonevents=r'''function Get-WinEvent {param($FilterHashtable,$MaxEvents,$ErrorAction) Write-Error -Message 'Sin eventos' -ErrorId NoMatchingEventsFound -ErrorAction Stop}'''
    expect('Sin eventos no produce error de consulta','eventos','','Eventos · System','good',nonevents)
    repeated=r'''function Get-WinEvent {param($FilterHashtable,$MaxEvents,$ErrorAction) 1..3|ForEach-Object {[pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power';Id=41;TimeCreated=(Get-Date);LevelDisplayName='Critical';Message='Apagado no limpio simulado'}}}'''
    r=scenario('eventos',overrides=repeated)
    assert len([x for x in r if x['Title']=='Microsoft-Windows-Kernel-Power · ID 41'])==2
    assert all('3 repeticiones' in x['Summary'] and 'No demuestra' in x['Next'] for x in r if 'ID 41' in x['Title'])
    print('OK: eventos repetidos agrupados y sin atribución de causa',flush=True)
    deny_dns=r'''function Resolve-DnsName {throw 'No debe consultarse DNS para una IP literal'}'''
    with socket.socket() as listener:
     listener.bind(('127.0.0.1',0));listener.listen(2)
     r=scenario('red',overrides=deny_dns,dest='127.0.0.1',port=listener.getsockname()[1])
     assert row(r,'Destino por IP')['State']=='info'
     tcp=row(r,'Conexión TCP al destino')
     assert tcp['State']=='good' or (tcp['State']=='unknown' and 'permiso' in tcp['Summary'])
     if(tcp['State']=='unknown'):print('LIMITACIÓN: sockets locales bloqueados por el entorno; prueba real sin verificar.',flush=True)
     closed_port=listener.getsockname()[1]
    r=scenario('red',dest='127.0.0.1',port=closed_port)
    assert row(r,'Conexión TCP al destino')['State'] in ('warning','unknown')
    for mode,state in [('connected','good'),('timeout','warning'),('failed','warning'),('blocked','unknown')]:
     override="function SocketProbe {param($hostName,$portNumber) [pscustomobject]@{Status='"+mode+"';Milliseconds=10;Message='simulado'}}"
     r=scenario('red',overrides=override,dest='127.0.0.1',port=443)
     assert row(r,'Conexión TCP al destino')['State']==state,(mode,r)
    print('OK: IP literal y TCP accesible, timeout, rechazo y bloqueo de permisos diferenciados en simulación.',flush=True)
    print('Regresiones de diagnóstico: todas correctas.',flush=True)

if __name__=='__main__':run_regressions()
