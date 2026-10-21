from pathlib import Path
import zipfile,hashlib,shutil,struct,sys
sys.stdout.reconfigure(encoding='utf-8',errors='replace')
base=Path(__file__).resolve().parent
out=base.parent/'compilado' if base.name=='codigo-fuente' else base.parents[1]/'outputs'
exe=out/'Pulso-IT-Portable.exe'
assert exe.is_file() and exe.stat().st_size>20000
data=exe.read_bytes()
assert data[:2]==b'MZ'
pe=struct.unpack_from('<I',data,0x3c)[0]
assert data[pe:pe+4]==b'PE\0\0'
assert struct.unpack_from('<H',data,pe+4)[0]==0x8664
versioned=out/'Pulso-IT-Portable-v2.1.0.exe'
shutil.copyfile(exe,versioned)
guide=out/'Pulso-IT-Portable-LEEME.md'
shutil.copyfile(base/'LEEME-2.1.0.md',guide)
previews={'preview.png':'Pulso-IT-Vista-previa.png','observation-preview.png':'Pulso-IT-Observacion-Demostracion.png','context-preview.png':'Pulso-IT-Contexto.png'}
for src,dest in previews.items():shutil.copyfile(base/src,out/dest)
for name in ('Pulso-IT-Logo.png','Pulso-IT-Logo.ico'):shutil.copyfile(base/name,out/name)
sources=('Program.cs','AssemblyInfo.cs','app.manifest','Incident.cs','Triage.cs','Observation.cs','HttpsProbe.cs','V2Tests.cs','PlainLanguage.cs','Assessment.cs','AssessmentTests.cs','diagnostics.py','build.py','test_checks.py','test_regression.py','test_additional.py','test_live.py','test_v2_validation.py','test_assessment_data.py','package.py','PrepareBrand.cs','Pulso-IT-Logo.png','Logo-Diseno.md','LEEME-2.1.0.md')
archive=out/'Pulso-IT-Portable-v2.1.0.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
 z.write(versioned,versioned.name);z.write(guide,'LEEME.md')
 for src,dest in previews.items():z.write(base/src,'vistas-demostracion/'+dest)
 for name in ('Pulso-IT-Logo.png','Pulso-IT-Logo.ico'):z.write(base/name,'logo/'+name)
 for name in sources:z.write(base/name,'codigo-fuente/'+name)
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 assert z.read(versioned.name)==data
 for name in sources:assert z.read('codigo-fuente/'+name)==(base/name).read_bytes()
shutil.copyfile(archive,out/'Pulso-IT-Portable.zip')
(out/'Pulso-IT-SHA256.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in (versioned,archive)),encoding='utf-8')
print('EXE comprobado:',versioned.resolve(),versioned.stat().st_size,'bytes')
print('ZIP comprobado:',archive.resolve(),archive.stat().st_size,'bytes')
