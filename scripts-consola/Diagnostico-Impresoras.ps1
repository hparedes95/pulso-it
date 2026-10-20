<#
.SYNOPSIS
Impresoras, puertos, cola y spooler; no borra trabajos ni imprime.
.DESCRIPTION
Consultas independientes sin elevación automática. Los errores no interrumpen otras secciones.
Muestra todos los resultados en PowerShell. No crea informes ni archivos. Cada script es independiente.
.EXAMPLE
.\Diagnostico-Impresoras.ps1
#>
#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'

# Compatible con Windows PowerShell 5.1 y PowerShell 7.
Set-StrictMode -Version 2.0
function Invoke-Consulta {
    param([string]$Nombre,[scriptblock]$Accion)
    try {
        $datos = @(& $Accion)
        [pscustomobject]@{Seccion=$Nombre;Estado=$(if($datos.Count){'CONSULTADO'}else{'SIN_DATOS'});Detalle='Una consulta correcta no implica un equipo saludable.';Datos=$datos}
    } catch {
        $estado = 'ERROR'
        if ($_.CategoryInfo.Category -eq 'PermissionDenied' -or $_.Exception -is [System.UnauthorizedAccessException]) { $estado='ACCESO_DENEGADO' }
        elseif ($_.Exception -is [System.Management.Automation.CommandNotFoundException]) { $estado='NO_DISPONIBLE' }
        [pscustomobject]@{Seccion=$Nombre;Estado=$estado;Detalle=$_.Exception.Message;Datos=@()}
    }
}
function Get-EventosToolkit {
    param([string]$Log,[int]$Dias,[int]$MaxEventos,[int[]]$Nivel=@(1,2,3))
    try { Get-WinEvent -FilterHashtable @{LogName=$Log;StartTime=(Get-Date).AddDays(-$Dias);Level=$Nivel} -MaxEvents $MaxEventos -ErrorAction Stop | Select-Object TimeCreated,Id,LevelDisplayName,ProviderName,Message }
    catch { if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { return }; throw }
}
function Test-PuertoToolkit {
    param([string]$Destino,[int]$Puerto,[int]$TimeoutMs=2000)
    $cliente=New-Object System.Net.Sockets.TcpClient
    try {
        $tarea=$cliente.ConnectAsync($Destino,$Puerto)
        if (-not $tarea.Wait($TimeoutMs)) { throw 'Tiempo de conexión agotado' }
        $tarea.GetAwaiter().GetResult()
        [pscustomobject]@{Destino=$Destino;Puerto=$Puerto;TCP='ACCESIBLE';Detalle='Solo conexión TCP; no verifica autenticación ni aplicación.'}
    } finally { $cliente.Dispose() }
}
function Show-ConsultaToolkit {
    param([object]$Consulta,[int]$Nivel=0)
    $sangria='  ' * $Nivel
    ''
    $sangria + '=== ' + $Consulta.Seccion + ' | ' + $Consulta.Estado + ' ==='
    if ($Consulta.Estado -eq 'SIN_DATOS') {
        $sangria + 'Sin resultados para esta consulta o con los filtros seleccionados.'
    } elseif ($Consulta.Estado -ne 'CONSULTADO') {
        $sangria + $Consulta.Detalle
    }
    foreach ($dato in @($Consulta.Datos)) {
        if ($null -eq $dato) { continue }
        if ($dato.PSObject.Properties['Seccion'] -and $dato.PSObject.Properties['Estado'] -and $dato.PSObject.Properties['Datos']) {
            Show-ConsultaToolkit -Consulta $dato -Nivel ($Nivel+1)
        } else {
            foreach ($propiedad in $dato.PSObject.Properties) {
                $valor=$propiedad.Value
                if ($null -eq $valor) { $valor='(sin valor)' }
                elseif ($valor -is [System.Collections.IEnumerable] -and $valor -isnot [string]) {
                    $valor=(@($valor | ForEach-Object { [string]$_ }) -join ', ')
                }
                '{0,-32}: {1}' -f $propiedad.Name,$valor
            }
            ''
        }
    }
}
function Show-ResultadosToolkit {
    param([string]$Nombre,[object[]]$Secciones)
    'Alter-IT-Toolkit 1.2 | ' + $Nombre
    'Equipo: ' + [Environment]::MachineName + ' | Fecha: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    'Solo lectura. Los resultados y errores se muestran en esta consola. No se crean archivos.'
    'CONSULTADO significa que se obtuvieron datos; no certifica que el equipo este saludable.'
    foreach ($seccion in $Secciones) { Show-ConsultaToolkit -Consulta $seccion }
}


$secciones = @()
$secciones += Invoke-Consulta -Nombre 'Impresoras' -Accion { Get-Printer -ErrorAction Stop | Select-Object Name,DriverName,PortName,PrinterStatus,Shared }
$secciones += Invoke-Consulta -Nombre 'Puertos' -Accion { Get-PrinterPort -ErrorAction Stop | Select-Object Name,PrinterHostAddress,PortNumber }
$secciones += Invoke-Consulta -Nombre 'Spooler' -Accion { Get-Service Spooler -ErrorAction Stop | Select-Object Name,Status,StartType }
$secciones += Invoke-Consulta -Nombre 'Trabajos' -Accion { foreach ($impresora in @(Get-Printer -ErrorAction Stop)) { Invoke-Consulta -Nombre $impresora.Name -Accion { Get-PrintJob -PrinterName $impresora.Name -ErrorAction Stop | Select-Object ID,PrinterName,JobStatus,SubmittedTime,Size } } }
Show-ResultadosToolkit -Nombre 'Diagnostico-Impresoras' -Secciones $secciones
