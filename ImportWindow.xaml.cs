using System.Windows;
using CholinanzasApp.ViewModels;

namespace CholinanzasApp
{
    public partial class ImportWindow : Window
    {
        public ImportWindow(ImportViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // cerrar el diálogo: devuelve "aceptado" solo si se importó algo
            viewModel.RequestClose = () => DialogResult = viewModel.ImportedCount > 0;
        }
    }
}
