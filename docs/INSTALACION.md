# Instalación y desinstalación

## Resumen

| Elemento | Valor |
|---|---|
| Instalador | `dist\MITHOR-Setup.exe` |
| Carpeta | `C:\Program Files\MITHOR` |
| Servicio | `MITHOR` (LocalSystem, inicio automático) |
| Ejecutable | `MITHOR.exe` |
| Agente | `MITHOR.exe --agent` |
| Datos del agente | `%LOCALAPPDATA%\MITHOR` |
| Registros del servicio | `C:\ProgramData\MITHOR\logs` |
| Registro del instalador | `%TEMP%\mithor-setup.log` |
| Entrada de desinstalación | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MITHOR` |

## Requisitos

- Windows 10 1809 o posterior / Windows 11.
- .NET Framework 4.8 (incluido en Windows).
- Cuenta con permisos de administrador para registrar el servicio.
- No se necesita conexión a Internet ni redistribuibles adicionales.

## Instalación paso a paso

1. Ejecuta `MITHOR-Setup.exe`.
2. El ejecutable se eleva por sí mismo (no lleva `requireAdministrator` en el manifiesto para poder
   ofrecer modos sin privilegios): aparecerá el aviso de UAC.
3. En la ventana del instalador, marca o desmarca **Crear acceso directo en el escritorio**.
4. Pulsa **Instalar**.

El instalador realiza estas operaciones:

1. Detiene el servicio anterior y cierra cualquier MITHOR en ejecución.
2. Crea `C:\Program Files\MITHOR` y extrae `MITHOR.exe`, `MITHOR.exe.config`, `MITHOR-Setup.exe`
   (para poder desinstalar) y `version.txt`.
3. Registra el servicio `MITHOR`:

   ```
   sc create MITHOR binPath= "\"C:\Program Files\MITHOR\MITHOR.exe\" --service"
                 start= auto DisplayName= "MITHOR (control de brillo)"
   sc description MITHOR "Mantiene MITHOR en segundo plano..."
   sc failure MITHOR reset= 86400 actions= restart/5000/restart/15000/restart/60000
   ```

4. Crea el acceso directo `%ProgramData%\Microsoft\Windows\Start Menu\Programs\MITHOR.lnk`
   y, opcionalmente, `C:\Users\Public\Desktop\MITHOR.lnk`.
5. Escribe la entrada de **Agregar o quitar programas**.
6. Inicia el servicio. El servicio detecta la sesión activa y lanza el agente; el icono aparece en la
   bandeja en unos segundos.

## Qué hace el servicio

- Arranca con Windows (`start= auto`).
- Cada 4 segundos comprueba las sesiones activas (consola y RDP) y, si no hay agente en una de ellas,
  lo lanza con `CreateProcessAsUser` en el escritorio `winsta0\default` del usuario.
- Si el agente se cierra (por ejemplo, desde el Administrador de tareas), lo vuelve a lanzar.
- Si el servicio falla, Windows lo reinicia con las acciones de recuperación configuradas.
- No controla el brillo por sí mismo: eso lo hace el agente, que es quien tiene acceso a la sesión
  gráfica, a las APIs DDC/CI y a la bandeja.

El agente es un proceso por sesión. La exclusividad se garantiza con un mutex local de sesión, de modo
que un segundo lanzamiento manual solo muestra la ventana existente.

## Instalación silenciosa

```powershell
.\MITHOR-Setup.exe --quiet              # instala sin interfaz
.\MITHOR-Setup.exe --quiet --nodesktop  # sin acceso directo en el escritorio
```

Desde un script que ya se ejecuta como administrador, el instalador no vuelve a pedir elevación.

## Modos sin administrador

| Comando | Efecto |
|---|---|
| `MITHOR-Setup.exe --extract C:\ruta` | Extrae `MITHOR.exe` y su `.config` en la carpeta indicada, sin tocar el sistema. |
| `MITHOR.exe` (sin instalar) | Modo portátil; datos en `%LOCALAPPDATA%` o junto al ejecutable. |

## Actualización

Ejecuta de nuevo `MITHOR-Setup.exe`. El instalador detiene el servicio, lo elimina, reemplaza los
archivos y lo vuelve a crear. Los ajustes del agente (`%LOCALAPPDATA%\MITHOR\settings.json`) se
conservan.

## Desinstalación

Desde **Configuración → Aplicaciones** o ejecutando:

```powershell
& "C:\Program Files\MITHOR\MITHOR-Setup.exe" --uninstall
```

El desinstalador:

1. detiene y elimina el servicio;
2. cierra los agentes;
3. borra los accesos directos;
4. elimina la entrada de Agregar o quitar programas;
5. borra los archivos de instalación (los que estén en uso se eliminan al reiniciar).

Los datos personales de `%LOCALAPPDATA%\MITHOR` **no** se borran, por si quieres conservar la
configuración si vuelves a instalar.

## Verificación

```powershell
sc query MITHOR
& "C:\Program Files\MITHOR\MITHOR.exe" --diagnose | Out-String
Get-Process MITHOR | Select-Object Id, SessionId, MainWindowTitle
```

- `sc query MITHOR` debe mostrar `RUNNING`.
- El diagnóstico debe indicar `Servicio: activo`.
- Debe haber un proceso `MITHOR` en tu sesión (el agente).

## Problemas frecuentes

| Síntoma | Qué revisar |
|---|---|
| El instalador no pide elevación | Ejecútalo con clic derecho → **Ejecutar como administrador**. |
| `No se pudo crear el servicio` | La cuenta no es administrador o una directiva bloquea la creación de servicios. Revisa `%TEMP%\mithor-setup.log`. |
| El servicio está activo pero no hay icono | Espera unos segundos; comprueba `%ProgramData%\MITHOR\logs`. El agente puede tardar por el arranque del explorador. |
| El icono está en los iconos ocultos | Pulsa `^` en la barra de tareas o arrastra el icono a la barra si lo quieres siempre visible. |
| El servicio no arranca tras instalar | `services.msc` → MITHOR → Iniciar; revisa el visor de eventos y `C:\ProgramData\MITHOR\logs`. |
| No se puede desinstalar | Detén el servicio y cierra MITHOR; vuelve a ejecutar el desinstalador como administrador. |

## Instalación manual (avanzada)

Si prefieres no usar el instalador:

```powershell
$dir = 'C:\Program Files\MITHOR'
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item .\dist\MITHOR.exe, .\dist\MITHOR.exe.config $dir -Force
sc.exe create MITHOR binPath= "`"$dir\MITHOR.exe`" --service" start= auto
sc.exe description MITHOR "MITHOR - control de brillo"
sc.exe start MITHOR
```

Para desinstalar manualmente: `sc.exe stop MITHOR`, `sc.exe delete MITHOR` y borra la carpeta.
