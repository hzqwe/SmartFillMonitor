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
using System.Windows.Shapes;
using SmartFillMonitor.Models;
using SmartFillMonitor.Services;

namespace SmartFillMonitor.Views
{
    /// <summary>
    /// LoginWindow.xaml 的交互逻辑
    /// </summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            //委托里要调用 await LoadUserAsync()（异步方法），必须给委托加 async 关键字
            Loaded+=async(s, e) =>
            {
                await LoadUserAsync();
                // 先确认“密码框控件已经加载出来了”，注意不要理解为密码框不为空
                if (PasswordBox!=null)
                {
                    PasswordBox.Focus();//设置焦点到密码框
                }
            };

            KeyDown +=LoginWindow_KeyDown;
        }

        //给登录窗口加 “按 Esc 键快速退出” 的快捷操作
        private void LoginWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            //检查用户按的是不是「Esc 键」
            if (e.Key == Key.Escape)
            {
                //这里填 this 就是告诉方法：“是登录窗口自己触发的，不是按钮点击
                //new RoutedEventArgs()由于没有点击按钮事件，所以新建一个空的参数凑数
                Cancel_Click(this, new RoutedEventArgs());
                //处理键盘 / 鼠标等事件时的 “终止标记”，不触发任何额外操作
                e.Handled = true;
            }
        }

        private async Task LoadUserAsync()
        {
            try
            {
                List<User> users = await UserService.GetAllUsersAsync();
                //UserNameCombo.ItemsSource = users; 跟前面xaml代码里面的DisplayMemberPath = "UserName" SelectedValuePath = "UserName"进行一个响应
                UserNameCombo.ItemsSource = users;
                if (PasswordBox!=null&&users.Count>0)
                {
                    UserNameCombo.SelectedIndex = 0;//默认选择第一个用户
                }
            }
            catch
            {

            }
        }

        //给登录窗口加 “鼠标拖动” 的功能
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            //if (e.ChangedButton == MouseButton.Left) 只处理「鼠标左键」点击，右键 / 中键点击不管
            if (e.ChangedButton==MouseButton.Left)
            {
                //窗口的 “拖动方法”
                this.DragMove();
            }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            //从用户名下拉框中获取 “选中项的实际值”，并尝试转换成字符串类型
            var username = (UserNameCombo.SelectedValue as string) ?? string.Empty;
            var password = PasswordBox.Password ?? string.Empty;
            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("请输入用户名", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                UserNameCombo.Focus();
                return;
            }

            IsEnabled = false; // 点击后禁用所有控件，防止重复登录
            try
            {
                //验证身份
                var ok = await UserService.AuthenticateAsync(username, password);
                if (ok)
                {
                    DialogResult = true;
                    //Close() 已经封装了 Dispose 的核心逻辑，WPF 框架会自动调用窗口的 Dispose() 方法
                    Close();

                }
                else
                {
                    MessageBox.Show("用户名或密码错误", "登录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    //Dispatcher	WPF 里管理 UI 线程的 “调度员”，所有 UI 操作（比如改控件内容、设置焦点）都必须通过它在 UI 线程执行
                    //BeginInvoke	告诉 “调度员”：“把后面的操作加到 UI 线程的任务队列里，异步执行，不用等它完成”
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        PasswordBox.Clear(); 
                        PasswordBox.Focus();
                        Keyboard.Focus(PasswordBox);
                    }));
                }
            }
            finally
            {
                IsEnabled = true; // 无论成功/失败，都恢复启用
            }

            }


        

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
