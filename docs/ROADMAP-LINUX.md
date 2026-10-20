# Hoja de ruta: Linux

## Principio

El núcleo (`Mithor.Core`) **no conoce Windows**. Solo usa `System`, `System.IO`, `System.Threading` y
tipos propios. Por eso el mismo `BrightnessController`, la misma configuración y el mismo JSON sirven en
Linux. Lo único que cambia es el par `IDisplayEnumerator` + `IBackendProvider`.

## Lo que ya está en el repositorio

```
src/Mithor.Platform.Linux/
├─ LinuxDisplayEnumerator.cs      xrandr --query y /sys/class/backlight
├─ LinuxProcess.cs                ejecución de utilidades con timeout
├─ LinuxBackendProvider.cs        prioridades por tipo de pantalla
└─ Backends/
   ├─ SysfsBacklightBackend.cs    /sys/class/backlight/*/{brightness,max_brightness}
   ├─ DdcutilBackend.cs           ddcutil detect / getvcp 10 / setvcp 10
   ├─ RandrGammaBackend.cs        xrandr --output … --brightness
   └─ LinuxOverlayBackend.cs      punto de extensión (pendiente)
```

Estos archivos compilan también en Windows (las rutas simplemente no existen y los backends se declaran
no compatibles), lo que permite mantenerlos sin un entorno Linux mientras se desarrolla.

## Fases

### Fase 1 — Paneles internos (sysfs)

**Objetivo:** portátiles y todo-en-uno sin privilegios de root.

1. Regla udev que dé al grupo `video` permiso de escritura sobre `brightness`:

   ```udev
   ACTION=="add", SUBSYSTEM=="backlight", RUN+="/bin/chgrp video /sys/class/backlight/%k/brightness"
   ACTION=="add", SUBSYSTEM=="backlight", RUN+="/bin/chmod g+w /sys/class/backlight/%k/brightness"
   ```

2. Alternativa sin udev: `org.freedesktop.login1.Session.SetBrightness("backlight", device, value)` por
   D-Bus (systemd-logind), que es lo que usan los escritorios.
3. Detección del dispositivo correcto: `intel_backlight`, `amdgpu_bl0`, `acpi_video0`, `nvidia_0`.
   El backend ya prioriza los dos primeros para paneles internos.

### Fase 2 — Monitores externos (ddcutil)

1. Dependencia: `ddcutil` (paquete en Debian/Ubuntu/Fedora/Arch).
2. Permisos: añadir el usuario al grupo `i2c` y una regla udev para `/dev/i2c-*`.
3. Sustituir la invocación de la CLI por **libddcutil** (`libddcutil.so`) mediante P/Invoke cuando esté
   disponible, para evitar un proceso por operación.
4. Emparejar `Display N` de ddcutil con el conector de `xrandr` leyendo el EDID
   (`ddcutil detect --verbose` incluye el serial).

### Fase 3 — Wayland

Wayland no permite leer la geometría ni escribir gamma sin el compositor. Plan por compositor:

| Compositor | Brillo de pantalla | Gamma | Superposición |
|---|---|---|---|
| GNOME (Mutter) | D-Bus `org.gnome.SettingsDaemon.Power.Screen.Brightness` | `org.gnome.Mutter.DisplayConfig` / portal | capa propia (layer-shell) |
| KDE Plasma | D-Bus `org.kde.ScreenBrightness` | `org.kde.KWin` / `kscreen-doctor` | layer-shell |
| wlroots (Sway, Hyprland, River) | `brightnessctl` / sysfs | `wlr-gamma-control-unstable-v1` | layer-shell |
| COSMIC / Otros | portal `org.freedesktop.portal.Settings` | — | — |

1. `LinuxDisplayEnumerator`: usar `wlr-randr --json` o el portal `org.freedesktop.portal.ScreenCast`
   para la lista de salidas.
2. `RandrGammaBackend`: detectar `WAYLAND_DISPLAY` y cambiar a `wlr-randr --output … --brightness` o al
   protocolo `wlr-gamma-control`.
3. `LinuxOverlayBackend`: implementar con `gtk-layer-shell` (GTK4) o `wlr-layer-shell`; en GNOME/KDE
   hacerlo con una ventana `override-redirect` sólo si el compositor lo permite.

### Fase 4 — Interfaz y empaquetado

1. Interfaz: **Avalonia** (recomendada, un solo código para Windows/Linux/macOS) o GTK4.
   El núcleo y los backends no cambian.
2. Bandeja: `StatusNotifierItem` (KDE, GNOME con extensión AppIndicator).
3. Arranque automático: unidad de usuario `systemd` (`~/.config/systemd/user/mithor.service`) con
   `Restart=on-failure`, equivalente al servicio de Windows. El agente se lanza al iniciar la sesión
   gráfica, no desde una sesión de sistema.
4. Atajos globales: portal `org.freedesktop.portal.GlobalShortcuts` (Wayland) o `XGrabKey` (X11).
5. Empaquetado:
   - AppImage (universal, sin instalación);
   - `.deb` / `.rpm` con dependencia recomendada de `ddcutil`;
   - AUR para Arch;
   - Flatpak con permisos para `/dev/i2c-*` y `/sys/class/backlight`.

## Compilación con .NET 8

Los `.csproj` del repositorio están preparados para esta etapa:

```bash
dotnet build src/Mithor.Core/Mithor.Core.csproj -c Release
dotnet build src/Mithor.Platform.Linux/Mithor.Platform.Linux.csproj -c Release
```

> La compilación de Windows (`build.ps1`) **no** usa estos proyectos: compila las fuentes con `csc.exe`
> de .NET Framework 4.8 para no exigir la instalación del SDK. Los `.csproj` se validarán al montar el
> entorno .NET 8.

## Cambios previstos en el núcleo

Ninguno estructural. Solo:

- registrar `LinuxBackendProvider` en el arranque cuando `RuntimeInformation.IsOSPlatform(OSPlatform.Linux)`;
- añadir `Mithor.Linux.App` como proyecto de interfaz;
- sustituir `StartupManager` (registro de Windows) por un archivo `.desktop`;
- mover los atajos globales a la capa de plataforma.

## Riesgos y decisiones abiertas

- **Wayland**: no hay API estándar de brillo; habrá que soportar compositores por separado.
- **ddcutil**: es lento (proceso por operación); conviene libddcutil o un demonio.
- **Permisos**: escribir en `/sys/class/backlight` requiere udev/logind; documentarlo en el instalador.
- **HDR**: el soporte HDR en Linux es heterogéneo; el gamma se comportará de forma distinta por driver.
- **Múltiples GPU**: el brillo del panel integrado puede depender del driver activo; el backend sysfs ya
  elige el dispositivo correcto por nombre.

## Criterios de aceptación para la v1.0 de Linux

1. Portátil Intel/AMD: brillo del panel con sysfs o logind.
2. Monitor externo: brillo por ddcutil.
3. Sesión Wayland (GNOME y KDE): al menos un método funcional por pantalla.
4. Interfaz con una tarjeta por pantalla, bandeja y persistencia.
5. `--list`, `--set`, `--diagnose` y `--selftest` funcionando igual que en Windows.
