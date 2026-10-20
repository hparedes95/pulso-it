from pathlib import Path
import json,math,subprocess,sys
from test_regression import scenario,row
sys.stdout.reconfigure(encoding='utf-8',errors='replace')

ram=row(scenario('pc'),'Memoria RAM')
assert ram['Code']=='memory.snapshot' and ram['Data']['UsePercent']==50
overrides=r'''
function Resolve-DnsName {param($Name,[switch]$DnsOnly,[switch]$QuickTimeout) [pscustomobject]@{Name=$Name;Type='A';IPAddress='10.0.0.10'}}
function SocketProbe {param($hostName,$portNumber) [pscustomobject]@{Status='connected';Milliseconds=3;Message='Simulado'}}
function Get-Content {param($LiteralPath) '10.0.0.10 host.ejemplo # Coincidencia';'10.0.0.99 otro.ejemplo # No mostrar'}
'''
rows=scenario('red',overrides=overrides,dest='host.ejemplo',port=443)
assert row(rows,'Resolución DNS')['Data']['Target']=='host.ejemplo'
assert row(rows,'Conexión TCP al destino')['Data']['Status']=='connected'
hosts=row(rows,'Coincidencias del destino en hosts')
assert hosts['Data']['Matches']==1 and 'otro.ejemplo' not in hosts['Evidence']
print('Contexto DNS, metadatos y coincidencias de hosts: correcto.',flush=True)

window=r'''
function Get-WinEvent {param($FilterHashtable,$MaxEvents,$ErrorAction)
 if($FilterHashtable.StartTime -ne $Desde -or $FilterHashtable.EndTime -ne $Hasta){throw 'Ventana incorrecta'}
}
'''
rows=scenario('eventos',options="$Desde=[datetime]'2026-10-19T09:45:00';$Hasta=[datetime]'2026-10-19T10:15:00';",overrides=window)
assert not any(r['State']=='unknown' for r in rows)
print('Ventana histórica de eventos con principio y final: correcto.',flush=True)

base=Path(__file__).resolve().parent
out=base.parent/'compilado' if base.name=='codigo-fuente' else base.parents[1]/'outputs'
exe=out/'Pulso-IT-Portable.exe'
before={p.name for p in exe.parent.iterdir()}
result=subprocess.run([str(exe),'--observe-worker','3'],capture_output=True,timeout=30)
assert result.returncode==0,result.stderr.decode('utf-8',errors='replace')[:1500]
samples=[json.loads(line) for line in result.stdout.decode('utf-8-sig').splitlines() if line.strip()]
assert samples and all(samples[i]['Elapsed']<samples[i+1]['Elapsed'] for i in range(len(samples)-1))
coverage={}
for sample in samples:
 for key,value in sample['Metrics'].items():
  assert value is None or isinstance(value,(int,float)) and math.isfinite(value) and value>=0,(key,value)
  if value is not None:coverage[key]=coverage.get(key,0)+1
 for process in sample['Processes']:
  assert process['CpuPercent'] is None or 0<=process['CpuPercent']<=100
assert before=={p.name for p in exe.parent.iterdir()}
print('Observación nativa real local:',len(samples),'muestras; métricas válidas:',', '.join(sorted(coverage)) or 'ninguna; se indican sin verificar',flush=True)
print('No se imprimen ni guardan nombres de procesos o datos personales. El proceso de observación no ha creado archivos en la carpeta de entrega.',flush=True)
