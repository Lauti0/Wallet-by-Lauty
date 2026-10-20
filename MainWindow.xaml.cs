using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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

            Loaded += (_, _) => LabelFormFields(this);
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
