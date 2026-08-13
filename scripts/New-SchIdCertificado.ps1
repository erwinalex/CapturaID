<#
.SYNOPSIS
    Genera los certificados que SchId necesita para servir HTTPS en la red local.

.DESCRIPTION
    Crea una CA propia y, firmado por ella, el certificado del servidor.

    ¿Por qué una CA y no un solo certificado autofirmado? Porque en el kiosko lo
    que se declara como "de confianza" es la CA, no el certificado del servidor.
    Con una CA propia de larga vida, cuando toque renovar el certificado del
    servidor (cada 2 años) basta con volver a correr este script: no hay que
    tocar ni reinstalar la app Android. Con un autofirmado suelto, cada
    renovación obliga a actualizar la app en cada kiosko.

    El certificado del servidor se queda en el almacén de Windows y Kestrel lo
    lee de ahí por su nombre. Así no hay archivo .pfx con contraseña que
    proteger, igual que la cadena de conexión usa autenticación integrada en
    lugar de una contraseña de base de datos.

    IMPORTANTE: el SAN (Subject Alternative Name) tiene que incluir exactamente
    la IP o el nombre con el que el kiosko va a llamar a la API. Android valida
    el SAN e ignora el CN desde hace años: si el kiosko apunta a
    https://192.168.1.50 y esa IP no está en el SAN, la conexión falla aunque el
    certificado sea válido.

.PARAMETER Direcciones
    IPs y/o nombres DNS con los que los kioskos van a alcanzar al servidor.
    Se distinguen solos: lo que parezca IP se registra como IPAddress y el resto
    como DNS. Incluye TODAS las que se vayan a usar.

.PARAMETER Instalar
    Instala en ESTE servidor el certificado compartido que se generó en otra
    parte. No crea nada: importa la CA y el .pfx. Es lo que se corre en las
    otras 69 ubicaciones.

.PARAMETER CarpetaSalida
    Dónde dejar el certificado de la CA que se copia al proyecto Android.

.PARAMETER CuentaServicio
    Cuenta con la que corre el servicio SchIdApi, si no es LocalSystem. Se le da
    permiso de lectura sobre la llave privada.

.EXAMPLE
    # Un solo certificado para todas las ubicaciones. Se corre UNA vez, y el
    # .pfx que exporta se instala igual en los 70 servidores.
    .\New-SchIdCertificado.ps1 -Compartido

.EXAMPLE
    # En cada una de las demás ubicaciones, con los dos archivos que dejó el
    # modo compartido.
    .\New-SchIdCertificado.ps1 -Instalar -ArchivoPfx C:\SchId\schid_servidor.pfx -ArchivoCa C:\SchId\schid_ca.crt

.EXAMPLE
    # Un certificado atado a las direcciones de UNA ubicación. Todas van en la
    # misma lista, separadas por comas; si alguna queda suelta fuera de
    # -Direcciones, no entra al certificado.
    .\New-SchIdCertificado.ps1 -Direcciones 192.168.1.226,192.168.1.250,schid-servidor

.EXAMPLE
    .\New-SchIdCertificado.ps1 -Direcciones 192.168.1.50 -CuentaServicio "NT SERVICE\SchIdApi"

.NOTES
    Correr como administrador, en la PC donde está instalada la API.

    Si Windows responde "la ejecución de scripts está deshabilitada en este
    sistema", habilítala solo para esta ventana de PowerShell:

        Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

    Con -Scope Process el permiso muere al cerrar la ventana, así que no deja
    la máquina permanentemente más abierta por una tarea que se hace una vez.
