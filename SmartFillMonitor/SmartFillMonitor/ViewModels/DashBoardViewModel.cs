using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFillMonitor.Models;
using LiveCharts;
using LiveCharts.Wpf;
using System.Windows.Media;
using SmartFillMonitor.Services;
using System.Windows;

namespace SmartFillMonitor.ViewModels
{
   public partial class DashBoardViewModel:ObservableObject
    {
        [ObservableProperty]
        private int actualCount;

        [ObservableProperty]
        private int targetCount;

        [ObservableProperty]
        private double currentTemp;

        [ObservableProperty]
        private double settingTemp;

        [ObservableProperty]
        private double runningTime;

        [ObservableProperty]
        private string deviceStatus="自动运行";

        [ObservableProperty]
        private double currentCycleTime;

        [ObservableProperty]
        private double standardCycleTime;

        [ObservableProperty]
        private bool valueOpen=false;

        [ObservableProperty]
        private double liquidLevel;

        private string _lastbarCode = string.Empty;

        public ObservableCollection <AlarmUiModel> RecentAlarms { get;  }= new ObservableCollection<AlarmUiModel>();

        [ObservableProperty]
        private SeriesCollection? tempLiveCharts;

        [ObservableProperty]
        private LightState indicatorState=LightState.Off;

        public DashBoardViewModel()
        {
            PlcService.DataReceived += OnDataReceived;
            PlcService.ConnectionChanged += OnConnectionChanged; // 新增
            AlarmService.AlarmTriggered += AlarmService_AlarmTriggered;
            TempLiveCharts = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title="温度趋势",
                    Values=new LiveCharts.ChartValues<double>(),
                    Fill=Brushes.DodgerBlue,
                    Stroke=Brushes.Cyan,
                    StrokeThickness=1,
                }
            };
        }

        private void OnConnectionChanged(object? sender, bool connected)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!connected)
                {
                    // 重置所有设备状态数据
                    ActualCount = 0;
                    TargetCount = 0;
                    CurrentTemp = 0;
                    SettingTemp = 0;
                    RunningTime = 0;
                    CurrentCycleTime = 0;
                    StandardCycleTime = 0;
                    LiquidLevel = 0;
                    ValueOpen = false;        // 阀门关闭
                    DeviceStatus = "未连接";
                    TempLiveCharts?[0]?.Values?.Clear();
                }
                else
                {
                    DeviceStatus = "已连接";
                }
            });
        }

        private void AlarmService_AlarmTriggered(object? sender, AlarmRecord e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 1. 往RecentAlarms集合的“第一个位置”插入新的报警记录（最新的报警显示在最前面）
                //    AlarmUiModel.FormRecord(e)：把原始报警数据（e）转换成UI能显示的报警模型
                RecentAlarms.Insert(0, AlarmUiModel.FormRecord(e));
                // 2. 限制报警记录最多显示10条，超过就删除最后一条（最旧的），避免列表过长
                if (RecentAlarms.Count>10)
                {
                    RecentAlarms.RemoveAt(RecentAlarms.Count - 1);
                }
            });


         }

        private void OnDataReceived(object? sender, DeviceState state)
        {
            if(state!=null)
            {
                //Task.Run就是 “把耗时的、会卡线程的操作（比如轮询 PLC、打开串口）丢到后台去干”，
                //保证当前线程（尤其是 UI 线程）不被阻塞
                _ = Task.Run(async () =>
                {
                    //把 PLC 传过来的「原始设备状态数据（state）」，逐个赋值给 ViewModel 的属性
                    //—— 这些属性都和 UI 界面的控件绑定，赋值后 UI 会自动刷新显示最新的 PLC 数据。

                    //数据流转路径：
                    //PLC硬件 → PlcService.ReadStateAsync()读取 → DeviceState state封装 → ViewModel属性赋值 → UI控件显示
                    ActualCount = state.ActualCount;
                    TargetCount = state.TargetCount;
                    CurrentTemp = state.CurrentTemp;
                    SettingTemp = state.SettingTemp;
                    RunningTime = state.RunningTime;
                    CurrentCycleTime = state.CurrentCycleTime;
                    StandardCycleTime = state.StandardCycleTime;
                    LiquidLevel = state.LiquidLevel;
                    ValueOpen = state.ValueOpen;
                    var barcode = state.BarCode ?? string.Empty;

                    //如果条码发送变化，记录生产数据，意味着一个新的产品到来
                    if (!string.IsNullOrEmpty(barcode) && barcode != _lastbarCode)
                    {
                        _lastbarCode = barcode;
                        var record = new ProductionRecord
                        {
                            Time = DateTime.Now,
                            BatchNo = barcode,
                            SettingTemp = state.SettingTemp,
                            ActualTemp = state.CurrentTemp,
                            ActualCount = state.ActualCount,
                            TargetCount = state.TargetCount,
                            IsNG = false,
                            CycleTime = state.CurrentCycleTime,
                            Operator = ""
                        };

                        await DbProvider.Fsql.Insert(record).ExecuteAffrowsAsync();
                    }
                });



                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (TempLiveCharts == null || TempLiveCharts.Count == 0)
                        return;
                    
                    if (TempLiveCharts[0].Values == null)
                        TempLiveCharts[0].Values = new LiveCharts.ChartValues<double>();
                    
                    TempLiveCharts[0].Values.Add(state.CurrentTemp);
                    // 检查数值集合的长度（已有的温度数据点数量）是否超过40个
                    if (TempLiveCharts[0].Values.Count > 40)
                    {
                        // 删掉集合里第一个（最旧的）温度数据点
                        TempLiveCharts[0].Values.RemoveAt(0);
                    }
                });
            }
        }

        [RelayCommand]
        private async Task StartProductionAsync()
        {
            try
            {
                DeviceStatus = "启动中...";
                IndicatorState = LightState.Green;
                await PlcService.WriteCommandAsync("Start", true);
                await Task.Delay(2000);
                DeviceStatus = "运行中...";
                LogService.Info("发送启动命令到PLC");
            }
            catch (Exception ex)
            {
                DeviceStatus = "启动失败";
                IndicatorState = LightState.Red;
                LogService.Error("发送启动命令到PLC失败", ex);
            }
        }

        [RelayCommand]
        private async Task StopProductionAsync()
        {
            try
            {
                DeviceStatus = "停止中...";
                IndicatorState = LightState.Red;
                await PlcService.WriteCommandAsync("Stop", true);
                await Task.Delay(2000);
                DeviceStatus = "已停止";
                LogService.Info("发送停止命令到PLC");
            }
            catch (Exception ex)
            {
                DeviceStatus = "停止失败";
                IndicatorState = LightState.Red;
                LogService.Error("发送停止命令到PLC失败", ex);
            }
        }

        [RelayCommand]
        private async Task ResetProductionAsync()
        {
            try
            {
                //复位是 “瞬间触发”（传 false）
                //复位操作必须先 “解除所有锁定 / 停止状态”，再执行复位
                DeviceStatus = "复位中...";
                IndicatorState = LightState.Yellow;
                await PlcService.WriteCommandAsync("Stop", false);
                await Task.Delay(2000);
                await PlcService.WriteCommandAsync("Reset", false);
                DeviceStatus = "已就绪";
                IndicatorState = LightState.Off;
                LogService.Info("发送复位脉冲到PLC");
            }
            catch (Exception ex) 
            {
                DeviceStatus = "复位失败";
                IndicatorState = LightState.Red;
                LogService.Error("发送复位脉冲到PLC失败", ex);
            }
        }
    }
}
