"""Verifica que las consultas entregan hechos estructurados a las reglas clínicas."""
import sys
from test_regression import scenario, row
sys.stdout.reconfigure(encoding='utf-8',errors='replace')

pc=scenario('pc')
assert row(pc,'Carga de CPU')['Code']=='cpu.snapshot'
assert len(row(pc,'Carga de CPU')['Data']['Samples'])==3
assert row(pc,'Estado de dispositivos')['Code']=='device.errors'
assert row(scenario('servicios'),'Automáticos detenidos')['Code']=='service.errors'
assert row(scenario('impresoras'),'Spooler')['Code']=='printer.spooler'
assert row(scenario('rdp'),'Escucha RDP local')['Data']['Enabled'] is True
assert row(scenario('defender'),'Protección de Defender')['Code']=='security.status'
print('Metadatos de CPU, dispositivos, servicios, impresión, RDP y seguridad: correctos.',flush=True)

events=r'''
function Get-WinEvent {param($FilterHashtable,$MaxEvents,$ErrorAction)
 if($FilterHashtable.Level -eq 3){return [pscustomobject]@{ProviderName='disk';Id=153;Level=3;TimeCreated=(Get-Date);LevelDisplayName='Warning';Message='Reintento simulado'}}
 $e=[pscustomobject]@{ProviderName='Application Error';Id=1000;Level=2;TimeCreated=(Get-Date);LevelDisplayName='Error';Message='Fallo simulado'}
 $e|Add-Member -MemberType ScriptMethod -Name ToXml -Value {'<Event><EventData><Data Name="AppName">Editor-simulado.exe</Data></EventData></Event>'}
 return $e
}
'''
rows=scenario('eventos',overrides=events)
warning=row(rows,'disk · ID 153')
assert warning['Data']['Level']==3 and warning['Data']['Log']=='System'
assert len(warning['Data']['TimesUtc'])==1
app=row(rows,'Application Error · ID 1000')
assert app['Data']['Subject']=='Editor-simulado.exe' and app['Data']['Occurrences']==1
assert not any(r['State']=='unknown' for r in rows)
print('Advertencias específicas, fechas y sujeto XML, sin interpretar mensajes como instrucciones: correcto.',flush=True)

denied=r'''
function Get-WinEvent {param($FilterHashtable,$MaxEvents,$ErrorAction)
 if($FilterHashtable.Level -eq 3){throw [UnauthorizedAccessException]::new('Permiso denegado simulado')}
 [pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power';Id=41;TimeCreated=(Get-Date);LevelDisplayName='Critical';Message='Simulado'}
}
'''
rows=scenario('eventos',overrides=denied)
assert row(rows,'Advertencias relevantes de System')['State']=='unknown'
assert row(rows,'Microsoft-Windows-Kernel-Power · ID 41')['Data']['Id']==41
print('La denegación de advertencias conserva los errores/críticos ya leídos.',flush=True)

limited=r'''
function Get-WinEvent {param($FilterHashtable,$MaxEvents,$ErrorAction)
 if($FilterHashtable.Level -eq 3){1..40|ForEach-Object {[pscustomobject]@{ProviderName='disk';Id=153;Level=3;TimeCreated=(Get-Date).AddSeconds(-$_);LevelDisplayName='Warning';Message='Simulado'}}}
}
'''
rows=scenario('eventos',overrides=limited)
assert row(rows,'disk · ID 153')['Data']['Limited'] is True
assert len(row(rows,'disk · ID 153')['Data']['TimesUtc'])==20
assert row(rows,'Cobertura · System')['Data']['Limited'] is True
print('Muestras limitadas y cronología acotada se comunican a la conclusión.',flush=True)

different_apps=r'''
function Get-WinEvent {param($FilterHashtable,$MaxEvents,$ErrorAction)
 if($FilterHashtable.Level -eq 3){return}
 foreach($name in @('Editor.exe','Navegador.exe')){
  $e=[pscustomobject]@{ProviderName='Application Error';Id=1000;Level=2;TimeCreated=(Get-Date);LevelDisplayName='Error';Message='Simulado';SubjectForTest=$name}
  $e|Add-Member -MemberType ScriptMethod -Name ToXml -Value {'<Event><EventData><Data Name="AppName">'+$this.SubjectForTest+'</Data></EventData></Event>'}
  $e
 }
}
'''
rows=scenario('eventos',overrides=different_apps)
apps=[r for r in rows if r.get('Code')=='event.group']
assert len(apps)==4 and all(r['Data']['Occurrences']==1 for r in apps)
assert {r['Data']['Subject'] for r in apps}=={'Editor.exe','Navegador.exe'}
assert len({r['Data']['Identity'] for r in apps})==4
print('Aplicaciones distintas con el mismo ID no se agrupan ni se atribuyen a una sola.',flush=True)
