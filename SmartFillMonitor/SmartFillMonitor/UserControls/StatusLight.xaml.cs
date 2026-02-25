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
using SmartFillMonitor.Models;

namespace SmartFillMonitor.UserControls
{
    /// <summary>
    /// StatusLight.xaml 的交互逻辑
    /// </summary>
    public partial class StatusLight : UserControl
    {

        public LightState State
        {
            get { return (LightState)GetValue(StateProperty); }
            set { SetValue(StateProperty, value); }
        }

        // Using a DependencyProperty as the backing store for State.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty StateProperty =
            DependencyProperty.Register(nameof(State), typeof(LightState), typeof(StatusLight), new PropertyMetadata(LightState.Off));




        public StatusLight()
        {
            InitializeComponent();
        }
    }
}
