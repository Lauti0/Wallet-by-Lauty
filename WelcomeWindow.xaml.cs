using System.Windows;

namespace CholinanzasApp
{
    /// <summary>Recorrido de bienvenida de tres pasos. Se muestra la primera vez y se puede repetir desde Ayuda.</summary>
    public partial class WelcomeWindow : Window
    {
        private const int LastPage = 3;
        private int _page = 1;

        public WelcomeWindow()
        {
            InitializeComponent();
            ShowPage();
        }

        /// <summary>Tag de la pestaña a la que el usuario pidió ir al terminar ("Cuentas y datos" o "Movimientos"), o null.</summary>
        public string? Destination { get; private set; }

        private void ShowPage()
        {
            Page1.Visibility = _page == 1 ? Visibility.Visible : Visibility.Collapsed;
            Page2.Visibility = _page == 2 ? Visibility.Visible : Visibility.Collapsed;
            Page3.Visibility = _page == 3 ? Visibility.Visible : Visibility.Collapsed;

            StepText.Text = $"Paso {_page} de {LastPage}";
            BackButton.Visibility = _page == 1 ? Visibility.Collapsed : Visibility.Visible;
            NextButton.Content = _page == LastPage ? "Empezar" : "Siguiente";
            SkipButton.Visibility = _page == LastPage ? Visibility.Collapsed : Visibility.Visible;

            // el foco queda en el botón principal: con Enter se avanza, y un lector de pantalla anuncia el paso
            NextButton.Focus();
        }

        // Esc cierra el recorrido en cualquier paso, también en el último (donde no hay botón "Saltar").
        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            DialogResult = false;
        }

        private void Skip_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_page == LastPage)
            {
                DialogResult = true;
                return;
            }
            _page++;
            ShowPage();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_page > 1) _page--;
            ShowPage();
        }

        private void GoToAccounts_Click(object sender, RoutedEventArgs e) => Finish("Cuentas y datos");

        private void GoToTransactions_Click(object sender, RoutedEventArgs e) => Finish("Movimientos");

        private void Finish(string destination)
        {
            Destination = destination;
            DialogResult = true;
        }
    }
}
