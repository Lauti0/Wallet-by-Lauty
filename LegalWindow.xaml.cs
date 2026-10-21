using System.Windows;
using System.Windows.Automation;

namespace CholinanzasApp
{
    /// <summary>
    /// Muestra un texto legal. En modo "aceptación" tiene los botones «Acepto» y «No acepto» (el resultado es true solo si
    /// acepta); en modo lectura solo tiene «Cerrar».
    /// </summary>
    public partial class LegalWindow : Window
    {
        public LegalWindow(string title, string body, bool requireAcceptance, string? intro = null)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            BodyText.Text = body;
            AutomationProperties.SetName(BodyText, title);

            if (intro is not null)
            {
                IntroText.Text = intro;
                IntroText.Visibility = Visibility.Visible;
            }

            if (requireAcceptance)
            {
                // sin botón de cerrar: solo se sale aceptando o rechazando de forma explícita
                AcceptButton.IsDefault = false;
                AcceptButton.Focus();
            }
            else
            {
                AcceptButton.Visibility = Visibility.Collapsed;
                DeclineButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible;
            }

            // arranca arriba del texto, no al final
            Loaded += (_, _) => BodyText.ScrollToHome();
        }

        private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Decline_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
