# Arquitectura de MITHOR

## Objetivo

Un controlador de brillo que funcione en **cualquier** pantalla de Windows y cuyo núcleo pueda
reutilizarse en Linux sin reescribirlo. La clave es separar *qué* hay que hacer (bajar el brillo de una
pantalla) de *cómo* se hace en cada plataforma y en cada tipo de monitor.

## Capas

```
┌─────────────────────────────────────────────────────────────┐
│  Mithor.App (WinForms)                                      │
│  MainForm · MonitorCard · BrightnessSlider · Tray · CLI     │
└───────────────────────────┬─────────────────────────────────┘
                            │ eventos + comandos
┌───────────────────────────▼─────────────────────────────────┐
│  Mithor.Core (portable, sin WinForms ni System.Drawing)     │
│  BrightnessController · AppSettings · Json · Log            │
│  IBrightnessBackend · IDisplayEnumerator · IBackendProvider │
└───────────────────────────┬─────────────────────────────────┘
                            │ implementaciones
┌───────────────────────────▼─────────────────────────────────┐
│  Mithor.Platform.Windows        Mithor.Platform.Linux       │
│  DDC/CI · WMI · Gamma · Overlay  sysfs · ddcutil · RandR    │
│  QueryDisplayConfig              /sys/class/drm, xrandr     │
└─────────────────────────────────────────────────────────────┘
```

El núcleo no referencia `System.Windows.Forms` ni `System.Management`. La única concesión es
`IntPtr Handle` en `DisplayInfo`, que la plataforma usa como handle opaco (HMONITOR en Windows).

## Contratos

### `IBrightnessBackend`

```csharp
string Id;                 // "ddc", "wmi", "gamma", "overlay"
string Label;              // texto para la interfaz
int Priority;              // menor = se prueba antes
bool IsPlatformSupported;
BackendProbe Probe(DisplayInfo display);
int? TryRead(DisplayInfo display);
bool TryWrite(DisplayInfo display, int percent);
void OnDisplaysChanged(IList<DisplayInfo> displays);
void Shutdown();
```

`Probe` devuelve si el método sirve para esa pantalla, el rango nativo (`0..255`, `20..80`, etc.) y el
valor actual cuando se puede leer. `TryWrite` trabaja siempre en porcentaje (0..100) y cada backend
convierte al rango del hardware.

### `IDisplayEnumerator`

Devuelve `DisplayInfo` con identificador estable, nombre, tecnología de salida, geometría, si es panel
interno o pantalla virtual, y si está en HDR. La implementación de Windows usa `QueryDisplayConfig`;
la de Linux, `xrandr` y `/sys/class/drm`.

### `IBackendProvider`

Decide **el orden** en que se prueban los métodos para cada pantalla. Es la pieza que cambia entre
plataformas sin tocar el controlador:

| Plataforma | Panel interno | Monitor externo | Pantalla virtual |
|---|---|---|---|
| Windows | WMI → DDC → gamma/overlay | DDC → WMI → gamma/overlay | DDC → WMI → overlay → gamma |
| Windows + HDR | WMI → DDC → overlay → gamma | DDC → WMI → overlay → gamma | — |
| Linux | sysfs → ddcutil → RandR | ddcutil → RandR | RandR |

## Controlador

`BrightnessController` es el corazón del programa y se ejecuta en **un único hilo de trabajo**:

- La interfaz nunca se bloquea: las llamadas a DDC/CI o WMI pueden tardar cientos de milisegundos.
- Las peticiones se **coalescen** por pantalla: si el usuario arrastra el deslizador, solo se aplica el
  último valor de cada pantalla, no los intermedios.
- Los sondeos se **cachean** (120 s si el método funciona, 20 s si no) para no saturar el bus I2C en cada
  actualización de pantallas.
- Si un método falla dos veces y el modo es automático, se **reasigna** esa pantalla al siguiente
  método compatible.
