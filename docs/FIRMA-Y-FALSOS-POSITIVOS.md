# Firma digital y falsos positivos

Qué hacer cuando una plataforma rechaza `MITHOR-Setup.exe` porque VirusTotal lo marca con
varios motores y el binario no está firmado.

---

## 1. Qué archivo rechazaron

| Dato | Valor |
|---|---|
| Archivo | `dist\MITHOR-Setup.exe` (instalador) |
| Versión | 1.1.0.0 |
| Tamaño | 352 256 bytes (344 KB) |
| SHA-256 | `865db0e0834beb36e552097692111c336fb6c6e25549a0d567a336e4d0ce81eb` |
| Firma Authenticode | **NotSigned** |
| Compilado con | `csc.exe` de .NET Framework 4.8, AnyCPU, `/optimize+` |
| Empaquetado / ofuscación | Ninguno |

El SHA-256 coincide exactamente con el del enlace de VirusTotal del aviso, así que el archivo
rechazado es el instalador, no `MITHOR.exe`.

`MITHOR.exe` (el que va embebido como recurso) también está sin firmar:
SHA-256 `8746a20e0ed6a3b27721b3050d5b83eaf3ac69143049da5642ec9c79bf034098`.

---

## 2. Las 6 detecciones y qué significan

| Motor | Detección | Naturaleza |
|---|---|---|
| Arctic Wolf | `Unsafe` | Reputación / política, no es un nombre de malware |
| Bkav Pro | `W32.Malware.40F46C28` | Genérico (sin familia) |
| Kingsoft | `Malware.kb.c.964` | Genérico (sin familia) |
| Malwarebytes | `MachineLearning/Anomalous.95%` | Machine learning, patrón |
| SecureAge | `Malicious` | Heurístico |
| Trapmine | `Suspicious.low.ml.score` | Machine learning, confianza baja |

**Ninguna es una detección con familia concreta** (no hay `Trojan:Win32/Wacatac`,
`Backdoor.*`, `Ransom.*`…). Todas son heurísticas, de reputación o de machine learning.
Es el patrón clásico de falso positivo sobre un instalador legítimo, nuevo y **sin firma**.

> Ojo: el aviso dice «estos 3 puntos» pero la lista tiene 6. Conviene citar los 6 en la respuesta.

---

## 3. Por qué ocurre

El instalador hace, en este orden, cosas que los modelos de ML puntúan alto:

1. se eleva a administrador (manifiesto `requireAdministrator`);
2. extrae un ejecutable embebido como recurso y lo escribe en `C:\Program Files\MITHOR`;
3. se copia a sí mismo dentro de esa carpeta;
4. registra un servicio de Windows en cuenta **LocalSystem** con inicio automático (`CreateService`);
5. crea accesos directos y una clave de desinstalación en el Registro;
6. el servicio relanza el agente y lo reinicia si se cierra;
7. el agente mata procesos por nombre para sustituir versiones anteriores.

Elevación + escritura en Program Files + servicio LocalSystem + auto-relanzado + matar procesos
es, literalmente, la firma de comportamiento de un instalador de malware. Un motor de ML no
distingue intención: ve el patrón. Y sin firma ni reputación no hay nada que lo compense.

**No hay nada que «arreglar» en el código**: el comportamiento es legítimo y necesario para lo
que hace MITHOR. Lo que falta es reputación verificable, y eso se consigue firmando.

---

## 4. Qué NO hacer

- **No cambiar el binario para que cambie el hash** (recompilar con relleno, renombrar, comprimir
  con UPX, ofuscar). Cambia el hash, se pierde el historial y los motores vuelven a marcar el
  archivo nuevo. Además empeora la heurística y es exactamente el patrón de evasión.
- **No enviar el mismo archivo otra vez.** El hash es el mismo y el resultado será el mismo.
- **No borrar el análisis de VirusTotal.** El historial del hash es público y la plataforma ya
  lo tiene.

---

## 5. Plan

### Paso 1 — Certificado de firma de código

Sin esto no hay solución: la plataforma lo exige explícitamente y es lo que da reputación a
SmartScreen y a los motores de reputación.

| Opción | Coste aprox. | Cuándo conviene |
|---|---|---|
| **Certum Open Source** | Decenas de €/año | Si el repositorio es público y el proyecto es OSS. La más barata con validez en Windows. |
| **OV (Organization Validation)** | 200–400 USD/año | Empresa con datos fiscales verificables. 1–3 días de validación. |
| **EV (Extended Validation)** | 300–700 USD/año | Reputación inmediata en SmartScreen, sin periodo de calentamiento. |
| **Azure Trusted Signing** | ~10 USD/mes | Solo si la organización está en país admitido (EE. UU., Canadá, UE, Reino Unido…) y tiene 3 años de antigüedad demostrable. **Honduras no está admitido.** |

Detalles importantes:

- Desde **junio de 2023** (CA/Browser Forum) los certificados OV y EV exigen que la clave privada
  viva en un **token USB o HSM**. Ya no se entrega un `.pfx` suelto: la firma se hace con el
  token conectado o con el servicio en la nube del emisor.
