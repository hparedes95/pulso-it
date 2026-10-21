from pathlib import Path
import json,subprocess,sys,zipfile,hashlib
sys.stdout.reconfigure(encoding='utf-8',errors='replace')
from diagnostics import packaged

base=Path(__file__).resolve().parent
out=base.parent/'compilado' if base.name=='codigo-fuente' else base.parents[1]/'outputs'
out.mkdir(exist_ok=True)
checks=base/'checks.json'
checks.write_text(json.dumps(packaged(),ensure_ascii=False),encoding='utf-8')
csc=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
converter=base/'PrepareBrand.exe'
subprocess.run([str(csc),'/nologo',f'/out:{converter}','/reference:System.Drawing.dll',str(base/'PrepareBrand.cs')],check=True)
subprocess.run([str(converter),str(base/'Pulso-IT-Logo.png'),str(base/'Pulso-IT-Logo.ico')],check=True)
exe=out/'Pulso-IT-Portable.exe'
args=[str(csc),'/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',f'/out:{exe}',f'/win32manifest:{base / "app.manifest"}',f'/win32icon:{base / "Pulso-IT-Logo.ico"}',f'/resource:{base / "Pulso-IT-Logo.png"},brand.png',f'/resource:{base / "Pulso-IT-Logo.ico"},brand.ico',f'/resource:{checks},checks.json','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',str(base/'Program.cs'),str(base/'AssemblyInfo.cs')]
args.extend(str(base/name) for name in ('Incident.cs','Triage.cs','Observation.cs','HttpsProbe.cs','V2Tests.cs','PlainLanguage.cs','Assessment.cs','AssessmentTests.cs'))
result=subprocess.run(args,capture_output=True,text=True,encoding='utf-8',errors='replace')
print(result.stdout,result.stderr)
if result.returncode:sys.exit(result.returncode)
r=subprocess.run([str(exe),'--self-test'],timeout=100,capture_output=True,text=True,encoding='utf-8',errors='replace')
if r.stderr:print(r.stderr)
print('Pruebas internas:',r.returncode)
if r.returncode:sys.exit(r.returncode)
preview=base/'preview.png'
r=subprocess.run([str(exe),'--preview',str(preview)],timeout=30)
print('Vista de interfaz:',r.returncode)
if r.returncode:sys.exit(r.returncode)
for mode,name in (('--preview-observation','observation-preview.png'),('--preview-context','context-preview.png')):
 r=subprocess.run([str(exe),mode,str(base/name)],timeout=20)
 if r.returncode:sys.exit(r.returncode)
print('Ejecutable disponible:',exe,exe.stat().st_size,'bytes')
