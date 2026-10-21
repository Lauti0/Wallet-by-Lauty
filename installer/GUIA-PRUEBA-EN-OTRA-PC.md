# Guía: probar el instalador en otra PC

Objetivo: comprobar que Cholinanzas se instala y funciona en una computadora **limpia**, o sea, una que no tiene nada de lo que tenés vos para programar (.NET, Visual Studio, etc.). Es la prueba que más importa antes de dárselo a alguien.

Tiempo estimado: 30 a 40 minutos.

---

## 0. Qué PC usar

Elegí **una** de estas opciones, de la mejor a la más trabajosa:

| Opción | Cómo | Ventaja |
|---|---|---|
| **Windows Sandbox** (recomendada) | Viene con Windows 10/11 **Pro** o Enterprise. Es un Windows limpio y descartable dentro de tu propia PC. Al cerrarlo se borra todo. | No necesitás otra computadora y cada prueba arranca de cero. |
| **Otra PC o notebook** | La de un familiar o amigo, con su permiso. | Es la prueba más parecida a la realidad. |
| **Máquina virtual** | VirtualBox o Hyper-V con Windows 10/11 recién instalado. | Sirve si no tenés Windows Pro. |

**Requisitos de la PC de prueba:** Windows 10 versión 1809 o más nuevo (o Windows 11), de 64 bits. No hace falta internet para instalar; solo para probar la cotización del dólar.

### Cómo activar Windows Sandbox (una sola vez)

1. Buscá en el menú Inicio **"Activar o desactivar las características de Windows"**.
2. Marcá **Windows Sandbox** y aceptá. Reiniciá la PC cuando lo pida.
3. Abrí **Windows Sandbox** desde el menú Inicio. Se abre una ventana con un Windows limpio.

Si no aparece la opción, tu Windows no es Pro, o falta activar la virtualización en el BIOS. En ese caso usá otra PC o una máquina virtual.

---

## 1. Llevar el instalador

El instalador está en tu PC, en:

```
C:\Users\User\Cholinanzas\CholinanzasApp\installer\output\Cholinanzas-Setup-1.0.0.exe
```

- **Sandbox:** copiá el archivo (Ctrl+C) y pegalo (Ctrl+V) dentro de la ventana del Sandbox, en el escritorio.
- **Otra PC:** pasalo en un pendrive o por una nube (OneDrive, Google Drive).

### Comprobar que llegó sin cambios

En la PC de prueba, abrí PowerShell en la carpeta del archivo y corré:

```powershell
Get-FileHash .\Cholinanzas-Setup-1.0.0.exe -Algorithm SHA256
```

El resultado tiene que ser **igual** al que mostró `installer\build.ps1` al terminar de generarlo (línea "SHA-256"). Si no coincide, el archivo se dañó en el camino: copialo de nuevo.

---

## 2. Confirmar que la PC está realmente limpia

En PowerShell de la PC de prueba:

```powershell
dotnet --list-runtimes
```

Lo esperado es un error del tipo **"dotnet no se reconoce como nombre de un cmdlet…"**. Eso confirma que no hay .NET instalado y que la prueba vale: Cholinanzas lo trae incluido.

Si te lista versiones de .NET, la prueba sirve menos. Probalo igual, pero mejor repetirlo en una PC sin .NET.

---

## 3. Instalar

1. Doble clic en **Cholinanzas-Setup-1.0.0.exe**.
2. **Si aparece "Windows protegió su PC"** (pantalla azul de SmartScreen): es esperable, porque el instalador todavía no está firmado digitalmente. Tocá **Más información** y después **Ejecutar de todas formas**.
   - Si pasaste el archivo por USB puede que no aparezca. Si lo bajaste de internet o de una nube, sí.
3. **Puede preguntar si instalar "solo para mí" o "para todos los usuarios".** Elegí **solo para mí**: no pide contraseña de administrador y es lo normal para esta app. Si con esa opción igual pide la contraseña, anotalo: es un problema. (Anotá también si te resultó confusa la pregunta.)
4. El asistente tiene que estar **en español**. La primera pantalla es el **acuerdo de licencia** (los términos de uso y privacidad): tiene que venir marcado «No acepto el acuerdo» y el botón «Siguiente» tiene que estar deshabilitado hasta que marques «Acepto el acuerdo». Probá avanzar sin aceptar. Después seguí los pasos:
   - Carpeta de instalación: por defecto `%LOCALAPPDATA%\Programs\Cholinanzas`.
   - Acceso directo en el escritorio: viene destildado (opcional).
   - Al terminar, dejá marcado **"Abrir Cholinanzas"**.

**Anotá:** cuánto tardó, si hubo algún cartel inesperado y si el antivirus lo bloqueó o lo marcó.

---

## 4. Primer arranque

- Se abre la ventana de Cholinanzas con el ícono azul con "$" en la barra de tareas.
- Primero aparece la pantalla **«Términos de uso y política de privacidad»** con los botones «Acepto» y «No acepto». Probá «No acepto»: la app tiene que cerrarse sin crear nada. Volvé a abrirla y elegí «Acepto»: ya no tiene que volver a pedirlo.
- Después aparece el **recorrido de bienvenida** (3 pasos). Probá "Siguiente", "Anterior" y, en el último paso, **Ir a Cuentas**.
- Si cerrás el recorrido y reabrís la app, **no** tiene que volver a aparecer. Para verlo de nuevo: pestaña **Ayuda → Ver el recorrido de bienvenida**.