- El certificado debe incluir el **EKU Code Signing** (`1.3.6.1.5.5.7.3.3`) y ser válido para
  Windows (todos los anteriores lo son).
- Verifica que el nombre del sujeto coincida con la marca que verá el usuario («MITHOR» solo es
  válido si está registrada como entidad; si no, el sujeto será tu nombre o razón social).

### Paso 2 — Instalar `signtool`

`signtool.exe` viene con el **Windows SDK**, no con .NET Framework. No está instalado en este
equipo.

Instala solo la parte necesaria: *Windows SDK → Windows SDK Signing Tools for Desktop Apps*.
Queda en `C:\Program Files (x86)\Windows Kits\10\bin\<versión>\x64\signtool.exe`.

### Paso 3 — Firmar en el orden correcto

Este es el punto que más se falla: **el instalador lleva `MITHOR.exe` embebido como recurso**.
Si firmas después de compilar el instalador, el ejecutable de dentro sigue sin firma y los
antivirus lo analizan al extraerlo.

Orden correcto:

1. compilar `MITHOR.exe`;
2. **firmar `MITHOR.exe`**;
3. compilar `MITHOR-Setup.exe` (embebe el `MITHOR.exe` ya firmado);
4. **firmar `MITHOR-Setup.exe`**.

`build.ps1` ya hace esto automáticamente si defines `MITHOR_SIGN_THUMBPRINT`:

```powershell
$env:MITHOR_SIGN_THUMBPRINT = 'A1B2C3...'   # huella del certificado en Cert:\CurrentUser\My
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Clean
```

Equivale a estas dos órdenes, si prefieres hacerlo a mano:

```powershell
signtool sign /sha1 <HUELLA> /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /d "MITHOR - control de brillo" .\dist\MITHOR.exe

signtool sign /sha1 <HUELLA> /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /d "MITHOR 1.1.0 - instalador" .\dist\MITHOR-Setup.exe
```

El sellado de tiempo (`/tr` + `/td`) es obligatorio en la práctica: mantiene la firma válida
cuando caduque el certificado.

### Paso 4 — Verificar antes de enviar

```powershell
Get-AuthenticodeSignature .\dist\MITHOR.exe       | Format-List Status, SignerCertificate
Get-AuthenticodeSignature .\dist\MITHOR-Setup.exe | Format-List Status, SignerCertificate
```

Debe decir `Valid` en ambos. Si dice `HashMismatch`, has modificado el archivo después de firmar.
Si dice `UnknownError`, falta la cadena de certificados o el sello de tiempo.

Comprobación rápida de que el ejecutable interno también va firmado:

```powershell
$tmp = Join-Path $env:TEMP 'mithor-check'
New-Item -ItemType Directory -Force $tmp | Out-Null
.\dist\MITHOR-Setup.exe --quiet          # instala en Program Files\MITHOR
Get-AuthenticodeSignature 'C:\Program Files\MITHOR\MITHOR.exe' | Select-Object Status
.\dist\MITHOR-Setup.exe --uninstall --quiet
```

### Paso 5 — Analizar el binario nuevo en VirusTotal

El binario firmado **es otro archivo, con otro hash**. Hay que subirlo de nuevo:

1. sube `MITHOR-Setup.exe` firmado a VirusTotal;
2. espera el análisis completo;
3. **hazlo público** (el enlace que verá la plataforma debe ser accesible sin cuenta);
4. guarda el enlace nuevo: es la prueba que hay que adjuntar.

Un binario firmado con OV/EV suele caer a 0–2 detecciones. Si quedan 1–3 de los motores
heurísticos, no es bloqueante para la mayoría de plataformas, pero aquí conviene llegar a 0.

### Paso 6 — Reclamar los falsos positivos a los fabricantes

Se hace **con el archivo ya firmado**. Cada fabricante actualiza su firma de forma
independiente; tarda entre 24 h y 5 días.

| Motor | Canal |
|---|---|
| Microsoft (Defender) | https://www.microsoft.com/en-us/wdsi/filesubmission/ → *Software developer* → *Incorrectly detected* |
| Malwarebytes | https://www.malwarebytes.com/false-positive |
| SecureAge | https://www.secureage.com/support/report-false-positive |
| Bkav Pro | Soporte de Bkav (bkav.com) — no hay formulario público estable |
| Kingsoft | Soporte de Kingsoft (kingsoft.com); alternativa: comentario en el análisis de VirusTotal |
| Trapmine | Soporte de Trapmine (trapmine.com) |
| Arctic Wolf | Soporte de Arctic Wolf (arcticwolf.com); «Unsafe» es un flag de reputación, no un nombre de malware |

Los formularios cambian de URL a menudo: si uno falla, busca «false positive» en la web del
fabricante. Microsoft es el más importante, porque es el que mira la plataforma.

Después de que confirmen, pide **reanálisis** del mismo hash en VirusTotal (botón *Reanalyze*):
la mayoría de motores actualizan su veredicto sin necesidad de subir el archivo otra vez.

### Paso 7 — Reenviar

