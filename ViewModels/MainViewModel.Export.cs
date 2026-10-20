using System.Diagnostics;
using System.IO;
using System.Windows;
using CholinanzasApp.Services;
using Microsoft.Win32;

namespace CholinanzasApp.ViewModels;

public partial class MainViewModel
{
    private RelayCommand? _exportExcelCommand, _exportCsvCommand;

    public RelayCommand ExportExcelCommand => _exportExcelCommand ??= new(_ =>
        Export("Excel (*.xlsx)|*.xlsx", ".xlsx", "Cholinanzas", ExportService.ExportExcel));

    public RelayCommand ExportCsvCommand => _exportCsvCommand ??= new(_ =>
        Export("CSV (*.csv)|*.csv", ".csv", "Cholinanzas-movimientos", ExportService.ExportMovementsCsv));

    // Pide dónde guardar, escribe el archivo y ofrece abrirlo. Los errores comunes se explican en criollo.
    private static void Export(string filter, string extension, string baseName, Action<string> write)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar",
            Filter = filter,
            DefaultExt = extension,
            FileName = $"{baseName}-{DateTime.Today:yyyy-MM-dd}",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            write(dialog.FileName);
        }
        catch (IOException)
        {
            MessageBox.Show("No se pudo guardar el archivo. Si lo tenés abierto en Excel, cerralo e intentá de nuevo.",
                "Exportar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("No tenés permiso para guardar en esa carpeta. Probá con otra, por ejemplo Documentos.",
                "Exportar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Listo, se guardó en:\n{dialog.FileName}\n\n¿Querés abrirlo ahora?", "Exportar",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
    }
}
