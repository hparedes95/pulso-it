"""Prueba local de solo lectura: no guarda ni imprime los datos del equipo."""
from diagnostics import packaged
import subprocess,base64,json
ps='C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe'
bootstrap="[Console]::InputEncoding=New-Object Text.UTF8Encoding($false);$b=[Console]::In.ReadToEnd(); & ([scriptblock]::Create($b))"
for name in ('pc','red','discos','servicios'):
 command="$Destino='';$Puerto=443;$Servidor='';$Horas=24;$EmitirProgreso=$false;"+packaged()[name]
 r=subprocess.run([ps,'-NoProfile','-NonInteractive','-EncodedCommand',base64.b64encode(bootstrap.encode('utf-16le')).decode()],input=command.encode(),capture_output=True,timeout=110)
 assert r.returncode==0,(name,'El proceso terminó con error')
 data=json.loads(r.stdout.decode('utf-8-sig'))
 assert data and all(x['State'] in ('info','good','warning','critical','unknown') and x['Title'] and x['Next'] for x in data)
 unknown=sum(x['State']=='unknown' for x in data)
 print(name,': respuesta válida de consulta real local;',len(data),'resultados;',unknown,'sin verificar. Datos no mostrados ni guardados.',flush=True)
