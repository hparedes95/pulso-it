# Pulso IT 2.0.0 · Diagnóstico portable

## Abrir

Abre `Pulso-IT-Portable-v2.0.0.exe`. Puedes copiar únicamente ese archivo a un USB: incluye las consultas, las ventanas y el logo. No instala servicios, no solicita elevación, no descarga módulos y no guarda informes, resultados, ajustes ni referencias. Todo el diagnóstico permanece en memoria hasta cerrar. Las consultas empiezan al pulsar un botón.

Necesita Windows 10/11 x64 o Windows Server x64 con escritorio, .NET Framework 4.7.2 o posterior y Windows PowerShell 5.1. Utiliza los componentes de Windows ya instalados. Si la organización bloquea el ejecutable o PowerShell, sigue su procedimiento autorizado: Pulso IT no modifica políticas, firewall, WinRM ni permisos. El ejecutable no tiene firma digital.

## Recorrido recomendado

1. Abre **Contexto de incidencia** y concreta el síntoma, a quién afecta, el momento y los cambios conocidos. Las respuestas son contexto aportado por el técnico, no pruebas de causalidad.
2. Pulsa **Diagnóstico rápido**, **Analizar síntoma** o consulta una sección. Los resultados aparecen progresivamente. Cada sección tiene un límite de 90 segundos; **Detener** conserva lo ya obtenido y marca la consulta incompleta.
3. Si el problema ocurre ahora, pulsa **Observar 30 s** o **Observar 60 s** y reproduce el fallo. Puedes detener y conservar el intervalo parcial.
4. Lee **Interpretación**: muestra hasta tres hipótesis ordenadas, sus evidencias, la confirmación que falta y el siguiente paso de revisión. Si faltan pruebas, no inventa una causa.
5. Para contrastar cambios, pulsa **Guardar referencia**, repite las consultas u observación y pulsa **Comparar**. La referencia solo vive en memoria y se reemplaza al guardar otra.

## Qué añade esta versión

### Observación del rendimiento

Gráficas de CPU, porcentaje de memoria comprometida, RAM disponible, páginas escritas a paginación, latencia de lectura/escritura del disco y nuevos errores/descartes de las interfaces IPv4 activas. El detalle añade cola y actividad del disco y procesos visibles con CPU y memoria residente. No realiza pruebas de estrés.

El muestreo previsto es aproximadamente una vez por segundo; la disponibilidad y velocidad de los proveedores pueden reducir la cobertura. Los huecos significan datos sin verificar. La CPU por proceso se calcula entre lecturas, se normaliza por procesadores lógicos visibles y distingue reutilizaciones de PID. Los procesos protegidos pueden quedar fuera. Los contadores agregados de disco no identifican por sí solos una unidad o aplicación responsable.

Las hipótesis de CPU requieren al menos diez muestras y un uso de al menos el 90% en el 70% de las muestras válidas. La presión de memoria contrasta compromiso elevado con memoria disponible o actividad de paginación; una RAM ocupada o un archivo de paginación lleno no demuestran por sí solos falta de memoria. La hipótesis de almacenamiento considera latencias de al menos 50 ms repetidas durante actividad, con diez muestras válidas. Son criterios orientativos del programa, no límites universales del hardware. Un intervalo corto puede no capturar un fallo intermitente.

### Interpretación y momento del fallo

Se correlacionan, cuando existen, rendimiento sostenido, APIPA en enlaces activos, DNS/TCP del mismo destino, TLS, errores de aplicaciones, colas de impresión, espacio, fiabilidad de discos y amenazas activas. Se separa la evidencia de la posible explicación. Un evento 41 indica apagado no limpio y no prueba una fuente de alimentación averiada.

Para una incidencia pasada se consulta una ventana alrededor de la fecha elegida, con margen de 5 a 120 minutos a cada lado. Para una incidencia actual se mantienen las ventanas de 2 horas, 24 horas, 3 o 7 días. Las mediciones actuales no reconstruyen el rendimiento pasado. Las hipótesis excluyen resultados de más de diez minutos, de otro contexto temporal o de servidores remotos cuando se analiza el PC local.

