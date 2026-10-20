# Cholinanzas

App de escritorio para Windows que administra tus finanzas personales: cuánta plata tenés, cuánta vas a tener, en qué gastás y cuánto te conviene ahorrar. Funciona sin internet y los datos quedan en tu computadora.

## Qué hace

- **Cuentas** en pesos (ARS) y dólares (USD), con saldo inicial y saldo actual.
- **Movimientos**: ingresos y gastos con categoría, fecha y descripción.
- **Transferencias** entre cuentas, incluso de distinta moneda (queda guardada la cotización de la operación).
- **Recurrentes** (alquiler, sueldo, suscripciones) y **proyección** del saldo a fin de mes, con cuánto podés gastar por día.
- **Presupuestos** por categoría, con **alertas** cuando estás cerca del límite o lo superaste.
- **Metas de ahorro** con cuánto hay que ahorrar por mes para llegar a tiempo.
- **Cotización del dólar** cargada a mano y patrimonio total en pesos y en dólares.
- **Panorama general** con totales históricos, promedios, gráficos mes a mes y en qué se gasta más.
- **Exportar** todo a Excel (`.xlsx`, una hoja por tema) y los movimientos a CSV.
- **Importar movimientos desde el CSV del banco**: un asistente detecta el formato (separador, codificación, fechas, números, columnas de débito y crédito) y muestra una vista previa antes de guardar. Omite los movimientos que ya existen, así que se puede importar el mismo extracto dos veces sin duplicar, y hace un respaldo antes de importar.
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

## Tus datos

| Qué | Dónde |
| --- | --- |
| Base de datos (SQLite) | `%LOCALAPPDATA%\CholinanzasApp\finanzas.db` |
| Respaldos | `%LOCALAPPDATA%\CholinanzasApp\Backups` (se conservan los últimos 30) |
| Configuración | `%LOCALAPPDATA%\CholinanzasApp\settings.json` (carpeta extra de respaldos) |
| Registro de errores | `%LOCALAPPDATA%\CholinanzasApp\errores.log` |

La variable de entorno `CHOLINANZAS_DB` permite usar otro archivo de base de datos (por ejemplo, para probar con datos ficticios); los respaldos y la configuración se guardan junto a ese archivo.

## Privacidad

- Esta versión no se conecta a internet, no tiene cuentas de usuario y no envía información a nadie.
- Los datos solo salen de la computadora si el usuario lo decide: al exportar un archivo o al elegir una carpeta extra para los respaldos.
- Los datos **no están cifrados**. Quien comparta la computadora debería proteger su usuario de Windows con contraseña.
- Si en el futuro se agregan funciones que usen la red (por ejemplo, bajar la cotización automáticamente), este apartado debe actualizarse y revisarse qué datos salen y con qué consentimiento.

## Accesibilidad

Qué se hizo:

- Contraste de colores de texto de al menos 4,5:1 (nivel AA de WCAG 2.x), medido con la fórmula de las pautas.
- Anillo de foco visible al navegar con teclado en botones y menú.
- Nombres accesibles para campos de formulario, botones de ícono (✕, flechas de mes), tablas y barras de progreso.
- Texto alternativo de los gráficos (descripción con todos los valores); los mismos números están en la tabla "Mes a mes".
- El significado nunca depende solo del color: hay texto ("Excedido", "Cerca del límite"), signo en los montos y columna de tipo.
- Las alertas de presupuesto se exponen a tecnologías de asistencia como región en vivo.

Qué **no** se probó: la app se verificó con UI Automation de Windows (lo que consumen los lectores de pantalla), pero no con un lector real como NVDA o Narrador, ni con alto contraste de Windows. No hay modo oscuro. No se afirma conformidad con ningún estándar.

## Seguridad

Es una app local sin servidor ni puertos abiertos, por lo que los ataques de denegación de servicio por red no aplican. Lo que sí se contempló:

- Una sola ventana por base de datos (evita bloqueos del archivo SQLite).
- Tope en montos (hasta 1.000.000.000.000) y en la longitud de textos; límite de tamaño del registro de errores; rotación de respaldos.
- Errores inesperados: aviso al usuario y registro, en vez de cierre brusco.
- Exportación a CSV con protección contra inyección de fórmulas en Excel.
- Importación de CSV con topes (archivo de hasta 10 MB, 50.000 filas, 50 columnas, textos recortados), sin ejecutar nada del archivo, y todo o nada al guardar.
- Copia de la base con la API nativa de SQLite y verificación de integridad antes de restaurar.

## Estructura

```
App.xaml(.cs)          Arranque: instancia única, respaldo diario, manejo de errores
MainWindow.xaml(.cs)   Interfaz (menú lateral y páginas)
ImportWindow.xaml(.cs) Asistente de importación de CSV
Themes/Styles.xaml     Paleta y estilos
ViewModels/            Lógica de pantalla (MainViewModel dividido en archivos por tema)
Services/              Respaldos, exportación e importación
Data/, Models/         Base de datos (EF Core + SQLite) y modelos
```

## Aviso

Esta app es una herramienta de organización personal: no da asesoramiento financiero, contable ni legal.

## Licencia

Sin definir todavía.
