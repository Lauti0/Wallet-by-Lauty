using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using CholinanzasApp.Services;
using CholinanzasApp.ViewModels;

namespace CholinanzasApp
{
    public partial class MainWindow : Window
    {
        static MainWindow()
        {
            EventManager.RegisterClassHandler(typeof(DataGrid), UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(ForwardWheelToParent));
        }

        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new MainViewModel();
            DataContext = viewModel;

            // Al apretar Editar, el formulario (arriba de la página) puede quedar fuera de la pantalla: se sube hasta él.
            viewModel.EditStarted += () =>
            {
                if (((TabControl)Content).SelectedContent is ScrollViewer page) page.ScrollToTop();
            };

            viewModel.WelcomeRequested += () => ShowWelcome(viewModel);

            Loaded += (_, _) =>
            {
                LabelFormFields(this);

                // Loaded puede dispararse más de una vez: lo inicial (términos y recorrido) se revisa una sola vez
                if (_welcomeChecked) return;
                _welcomeChecked = true;

                // sin aceptar los términos no se usa la aplicación: si los rechaza, se cierra sin tocar nada
                if (!EnsureTermsAccepted(viewModel))
                {
                    Close();
                    return;
                }
                if (viewModel.ShouldShowWelcome()) ShowWelcome(viewModel);
            };

            viewModel.LegalRequested += kind => ShowLegal(kind);
        }

        // Pide aceptar los términos la primera vez, y de nuevo si cambió su texto.
        private bool EnsureTermsAccepted(MainViewModel viewModel)
        {
            if (!viewModel.NeedsTermsAcceptance()) return true;

            var window = new LegalWindow("Términos de uso y política de privacidad", LegalTexts.Terms(), requireAcceptance: true,
                intro: "Leé estos términos y aceptalos para usar Cholinanzas. Si no los aceptás, la aplicación se cierra.")
            { Owner = this };
            if (window.ShowDialog() != true) return false;

            viewModel.AcceptTerms();
            return true;
        }

        // Botones de la pestaña Ayuda: los textos se pueden volver a leer cuando se quiera.
        private void ShowLegal(string kind)
        {
            var (title, body) = kind == "notices"
                ? ("Avisos de software de terceros", LegalTexts.ThirdPartyNotices())
                : ("Términos de uso y política de privacidad", LegalTexts.Terms());
            new LegalWindow(title, body, requireAcceptance: false) { Owner = this }.ShowDialog();
        }

        private bool _welcomeChecked, _welcomeOpen;

        // Cerrar el recorrido de cualquier forma cuenta como visto; los botones del último paso llevan a una pestaña.
        private void ShowWelcome(MainViewModel viewModel)
        {
            if (_welcomeOpen) return;   // nunca dos recorridos a la vez (por ejemplo, doble clic en el botón de Ayuda)
            _welcomeOpen = true;
            try
            {
                var welcome = new WelcomeWindow { Owner = this };
                welcome.ShowDialog();
                viewModel.MarkWelcomeSeen();

                if (welcome.Destination is { } tag && Content is TabControl tabs)
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag as string == tag) ?? tabs.SelectedItem;
            }
            finally
            {
                _welcomeOpen = false;
            }
        }

        // Los formularios ponen una etiqueta (TextBlock) arriba de cada campo. Acá se la asocia al campo
        // como nombre accesible, para que un lector de pantalla diga "Monto" y no solo "cuadro de texto".
        private static void LabelFormFields(DependencyObject parent)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            {
                if (child is StackPanel { Children.Count: 2 } panel
                    && panel.Children[0] is TextBlock { Text.Length: > 0 } label
                    && panel.Children[1] is Control field
                    && string.IsNullOrEmpty(AutomationProperties.GetName(field)))
                {
                    AutomationProperties.SetName(field, label.Text);
                }
                LabelFormFields(child);
            }
        }

        // Una tabla dentro de una página con scroll se traga la rueda del mouse: se la pasamos a la
        // página, salvo que la tabla misma todavía pueda scrollear en esa dirección.
        private static void ForwardWheelToParent(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not DataGrid grid) return;

            if (grid.Template.FindName("DG_ScrollViewer", grid) is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                var canScroll = e.Delta > 0 ? sv.VerticalOffset > 0 : sv.VerticalOffset < sv.ScrollableHeight;
                if (canScroll) return;
            }

            e.Handled = true;
            if (grid.Parent is UIElement parent)
                parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = grid
                });
        }
    }
}
