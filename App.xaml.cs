using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CholinanzasApp.Data;
using CholinanzasApp.Services;

namespace CholinanzasApp
{
    public partial class App : Application
    {
        private const long MaxLogBytes = 512 * 1024;

        // Se guarda en un campo para que el mutex viva mientras la app esté abierta.
        private static Mutex? _singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnUnhandledError;
            AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject as Exception);

            // Una sola ventana por base de datos: dos copias escribiendo el mismo archivo SQLite
            // se bloquearían entre sí. Si ya hay una abierta, se la trae al frente y esta se cierra.
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppDbContext.DbPath.ToLowerInvariant())))[..16];
            _singleInstance = new Mutex(initiallyOwned: true, $"Local\\Cholinanzas-{id}", out var isFirst);
            if (!isFirst)
            {
                BringExistingWindowToFront();
                Environment.Exit(0);
            }

            // primero el respaldo del día: así queda una copia de los datos tal como estaban
            // antes de cualquier actualización del esquema de la base
            BackupService.RunAutomatic();
            AppDbContext.Initialize();
        }

        // Ante un error inesperado la app avisa y sigue, en vez de cerrarse de golpe y sin explicación.
        private void OnUnhandledError(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogError(e.Exception);
            MessageBox.Show(
                "Ocurrió un error inesperado. Los detalles quedaron guardados en el archivo errores.log, " +
                "junto a tus datos.\n\nSi el problema se repite, podés volver a un respaldo desde la pestaña Cuentas.",
                "Cholinanzas", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        // El log tiene tope de tamaño: si crece de más se empieza de nuevo (no debe llenar el disco).
        private static void LogError(Exception? ex)
        {
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(AppDbContext.DbPath)!, "errores.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes) File.Delete(path);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // si ni siquiera se puede escribir el log, no hay nada más que hacer
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? className, string windowTitle);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static void BringExistingWindowToFront()
        {
            var window = FindWindow(null, "Cholinanzas");
            if (window == IntPtr.Zero) return;
            if (IsIconic(window)) ShowWindow(window, 9); // SW_RESTORE
            SetForegroundWindow(window);
        }
    }
}