---

## 5. Lista de pruebas de la app

Marcá cada punto. Es una prueba de humo: no hace falta revisar cada número, sí que nada se rompa.

**Cuentas y movimientos**
- [ ] En **Cuentas**, crear una cuenta en pesos con saldo inicial (por ejemplo "Banco", 100000) y otra en dólares (por ejemplo "Dólares", 200).
- [ ] En **Movimientos**, cargar un ingreso y un gasto. Al escribir la descripción, la app sugiere una categoría.
- [ ] El saldo de la cuenta cambia como corresponde.
- [ ] Editar un movimiento (botón *Editar*) y eliminar otro (✕).

**Resúmenes**
- [ ] **General** muestra tu plata hoy, las cuentas y los gráficos.
- [ ] **Este mes** muestra ingresos, gastos y "Hacia fin de mes".
- [ ] El selector de mes cambia de mes.

**Planificación**
- [ ] En **Recurrentes**, crear uno (por ejemplo "Alquiler") y apretar **Registrar**.
- [ ] En **Presupuestos**, poner un límite bajo en una categoría y cargar un gasto que lo supere: aparece el **aviso rojo** arriba.
- [ ] En **Metas**, crear una meta y **Aportar**.

**Dólar y transferencias**
- [ ] En **Cuentas**, cargar la cotización a mano.
- [ ] Con internet: **Actualizar ahora** trae la cotización y la muestra con su origen (por ejemplo "Blue (venta)").
- [ ] **Sin internet** (desactivá el wifi o el cable): **Actualizar ahora** muestra un aviso claro y la app sigue funcionando con la última cotización.
- [ ] En **Transferencias**, pasar plata de una cuenta a otra.

**Datos**
- [ ] **Exportar todo a Excel**: elegir dónde guardar y abrir el archivo.
- [ ] **Respaldar ahora**: aparece el respaldo en la lista.
- [ ] **Importar desde CSV o Excel…** (en Movimientos): si tenés un extracto de ejemplo, probarlo en CSV y en `.xlsx`. Con un Excel de varias hojas, tiene que aparecer el selector "Hoja del Excel".

**Apariencia y ayuda**
- [ ] En **Cuentas → Apariencia**, cambiar entre Claro, Oscuro y Según Windows.
- [ ] Revisar que la pestaña **Ayuda** se lee bien, y que los botones **Términos y privacidad** y **Avisos de terceros** abren los textos.

**Cerrar y reabrir**
- [ ] Cerrar la app y volver a abrirla desde el menú Inicio: **los datos siguen ahí** y el recorrido no reaparece.
- [ ] Abrir la app dos veces seguidas: la segunda no abre otra ventana, trae la primera al frente.

---

## 6. Actualizar encima (simula una versión nueva)

1. Con la app **abierta** y con datos cargados, volvé a ejecutar el mismo instalador.
2. Tiene que ofrecer cerrar la app, instalar y terminar sin errores.
3. Abrí la app: **todos tus datos tienen que seguir**.

---

## 7. Desinstalar

1. **Configuración → Aplicaciones → Aplicaciones instaladas → Cholinanzas → Desinstalar.**
2. Aparece una pregunta: *"¿Querés borrar también tus datos…?"*.
   - **Primera vez, elegí "No".** Verificá que la carpeta `%LOCALAPPDATA%\CholinanzasApp` sigue existiendo, con `finanzas.db` adentro. Reinstalá y comprobá que tus datos vuelven.
   - **Segunda vez, elegí "Sí".** Verificá que esa carpeta desapareció.
3. En ambos casos: la carpeta `%LOCALAPPDATA%\Programs\Cholinanzas` y el acceso del menú Inicio tienen que desaparecer.

---

## 8. Opcional: probar con el lector de pantalla de Windows

Es la prueba de accesibilidad que todavía no se hizo con una persona real.

1. Activá el **Narrador**: `Win + Ctrl + Enter`.
2. Recorré la app con `Tab` y las flechas, sin mirar la pantalla si podés.
3. Fijate si cada botón y cada campo dice su nombre ("Monto", "Agregar", "Editar"), si los títulos se leen y si al cambiar de pestaña se entiende dónde estás.
4. Desactivalo con `Win + Ctrl + Enter`.

Anotá todo lo que se lea mal o no se lea. Sirve más que cualquier medición.

---

## 9. Si algo falla

Juntá esto y mandámelo:

- **Qué hiciste** justo antes, paso a paso.
- **Una captura** de pantalla del problema.
- **El archivo de errores**, si existe: `%LOCALAPPDATA%\CholinanzasApp\errores.log`.
- **La versión de Windows**: `Win + R`, escribir `winver`, Enter.
- Si fue el instalador: el archivo de registro. Se genera ejecutándolo así desde PowerShell:
  ```powershell
  .\Cholinanzas-Setup-1.0.0.exe /LOG="$env:TEMP\instalacion.log"
  ```

---

## Resultado

| Punto | ¿Salió bien? | Notas |
|---|---|---|
| PC sin .NET confirmada | | |
| Instalación sin permisos de administrador | | |
| Aviso de SmartScreen (esperable) | | |
| Primer arranque y recorrido | | |
| Lista de pruebas de la app | | |
| Actualizar encima conserva los datos | | |
| Desinstalar con "No" conserva los datos | | |
| Desinstalar con "Sí" los borra | | |
| Narrador (opcional) | | |
