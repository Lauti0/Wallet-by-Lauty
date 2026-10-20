using System.Windows;
using CholinanzasApp.Services;
using CholinanzasApp.ViewModels;

namespace CholinanzasApp
{
    /// <summary>Pregunta si se quiere crear una regla a partir de una corrección, con el texto sugerido editable.</summary>
    public partial class RulePromptWindow : Window
    {
        public RulePromptWindow(RuleOffer offer)
        {
            InitializeComponent();

            MessageText.Text = $"Pusiste la categoría «{offer.CategoryName}» a «{offer.Description}».\n\n" +
                               "¿Querés que la app lo haga sola la próxima vez? Se crea una regla con este texto:";

            if (offer.Note is not null)
            {
                NoteText.Text = offer.Note;
                NoteText.Visibility = Visibility.Visible;
            }

            PatternBox.Text = offer.SuggestedPattern;
            Loaded += (_, _) =>
            {
                PatternBox.Focus();
                PatternBox.SelectAll();
            };
        }

        /// <summary>El texto de la regla que eligió el usuario.</summary>
        public string Pattern => PatternBox.Text.Trim();

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (CsvImportService.Normalize(Pattern).Length < CategoryRuleService.MinPatternLength)
            {
                ErrorText.Text = $"Escribí al menos {CategoryRuleService.MinPatternLength} letras.";
                ErrorText.Visibility = Visibility.Visible;
                PatternBox.Focus();
                return;
            }
            DialogResult = true;
        }
    }
}
