# Compatibilidad con pantallas

MITHOR combina cuatro métodos. La gracia es que **ninguno depende de la marca del monitor**: se usan
las interfaces estándar del sistema y del propio monitor.

## Matriz detallada

| Escenario | Método que gana en automático | Por qué | Si falla |
|---|---|---|---|
| Portátil, panel eDP/LVDS/DSI | WMI | Windows expone `WmiMonitorBrightnessMethods` para el panel interno | DDC/CI (algunos paneles lo soportan), luego gamma y superposición |
| Portátil con GPU híbrida (Intel + NVIDIA/AMD) | WMI | El brillo del panel lo controla el driver del iGPU a través de WMI | Igual que arriba |
| Monitor externo HDMI / DisplayPort / DVI / VGA | DDC/CI | VCP `0x10` es el estándar MCCS para brillo | Gamma, luego superposición |
| Monitor por USB-C (DisplayPort Alt Mode) | DDC/CI | Si el cable/hub transporta el canal DDC | Gamma, superposición |
| Dock Thunderbolt/USB-C | DDC/CI | El dock suele propagar DDC; algunos no | Gamma, superposición |
| Televisor HDMI | DDC/CI | Los televisores modernos implementan MCCS | Gamma (afecta a la señal, no al panel), superposición |
| Proyector | DDC/CI | Muchos proyectores admiten VCP `0x10` | Superposición (el gamma suele ser ineficaz) |
| Conmutador KVM | DDC/CI tras cada conmutación | El KVM puede cortar el canal I2C | Reprobar con **Actualizar**; gamma, superposición |
| Monitor con DDC/CI desactivado | Gamma | El monitor no responde a VCP | Superposición |
| Monitor que acepta escritura pero no lectura | DDC/CI | `GetMonitorCapabilities` indica MCCS | Se aplica igualmente y se guarda el valor |
| Pantalla en HDR | DDC/CI o superposición | Windows ignora las rampas de gamma en HDR | Superposición |
| OLED con limitador automático (ABL) | DDC/CI | El panel puede atenuar por su cuenta en escenas claras | Es comportamiento del monitor, no de MITHOR |
| Escritorio remoto (RDP) | Superposición | Los drivers virtuales aceptan gamma pero no lo aplican | — |
| Máquina virtual (Hyper-V, VMware, VirtualBox) | Superposición | No hay monitor físico | — |
| Pantalla virtual (IDD, spacedesk, Parsec, Sunshine) | Superposición | No existe control de hardware | — |
| Capturadora / HDMI virtual | Superposición | — | — |
| Monitor Apple (Studio Display, Pro Display XDR) | Gamma o superposición | No exponen DDC/CI en Windows | — |
| Varios monitores idénticos | Uno por tarjeta | El identificador usa la ruta de instancia del dispositivo, no solo el modelo | — |
| Rotación de pantalla | Sin cambios | La superposición usa el rectángulo físico del monitor | — |
| Varios adaptadores de vídeo | DDC/CI por HMONITOR | Cada salida se resuelve con su propio handle | Gamma por dispositivo |

## Activar DDC/CI en el monitor

Si el diagnóstico dice «el monitor no responde a DDC/CI», casi siempre está desactivado en el OSD.
Rutas habituales:

| Marca | Ruta típica |
|---|---|
| Dell | Menú OSD → *Others* / *Display* → **DDC/CI: On** |
| LG | *Settings* → *General* → **DDC/CI: On** |
| Samsung | *Menú* → *Sistema* → **DDC/CI: On** |
| HP | *Management* → **DDC/CI: Enable** |
| ASUS | *System Setup* → **DDC/CI: On** |
| BenQ | *System* → **DDC/CI: On** |
| ViewSonic | *Setup Menu* → **DDC/CI: On** |
| AOC / Philips | *Extra* / *Setup* → **DDC/CI** |
| Acer | *OSD* → **DDC/CI: On** |

