# Cholinanzas

App de escritorio para Windows que administra tus finanzas personales: cuánta plata tenés, cuánta vas a tener, en qué gastás y cuánto te conviene ahorrar. Funciona sin internet y los datos quedan en tu computadora.

## Qué hace

- **Cuentas** en pesos (ARS) y dólares (USD), con saldo inicial y saldo actual. Se pueden **eliminar**: antes avisa cuántos movimientos, transferencias y recurrentes se borran con la cuenta, guarda un respaldo automático (restaurable) y no deja borrar la última cuenta.
- **Movimientos**: ingresos y gastos con categoría, fecha y descripción.
- **Transferencias** entre cuentas, incluso de distinta moneda (queda guardada la cotización de la operación).
- **Recurrentes** (alquiler, sueldo, suscripciones) y **proyección** del saldo a fin de mes, con cuánto podés gastar por día.
- **Presupuestos** por categoría, con **alertas** cuando estás cerca del límite o lo superaste.
- **Metas de ahorro** con cuánto hay que ahorrar por mes para llegar a tiempo.
- **Cotización del dólar** cargada a mano o traída de internet (oficial, blue, MEP, CCL o tarjeta; compra o venta; opcionalmente una vez por día), y patrimonio total en pesos y en dólares. Cada cotización guarda de dónde salió.
- **Panorama general** con totales históricos, promedios, gráficos mes a mes y en qué se gasta más.
- **Exportar** todo a Excel (`.xlsx`, una hoja por tema) y los movimientos a CSV.
- **Importar movimientos desde el CSV o el Excel (`.xlsx`) del banco**: un asistente detecta el formato (separador, codificación, hoja, fechas, números, columnas de débito y crédito) y muestra una vista previa antes de guardar. En un Excel con varias hojas se elige cuál importar; los `.xls` antiguos y los `.xlsm` hay que guardarlos antes como `.xlsx` o CSV. Omite los movimientos que ya existen, así que se puede importar el mismo extracto dos veces sin duplicar, y hace un respaldo antes de importar.
- **Modo oscuro**: tema claro, oscuro o "según Windows" (sigue el cambio del sistema), que se elige en la pestaña Cuentas y se aplica al instante.
- **Recorrido de bienvenida** de 3 pasos, solo la primera vez y solo a quien arranca de cero (quien ya tiene movimientos no lo ve). Se puede repetir desde la pestaña *Ayuda*.
- **Ayuda** dentro de la app (pestaña *Ayuda*): primeros pasos, dónde ver cada dato, cosas que conviene saber y qué hacer si algo sale mal. Funciona sin internet.
- **Editar** movimientos, transferencias, recurrentes, presupuestos, metas y cuentas ya cargados (botón *Editar* en cada fila: carga el elemento en el formulario). La moneda de una cuenta con movimientos no se puede cambiar.
- **Reglas de categorías** ("si la descripción contiene *carrefour*, la categoría es Supermercado"): se aplican al importar, se sugieren al escribir un movimiento y pueden recategorizar lo que quedó en "Otros". Vienen unas reglas de ejemplo que se pueden cambiar o borrar. Cuando corregís la categoría de un movimiento, la app ofrece crear la regla para recordarlo (con el texto sugerido, que se puede editar).
- **Respaldos automáticos** diarios, con copia opcional en otra carpeta (por ejemplo OneDrive) y restauración.

## Requisitos y cómo ejecutarla

- Windows 10 u 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (la app es WPF sobre `net10.0-windows`).

```powershell
dotnet run
```

También se puede abrir `CholinanzasApp.slnx` con Visual Studio y ejecutar con F5.

### Instalador

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Genera `installer\output\Cholinanzas-Setup-<versión>.exe` (unos 46 MB). La app va **autocontenida** (incluye .NET): quien la instala no necesita instalar nada más. Se instala solo para el usuario actual, sin pedir permisos de administrador, en `%LOCALAPPDATA%\Programs\Cholinanzas`; crea el acceso del menú Inicio (el del escritorio es opcional) y aparece en *Aplicaciones instaladas*. Instalar una versión nueva encima conserva todos los datos. Al desinstalar pregunta si querés borrar también tus datos (por defecto, no).

