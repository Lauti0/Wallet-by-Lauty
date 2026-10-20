using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CholinanzasApp.Data;
using CholinanzasApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace CholinanzasApp.ViewModels;

public record BackupRow(string Path, DateTime Date, long Size)
{
    public string DateText => Date.ToString("dd/MM/yyyy HH:mm");
    public string SizeText => Size >= 1_048_576 ? $"{Size / 1_048_576.0:0.#} MB" : $"{Math.Max(1, Size / 1024)} KB";
}

public partial class MainViewModel
{
    private string _backupStatus = "", _backupError = "", _extraFolderText = "";
    private bool _hasExtraFolder;

    private RelayCommand? _backupNowCommand, _openBackupFolderCommand, _chooseExtraFolderCommand,
        _clearExtraFolderCommand, _restoreBackupCommand;

    public ObservableCollection<BackupRow> Backups { get; } = [];

    public string BackupStatus { get => _backupStatus; private set => Set(ref _backupStatus, value); }
    public string BackupError { get => _backupError; private set => Set(ref _backupError, value); }
    public string ExtraFolderText { get => _extraFolderText; private set => Set(ref _extraFolderText, value); }
    public bool HasExtraFolder { get => _hasExtraFolder; private set => Set(ref _hasExtraFolder, value); }

    public RelayCommand BackupNowCommand => _backupNowCommand ??= new(_ => BackupNow());
    public RelayCommand OpenBackupFolderCommand => _openBackupFolderCommand ??= new(_ => OpenBackupFolder());
    public RelayCommand ChooseExtraFolderCommand => _chooseExtraFolderCommand ??= new(_ => ChooseExtraFolder());
    public RelayCommand ClearExtraFolderCommand => _clearExtraFolderCommand ??= new(_ => ClearExtraFolder());
    public RelayCommand RestoreBackupCommand => _restoreBackupCommand ??= new(p => RestoreBackup(p as BackupRow));

    private void RefreshBackups()
    {
        Backups.Clear();
        var list = BackupService.List();
        foreach (var b in list) Backups.Add(new BackupRow(b.Path, b.Date, b.Size));

        BackupStatus = list.Count == 0
            ? "Todavía no hay respaldos."
            : $"Último respaldo: {list[0].Date:dd/MM/yyyy HH:mm}  ·  {list.Count} {(list.Count == 1 ? "guardado" : "guardados")} (se conservan los últimos {BackupService.KeepCount})";
        BackupError = BackupService.LastError ?? "";

        var extra = BackupService.ExtraFolder;
        HasExtraFolder = !string.IsNullOrWhiteSpace(extra);
        ExtraFolderText = HasExtraFolder ? extra! : "Sin carpeta extra";
    }

    private void BackupNow()
    {
        try
        {
            BackupService.CreateBackup();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo hacer el respaldo:\n{ex.Message}", "Respaldos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshBackups();
    }

    private static void OpenBackupFolder()
    {
        Directory.CreateDirectory(BackupService.BackupDir);
        Process.Start(new ProcessStartInfo(BackupService.BackupDir) { UseShellExecute = true });
    }

    private void ChooseExtraFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Elegí la carpeta extra para los respaldos (por ejemplo, OneDrive)" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            BackupService.SetExtraFolder(dialog.FolderName);
            BackupService.CreateBackup(); // deja ya una copia en la carpeta nueva
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo usar esa carpeta:\n{ex.Message}", "Respaldos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshBackups();
    }

    private void ClearExtraFolder()
    {
        BackupService.SetExtraFolder(null);
        RefreshBackups();
    }

    private void RestoreBackup(BackupRow? row)
    {
        if (row is null) return;

        var answer = MessageBox.Show(
            $"Vas a reemplazar TODOS tus datos actuales por los del respaldo del {row.DateText}.\n\n" +
            "Antes de restaurar se guarda un respaldo de lo que tenés ahora, así que podés volver atrás.\n\n¿Continuar?",
            "Restaurar respaldo", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            BackupService.Restore(row.Path);

            // los datos cambiaron por debajo: se vuelve a cargar todo
            using (var db = new AppDbContext())
                _allCategories = db.Categories.AsNoTracking().OrderBy(c => c.Name).ToList();
            RefreshFilteredCategories();
            RefreshRecFilteredCategories();
            NewBudgetCategory = ExpenseCategories.FirstOrDefault();
            Reload();

            MessageBox.Show("Listo, tus datos se restauraron.", "Restaurar respaldo",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo restaurar el respaldo.\n\n{ex.Message}\n\n" +
                "Si tus datos se ven raros, restaurá el respaldo más reciente de la lista: es el que se hizo justo antes.",
                "Restaurar respaldo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshBackups();
    }
}