En portátiles no hay OSD: el panel integrado se controla por WMI.

## Cables y adaptadores

- **VGA**: el canal DDC viaja por los pines 12/15. Algunos adaptadores VGA-HDMI baratos no lo cablean.
- **HDMI**: DDC usa el pin 15/16 (SCL/SDA). Los cables pasivos suelen funcionar; los convertidores
  DisplayPort→HDMI activos pueden no propagarlo.
- **DisplayPort**: AUX CH transporta DDC. Los hubs MST lo propagan normalmente.
- **USB-C**: necesita un cable con DisplayPort Alt Mode y un hub que pase DDC.

## Cómo comprobar qué está pasando

```powershell
# Qué método usa cada pantalla y con qué resultado
& .\dist\MITHOR.exe --diagnose | Out-String

# Sondeo de todos los métodos, sin cambiar el brillo
& .\dist\MITHOR.exe --selftest | Out-String
```

El informe incluye, por pantalla:

- identificador, dispositivo GDI, tecnología de salida;
- resolución, frecuencia, rotación y HDR;
- método elegido y resultado del sondeo (`0-100 (actual 42) VCP 0x10 …`);
- motivo del fallo si lo hubo.

También puedes forzar un método para aislar el problema:

```powershell
& .\dist\MITHOR.exe --backend gamma
& .\dist\MITHOR.exe --backend overlay --set 50
& .\dist\MITHOR.exe --backend ddc --display 2 --set 30
```

## Notas sobre el control por hardware

- **El brillo por DDC/CI es el del monitor**, no el de la señal. Afecta a la retroiluminación y, en OLED,
  al nivel de los píxeles. Es el método que usan las herramientas profesionales.
- **El brillo por WMI es el del panel**, y en un portátil puede estar limitado por el firmware a ciertos
  niveles (MITHOR los respeta).
- **Varios monitores idénticos** pueden compartir nombre y fabricante, pero no identificador: la
  configuración se guarda por ruta de instancia.

## Notas sobre el control por software

- **Gamma**: modifica la curva de la señal. Se ve como una atenuación uniforme, pero puede perder
  detalle en sombras y no afecta al negro absoluto. Es la opción más compatible tras el hardware.
- **Superposición**: dibuja una capa negra translúcida. Es la que mejor imita el brillo real y funciona
  en HDR, pero no puede cubrir juegos en pantalla completa exclusiva ni ventanas protegidas por DRM.

## Por debajo del mínimo del monitor

El brillo negativo de MITHOR no es un truco del monitor: mantiene el hardware en su mínimo (0 %) y
superpone una capa de atenuación calibrada. Por eso funciona en cualquier pantalla, incluidos los
paneles que no permiten bajar más y las pantallas virtuales.

- La capa extra se combina con el método activo y no lo sustituye.
- En HDR la capa sigue funcionando (el gamma no).
- En juegos a pantalla completa exclusiva la capa no se dibuja; el hardware sigue en 0 %.
- Si tu monitor apaga la retroiluminación al 0 %, usa la atenuación extra sin bajar de 0 % o deja un
  valor mínimo positivo.

## Rendimiento

- El sondeo inicial de DDC/CI tarda entre 50 y 300 ms por monitor en condiciones normales.
- Al arrastrar un deslizador se envían como máximo unas pocas decenas de escrituras por segundo, y solo
  el último valor de cada pantalla.
- La superposición no consume CPU de forma perceptible: es una ventana estática.

## Qué no puede hacer MITHOR

- Encender una pantalla apagada o desconectada.
- Cambiar el brillo de una pantalla que no esté activa en el escritorio (duplicada o deshabilitada).
- Controlar el brillo de un monitor que no exponga DDC/CI ni WMI y cuyo driver ignore el gamma:
  en ese caso solo queda la superposición.
- Evitar que el monitor aplique su propio limitador automático (ABL) en OLED.
