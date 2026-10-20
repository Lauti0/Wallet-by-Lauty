using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using CholinanzasApp.Data;
using CholinanzasApp.Models;
using CholinanzasApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace CholinanzasApp.ViewModels;

public record ColumnOption(int Index, string Label);
public record Choice<T>(T Value, string Label);
public record ImportPreviewRow(int Line, string DateText, string Description, string CategoryText, string AmountText,
    string TypeText, string StatusText, ImportStatus Status);

/// <summary>
/// Lógica del asistente de importación: lee el archivo, propone la configuración y muestra una vista previa.
/// No escribe nada en la base hasta que el usuario confirma.
/// </summary>
public class ImportViewModel : ViewModelBase
{
    private const int PreviewLimit = 300;
    private static readonly ColumnOption None = new(-1, "(ninguna)");

    // para decidir la categoría de cada fila (archivo → regla → por defecto)
    private readonly List<Category> _categories;
    private readonly List<RuleEntry> _rules;
    private readonly Dictionary<(TransactionType Type, string Name), Category> _byName;

    private string _text = "";
    private string _encoding = "";
    private bool _isExcel;
    private string? _excelPath, _selectedSheet;
    private List<string> _sheets = [];
    private List<string[]> _allRows = [];
    private List<ImportRow> _rows = [];
    private bool _loading;

    private string _fileName = "Ningún archivo elegido";
    private string _fileInfo = "";
    private Choice<char> _delimiter;
    private string _skipRowsText = "0";
    private bool _hasHeader;
    private Account? _account;
    private List<ColumnOption> _columns = [None];
    private ColumnOption _dateCol = None, _descCol = None, _amountCol = None, _debitCol = None, _creditCol = None, _categoryCol = None;
    private bool _split, _invert, _skipDuplicates = true;
    private Choice<DateStyle> _dateStyle;
    private Choice<DecimalStyle> _decimalStyle;
    private Category? _defaultExpense, _defaultIncome;
    private string _summary = "Elegí un archivo CSV o Excel para empezar.";
    private string _importText = "Importar";
    private bool _canImport;

    private RelayCommand? _chooseFile, _import, _cancel;

    public ImportViewModel()
    {
        using (var db = new AppDbContext())
        {
            Accounts = db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToList();
            var categories = db.Categories.AsNoTracking().OrderBy(c => c.Name).ToList();
            ExpenseCategories = categories.Where(c => c.Type == TransactionType.Gasto).ToList();
            IncomeCategories = categories.Where(c => c.Type == TransactionType.Ingreso).ToList();

            _categories = categories;
            _rules = CategoryRuleService.Load(db);
            _byName = categories.GroupBy(c => (c.Type, CsvImportService.Normalize(c.Name))).ToDictionary(g => g.Key, g => g.First());
        }

        _account = Accounts.FirstOrDefault();
        _defaultExpense = ExpenseCategories.FirstOrDefault(c => c.Name == "Otros gastos") ?? ExpenseCategories.FirstOrDefault();
        _defaultIncome = IncomeCategories.FirstOrDefault(c => c.Name == "Otros ingresos") ?? IncomeCategories.FirstOrDefault();

        Delimiters =
        [
            new(';', "Punto y coma ( ; )"), new(',', "Coma ( , )"), new('\t', "Tabulación"), new('|', "Barra ( | )")
        ];
        DateStyles =
        [
            new(DateStyle.Auto, "Detectar automáticamente"), new(DateStyle.DayFirst, "Día/Mes/Año  (31/12/2026)"),
            new(DateStyle.MonthFirst, "Mes/Día/Año  (12/31/2026)"), new(DateStyle.Iso, "Año-Mes-Día  (2026-12-31)")
        ];
        DecimalStyles =
        [
            new(DecimalStyle.Auto, "Detectar automáticamente"), new(DecimalStyle.CommaDecimal, "1.234,56  (coma decimal)"),
            new(DecimalStyle.DotDecimal, "1,234.56  (punto decimal)")
        ];
        _delimiter = Delimiters[0];
        _dateStyle = DateStyles[0];
        _decimalStyle = DecimalStyles[0];
    }

    // ───────────── Listas para los desplegables ─────────────

