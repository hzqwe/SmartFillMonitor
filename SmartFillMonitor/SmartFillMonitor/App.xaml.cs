using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection; // .NET内置依赖注入容器相关命名空间
using Serilog; // Serilog日志框架核心命名空间
using SmartFillMonitor.Services;
using SmartFillMonitor.ViewModels; // 视图模型命名空间
using SmartFillMonitor.Views; // 视图命名空间

namespace SmartFillMonitor
{
    /// <summary>
    /// 应用程序入口类，负责程序启动、日志配置、依赖注入容器初始化
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // 全局静态日志显示控件：整个程序唯一的日志展示富文本框
        public static RichTextBox LogView = new RichTextBox()
        {
            IsReadOnly = true, // 设置为只读，防止用户修改日志内容
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, // 自动显示垂直滚动条（日志过多时）
            Background = Brushes.Black, // 日志框背景色：黑色（符合日志查看习惯）
            Foreground = Brushes.White, // 日志文字颜色：白色（黑底白字对比清晰）
            FontFamily = new FontFamily("Consolas"), // 等宽字体：保证日志格式对齐，易读性高
        };

        // 日志输出模板：定义每条日志包含的字段和显示格式（结构化日志核心）
        // {Timestamp}：日志时间（精确到毫秒） | {Level}：日志级别 | {ThreadId}：线程ID | {Message}：日志内容 | {Exception}：异常信息
        private const string LogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss fff} [{Level}] ({ThreadId}) {Message}{NewLine}{Exception}";
        // 文本日志文件存储路径：log-.txt是滚动文件占位符，最终生成如log-2026-02-06.txt的按天命名文件
        private const string LogPath = "Logs\\log-.txt";
        // SQLite数据库文件路径：日志会持久化到该数据库文件中
        private const string DbFilePath = "SmartFillMonitor.db";
        private const string DbConnectionString = "Data Source=SmartFillMonitor.db";//给Freesql使用

        // 全局依赖注入容器：用于管理程序中所有ViewModel的生命周期和实例
        public IServiceProvider ServiceProvider { get; private set; }

        /// <summary>
        /// 程序启动时执行的核心方法：优先初始化日志，再初始化DI容器
        /// </summary>
        /// <param name="e">启动参数</param>
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // 调用基类的启动逻辑

            SetExceptionHandling();//设置全局异常处理
            //必须先初始化日志（ConfigLogging()），再初始化 DI 容器
            ConfigLogging(); // 第一步：初始化日志配置（必须优先执行，保证后续操作有日志记录）
            try
            {
                var services = new ServiceCollection(); // 创建依赖注入容器构建器
                ConfigureServices(services); // 第二步：配置DI容器，注册所有ViewModel
                ServiceProvider = services.BuildServiceProvider(); // 第三步：构建DI容器实例，供全局使用

                await InitialCoreServicesAsync();

                await InitialLoginFolowAsync();

                LogService.Debug("Initalizing PLC Service...");
                var plcSettings = await ConfigServices.LoadSettingsAsync();
                await PlcService.Initialize(plcSettings);
            }
            catch (Exception ex)
            {
                LogService.Fatal("应用程序启动失败：{0}", ex);
                MessageBox.Show($"应用程序启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); // 弹出错误提示框
                Shutdown(-1);
            }
        }

        private void SetExceptionHandling()
        {
            //1.Ui线程捕获异常
            DispatcherUnhandledException += (s, e) =>
            {
                LogService.Error("未处理的UI线程异常：{0}", e.Exception);
                //阻止程序因为这个未处理异常直接崩溃退出。
                e.Handled = true;
                MessageBox.Show($"UI异常{e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            //2.非Ui线程捕获异常
            //这个事件本身没有 “标记异常已处理” 的属性
            AppDomain.CurrentDomain.UnhandledException+= (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                LogService.Fatal("未处理的非UI线程异常");
            };

            //2.Task内部捕获异常
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogService.Error("Task,UnobservedTaskException");
                e.SetObserved();//标记为已处理，防止程序崩溃
            };
        }

        /// <summary>
        /// 程序退出时执行的方法（当前仅调用基类逻辑，可扩展清理资源、关闭日志等操作）
        /// </summary>
        /// <param name="e">退出参数</param>
        protected override void OnExit(ExitEventArgs e)
        {
            Log.CloseAndFlush();//确保log缓存日志写入文件
            base.OnExit(e); // 调用基类的退出逻辑
        }

        #region DI 依赖注入配置区域
        /// <summary>
        /// 配置依赖注入容器：注册所有ViewModel为单例模式
        /// 单例模式：整个程序生命周期内只有一个实例，保证数据共享一致
        /// </summary>
        /// <param name="services">DI容器构建器</param>
        /// 


        private async Task InitialCoreServicesAsync()
        {
            Log.Debug("正在初始化核心服务..."); // 记录日志：开始初始化核心服务
            DbProvider.Initalize(DbConnectionString); // 初始化数据库连接（SQLite），日志会自动记录连接状态和错误

            await UserService.InitalizeAsync();//确保数据库结构存在

            LogService.Info("Core Services Initialized successfully.");
        }

        private async Task InitialLoginFolowAsync()
        {
            //第一步（登录前）：设为 OnExplicitShutdown → 登录窗口关闭时，程序不会自动退出，能顺利启动主窗口；
            //先设置为手动关闭，等登录成功后再改回正常模式
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var loginWindow = new LoginWindow// 创建登录窗口实例
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen // 登录窗口居中显示
            };
            // 显示登录窗口并等待用户操作（ShowDialog会阻塞当前线程，直到窗口关闭）
            //ShowDialog()跟DialogResult有关联，而DialogResult是写在xaml.cs里面，DialogResult为True，ShowDialog()就为true,result就为true
            bool? result = loginWindow.ShowDialog();
            if (result == true) // 登录成功（登录窗口返回true）
            {
                LogService.Info("登录成功，启动主窗口");
                var mainVM = ServiceProvider.GetRequiredService<MainWindowViewModel>(); // 从DI容器获取主窗口实例（自动注入依赖）
                var mainWindow = new MainWindow
                {
                    DataContext = mainVM // 设置主窗口的数据上下文为从DI容器获取的实例
                };
                Current.MainWindow = mainWindow; // 设置当前应用程序的主窗口
                //第二步（登录成功）：改回 OnMainWindowClose → 后续用户关闭主窗口，程序自动退出
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            else // 登录失败或用户取消登录（登录窗口返回false或null）
            {
                Shutdown(); // 退出程序
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // 注册各模块ViewModel为单例（整个程序生命周期唯一实例）
            services.AddSingleton<AlarmsViewModel>(); // 报警模块视图模型
            services.AddSingleton<DashBoardViewModel>(); // 仪表盘模块视图模型
            services.AddSingleton<DashQueryViewModel>(); // 仪表盘查询模块视图模型
            services.AddSingleton<LogsViewModel>(); // 日志模块视图模型
            services.AddSingleton<SettingViewModel>(); // 设置模块视图模型
            services.AddSingleton<MainWindowViewModel>(); // 主窗口视图模型
        }

        /// <summary>
        /// 配置Serilog日志框架：多输出源（界面/控制台/文件/SQLite）+ 结构化格式
        /// </summary>
        private void ConfigLogging()
        {
            // 构建Serilog日志配置器，链式配置各项规则
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug() // 设置日志最低级别为Debug（Debug及以上都记录）
                .Enrich.WithThreadId() // 丰富日志信息：添加线程ID字段（排查多线程问题）
                .WriteTo.RichTextBox(LogView, outputTemplate: LogTemplate) // 输出到界面日志框
                .WriteTo.Console(outputTemplate: LogTemplate) // 输出到控制台（开发调试用）
                //rollingInterval: RollingInterval.Day 的意思是：Serilog 会按 “天” 来分割日志文件，每天自动生成一个新的日志文件，不会把所有日志都堆在一个文件里。
                .WriteTo.File(LogPath, rollingInterval: RollingInterval.Day, outputTemplate: LogTemplate, shared: true) // 输出到按天滚动的文本文件，shared:true允许多线程写文件
                .WriteTo.SQLite(DbFilePath, tableName: "SystemLog", storeTimestampInUtc: false) // 输出到SQLite数据库，tableName指定日志表名，storeTimestampInUtc:false使用本地时间
                .CreateLogger(); // 创建日志实例并赋值给Serilog全局日志对象
        }
        #endregion
    }
}