- Los eventos (`DisplaysChanged`, `DisplayUpdated`, `Notice`) se publican desde el hilo de trabajo;
  la interfaz los marshala con `BeginInvoke`.

## Persistencia

`AppSettings` se guarda como JSON con un serializador propio (`JsonValue`), sin dependencias externas y
compatible con .NET Framework 4.8 y .NET 8. La escritura es atómica (`.tmp` + `.bak`) para no corromper
el archivo si el proceso se interrumpe.

Claves:

- `defaultBackend`: `auto`, `ddc`, `wmi`, `gamma`, `overlay`.
- `displays[id].backend`: preferencia por pantalla (el menú `⋮`).
- `displays[id].brightness`: último brillo conocido.
- `restoreOnStartup`, `startWithWindows`, `startMinimized`, `hotkeysEnabled`, `showTrayIcon`,
  `stepPercent`, `overlayMaxOpacity`, `verboseLog`.

## Backends de Windows

### DDC/CI (`dxva2.dll`)

1. `GetPhysicalMonitorsFromHMONITOR` obtiene los monitores físicos de una salida.
2. `GetVCPFeatureAndVCPFeatureReply` lee el brillo (VCP `0x10`).
3. `SetVCPFeature` lo escribe.
4. `DestroyPhysicalMonitors` libera los handles; se reabren cuando cambia la lista de pantallas.

Si la lectura falla pero `GetMonitorCapabilities` indica MCCS, se marca como «soportado sin lectura» y
se permite escribir. Es el caso de monitores que aceptan `SetVCP` pero no responden a `GetVCP`.

### WMI (`root\wmi`)

- `WmiMonitorBrightness` aporta el brillo actual y los niveles admitidos.
- `WmiMonitorBrightnessMethods.WmiSetBrightness(timeout, brillo)` lo aplica.
- La pantalla se empareja con el `InstanceName` de WMI usando el sufijo `UID####`, presente tanto en
  `WmiMonitorBrightness` como en la ruta del conector de `QueryDisplayConfig`.
- Se usa `ManagementScope.Options.Timeout` para no colgar la aplicación si WMI no responde (por
  ejemplo, en un equipo de escritorio sin panel interno).

### Gamma (`SetDeviceGammaRamp`)

Se abre el dispositivo GDI (`CreateDC("DISPLAY", "\\\\.\\DISPLAYn")`) y se escribe una rampa lineal de
0 a 65535 escalada por el porcentaje. La rampa original se guarda en memoria y se restaura al salir.
Limitaciones: Windows la ignora en HDR y algunos drivers la reinician al cambiar de modo; el backend la
vuelve a aplicar en `OnDisplaysChanged`.

### Superposición

Una ventana negra, `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST |
WS_EX_NOACTIVATE`, cubre cada pantalla con la opacidad adecuada. Es el método universal: funciona en
monitores, televisores, proyectores, pantallas virtuales, escritorio remoto y capturadoras.

- Se crea en un **hilo propio con bucle de mensajes**, propiedad de las ventanas.
- Los clics atraviesan la capa (`WS_EX_TRANSPARENT` + `HTTRANSPARENT`).
- La ventana de MITHOR se **recorta** de la región de la capa (`SetWindowRgn` + `CombineRgn`), de modo
  que la interfaz sigue visible y utilizable aunque el resto de la pantalla esté muy oscuro.
- La opacidad máxima es configurable (92 % por defecto) para evitar pantallas negras sin retorno.
- `Shutdown` destruye las ventanas; si el proceso muere, Windows las elimina y la pantalla vuelve a la
  normalidad.

### Identificación de pantallas

`FlashIdentity` crea una ventana de superposición temporal con el número de pantalla y el nombre del
monitor, con temporizador propio. La bandeja las muestra de forma secuencial (1,6 s por pantalla).

## Enumeración en Windows

`WindowsDisplayEnumerator` usa `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)`:

