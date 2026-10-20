# Pulso IT 1.1.0 · revisión del código y diagnóstico

La revisión se aplicó al código de la aplicación. El ejecutable sigue siendo un único archivo portable, con el logo integrado, de solo lectura y sin informes ni archivos de configuración. Ninguna sugerencia de la pantalla ejecuta reparaciones.

## Problemas corregidos

| Hallazgo | Cambio aplicado |
|---|---|
| Una consulta lenta podía hacer perder los resultados de toda la sección. | Cada comprobación envía su resultado al terminar; se conserva ante cancelación, timeout o error posterior. |
| Los cuerpos de diagnóstico ampliados podían superar el límite de longitud de una línea de comandos. | Transporte UTF-8 por entrada estándar, sin escribir scripts en disco; probado con un cuerpo de más de 60.000 caracteres. |
| Se podía cerrar la ventana antes de detener la consulta propia. | El cierre cancela la ejecución y espera su limpieza antes de cerrar la app. |
| Un pico aislado de CPU se interpretaba como carga alta. | Tres muestras; aviso solo si las tres alcanzan el umbral. CPU por proceso calculada en un intervalo y protegida frente a reutilización de PID. |
| Números nulos podían confundirse con cero. | Validación de RAM y espacio: no se presentan como correctos ni como llenos los datos ausentes o incoherentes. |
| RDP deshabilitado, APIPA en interfaces desconectadas y servicios detenidos podían producir avisos engañosos. | Se considera el contexto: RDP local habilitado, enlace activo y códigos reales del servicio. El código 1077 se muestra como contexto. |
| La falta de IME podía parecer un fallo de Intune. | Ausencia del servicio/carpeta como dato; los accesos denegados siguen como sin verificar. |
| Muchos eventos repetidos saturaban la tabla. | Agrupación por canal/proveedor/ID, última fecha y hasta tres mensajes; ventana de 2 h, 24 h, 3 días o 7 días. Se explica el límite de la muestra. |
| Un bloqueo local de sockets podía parecer una avería de red. | Se distinguen permiso denegado, timeout, rechazo y conexión establecida. Una IP literal no requiere DNS inverso. |
| Al refrescar la tabla se creaban fuentes repetidas y se perdía la posición del detalle. | Fuente compartida, refresco por lotes, conservación de selección y desplazamiento. |

## Diagnóstico ampliado

- Perfiles por síntoma: lentitud, bloqueos/reinicios, conexión, impresión, VPN, RDP e Intune.
- Rendimiento: RAM, paginación, CPU con muestras, procesos por CPU/memoria, dispositivos con códigos de error, indicadores parciales de reinicio y actualizaciones registradas por QFE.
- Red: interfaces activas, IP/APIPA, rutas, DNS, DHCP, perfiles, contadores acumulados y proxy del usuario. Las pruebas de destino son opcionales y solo al host/puerto indicado.
- Almacenamiento: salud por proveedor, espacio validado, desgaste, errores no corregidos, temperatura frente al máximo comunicado, volúmenes y marca dirty. Si el proveedor no entrega contadores se indica sin verificar.
- Eventos: interpretación prudente de apagados no limpios, errores de parada, hardware, almacenamiento y servicios; solo metadatos de volcados.
- Seguridad: estados ausentes, modo pasivo, firmas, antivirus registrados y amenazas ya comunicadas por Defender, sin iniciar análisis.
- Impresión: errores de cola, trabajos antiguos y eventos de impresión, sin nombres de documentos ni borrado de trabajos.
- Intune: últimas ejecuciones de tareas y sus códigos, sin ejecutar tareas, leer tokens ni consultar el tenant.
- Servidores: datos numéricos validados, servicios con contexto y cierre de la sesión incluso ante errores.

## Pruebas realizadas

- Compilación x64 y apertura de la interfaz con logo integrado.
- Pruebas internas de navegación, búsqueda, filtros, detalle, UTF-8, destinos válidos y no válidos, transporte de consultas largas y cancelación.
- Timeout y cancelación con un resultado anterior al bloqueo: el resultado llega y se conserva.
- Las 12 secciones con proveedores simulados y con denegaciones de acceso; sintaxis y auditoría de comandos en Windows PowerShell 5.1.
- Regresiones: datos ausentes, pico y carga repetida de CPU, APIPA activa/desconectada, RDP habilitado/deshabilitado, códigos de servicio 0/1067/1077, desgaste ausente/0/90/100, tareas con estados normales o error, trabajo antiguo, ausencia de IME, consulta sin eventos y eventos repetidos.
- TCP simulado: acceso, rechazo, timeout y bloqueo local. La prueba real de loopback quedó bloqueada por permisos del entorno y se clasificó sin verificar.
- Sesión remota simulada: cerrada aunque fallen las cuatro consultas del proveedor.
- Consultas reales locales de PC, red, discos y servicios: respuestas estructuradas válidas, con varias comprobaciones sin verificar por restricciones del entorno; sin guardar ni imprimir datos del equipo.

## Límites que se mantienen

No identifica automáticamente la causa ni sustituye análisis de volcados, pruebas físicas, diagnóstico de arranque, consola de Intune, revisión de backups o comprobación funcional de aplicaciones. La cobertura real depende de permisos, proveedor, edición y componentes. Falta validar impresoras, hardware, VPN, Intune y servidores en el entorno de la organización. No se han cambiado las políticas para sortear restricciones de las pruebas.

## Referencias usadas

El desgaste consumido, el máximo de temperatura y los errores comunicados se interpretan según [MSFT_StorageReliabilityCounter](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter). El evento 41 se trata como evidencia de apagado no limpio, de acuerdo con [la guía de Microsoft](https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/event-id-41-restart). Los estados del programador se contrastaron con [Task Scheduler](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-error-and-success-constants); el significado del código 1077 con [los códigos de error del sistema](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1000-1299-). Los umbrales del toolkit son orientativos y están documentados en cada comprobación.
