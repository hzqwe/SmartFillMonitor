using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFillMonitor.Models;
using SmartFillMonitor.Services;

namespace SmartFillMonitor.ViewModels
{
    /// <summary>
    /// 报警模块视图模型（MVVM架构的核心层）
    /// 负责处理报警页面的业务逻辑、数据绑定、命令响应
    /// 继承ObservableObject实现属性通知，使UI自动刷新
    /// </summary>
    public partial class AlarmsViewModel : ObservableObject
    {
        /// <summary>
        /// 活动报警集合（绑定到UI的活动报警列表）
        /// ObservableCollection自动通知UI集合变化（新增/删除/修改）
        /// </summary>
        public ObservableCollection<AlarmUiModel> ActiveAlarms { get; set; }

        /// <summary>
        /// 历史报警集合（绑定到UI的历史报警表格）
        /// </summary>
        public ObservableCollection<AlarmUiModel> HistroyAlarms { get; set; }

        /// <summary>
        /// 活动报警数量（绑定到UI的数字角标）
        /// [ObservableProperty]自动生成属性通知，值变化时UI自动刷新
        /// </summary>
        [ObservableProperty]
        private int activeAlarmCount;

        /// <summary>
        /// 历史报警查询的开始时间（默认值：今天往前推1天）
        /// 绑定到UI的开始时间选择器
        /// </summary>
        [ObservableProperty]
        private DateTime historyStartDate = DateTime.Today.AddDays(-1);

        /// <summary>
        /// 历史报警查询的结束时间（默认值：今天）
        /// 绑定到UI的结束时间选择器
        /// </summary>
        [ObservableProperty]
        private DateTime historyEndDate = DateTime.Today;

        /// <summary>
        /// 构造函数：初始化数据和事件订阅
        /// </summary>
        public AlarmsViewModel()
        {
            // 初始化报警集合
            ActiveAlarms = new ObservableCollection<AlarmUiModel>();
            HistroyAlarms = new ObservableCollection<AlarmUiModel>();

            // 订阅报警触发事件：当有新报警时，自动执行OnAlarmTriggered方法
            //完整的执行逻辑是 * *「按钮点击 → 触发报警服务 → 发布事件 → 订阅事件 → 更新 UI」**
            AlarmService.AlarmTriggered += OnAlarmTriggered;

            // 异步加载活动报警（启动时自动加载）
            // 使用_ = 忽略返回值，避免异步警告（非阻塞初始化）          
            _ = loadActiveAlarmsAsync();
        }

        /// <summary>
        /// 异步加载活动报警数据（核心私有方法）
        /// 从数据库获取IsActive=true的报警，更新到ActiveAlarms集合
        /// </summary>
        /// <returns>异步任务</returns>
        private async Task loadActiveAlarmsAsync()
        {
            try
            {
                // 调用服务层方法，获取数据库中的活动报警记录
                var records = await AlarmService.GetActiveAlarmsAsync();

                // Dispatcher.Invoke：切换到UI线程更新集合（WPF要求UI元素必须在UI线程修改）
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 清空原有数据，避免重复显示
                    ActiveAlarms.Clear();

                    // 遍历数据库记录，转换为UI模型并添加到集合
                    foreach (var item in records)
                    {
                        ActiveAlarms.Add(AlarmUiModel.FormRecord(item));
                    }

                    // 更新活动报警数量，同步UI角标数字
                    //ActiveAlarmCount是用[ObservableProperty] 标记的属性
                    //ActiveAlarms.Count 是当前活动报警列表的实际数量
                    ActiveAlarmCount = ActiveAlarms.Count;
                }
           );
            }
            catch (Exception ex)
            {
                // 捕获异常并记录日志，避免程序崩溃
                LogService.Error($"活动报警异常", ex);
            }
        }

        /// <summary>
        /// 报警触发事件的回调方法
        /// 当AlarmService触发新报警时，自动执行此方法更新UI
        /// </summary>
        /// <param name="sender">事件发送者（此处为null）</param>
        /// <param name="record">新触发的报警记录</param>
        private void OnAlarmTriggered(object? sender, AlarmRecord record)
        {
            // 跨线程处理：因为TriggerAlarmAsync是异步执行（非UI线程），必须切回UI线程修改UI集合
            // 切换到UI线程更新集合
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 将数据库记录转换为UI模型
                var alarm = AlarmUiModel.FormRecord(record);

                // 更新UI集合：把新报警插入到列表开头（最新的在最上面）
                ActiveAlarms.Insert(0, alarm);

                // 更新报警数量：同步UI上的数字角标
                ActiveAlarmCount = ActiveAlarms.Count;

                // 日志：记录UI更新完成
                LogService.Error($"新报警：{alarm.Code}-{alarm.Title}");
            }
            );
        }

        /// <summary>
        /// 刷新活动报警命令（绑定到UI的刷新按钮）
        /// [RelayCommand]自动生成ICommand接口的RefreshCommand属性，供UI绑定
        /// </summary>
        /// <returns>异步任务</returns>
        [RelayCommand]
        private async Task RefreshAsync()
        {
            // 调用加载方法，重新获取最新的活动报警
            await loadActiveAlarmsAsync();
        }

        /// <summary>
        /// 加载历史报警命令（绑定到UI的历史查询按钮）
        /// </summary>
        /// <returns>异步任务</returns>
        [RelayCommand]
        private async Task LoadHistroyAlarmsAsync()
        {
            // 查询按钮逻辑
            // 调用服务层方法，分页查询历史报警（第1页，每页20条，按时间范围过滤）
            // 参数5：severity（报警级别）
            var records = await AlarmService.GetAlarmHistroyAsync(1, 20, HistoryStartDate, HistoryEndDate.AddDays(1), AlarmServerity.All);

            // 切换到UI线程更新历史报警集合
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 清空原有历史数据
                HistroyAlarms.Clear();

                // 遍历查询结果，转换为UI模型并添加到集合
                foreach (var item in records.Item)
                {
                    HistroyAlarms.Add(AlarmUiModel.FormRecord(item));
                }
            }
            );
        }

        /// <summary>
        /// 确认/复位报警命令（绑定到UI的确认按钮）
        /// </summary>
        /// <param name="alarm">当前要确认的报警UI模型（通过CommandParameter传递）</param>
        /// <returns>异步任务</returns>
        [RelayCommand]
        private async Task AcknowledgeAlarmAsync(AlarmUiModel alarm)
        {
            // 空值校验：避免空指针异常
            if (alarm == null) return;

            try
            {
                // 调用服务层方法，更新数据库的报警确认状态
                //alarm.Id表示要确认的报警的唯一 ID（数据库里的自增主键）
                //" "表示确认人名称（operatorName）
                var success = await AlarmService.AcknowledgeAlarmAsync(alarm.Id, "");

                // 如果确认成功，更新UI
                if (success)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 从活动报警列表移除该报警
                        ActiveAlarms.Remove(alarm);

                        // 更新活动报警数量
                        ActiveAlarmCount = ActiveAlarms.Count;
                    });

                    // 记录确认成功日志
                    LogService.Info($"确认报警成功{alarm.Code}");
                }
            }
            catch (Exception ex)
            {
                // 捕获异常并记录日志
                LogService.Error($"确认报警异常{alarm.Code}", ex);
            }
        }
    }
}