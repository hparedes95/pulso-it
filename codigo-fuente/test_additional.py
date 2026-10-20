from test_regression import expect,scenario,row,mock
from diagnostics import COMMON,CHECKS
import subprocess,base64,json
expect('Servicio nunca iniciado (1077) no se considera avería','servicios','$CaseServiceCode=1077;','Automáticos detenidos','info')
expect('Paginación ocupada aislada no prueba presión de memoria','pc','$CasePagingHigh=$true;','Archivo de paginación','info')
expect('Paginación normal informa sin alarmar','pc','','Archivo de paginación','info')

# La sesión remota debe cerrarse incluso si fallan las consultas del proveedor.
code="$EmitirProgreso=$false;$Destino='';$Servidor='servidor-simulado';$Puerto=443;"+mock+COMMON+r'''
function Get-CimInstance {throw 'Fallo del proveedor simulado'}
'''+CHECKS['servidores']+r'''
Row 'info' 'Sesión cerrada' ([string]$global:closed) '' 'prueba'
ConvertTo-Json -InputObject @($script:rows.ToArray()) -Depth 8 -Compress
'''
bootstrap="[Console]::InputEncoding=New-Object Text.UTF8Encoding($false);$b=[Console]::In.ReadToEnd(); & ([scriptblock]::Create($b))"
r=subprocess.run(['powershell','-NoProfile','-NonInteractive','-EncodedCommand',base64.b64encode(bootstrap.encode('utf-16le')).decode()],input=code.encode(),capture_output=True,timeout=25)
assert r.returncode==0
data=json.loads(r.stdout.decode('utf-8-sig'))
assert row(data,'Sesión cerrada')['Summary']=='True'
assert len([x for x in data if x['State']=='unknown'])==4
print('OK: sesión CIM cerrada tras errores de todas las consultas.',flush=True)
