using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;
using SmartFillMonitor.ViewModels;

namespace SmartFillMonitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var app = Application.Current as App;
            if (app.ServiceProvider != null)
            {
                //整个 MainWindow 及其所有子控件的默认 DataContext 都是 MainWindowViewModel 的实例。
                DataContext = app.ServiceProvider.GetRequiredService<MainWindowViewModel>();
            }
        }
    }
}