### Resultados y comparación

Los filtros **Para revisar**, **Pendientes** y **Contexto** distinguen incidencias de consultas sin verificar y datos informativos. **Todos** y **Buscar** permiten explorar el conjunto. Cada resultado muestra lo observado, qué revisar y fecha/alcance.

**Incidencia/Revisar** significa que una comprobación ha encontrado una condición de atención. **Sin alerta** se refiere solo a esa comprobación. **Dato** aporta contexto. **Sin verificar** significa que faltan permisos, componentes, datos válidos o tiempo. No se convierten los valores ausentes en cero ni se interpreta una consulta bloqueada como una avería.

La comparación usa consultas posteriores a la referencia, del mismo equipo/destino y contexto. Indica cambios, nuevos resultados y resultados que ya no constan; una ausencia puede deberse a cobertura parcial y no significa resolución automática. Los grupos de eventos de ventanas de distinta duración no se comparan como equivalentes. Las observaciones antes/después pueden corresponder a cargas diferentes.

### Red y HTTPS opcional

En **Red** o **Escritorio remoto**, indica un destino y puerto y consulta esa sección. Se distingue resolución DNS de conexión TCP, rechazo, espera agotada y permisos. En Red se muestran configuración DNS/sufijos y coincidencias del destino en el archivo hosts, sin modificarlo. Un destino IP no necesita resolución de nombre.

**Validar HTTPS** está desactivado inicialmente y se aplica únicamente al consultar Red con un destino explícito. Envía `HEAD /` por HTTPS al puerto indicado, utiliza el proxy del sistema sin credenciales y no sigue redirecciones. Comprueba el certificado mediante la validación de Windows, sin aceptar certificados inválidos, y muestra los detalles disponibles. Una respuesta 401/403/404/405 demuestra respuesta HTTP, pero no el funcionamiento de la aplicación o el acceso del usuario. No envía contraseñas ni inicia sesión. El proxy, DNS y la validación de certificados pueden consultar infraestructura adicional.

## Las doce secciones

| Sección | Qué revisa |
|---|---|
| PC y rendimiento | Sistema/hardware, arranque, RAM, paginación, CPU breve, procesos, dispositivos con error, indicadores parciales de reinicio y actualizaciones QFE. |
| Red | Interfaces/IP/DHCP, APIPA activa, rutas, DNS, perfil, contadores, proxy y pruebas opcionales del destino. |
| Escritorio remoto | Configuración RDP/NLA, servicio, puerto y escucha local; TCP remoto opcional. |
| Errores de Windows | Muestra limitada de errores/críticos de System/Application, agrupación por proveedor e ID y metadatos de Minidump. |
| Seguridad y Defender | Estado, modo, firmas, productos registrados cuando procede y amenazas ya notificadas como activas; no inicia análisis. |
| Discos | Espacio, salud, fiabilidad disponible, volúmenes y marca dirty; no repara ni realiza pruebas físicas. |
| VPN | Cliente integrado, adaptadores/rutas, RasMan, sufijos DNS y eventos RasClient; no conecta VPN. |
| Software instalado | Registro de desinstalación de máquina/usuario y Appx del usuario actual; no cubre todos los perfiles o portables. |
| Servicios | Lista y automáticos detenidos; un servicio detenido no se considera averiado por ese único motivo. |
| Impresoras | Impresoras/puertos/colas, trabajos con error o antiguos, Spooler y eventos; sin nombres de documentos o propietarios. |
| Intune y MDM | Evidencias locales: IME opcional, tareas EnterpriseMgmt, eventos MDM y metadatos de logs; no comprueba el portal. |
| Servidores | Sistema, memoria, CPU, discos y servicios; local o un servidor CIM/WSMan con tu identidad y permisos existentes. |

Los botones de diagnóstico rápido y de las doce secciones consultan el equipo local. Para consultar un servidor concreto utiliza únicamente su sección. Al repetir una sección se reemplazan sus resultados actuales; la referencia guardada permanece en memoria.