    public List<Account> Accounts { get; }
    public List<Category> ExpenseCategories { get; }
    public List<Category> IncomeCategories { get; }
    public List<Choice<char>> Delimiters { get; }
    public List<Choice<DateStyle>> DateStyles { get; }
    public List<Choice<DecimalStyle>> DecimalStyles { get; }
    public List<ColumnOption> Columns { get => _columns; private set => Set(ref _columns, value); }

    // Excel: el archivo puede traer varias hojas, y no tiene separador de columnas
    public bool IsExcel
    {
        get => _isExcel;
        private set
        {
            if (_isExcel == value) return;
            Set(ref _isExcel, value);
            Raise(nameof(IsCsv));
        }
    }

    public bool IsCsv => !_isExcel;

    public List<string> Sheets
    {
        get => _sheets;
        private set
        {
            Set(ref _sheets, value);
            Raise(nameof(HasSeveralSheets));
        }
    }

    public bool HasSeveralSheets => _sheets.Count > 1;

    public string? SelectedSheet
    {
        get => _selectedSheet;
        set
        {
            if (value is null || _selectedSheet == value) return;
            Set(ref _selectedSheet, value);
            if (!_loading) LoadSelectedSheet();
        }
    }
    public ObservableCollection<ImportPreviewRow> Preview { get; } = [];

    // ───────────── Estado visible ─────────────

    public string FileName { get => _fileName; private set => Set(ref _fileName, value); }
    public string FileInfo { get => _fileInfo; private set => Set(ref _fileInfo, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string ImportText { get => _importText; private set => Set(ref _importText, value); }
    public bool CanImport { get => _canImport; private set => Set(ref _canImport, value); }

    public int ImportedCount { get; private set; }
    public DateTime? NewestImportedDate { get; private set; }
    public Action? RequestClose { get; set; }

    // ───────────── Opciones (al cambiar cualquiera se recalcula la vista previa) ─────────────

    public Account? SelectedAccount { get => _account; set { if (value is not null) SetRebuild(ref _account, value); } }

    public Choice<char> SelectedDelimiter
    {
        get => _delimiter;
        set
        {
            if (value is null || EqualityComparer<Choice<char>>.Default.Equals(_delimiter, value)) return;
            Set(ref _delimiter, value);
            if (!_loading && _text.Length > 0) Reparse(guessLayout: true);
        }
    }

    public string SkipRowsText
    {
        get => _skipRowsText;
        set
        {
            if (_skipRowsText == value) return;
            Set(ref _skipRowsText, value);
            if (!_loading) RebuildColumns();
        }
    }

    public bool HasHeader
    {
        get => _hasHeader;
        set
        {
            if (_hasHeader == value) return;
            Set(ref _hasHeader, value);
            if (!_loading) RebuildColumns();
        }
    }

    public ColumnOption SelectedDateColumn { get => _dateCol; set { if (value is not null) SetRebuild(ref _dateCol, value); } }
    public ColumnOption SelectedDescriptionColumn { get => _descCol; set { if (value is not null) SetRebuild(ref _descCol, value); } }
    public ColumnOption SelectedAmountColumn { get => _amountCol; set { if (value is not null) SetRebuild(ref _amountCol, value); } }
    public ColumnOption SelectedDebitColumn { get => _debitCol; set { if (value is not null) SetRebuild(ref _debitCol, value); } }
    public ColumnOption SelectedCreditColumn { get => _creditCol; set { if (value is not null) SetRebuild(ref _creditCol, value); } }
    public ColumnOption SelectedCategoryColumn { get => _categoryCol; set { if (value is not null) SetRebuild(ref _categoryCol, value); } }

    public bool SplitColumns
    {
        get => _split;
        set
        {
            if (_split == value) return;
            Set(ref _split, value);
            Raise(nameof(UseSingleAmount));
            if (!_loading) Rebuild();
        }
    }

    // el par de botones de opción: "un solo monto" es lo contrario de "débito y crédito separados"
    public bool UseSingleAmount
    {
        get => !_split;
        set { if (value) SplitColumns = false; }
    }

    public bool InvertSign { get => _invert; set => SetRebuild(ref _invert, value); }
    public bool SkipDuplicates { get => _skipDuplicates; set => SetRebuild(ref _skipDuplicates, value); }
    public Choice<DateStyle> SelectedDateStyle { get => _dateStyle; set { if (value is not null) SetRebuild(ref _dateStyle, value); } }
    public Choice<DecimalStyle> SelectedDecimalStyle { get => _decimalStyle; set { if (value is not null) SetRebuild(ref _decimalStyle, value); } }
    public Category? DefaultExpenseCategory { get => _defaultExpense; set { if (value is not null) Set(ref _defaultExpense, value); } }
    public Category? DefaultIncomeCategory { get => _defaultIncome; set { if (value is not null) Set(ref _defaultIncome, value); } }

    public RelayCommand ChooseFileCommand => _chooseFile ??= new(_ => ChooseFile());
    public RelayCommand ImportCommand => _import ??= new(_ => Import());
    public RelayCommand CancelCommand => _cancel ??= new(_ => RequestClose?.Invoke());

    private void SetRebuild<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        Set(ref field, value, name);
        if (!_loading) Rebuild();
    }

    // ───────────── Cargar el archivo ─────────────

    private void ChooseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegí el archivo del banco (CSV o Excel)",
            Filter = "Extractos (*.csv;*.txt;*.xlsx)|*.csv;*.txt;*.xlsx|Todos los archivos (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        LoadFile(dialog.FileName);
    }

