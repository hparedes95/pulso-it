# Scripts independientes de consola · Alter-IT-Toolkit 1.2

Versión de consola previa a Pulso IT. Cada archivo contiene sus funciones y puede copiarse y ejecutarse por separado en Windows PowerShell 5.1 o PowerShell 7 con los proveedores Windows disponibles. También puede pegarse su código completo. No necesita `lib`, no supone permisos de administrador, no cambia la política de ejecución y muestra los resultados/errores en PowerShell, sin informes.

Estos scripts conservan el diagnóstico anterior a las mejoras de la aplicación 2.0.0. Las hipótesis, gráficas y comparación en memoria pertenecen a la aplicación.

```powershell
.\Diagnostico-PC.ps1
.\Diagnostico-Impresoras.ps1
.\Errores-Windows.ps1 -Dias 3 -MaxEventos 100
.\Diagnostico-RDP.ps1 -Destino servidor.ejemplo.local -Puerto 3389
.\Estado-Servicios.ps1 -Servicios Spooler,TermService
.\HealthCheck-Servidores.ps1 -ComputerName srv01,srv02
```

Consulta la ayuda incluida en cada script (`Get-Help .\Nombre.ps1 -Full`). Las consultas no disponibles o sin permisos se indican en consola; una consulta correcta no certifica la salud del equipo. Las conexiones opcionales generan tráfico al destino indicado. No realizan reparaciones ni elevación automática.