#>
# PositionalBinding en false a propósito: sin esto, un argumento suelto —una
# dirección que se quedó fuera de la lista de -Direcciones, por ejemplo— se
# ligaría en silencio al siguiente parámetro por posición, que es CarpetaSalida.
# El resultado sería un certificado sin esa dirección en el SAN y los archivos
# escritos en una carpeta con nombre de servidor. Así, en cambio, PowerShell
# rechaza el comando y lo dice.
[CmdletBinding(PositionalBinding = $false)]
param(
    <#
        Modo compartido: un solo certificado para TODAS las ubicaciones.

        El certificado lleva únicamente el nombre "schid-servidor", sin ninguna
        IP, y se exporta como .pfx para instalarlo tal cual en cada servidor. La
        app siempre pide ese nombre y lo resuelve por su cuenta hacia la IP que
        tenga configurada, así que el certificado no depende de la IP de nadie:
        sirve igual donde la dirección es fija y donde el router la cambia.

        Es lo que hace viable un despliegue de decenas de ubicaciones con una
        sola compilación de la app.
    #>
    [Parameter(Mandatory = $true, ParameterSetName = "Compartido")]
    [switch]$Compartido,

    [Parameter(Mandatory = $true, ParameterSetName = "PorUbicacion", Position = 0)]
    [string[]]$Direcciones,

    <#
        Modo instalación: pone en ESTE servidor el certificado compartido que ya
        se generó en otra parte. No crea CA ni certificado.

        Importa dos cosas, y las dos hacen falta:

        - El .pfx en Cert:\LocalMachine\My, que es de donde Kestrel lo lee.
        - La CA en Cert:\LocalMachine\Root. Sin esto la cadena del certificado
          no es de confianza para esta máquina, y con AllowInvalid=false
          Kestrel ni siquiera lo encuentra al buscarlo por nombre.
    #>
    [Parameter(Mandatory = $true, ParameterSetName = "Instalar")]
    [switch]$Instalar,

    [Parameter(Mandatory = $true, ParameterSetName = "Instalar")]
    [string]$ArchivoPfx,

    [Parameter(Mandatory = $true, ParameterSetName = "Instalar")]
    [string]$ArchivoCa,

    [string]$CarpetaSalida = "C:\SchId\certificados",

    [string]$NombreServidor = "schid-servidor",

    [int]$AniosCA = 10,

    [int]$AniosServidor = 2,

    [string]$CuentaServicio
)

$ErrorActionPreference = "Stop"

$esAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $esAdmin) {
    throw "Este script tiene que correrse como administrador: escribe en el almacén de certificados de la máquina."
}

