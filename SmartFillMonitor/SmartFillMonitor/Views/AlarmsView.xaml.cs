using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using SmartFillMonitor.Models;
using SmartFillMonitor.Services;
using SmartFillMonitor.ViewModels;

namespace SmartFillMonitor.Views
{
    /// <summary>
    /// AlarmsView.xaml 的交互逻辑
    /// </summary>
    public partial class AlarmsView : UserControl
    {
        public AlarmsView()
        {
            InitializeComponent();

            var app = Application.Current as App;
            if (app.ServiceProvider != null)
            {
                DataContext = app.ServiceProvider.GetRequiredService<AlarmsViewModel>();
            }
        }
    }
}