- nombre GDI de cada salida (`DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME`);
- nombre EDID y ruta del conector (`GET_TARGET_NAME`);
- tecnología de salida → panel interno / externo / virtual;
- color avanzado (`GET_ADVANCED_COLOR_INFO`) → HDR activo;
- geometría con `GetMonitorInfo` y frecuencia con `EnumDisplaySettings`.

Si `QueryDisplayConfig` falla (arranques sin compositor, sesiones especiales), se recurre a
`EnumDisplayDevices`. Si todo falla, se sintetiza una pantalla virtual para que la aplicación siga
siendo utilizable.

## CLI y diagnóstico

`CliRunner` reutiliza el mismo controlador, sin pasar por WinForms. `SelfTest` comprueba:

1. serialización JSON de ida y vuelta;
2. conversión porcentaje ↔ rango nativo;
3. guardado y recuperación de la configuración;
4. enumeración de pantallas;
5. sondeo de todos los métodos (solo lectura);
6. creación y retirada de la capa de superposición.

`--diagnose` genera un informe con sistema, sesión, rutas, pantallas, método, sondeo, errores y notas.


## Servicio, agente y CLI

El mismo `MITHOR.exe` tiene varios modos de arranque, decididos en `Program.Main` antes de crear
cualquier ventana:

| Argumento | Modo |
|---|---|
| *(ninguno)* | Interfaz gráfica completa |
| `--agent` | Agente de sesión: arranca oculto, solo bandeja |
| `--service` | Servicio de Windows (`ServiceBase.Run`) |
| `--supervise` | Supervisor en primer plano (diagnóstico) |
| `--ensure-agent` | Lanza el agente una vez y sale (diagnóstico) |
| `--list`, `--set`, … | CLI sin interfaz |

El **servicio** no controla el brillo: su única misión es mantener vivo un agente por sesión activa.
Se ejecuta como LocalSystem, enumera las sesiones con `WTSEnumerateSessions` y, para cada sesión activa
sin agente, obtiene el token del usuario con `WTSQueryUserToken`, lo duplica y lanza
`MITHOR.exe --agent` con `CreateProcessAsUser` en `winsta0\default`. Si el agente muere, lo relanza
(con una espera de 8 s por sesión). El instalador configura el servicio como automático y con acciones
de recuperación.

El **agente** es la aplicación normal con `--agent`: ventana minimizada, sin botón en la barra de
tareas, icono en la bandeja y controlador de brillo activo. Cerrar la ventana solo la oculta.

## Atenuación por debajo del mínimo

`BrightnessRange` define el rango de usuario −100…100:

- `0…100` → porcentaje que se envía al backend (`HardwarePart`).
- `-100…-1` → el hardware recibe 0 y `ExtraDimPart` devuelve la parte positiva, que se envía a la capa
  de atenuación (`IDimLayer`).

`OverlayBackend` implementa `IBrightnessBackend` y `IDimLayer` a la vez. La capa combina dos
opacidades: la del brillo (si la superposición es el método elegido) y la de la atenuación extra:

```
alpha = 1 − (1 − alpha_brillo) · (1 − alpha_extra)
```

Así, la atenuación extra funciona con cualquier método (DDC/CI, WMI, gamma) y no depende de que la
superposición sea el método principal. Al restaurar un valor negativo guardado, el controlador vuelve a
aplicar hardware 0 + extra.

## Decisiones de diseño

- **Sin NVAPI/ADL/Intel**: DDC/CI por `dxva2` es neutral respecto al fabricante de GPU y cubre todas
  las salidas. Añadir APIs de vendor multiplicaría dependencias sin mejorar la compatibilidad.
- **Sin hooks ni inyección**: la superposición y el gamma cubren el software; no se engancha ningún
  proceso.
- **Sin administrador**: DDC/CI y WMI de brillo funcionan con privilegios de usuario.
- **Fallback explícito**: cada pantalla siempre tiene un método (`overlay` como último recurso), de modo
  que la interfaz nunca queda sin control.