# ---------------------------------------------------------------------------
# Modo instalación: no se genera nada, se instala lo que ya se generó.
# ---------------------------------------------------------------------------
if ($Instalar) {
    foreach ($archivo in @($ArchivoPfx, $ArchivoCa)) {
        if (-not (Test-Path $archivo)) {
            throw "No se encuentra el archivo: $archivo"
        }
    }

    # La CA primero: mientras no sea de confianza para esta máquina, la cadena
    # del certificado del servidor está incompleta y Kestrel lo descarta.
    $ca = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ArchivoCa)
    $yaEnRaiz = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Thumbprint -eq $ca.Thumbprint }

    if ($yaEnRaiz) {
        Write-Host "La CA ya estaba instalada como raíz de confianza." -ForegroundColor Yellow
    }
    else {
        $almacenRaiz = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
        $almacenRaiz.Open("ReadWrite")
        $almacenRaiz.Add($ca)
        $almacenRaiz.Close()
        Write-Host "CA instalada como raíz de confianza (vence $($ca.NotAfter.ToString('yyyy-MM-dd')))." -ForegroundColor Green
    }

    # Se retira el certificado de servidor anterior por la misma razón que al
    # generarlo: Kestrel lo busca por nombre y no debe haber dos.
    Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq "CN=$NombreServidor" } |
        ForEach-Object {
            Write-Host "  Quitando el certificado anterior $($_.Thumbprint)." -ForegroundColor DarkGray
            Remove-Item -Path "Cert:\LocalMachine\My\$($_.Thumbprint)" -Force
        }

    $clave = Read-Host "Contraseña del .pfx" -AsSecureString
    $certServidor = Import-PfxCertificate `
        -FilePath $ArchivoPfx `
        -CertStoreLocation Cert:\LocalMachine\My `
        -Password $clave

    Write-Host ""
    Write-Host "Certificado instalado. Huella: $($certServidor.Thumbprint)" -ForegroundColor Green
    Write-Host "Vence el $($certServidor.NotAfter.ToString('yyyy-MM-dd'))." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Falta en esta ubicación:" -ForegroundColor Cyan
    Write-Host "  1) La sección Kestrel de appsettings.json (es idéntica en todas)."
    Write-Host "  2) El puerto en el firewall:"
    Write-Host '     New-NetFirewallRule -DisplayName "SchId API (HTTPS)" -Direction Inbound -Protocol TCP -LocalPort 7443 -Action Allow' -ForegroundColor White
    Write-Host "  3) Reiniciar el servicio:  Restart-Service SchIdApi"
    Write-Host "  4) En el kiosko, capturar la IP de ESTE servidor en Ajustes."
    Write-Host ""
    Write-Host "Borra el .pfx de esta máquina: lleva la llave privada del servidor." -ForegroundColor Yellow

    return
}

New-Item -ItemType Directory -Force -Path $CarpetaSalida | Out-Null

# ---------------------------------------------------------------------------
# SAN: se separan IPs de nombres DNS porque van en campos distintos, y Android
# los trata distinto. Una IP registrada como DNS no sirve para https://<ip>.
# ---------------------------------------------------------------------------
if ($Compartido) {
    # Sin IPs: el nombre es lo único que se verifica, y es el mismo en todas
    # las ubicaciones.
    $Direcciones = @($NombreServidor)
}

$entradasSan = foreach ($direccion in $Direcciones) {
    $ip = [System.Net.IPAddress]::None
    if ([System.Net.IPAddress]::TryParse($direccion, [ref]$ip)) {
        "IPAddress=$direccion"
    }
    else {
        "DNS=$direccion"
    }
}

$san = "2.5.29.17={text}" + ($entradasSan -join "&")

Write-Host "Direcciones que quedarán en el certificado:" -ForegroundColor Cyan
$entradasSan | ForEach-Object { Write-Host "  $_" }
Write-Host ""

# ---------------------------------------------------------------------------
# 1. La CA
# ---------------------------------------------------------------------------
$nombreCA = "SchId CA Local"

$caExistente = Get-ChildItem Cert:\LocalMachine\My |
    Where-Object { $_.Subject -eq "CN=$nombreCA" -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($caExistente) {
    Write-Host "Se reutiliza la CA que ya existía (vence $($caExistente.NotAfter.ToString('yyyy-MM-dd')))." -ForegroundColor Yellow
    Write-Host "Los kioskos que ya confían en ella no necesitan cambios." -ForegroundColor Yellow
    $ca = $caExistente
}
else {
    Write-Host "Creando la CA..." -ForegroundColor Cyan
    $ca = New-SelfSignedCertificate `
        -Subject "CN=$nombreCA" `
        -KeyExportPolicy Exportable `
        -KeyUsage CertSign, CRLSign, DigitalSignature `
        -KeyUsageProperty All `
        -KeyLength 4096 `
        -HashAlgorithm SHA256 `
        -CertStoreLocation Cert:\LocalMachine\My `
        -NotAfter (Get-Date).AddYears($AniosCA) `
        -Type Custom `
        -TextExtension @("2.5.29.19={text}CA=true&pathlength=0")
}

# Que la propia PC confíe en la CA: si no, Kestrel considera inválida su cadena
# y herramientas locales (el PMS, un curl de diagnóstico) rechazan la conexión.
$yaEnRaiz = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Thumbprint -eq $ca.Thumbprint }
if (-not $yaEnRaiz) {
    $almacenRaiz = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $almacenRaiz.Open("ReadWrite")
    $almacenRaiz.Add($ca)
    $almacenRaiz.Close()
    Write-Host "CA instalada como raíz de confianza en esta PC." -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 2. El certificado del servidor, firmado por la CA
# ---------------------------------------------------------------------------
Write-Host "Creando el certificado del servidor..." -ForegroundColor Cyan

# Se retiran los certificados de servidor anteriores para que Kestrel no tenga
# que adivinar cuál usar cuando busca por nombre.
Get-ChildItem Cert:\LocalMachine\My |
    Where-Object { $_.Subject -eq "CN=$NombreServidor" } |
    ForEach-Object {
        Write-Host "  Quitando el certificado anterior $($_.Thumbprint)." -ForegroundColor DarkGray
        Remove-Item -Path "Cert:\LocalMachine\My\$($_.Thumbprint)" -Force
    }

$certServidor = New-SelfSignedCertificate `
    -Subject "CN=$NombreServidor" `
    -Signer $ca `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -CertStoreLocation Cert:\LocalMachine\My `
    -NotAfter (Get-Date).AddYears($AniosServidor) `
    -Type Custom `
    -TextExtension @(
        $san,
        "2.5.29.37={text}1.3.6.1.5.5.7.3.1"   # EKU: autenticación de servidor TLS
    )

Write-Host "Certificado del servidor creado. Huella: $($certServidor.Thumbprint)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 3. Permiso de lectura sobre la llave privada para la cuenta del servicio
# ---------------------------------------------------------------------------
if ($CuentaServicio) {
    Write-Host "Dando permiso de lectura sobre la llave privada a $CuentaServicio..." -ForegroundColor Cyan

    $llave = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certServidor)
    $nombreArchivo = $llave.Key.UniqueName

    # CNG (lo que usa New-SelfSignedCertificate) y CryptoAPI guardan las llaves
    # en carpetas distintas; se busca en ambas.
    $rutas = @(
        (Join-Path $env:ProgramData "Microsoft\Crypto\Keys\$nombreArchivo"),
        (Join-Path $env:ProgramData "Microsoft\Crypto\RSA\MachineKeys\$nombreArchivo")
    ) | Where-Object { Test-Path $_ }

    if (-not $rutas) {
        Write-Warning "No se encontró el archivo de la llave privada. Dale permiso a mano con certlm.msc: botón derecho sobre el certificado > Todas las tareas > Administrar claves privadas."
    }
    else {
        foreach ($ruta in $rutas) {
            $acl = Get-Acl $ruta
            $regla = New-Object System.Security.AccessControl.FileSystemAccessRule($CuentaServicio, "Read", "Allow")
            $acl.AddAccessRule($regla)
            Set-Acl -Path $ruta -AclObject $acl
            Write-Host "  Permiso aplicado en $ruta" -ForegroundColor Green
        }
    }
}

# ---------------------------------------------------------------------------
# 4. La CA en formato .crt, que es lo que se copia al proyecto Android
# ---------------------------------------------------------------------------
$rutaCaCrt = Join-Path $CarpetaSalida "schid_ca.crt"
$bytes = $ca.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
[System.IO.File]::WriteAllBytes($rutaCaCrt, $bytes)

# ---------------------------------------------------------------------------
# 5. En modo compartido, el certificado se exporta para instalarlo en los demás
#    servidores. Es el mismo archivo para las 70 ubicaciones.
# ---------------------------------------------------------------------------
if ($Compartido) {
    $rutaPfx = Join-Path $CarpetaSalida "schid_servidor.pfx"

    Write-Host ""
    Write-Host "Modo compartido: hay que exportar el certificado con su llave privada" -ForegroundColor Cyan
    Write-Host "para poder instalarlo en los demás servidores. Elige una contraseña" -ForegroundColor Cyan
    Write-Host "para el archivo (la vas a necesitar al importarlo)." -ForegroundColor Cyan
    $clave = Read-Host "Contraseña del .pfx" -AsSecureString

    Export-PfxCertificate -Cert $certServidor -FilePath $rutaPfx -Password $clave | Out-Null
    Write-Host "Exportado: $rutaPfx" -ForegroundColor Green
}

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " Listo." -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1) Pon esto en src\SchId.Api\appsettings.json, reemplazando la"
Write-Host "   sección Kestrel que traiga:"
Write-Host ""
Write-Host @"
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://0.0.0.0:7443",
        "Certificate": {
          "Subject": "$NombreServidor",
          "Store": "My",
          "Location": "LocalMachine",
          "AllowInvalid": false
        }
      }
    }
  }
"@ -ForegroundColor White
Write-Host ""
Write-Host "   Deja fuera la sección 'Http' cuando los kioskos ya estén en HTTPS:"
Write-Host "   mientras siga ahí, un kiosko mal configurado puede mandar su token"
Write-Host "   en claro sin que nadie se entere."
Write-Host ""
Write-Host "2) Abre el puerto en el firewall de Windows:"
Write-Host ""
Write-Host '   New-NetFirewallRule -DisplayName "SchId API (HTTPS)" -Direction Inbound ' -ForegroundColor White -NoNewline
Write-Host '-Protocol TCP -LocalPort 7443 -Action Allow' -ForegroundColor White
Write-Host ""
Write-Host "3) Copia este archivo al proyecto Android, en"
Write-Host "   app\src\main\res\raw\schid_ca.crt :"
Write-Host ""
Write-Host "   $rutaCaCrt" -ForegroundColor White
Write-Host ""
Write-Host "4) Reinicia el servicio:  Restart-Service SchIdApi"
Write-Host ""
if ($Compartido) {
    Write-Host ""
    Write-Host "En cada uno de los DEMÁS servidores, copia estos dos archivos" -ForegroundColor Cyan
    Write-Host "junto con el script y corre:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  .\New-SchIdCertificado.ps1 -Instalar ``" -ForegroundColor White
    Write-Host "    -ArchivoPfx .\schid_servidor.pfx -ArchivoCa .\schid_ca.crt" -ForegroundColor White
    Write-Host ""
    Write-Host "Hacen falta los dos: el .pfx es el certificado, y sin la CA en el" -ForegroundColor Cyan
    Write-Host "almacén de raíces esa máquina no confía en su propia cadena y Kestrel" -ForegroundColor Cyan
    Write-Host "no llega a cargarlo." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "El .pfx lleva la llave privada del servidor: trátalo como una contraseña." -ForegroundColor Yellow
    Write-Host "Cuando termines de instalarlo en las ubicaciones, bórralo de donde lo" -ForegroundColor Yellow
    Write-Host "hayas copiado." -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "El certificado del servidor vence el $($certServidor.NotAfter.ToString('yyyy-MM-dd'))." -ForegroundColor Yellow
Write-Host "La CA vence el $($ca.NotAfter.ToString('yyyy-MM-dd')). Mientras la CA siga vigente," -ForegroundColor Yellow
Write-Host "renovar el certificado del servidor es volver a correr este script, sin" -ForegroundColor Yellow
Write-Host "tocar los kioskos." -ForegroundColor Yellow
