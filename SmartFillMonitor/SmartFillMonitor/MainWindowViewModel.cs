using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartFillMonitor.Views;
using SmartFillMonitor.ViewModels;
using SmartFillMonitor.Models;
using SmartFillMonitor.Services;
using System.Windows;

namespace SmartFillMonitor
{
   public partial class MainWindowViewModel:ObservableObject
    {
        [ObservableProperty]
        private object mainContent;

        [ObservableProperty]
        private string currentBatchNo;

        [ObservableProperty]
        private string currentTime;//把currentTime变成CurrentTime

        [ObservableProperty]
        private bool isPlcConnected;

        [ObservableProperty]
        private bool isAdmin;

        [ObservableProperty]
        private bool isUserLoggedIn;

        [ObservableProperty]
        private LightState indicatorState=LightState.Off;

        [ObservableProperty]
        private string currentUserDisplayName="未登录";

        private readonly IServiceProvider _serviceProvider;
        private readonly DispatcherTimer _timer;


        public MainWindowViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            // 使用 Dispatcher 确保在 UI 线程更新连接状态
            PlcService.ConnectionChanged += (x, connected) =>
            {
                Application.Current.Dispatcher.Invoke(() => IsPlcConnected = connected);
            };
            PlcService.DataReceived += PlcService_DataReceived;
            UserService.LoginStateChanged += user =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateUser(user);
                });
            };

            UpdateUser(UserService.CurrentUser);


            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            MainContent = _serviceProvider.GetRequiredService<DashBoardViewModel>();

        }

        private void UpdateUser(User? user)
        {
            if (user == null)
            {
                CurrentUserDisplayName = "未登录";
                IsUserLoggedIn = false;
                IsAdmin = false;
                
            }
            else
            {
                CurrentUserDisplayName = user.UserName;
                IsUserLoggedIn = true;
                IsAdmin = user.Role==Role.admin;
            }
        }

        private void PlcService_DataReceived(object? sender, DeviceState e)
        {
            Application.Current.Dispatcher.Invoke(() => CurrentBatchNo = e.BarCode);
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        #region Navigation Commands
        [RelayCommand]
        private void Navigate(string? destination)
        {
            if (string.IsNullOrEmpty(destination))
            {
                return;
            }

            switch (destination)
            {
                case "DashBoard":
                    MainContent = _serviceProvider.GetRequiredService<DashBoardViewModel>();
                    break;
                case "DataQuery":
                    MainContent = _serviceProvider.GetRequiredService<DashQueryViewModel>();
                    break;
                case "Logs":
                    MainContent = _serviceProvider.GetRequiredService<LogsViewModel>();
                    break;
                case "Alarms":
                    MainContent = _serviceProvider.GetRequiredService<AlarmsViewModel>();
                    break;
                case "Setting":
                    MainContent = _serviceProvider.GetRequiredService<SettingViewModel>();
                    break;
                default:
                    break;
            }
        }
        #endregion

        [RelayCommand]
        private void Login()
        {
            var loginWin = new LoginWindow()
            {
                Owner = Application.Current.MainWindow
            };
            
            loginWin.WindowStartupLocation=WindowStartupLocation.CenterScreen;
            var result = loginWin.ShowDialog();
            UpdateUser(UserService.CurrentUser);
        }

        [RelayCommand]
        private void ExecuteExit()
        {
            var result= MessageBox.Show("确定要退出吗？", "退出确认", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        }
}