    /// <summary>Lee el archivo (CSV o Excel .xlsx), detecta su formato y arma la vista previa.</summary>
    public void LoadFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".xlsx")
        {
            LoadExcel(path);
            return;
        }
        if (extension is ".xls" or ".xlsm" or ".xlsb")
        {
            MessageBox.Show(
                "Ese tipo de Excel no se puede leer (los .xls antiguos y los que tienen macros).\n\n" +
                "Abrilo en Excel y guardalo como «Libro de Excel (.xlsx)» o como CSV, y elegí ese archivo.",
                "Importar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _text = CsvImportService.ReadText(path, out _encoding);
            FileName = Path.GetFileName(path);
            _excelPath = null;
            IsExcel = false;
            Sheets = [];

            _loading = true;
            var detected = CsvImportService.DetectDelimiter(_text);
            SelectedDelimiter = Delimiters.First(d => d.Value == detected);
            _loading = false;

            Reparse(guessLayout: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show($"No se pudo leer el archivo:\n{ex.Message}", "Importar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Reparse(bool guessLayout)
    {
        try
        {
            _allRows = CsvImportService.Parse(_text, SelectedDelimiter.Value);
        }
        catch (InvalidDataException ex)
        {
            _allRows = [];
            MessageBox.Show(ex.Message, "Importar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        FinishLoad(guessLayout, _allRows.Count == 0
            ? ""
            : $"Codificación: {_encoding}  ·  {_allRows.Count:N0} filas  ·  {_allRows.Max(r => r.Length)} columnas");
    }

    // Ya con las filas leídas (de un CSV o de una hoja de Excel): propone dónde empieza la tabla y qué es cada columna.
    private void FinishLoad(bool guessLayout, string info)
    {
        if (guessLayout)
        {
            _loading = true;
            var (skip, header) = CsvImportService.GuessLayout(_allRows);
            SkipRowsText = skip.ToString();
            HasHeader = header;
            _loading = false;
        }

        FileInfo = info;
        RebuildColumns();
    }

    // ───────────── Excel ─────────────

    private void LoadExcel(string path)
    {
        try
        {
            var names = ExcelImportService.GetSheetNames(path);
            if (names.Count == 0) throw new InvalidDataException("El archivo Excel no tiene hojas con datos.");
            var rows = ExcelImportService.ReadRows(path, names[0]);

            _loading = true;
            _text = "";
            _excelPath = path;
            IsExcel = true;
            Sheets = names;
            SelectedSheet = names[0];
            FileName = Path.GetFileName(path);
            _loading = false;

            ShowSheet(rows);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Importar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // El usuario eligió otra hoja del mismo libro.
    private void LoadSelectedSheet()
    {
        if (_excelPath is null || _selectedSheet is null) return;
        try
        {
            ShowSheet(ExcelImportService.ReadRows(_excelPath, _selectedSheet));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Importar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowSheet(List<string[]> rows)
    {
        _allRows = rows;
        FinishLoad(guessLayout: true, rows.Count == 0
            ? ""
            : $"Excel  ·  hoja «{_selectedSheet}»  ·  {rows.Count:N0} filas  ·  {rows.Max(r => r.Length)} columnas");
    }

    private int SkipRows => int.TryParse(SkipRowsText, out var n) && n >= 0 ? Math.Min(n, _allRows.Count) : 0;

    private List<string[]> DataRows() => _allRows.Skip(SkipRows + (HasHeader ? 1 : 0)).ToList();

    // Arma la lista de columnas disponibles y propone cuál es cada una.
    private void RebuildColumns()
    {
        var skip = SkipRows;
        var header = HasHeader && skip < _allRows.Count ? _allRows[skip] : null;
        var data = DataRows();
        var width = Math.Max(header?.Length ?? 0, data.Count > 0 ? data.Max(r => r.Length) : 0);
        var sampleRow = data.FirstOrDefault();

        var options = new List<ColumnOption> { None };
        for (var i = 0; i < width; i++)
        {
            var title = header is not null && i < header.Length && header[i].Trim().Length > 0 ? header[i].Trim() : $"Columna {i + 1}";
            var sample = sampleRow is not null && i < sampleRow.Length ? sampleRow[i].Trim() : "";
            if (sample.Length > 24) sample = sample[..24] + "…";
            options.Add(new ColumnOption(i, sample.Length > 0 ? $"{title}   (ej: {sample})" : title));
        }

        _loading = true;
        Columns = options;
        var guess = CsvImportService.GuessColumns(header, data);
        ColumnOption Pick(int index) => options.FirstOrDefault(o => o.Index == index) ?? None;
        SelectedDateColumn = Pick(guess.Date);
        SelectedDescriptionColumn = Pick(guess.Description);
        SelectedAmountColumn = Pick(guess.Amount);
        SelectedDebitColumn = Pick(guess.Debit);
        SelectedCreditColumn = Pick(guess.Credit);
        SelectedCategoryColumn = Pick(guess.Category);
        SplitColumns = guess.Split;
        _loading = false;

        Rebuild();
    }

    private ImportOptions BuildOptions() => new()
    {
        DateColumn = SelectedDateColumn.Index,
        DescriptionColumn = SelectedDescriptionColumn.Index,
        AmountColumn = SelectedAmountColumn.Index,
        DebitColumn = SelectedDebitColumn.Index,
        CreditColumn = SelectedCreditColumn.Index,
        CategoryColumn = SelectedCategoryColumn.Index,
        SplitColumns = SplitColumns,
        InvertSign = InvertSign,
        DateStyle = SelectedDateStyle.Value,
        DecimalStyle = SelectedDecimalStyle.Value
    };

    // ───────────── Vista previa ─────────────

    private void Rebuild()
    {
        _rows = [];
        Preview.Clear();

        var data = DataRows();
        if (_allRows.Count == 0 || data.Count == 0)
        {
            Summary = _allRows.Count == 0 ? "Elegí un archivo CSV o Excel para empezar." : "No hay filas de datos con esa configuración.";
            UpdateImportState();
            return;
        }

        var options = BuildOptions();
        var hasAmount = options.SplitColumns ? options.DebitColumn >= 0 || options.CreditColumn >= 0 : options.AmountColumn >= 0;
        if (options.DateColumn < 0 || !hasAmount)
        {
            Summary = "Indicá qué columna es la fecha y cuál es el monto.";
            UpdateImportState();
            return;
        }

        var firstLine = SkipRows + (HasHeader ? 1 : 0) + 1;

        // primera pasada, solo para conocer el rango de fechas y traer lo que ya existe en esa cuenta
        var first = CsvImportService.Evaluate(data, options, firstLine, new Dictionary<DuplicateKey, int>());
        var dates = first.Where(r => r.Date.HasValue).Select(r => r.Date!.Value).ToList();

        Dictionary<DuplicateKey, int> existing = [];
        if (SelectedAccount is not null && dates.Count > 0)
        {
            var min = dates.Min();
            var max = dates.Max();
            var accountId = SelectedAccount.Id;
            using var db = new AppDbContext();
            existing = CsvImportService.BuildExistingMap(db.Transactions.AsNoTracking()
                .Where(t => t.AccountId == accountId && t.Date >= min && t.Date <= max).ToList());
        }

        _rows = CsvImportService.Evaluate(data, options, firstLine, existing);

        var currency = SelectedAccount?.Currency ?? Currency.ARS;
        foreach (var r in _rows.Take(PreviewLimit))
        {
            var failed = r.Status == ImportStatus.Error;
            var (category, source) = failed ? (null, "") : Resolve(r);
            Preview.Add(new ImportPreviewRow(
                r.Line,
                r.Date?.ToString("dd/MM/yyyy") ?? "—",
                r.Description,
                category is null ? "—" : source == "por defecto" ? $"{category.Name} (por defecto)" : category.Name,
                failed ? "—" : Money.Format(r.Type == TransactionType.Gasto ? -r.Amount : r.Amount, currency),
                failed ? "—" : r.Type.ToString(),
                r.Status switch { ImportStatus.New => "Nuevo", ImportStatus.Duplicate => "Ya existe", _ => r.Error ?? "Error" },
                r.Status));
        }

        var toImport = ToImport().Count;
        var duplicates = _rows.Count(r => r.Status == ImportStatus.Duplicate);
        var errors = _rows.Count(r => r.Status == ImportStatus.Error);
        var parts = new List<string> { $"{toImport:N0} para importar" };
        if (duplicates > 0) parts.Add($"{duplicates:N0} ya existen{(SkipDuplicates ? " (se omiten)" : "")}");
        if (errors > 0) parts.Add($"{errors:N0} con error (se omiten)");
        if (_rows.Count > PreviewLimit) parts.Add($"mostrando las primeras {PreviewLimit}");
        Summary = string.Join("  ·  ", parts);

        UpdateImportState();
    }

    // De dónde sale la categoría de una fila: la que trae el archivo, una regla, o la elegida por defecto.
    private (Category? Category, string Source) Resolve(ImportRow r)
    {
        if (r.CategoryName is not null
            && _byName.TryGetValue((r.Type, CsvImportService.Normalize(r.CategoryName)), out var fromFile))
            return (fromFile, "archivo");

        var rule = CategoryRuleService.FindMatch(r.Description, r.Type, _rules);
        var fromRule = rule is null ? null : _categories.FirstOrDefault(c => c.Id == rule.CategoryId);
        if (fromRule is not null) return (fromRule, "regla");

        return (r.Type == TransactionType.Gasto ? DefaultExpenseCategory : DefaultIncomeCategory, "por defecto");
    }

    private List<ImportRow> ToImport() => _rows
        .Where(r => r.Status == ImportStatus.New || (!SkipDuplicates && r.Status == ImportStatus.Duplicate))
        .ToList();

    private void UpdateImportState()
    {
        var count = ToImport().Count;
        CanImport = SelectedAccount is not null && count > 0;
        ImportText = count == 1 ? "Importar 1 movimiento" : $"Importar {count:N0} movimientos";
    }

    // ───────────── Importar ─────────────

    private void Import()
    {
        var chosen = ToImport();
        if (SelectedAccount is null || chosen.Count == 0) return;

        var answer = MessageBox.Show(
            (chosen.Count == 1 ? "Se va a importar 1 movimiento" : $"Se van a importar {chosen.Count:N0} movimientos") +
            $" a la cuenta \"{SelectedAccount.Name}\".\n\n" +
            "Antes se hace un respaldo automático de tus datos, por si querés volver atrás.\n\n¿Continuar?",
            "Importar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        if (RunImport()) RequestClose?.Invoke();
    }

    /// <summary>Guarda los movimientos elegidos (todo o nada). Devuelve false si no se pudo, sin cambiar nada.</summary>
    public bool RunImport()
    {
        var chosen = ToImport();
        if (SelectedAccount is null || chosen.Count == 0) return false;

        // una importación toca muchos datos de golpe: si no hay respaldo, no se importa
        try
        {
            BackupService.CreateBackup();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo hacer el respaldo previo, así que no se importó nada.\n\n{ex.Message}", "Importar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            using var db = new AppDbContext();

            foreach (var r in chosen)
            {
                db.Transactions.Add(new FinanceTransaction
                {
                    Date = r.Date!.Value,
                    Amount = r.Amount,
                    Type = r.Type,
                    Description = r.Description, // ya viene recortada por CsvImportService.Evaluate
                    AccountId = SelectedAccount.Id,
                    CategoryId = (Resolve(r).Category ?? _categories.First(c => c.Type == r.Type)).Id
                });
            }
            db.SaveChanges(); // todo o nada: una sola operación
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo importar. No se guardó ningún movimiento.\n\n{ex.Message}", "Importar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        ImportedCount = chosen.Count;
        NewestImportedDate = chosen.Max(r => r.Date);
        return true;
    }
}
