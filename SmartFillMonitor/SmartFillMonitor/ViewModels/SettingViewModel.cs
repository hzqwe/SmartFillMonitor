using System;
using System.Collections.Generic;
// 可观察集合：绑定界面控件（如下拉框），集合变化时界面自动刷新
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// MVVM工具包：提供ObservableProperty（自动实现属性通知）、RelayCommand（命令绑定）
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFillMonitor.Models;   // 引入配置模型
using SmartFillMonitor.Services; // 引入配置读写服务

namespace SmartFillMonitor.ViewModels
{
    // partial：部分类，配合MVVM工具包的特性生成代码
    // ObservableObject：实现INotifyPropertyChanged，属性变化时通知界面刷新
    public partial class SettingViewModel : ObservableObject
    {
        // 串口名称集合（绑定界面串口号下拉框）
        // ObservableCollection：集合内容变化时，界面下拉框自动更新
        public ObservableCollection<string> PortNames { get; } = new ObservableCollection<string>();

        // 波特率可选值集合（绑定界面波特率下拉框），初始化时添加常用波特率
        public ObservableCollection<int> BaudRates { get; } = new ObservableCollection<int>
        {
            9600,19200,38400,57600,115200
        };

        // 数据位可选值集合（绑定界面数据位下拉框）
        public ObservableCollection<int> DataBitsOptions { get; } = new ObservableCollection<int>
        {
            7,8
        };

        // 校验位可选值集合（绑定界面校验位下拉框）
        public ObservableCollection<string> ParityOptions { get; } = new ObservableCollection<string>
        {
            "None","Odd","Even"
        };

        // 停止位可选值集合（绑定界面停止位下拉框）
        public ObservableCollection<string> StopBitsOptions { get; } = new ObservableCollection<string>
        {
            "None","One","Two"
        };

        // 【MVVM特性】ObservableProperty：自动生成PortName属性+通知逻辑
        // 私有字段_portName → 公共属性PortName，赋值时自动通知界面刷新
        // 默认值：COM3
        [ObservableProperty]
        private string portName = "COM3";

        // 选中的波特率（绑定界面波特率下拉框选中项），默认115200
        [ObservableProperty]
        private int selectedBaud = 115200;

        // 选中的数据位（绑定界面数据位下拉框选中项），默认8
        [ObservableProperty]
        private int selectedDataBits = 8;

        // 选中的校验位（绑定界面校验位下拉框选中项），默认None
        [ObservableProperty]
        private string selectedParity = "None";

        // 选中的停止位（绑定界面停止位下拉框选中项），默认One
        [ObservableProperty]
        private string selectedStopBits = "One";

        // 自动连接开关（绑定界面"启动时自动连接设备"复选框），默认true
        [ObservableProperty]
        private bool autoConnect = true;

        // 报警声音开关（绑定界面"启用报警声音提示"复选框），默认true
        [ObservableProperty]
        private bool alarmSound = true;

        // 调试日志开关（绑定界面"开启日志调试"复选框），默认false
        [ObservableProperty]
        private bool debugLogMode = false;

        // 构造函数：ViewModel创建时执行（程序打开设置页面时）
        public SettingViewModel()
        {
            RefreshPortList();
            // 加载配置文件到ViewModel属性
            _=LoadSettings();

        }

        private void RefreshPortList()
        {
            PortNames.Clear();
            try
            {
                //??（空合并运算符）
                //如果 ?? 左边的值是 null → 执行右边的代码；
                //如果 ?? 左边的值不是 null → 直接用左边的值；
                var ports = PlcService.GetAvaliblePorts()??SerialPort.GetPortNames();
                foreach (var item in ports)
                {
                    PortNames.Add(item);
                }
                //自动把失效的旧选中项换成新列表里的第一个有效串口：
                if (!string.IsNullOrEmpty(PortName)&&!PortNames.Contains(PortName))
                {
                    PortName = PortNames.Count > 0 ? PortNames[0] : PortName;
                }


            }
            catch (Exception ex)
            {
                LogService.Error($"获取串口列表失败：{ex.Message}，可能没有权限访问串口设备");
                PortNames.Clear();
                PortNames.Add("COM1");
                PortNames.Add("COM2");
            }
        }

        // 异步加载配置：从文件读取配置，赋值给ViewModel属性
        // async void：仅用于"非await调用的异步方法"（构造函数不能用async Task）
        private async Task LoadSettings()
        {
            try
            {
                // 调用配置服务，异步加载配置文件
                DeviceSettings ds = await ConfigServices.LoadSettingsAsync();

                // 把加载的配置赋值给ViewModel属性（界面会自动刷新，因为用了ObservableProperty）
                PortName = ds.PortName;
                SelectedBaud = ds.BaudRate;
                SelectedDataBits = ds.DataBits;
                SelectedParity = ds.Parity;
                SelectedStopBits = ds.StopBits;
                AutoConnect = ds.AutoConnect;
                AlarmSound = ds.AlarmSound;
                DebugLogMode = ds.DebugLogMode;     
            }
            catch (Exception ex)
            {
                // 这里的 catch 才有效
                LogService.Error($"加载配置失败，使用默认值，原因：{ex.Message}");
            }
            
        }

        // 【MVVM特性】RelayCommand：绑定界面"保存"按钮，点击按钮执行SaveAsync方法
        [RelayCommand]
        private async Task SaveAsync()
        {
            try
            {
                // 第一步：把ViewModel的属性（界面用户选择的值）封装成DeviceSettings模型
                var model = new DeviceSettings
                {
                    PortName = PortName,          // 界面选中的串口号
                    BaudRate = SelectedBaud,      // 界面选中的波特率
                    DataBits = SelectedDataBits,  // 界面选中的数据位
                    Parity = SelectedParity,      // 界面选中的校验位
                    StopBits = SelectedStopBits,  // 界面选中的停止位
                    AutoConnect = AutoConnect,    // 界面选中的自动连接开关
                    AlarmSound = AlarmSound,      // 界面选中的报警声音开关
                    DebugLogMode = DebugLogMode   // 界面选中的调试日志开关
                };

                // 第二步：调用配置服务，异步保存配置到文件
                await ConfigServices.SaveDeviceSettingsAsync(model);

                // 可扩展：保存成功后给用户提示（比如MessageBox.Show("配置保存成功！")）
            }
            catch (Exception ex)
            {
                // 保存失败：可记录日志/提示用户（比如MessageBox.Show($"保存失败：{ex.Message}")）
                LogService.Error($"保存配置失败，原因：{ex.Message}");
            }
        }
    }
}