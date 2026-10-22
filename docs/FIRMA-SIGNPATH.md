# Firma de código con SignPath Foundation

Guía para solicitar la firma gratuita de MITHOR en [SignPath Foundation](https://signpath.org) y
para publicar versiones firmadas automáticamente desde GitHub Actions.

---

## 1. Por qué SignPath

SignPath Foundation emite certificados de firma de código a proyectos de código abierto y firma los
binarios que se compilan **desde el propio repositorio**. El certificado se emite a nombre de la
fundación, no al tuyo, y a cambio exige que la cadena «código fuente → binario firmado» sea
verificable. Es la vía gratuita para que Windows y los antivirus dejen de tratar MITHOR como un
desconocido.

La contrapartida es que MITHOR pasa a ser **totalmente abierto**: licencia OSI, sin componentes
propietarios y sin doble licenciamiento comercial. Si en el futuro quieres vender una versión Pro,
tendrás que ir por un certificado propio (ver §9).

---

## 2. Requisitos y cómo los cumple el repositorio

| Requisito de SignPath | Estado en MITHOR |
|---|---|
| Sin malware ni PUP | Cumple. Control de brillo, sin funciones ocultas. |
| Licencia aprobada por la OSI | **Añadida**: [`LICENSE`](../LICENSE) (MIT). |
| Sin código propietario | Cumple. Todo el código es propio; solo se usan bibliotecas del sistema (.NET Framework), permitidas como *System Libraries*. |
| Proyecto mantenido | Cumple. |
| Proyecto ya publicado en la forma que se va a firmar | **Pendiente**: hace falta al menos una release en GitHub. |
| Funcionalidad documentada en la página de descarga | Cumple: [`README.md`](../README.md) y la página de releases. |
| Se firman solo binarios propios | Cumple: `MITHOR.exe` y `MITHOR-Setup.exe` salen de este repositorio. |
| Sin herramientas de hacking | Cumple. |
| Privacidad respetada | Cumple: la aplicación no hace red. Política publicada en <https://tony-toaf86.github.io/mithor-privacy/>. |
| Cambios del sistema anunciados | Cumple: el instalador enumera los cambios antes de aplicarlos. |
| Desinstalación disponible | Cumple: entrada en «Agregar o quitar programas» y `--uninstall`. |
| Política de firma en la página del proyecto | **Añadida**: sección «Code signing policy» del README. |
| Roles de equipo definidos | **Añadido**: [`.github/CODEOWNERS`](../.github/CODEOWNERS). |
| Metadatos de producto fijados y verificados | **Añadido**: `product-name` y `product-version` en las artifact configurations. |
| Artefactos compilados de forma verificable | **Añadido**: [`.github/workflows/release.yml`](../.github/workflows/release.yml) compila, firma y publica desde GitHub. |
| MFA en GitHub y en SignPath | **Pendiente tuyo**: actívalo antes de solicitar. |

---

## 3. Antes de enviar la solicitud

1. **Haz público y presentable el repositorio.**
   - Pon una descripción: «Control de brillo universal para Windows».
   - Añade *topics*: `windows`, `brightness`, `ddc-ci`, `winforms`, `dotnet`.
   - Revisa que la rama por defecto sea `main`. Ahora mismo es `colaborators`, lo que despista a
     cualquiera que llegue al repositorio y a las políticas de SignPath.
2. **Publica una release real** (v1.1.0). SignPath exige que el proyecto «ya esté publicado en la
   forma que se va a firmar». Puede ser la build sin firmar actual: sirve como evidencia de que el
   proyecto existe y funciona.
3. **Activa MFA** en tu cuenta de GitHub.
4. **Comprueba que el CI pasa** en verde en la pestaña *Actions*.

> **Aviso honesto sobre la reputación.** SignPath lo dice en sus condiciones: no firman «código que
> nadie conoce». MITHOR es un repositorio nuevo, con 0 estrellas y 0 releases. Es posible que
> rechacen la primera solicitud por ese motivo. Publicar releases, tener el CI en verde y algo de
> difusión (foros, Reddit, Hacker News) mejora mucho las probabilidades. La solicitud es gratuita y
> se puede repetir más adelante.

---

## 4. Enviar la solicitud

En <https://signpath.org/apply.html>:

| Campo | Qué poner |
|---|---|
| Project name | `MITHOR` |
| Repository | `https://github.com/Tony-Toaf86/MITHOR-PROGRAM` |
| License | MIT |
| Website | `https://tony-toaf86.github.io/mithor-privacy/` (o la del repositorio) |
| Description | Control de brillo para Windows con DDC/CI, WMI, gamma y superposición. WinForms sobre .NET Framework 4.8. |
| Build system | GitHub Actions |
| ¿Firma tu propio código? | Sí, un único mantenedor (`Tony-Toaf86`) con todos los roles. |

En el campo de comentarios conviene explicar en dos líneas lo que más puede hacer dudar:

> The installer writes to Program Files, registers a Windows service named MITHOR that runs as
> LocalSystem (required for DDC/CI access), creates shortcuts and an uninstall entry. The
> application makes no network connections and collects no data. Privacy policy:
> https://tony-toaf86.github.io/mithor-privacy/

---

## 5. Después de la aprobación

### 5.1. En SignPath

1. Instala la **SignPath GitHub App** (<https://github.com/apps/signpath>) y dale acceso al
   repositorio. Es lo que permite verificar el origen de los artefactos.
2. Anota el **Organization ID** (aparece en la configuración de la organización).
3. Crea el proyecto `mithor` y enlázalo con el *Trusted Build System* **GitHub.com**.
4. Crea dos **artifact configurations** pegando el contenido de:
   - [`.signpath/mithor-app.xml`](../.signpath/mithor-app.xml) → slug `mithor-app`
   - [`.signpath/mithor-setup.xml`](../.signpath/mithor-setup.xml) → slug `mithor-setup`
5. Crea una **signing policy** con slug `release-signing` y **aprobación manual**. SignPath exige
   que cada release la apruebe una persona; no se puede automatizar del todo.
6. Genera un **API token** para un usuario con permiso de envío sobre esa política.

Los slugs de los pasos 4 y 5 están escritos en `.github/workflows/release.yml`. Si los cambias en
SignPath, cámbialos también en el workflow.

### 5.2. En GitHub

- **Secret** `SIGNPATH_API_TOKEN` → Settings → Secrets and variables → Actions → Secrets.
- **Variable** `SIGNPATH_ORGANIZATION_ID` → la misma pantalla, pestaña *Variables*.

---

## 6. Cómo se publica una versión

```powershell
# 1. Sube la versión en src\Mithor.App\AssemblyInfo.cs (AssemblyVersion y AssemblyFileVersion)
# 2. Compila y prueba en local
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 3. Etiqueta y publica
git tag v1.1.0
git push origin v1.1.0
```

A partir de ahí, el workflow `release` hace todo:

1. compila `MITHOR.exe` y `MITHOR-Setup.exe`;
2. lee la versión real del ejecutable y la pasa a SignPath como parámetro;
3. sube `MITHOR.exe` y **solicita la firma** (aprobación 1 en SignPath);
4. coloca el ejecutable firmado y recompila el instalador con `-PackageOnly`, para que el
   ejecutable embebido sea el firmado;
5. sube `MITHOR-Setup.exe` y **solicita la firma** (aprobación 2);
6. verifica que ambas firmas son válidas y publica la release con los binarios y `SHA256SUMS.txt`.

**Las dos aprobaciones son necesarias por el diseño actual**: el instalador lleva `MITHOR.exe`
embebido como recurso y SignPath no firma dentro de un PE cualquiera. Si algún día el instalador
deja de embeber el ejecutable (por ejemplo, con un MSI), bastará una sola solicitud.

---

## 7. Verificar la firma

```powershell
Get-AuthenticodeSignature .\dist\MITHOR.exe       | Format-List Status, SignerCertificate
Get-AuthenticodeSignature .\dist\MITHOR-Setup.exe | Format-List Status, SignerCertificate
```

Debe devolver `Valid` y un firmante con «SignPath Foundation» en el sujeto. También puedes
comprobarlo desde las propiedades del archivo, pestaña **Firmas digitales**.

---

## 8. Errores típicos

| Síntoma | Causa |
|---|---|
| «Metadata restriction violation» | La versión del ejecutable no coincide con `${version}`. El workflow la lee del binario, así que suele pasar solo si se firma a mano. |
| «Artifact not found» | Falta `actions/upload-artifact` antes del paso de firma, o se usó un `name` repetido. |
| «Origin verification failed» | La build no se ejecutó en un runner de GitHub, o falta la SignPath GitHub App. |
| Firma inválida en local | Se modificó el archivo después de firmarlo. Vuelve a compilar. |

---

## 9. Plan B: certificado propio

SignPath Foundation no admite doble licenciamiento comercial ni componentes propietarios. Si MITHOR
va a tener una versión de pago, la alternativa es un certificado propio:

| Opción | Coste aproximado | Notas |
|---|---|---|
| OV (Organization Validation) | 200–400 USD/año | Requiere entidad legal verificable. 1–3 días. |
| EV (Extended Validation) | 300–700 USD/año | Reputación inmediata en SmartScreen. |
| Certum Open Source | Decenas de €/año | Solo si el proyecto sigue siendo OSS puro. |

Desde junio de 2023 la clave privada debe vivir en un token USB o HSM, así que la firma se hace con
el token conectado o con el servicio en la nube del emisor. Los comandos para firmar en local ya
están en [`build.ps1`](../build.ps1) (`MITHOR_SIGN_THUMBPRINT`). El resto del proceso (falsos
positivos, VirusTotal, reenvío) está en [FIRMA-Y-FALSOS-POSITIVOS.md](FIRMA-Y-FALSOS-POSITIVOS.md).

---

## 10. Resumen de archivos añadidos

| Archivo | Para qué |
|---|---|
| `LICENSE` | Licencia MIT, obligatoria para SignPath. |
| `.github/CODEOWNERS` | Roles de autor, revisor y aprobador. |
| `.github/workflows/build.yml` | CI: compila y publica artefactos sin firmar. |
| `.github/workflows/release.yml` | Release: compila, firma en dos pasos y publica. |
| `.signpath/mithor-app.xml` | Configuración de firma de `MITHOR.exe`. |
| `.signpath/mithor-setup.xml` | Configuración de firma de `MITHOR-Setup.exe`. |