Adjunta:

- el binario firmado;
- el enlace de VirusTotal de **ese** binario;
- una nota breve explicando qué es MITHOR y que las detecciones eran heurísticas sobre un
  instalador sin firma.

---

## 6. Textos listos

### 6.1. Respuesta a la plataforma

> **Asunto:** Aclaración sobre el análisis de seguridad de MITHOR 1.1.0
>
> Hola:
>
> Gracias por la revisión. Quisiera aportar información sobre el archivo rechazado.
>
> El archivo es `MITHOR-Setup.exe`, versión 1.1.0, SHA-256
> `865db0e0834beb36e552097692111c336fb6c6e25549a0d567a336e4d0ce81eb`. Es el instalador oficial de
> MITHOR, una aplicación de control de brillo de pantalla para Windows desarrollada íntegramente por
> nosotros en C# sobre .NET Framework 4.8, sin empaquetadores, sin ofuscación y sin código de
> terceros.
>
> Las 6 detecciones que muestra VirusTotal son genéricas y heurísticas, ninguna corresponde a una
> familia de malware concreta:
>
> - Arctic Wolf: `Unsafe` (flag de reputación, no un malware)
> - Bkav Pro: `W32.Malware.40F46C28` (genérico)
> - Kingsoft: `Malware.kb.c.964` (genérico)
> - Malwarebytes: `MachineLearning/Anomalous.95%` (machine learning)
> - SecureAge: `Malicious` (heurístico)
> - Trapmine: `Suspicious.low.ml.score` (machine learning, confianza baja)
>
> Es el patrón típico de un instalador legítimo sin firma y sin reputación: eleva privilegios,
> copia los archivos en `Program Files`, registra el servicio `MITHOR` en LocalSystem, crea accesos
> directos y una entrada de desinstalación. Ese conjunto de operaciones es lo que puntúan alto los
> motores de machine learning, aunque el producto no haga nada malicioso.
>
> Somos conscientes de la falta de firma. Estamos tramitando un certificado de firma de código y,
> mientras tanto, hemos iniciado reclamaciones de falso positivo ante los fabricantes.
>
> Para poder resolverlo sin dar vueltas, agradecería que me confirmaran:
>
> 1. ¿Cuál es el criterio exacto que debe cumplir el binario: 0 detecciones en VirusTotal, un
>    umbral máximo, o motores concretos?
> 2. ¿Aceptan un binario firmado con certificado OV o EV aunque queden una o dos detecciones de
>    motores heurísticos?
> 3. ¿Les sirve como evidencia el enlace de VirusTotal de la nueva compilación firmada, o
>    necesitan otro tipo de análisis?
>
> Quedamos a su disposición para facilitar el código fuente, el script de compilación o cualquier
> artefacto que permita verificar la build.
>
> Un saludo,
> [Nombre]
> [Proyecto / web de contacto]

### 6.2. Reclamación de falso positivo a fabricantes (inglés)

Asunto: `False positive report - MITHOR 1.1.0 (signed) - <NOMBRE DEL MOTOR>`

> Hello,
>
> We are the developers of MITHOR, a Windows display-brightness control utility. Your engine flags
> our installer as `<DETECCIÓN>`. We believe this is a false positive.
>
> - File: `MITHOR-Setup.exe`, version 1.1.0
> - SHA-256: `<HASH NUEVO>`
> - Signature: Authenticode, signed by `<EMISOR>`, timestamped
> - VirusTotal: `<ENLACE NUEVO>`
> - Vendor: `<TU EMPRESA>`, `<PAÍS>`
>
> What the software does: MITHOR controls screen brightness through DDC/CI (external monitors) and
> WMI (laptop panels), and can dim below the monitor minimum with a software overlay. The installer
> copies the application to `Program Files\MITHOR`, registers a Windows service named `MITHOR` that
> launches the tray agent at logon, creates Start Menu shortcuts and writes an uninstall registry
> entry. The service runs as LocalSystem because DDC/CI access requires it.
>
> The application contains no network functionality, no persistence outside the service described
> above, no browser or credential access, and no obfuscation or packing. It is built from our own
> C# source with the .NET Framework compiler.
>
> We would be glad to provide source code, build scripts or any sample you need. Could you please
> review the file and remove the detection?
>
> Thank you,
> [Nombre]
> [Email de contacto]

---

## 7. Checklist

- [ ] Certificado de firma de código emitido (OV, EV o Certum OSS).
- [ ] `signtool.exe` instalado (Windows SDK, *Signing Tools*).
- [ ] `MITHOR.exe` firmado **antes** de compilar el instalador.
- [ ] `MITHOR-Setup.exe` firmado con sello de tiempo.
- [ ] `Get-AuthenticodeSignature` devuelve `Valid` en ambos.
- [ ] Build nueva subida a VirusTotal y enlace público guardado.
- [ ] Reclamación enviada a Microsoft y a los 6 motores.
- [ ] Detecciones a 0 (o al menos sin motores de familia concreta).
- [ ] Reenvío a la plataforma con el enlace nuevo y la nota del punto 6.1.