- Para probarlo en una PC limpia, seguí [la guía de prueba](installer/GUIA-PRUEBA-EN-OTRA-PC.md).
- La versión se cambia en `<Version>` de `CholinanzasApp.csproj`.
- Si no tenés [Inno Setup 6](https://jrsoftware.org/isinfo.php), el script baja una copia portable del compilador a `%TEMP%` (no instala nada en Windows).
- El instalador **no está firmado digitalmente**: Windows SmartScreen va a mostrar "Windows protegió su PC" hasta que se lo firme con un certificado de firma de código.

## Tus datos

| Qué | Dónde |
| --- | --- |
| Base de datos (SQLite) | `%LOCALAPPDATA%\CholinanzasApp\finanzas.db` |
| Respaldos | `%LOCALAPPDATA%\CholinanzasApp\Backups` (se conservan los últimos 30) |
| Configuración | `%LOCALAPPDATA%\CholinanzasApp\settings.json` (carpeta extra de respaldos y tema) |
| Registro de errores | `%LOCALAPPDATA%\CholinanzasApp\errores.log` |

La variable de entorno `CHOLINANZAS_DB` permite usar otro archivo de base de datos (por ejemplo, para probar con datos ficticios); los respaldos y la configuración se guardan junto a ese archivo.

## Privacidad

- No tiene cuentas de usuario y no envía tus datos a nadie. La única conexión a internet es opcional: traer la cotización pública del dólar de dolarapi.com (botón "Actualizar ahora" o, si se activa, una vez por día al abrir). Es un GET sin datos personales; el servicio ve la dirección IP, como cualquier sitio web. Si falla, la app sigue con la última cotización cargada.
- Los datos solo salen de la computadora si el usuario lo decide: al exportar un archivo o al elegir una carpeta extra para los respaldos.
- Los datos **no están cifrados**. Quien comparta la computadora debería proteger su usuario de Windows con contraseña.
- Si en el futuro se agregan funciones que usen la red (por ejemplo, bajar la cotización automáticamente), este apartado debe actualizarse y revisarse qué datos salen y con qué consentimiento.

## Accesibilidad

Qué se hizo:

- Contraste de colores de texto de al menos 4,5:1 (nivel AA de WCAG 2.x), medido con la fórmula de las pautas, en el tema claro y en el oscuro.
- Anillo de foco visible al navegar con teclado en botones y menú.
- Nombres accesibles para campos de formulario, botones de ícono (✕, flechas de mes), tablas y barras de progreso.
- Texto alternativo de los gráficos (descripción con todos los valores); los mismos números están en la tabla "Mes a mes".
- El significado nunca depende solo del color: hay texto ("Excedido", "Cerca del límite"), signo en los montos y columna de tipo.
- Las alertas de presupuesto se exponen a tecnologías de asistencia como región en vivo.

Qué **no** se probó: la app se verificó con UI Automation de Windows (lo que consumen los lectores de pantalla), pero no con un lector real como NVDA o Narrador, ni con alto contraste de Windows. No se afirma conformidad con ningún estándar.

## Seguridad

Es una app local sin servidor ni puertos abiertos, por lo que los ataques de denegación de servicio por red no aplican. Lo que sí se contempló:

- Una sola ventana por base de datos (evita bloqueos del archivo SQLite).
- Tope en montos (hasta 1.000.000.000.000) y en la longitud de textos; límite de tamaño del registro de errores; rotación de respaldos.
- Errores inesperados: aviso al usuario y registro, en vez de cierre brusco.
- Exportación a CSV con protección contra inyección de fórmulas en Excel (texto que empieza con `=`, `+`, `-`, `@`, tabulación o salto de línea). La exportación a Excel nunca crea fórmulas.
- Al exportar, «¿Querés abrirlo ahora?» solo se ofrece si el archivo quedó con la extensión esperada (`.xlsx` o `.csv`): así nunca se ejecuta un archivo que el usuario haya nombrado, por ejemplo, `.bat`.
- Los respaldos se reconocen y se limpian solo por su nombre exacto (`finanzas-AAAAMMDD-HHMMSS.db`): elegir como carpeta extra una que ya tenga archivos propios no los toca.
- Las rutas de la base y de los respaldos se pasan a SQLite por el «builder» de cadenas de conexión, no pegadas en el texto.
- La cotización del dólar se pide solo por HTTPS, con tope de 10 segundos y de 1 MB; no se sigue ninguna redirección a HTTP, y el valor recibido se valida antes de usarlo.
- `installer\build.ps1` verifica la huella SHA-256 del compilador del instalador antes de usarlo.
- Dependencias sin vulnerabilidades conocidas (`dotnet list package --vulnerable --include-transitive`, revisado al 20/10/2026).

### Lo que no cubre (a tener en cuenta)

- **La base de datos y los respaldos no están cifrados.** Quien tenga acceso a tu usuario de Windows (o a la carpeta extra, por ejemplo una nube) puede leerlos. Si más adelante hace falta, se puede agregar cifrado (por ejemplo SQLCipher).
- **La app va con .NET incluido** (autocontenida): las correcciones de seguridad de .NET no llegan solas. Hay que volver a generar el instalador cuando Microsoft publique una (revisar `dotnet list package --vulnerable` y las novedades del SDK).
- **El instalador no está firmado**: Windows muestra el aviso de SmartScreen y no se puede verificar quién lo publicó.
- Las variables de entorno `CHOLINANZAS_DB` y `CHOLINANZAS_RATE_URL` existen para pruebas (la segunda solo acepta direcciones de esta misma computadora). Las puede definir cualquier proceso del mismo usuario; no escalan privilegios, pero conviene saberlo.
- No se probó contra archivos Excel armados para atacar al lector (por ejemplo entidades XML anidadas); se confía en que la librería las rechaza por defecto.
- Importación de CSV y Excel con topes (archivo de hasta 10 MB, 50.000 filas, 50 columnas, textos recortados; un `.xlsx` que declara más de 100 MB descomprimido se rechaza), sin ejecutar nada del archivo (ni macros ni fórmulas) y todo o nada al guardar.
- Copia de la base con la API nativa de SQLite y verificación de integridad antes de restaurar.

## Estructura

```
App.xaml(.cs)          Arranque: instancia única, respaldo diario, manejo de errores
MainWindow.xaml(.cs)   Interfaz (menú lateral y páginas)
ImportWindow.xaml(.cs) Asistente de importación de CSV y Excel
WelcomeWindow.xaml(.cs) Recorrido de bienvenida
Themes/Styles.xaml     Paleta y estilos
ViewModels/            Lógica de pantalla (MainViewModel dividido en archivos por tema)
Services/              Respaldos, exportación e importación
Data/, Models/         Base de datos (EF Core + SQLite) y modelos
Assets/                Ícono de la app
installer/             Script del instalador (Inno Setup) y build.ps1
```

## Aviso

Esta app es una herramienta de organización personal: no da asesoramiento financiero, contable ni legal.

## Licencia

Sin definir todavía.