## Límites y privacidad

Windows tiene que arrancar y permitir ejecutar aplicaciones. Pulso IT no es un entorno de rescate, SMART completo, diagnóstico físico, consola de reparación ni prueba de backups, AD o aplicaciones de servidor. VPN/MDM/seguridad dependen de módulos y permisos disponibles. Los eventos son muestras limitadas; al alcanzarse el máximo, las repeticiones mostradas no son totales históricos. El desgaste y los errores de disco pueden ser históricos o depender del proveedor.

No se reinician servicios, borran trabajos, conectan VPN, modifican configuraciones, fuerzan sincronizaciones ni inician análisis. Las consultas y conexiones pueden quedar registradas por Windows, los destinos o la auditoría corporativa. Windows y sus proveedores pueden mantener su propia actividad habitual; la aplicación no crea informes ni archivos de resultados.

No hay telemetría de la aplicación ni envío de diagnósticos a un servicio externo. Las pruebas opcionales y las consultas remotas generan el tráfico descrito. Los resultados pueden contener nombres, direcciones, software y mensajes con datos personales: revisa lo que captures antes de compartirlo.

## Verificación de la entrega

Se compila como un único ejecutable PE x64. Las pruebas internas comprueban navegación, filtros, texto UTF-8, consultas extensas, cancelación/timeout y conservación de resultados parciales. La geometría de la ventana principal se comprueba con tamaños normal/mínimo y escalas simuladas del 100%, 125%, 150% y 200%; esto no sustituye un piloto con distintos monitores reales.

Las doce secciones se prueban con proveedores simulados, datos incompletos y accesos denegados. Se valida la sintaxis de PowerShell 5.1 y se auditan los comandos de consulta. Hay regresiones para CPU, memoria, discos, servicios, RDP, APIPA, tareas, impresión, conexión TCP y limpieza de sesiones remotas. Las pruebas de la versión 2 cubren hipótesis, datos ausentes/antiguos, separación de destinos/contextos, comparación, reutilización de PID, estados HTTP, transporte y cancelación de la observación.

Se ejecuta además el muestreador nativo en este entorno Windows: devuelve métricas de red y procesos visibles; los contadores PDH de CPU/memoria/disco no están disponibles aquí (error 0xC0000BC8, lectura de nombres de contadores). Se muestran como sin verificar. No se ha verificado una conexión HTTPS real desde este entorno restringido. Las consultas locales previas también encontraron restricciones CIM y de sockets. No se afirma validación completa del hardware: queda pendiente el piloto en equipos de la organización con impresoras, VPN, Intune, servidores y contadores disponibles.

El ZIP incluye fuentes y pruebas. Para compilar desde `codigo-fuente`, con Python 3 instalado, ejecuta `python build.py`; se crea `compilado` en el directorio superior. Ejecuta `python test_checks.py`, `python test_regression.py` y `python test_additional.py` para las consultas simuladas. `test_v2_validation.py` utiliza el ejecutable recién compilado para pruebas estructuradas y muestreo local. La compilación ejecuta las pruebas internas y genera vistas con datos simulados. Esas imágenes son herramientas de desarrollo explícitas; la aplicación de diagnóstico normal no las genera.

## Referencias técnicas

- [Contadores PDH y validez de las muestras](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcountervalue): los contadores de tasa necesitan lecturas sucesivas y estado válido.
- [Códigos de error PDH](https://learn.microsoft.com/en-us/windows/win32/perfctrs/pdh-error-codes): interpretación de fuentes no disponibles.
- [Memoria comprometida y archivo de paginación](https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/introduction-to-the-page-file): su ocupación no equivale por sí sola a presión de memoria.
- [Contadores de fiabilidad de almacenamiento](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter): significado de desgaste, temperatura y errores comunicados.
- [Evento 41](https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/event-id-41-restart): apagado no limpio y necesidad de investigar otras evidencias.
- [Estados del programador de tareas](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-error-and-success-constants) y [códigos del sistema](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1000-1299-): evitan considerar todo código distinto de cero una avería.
