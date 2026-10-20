# Pulso IT 1.1.1 · corrección de controles recortados

La etiqueta explicativa ocupaba 48 píxeles de alto desde la posición vertical 4. El selector de síntomas comenzaba en la posición 41 y el botón en la 39. Las áreas se solapaban y la etiqueta tapaba la parte superior de ambos controles. El problema existía en el diseño original y podía resultar más evidente al cambiar el tamaño o el escalado.

Se sustituyeron las posiciones fijas del bloque por una tabla con filas de altura automática, separación entre controles y columnas para el destino, puerto y ventana de eventos. Los textos largos reservan su altura antes de colocar el selector y el botón. El bloque también ajusta su altura dentro de la ventana principal. Se declaró explícitamente la escala de diseño de 96 DPI.

Las pruebas nuevas verifican la contención vertical y horizontal, ausencia de intersecciones entre controles y espacio para el texto en Resumen, Red, RDP, Intune y Servidores. Se ejecutaron con ventana normal y mínima y escalas de tamaño/texto simuladas del 100%, 125%, 150% y 200%. No se cambiaron los ajustes de pantalla del equipo; esto no sustituye una prueba en todos los monitores y configuraciones DPI reales.

Compilación y pruebas internas correctas. Se revisó la vista previa de la aplicación con datos simulados. Las consultas de diagnóstico no han cambiado en esta corrección.
