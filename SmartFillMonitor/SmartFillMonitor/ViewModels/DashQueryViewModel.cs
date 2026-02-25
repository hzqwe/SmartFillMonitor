using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartFillMonitor.Models;
using SmartFillMonitor.Services;

namespace SmartFillMonitor.ViewModels
{
   public partial class DashQueryViewModel:ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<ProductionRecord> records = new();

        [ObservableProperty]
        private ProductionRecord? selectedRecord;

        [ObservableProperty]
        private DateTime? startDate = DateTime.Today.AddDays(-7);

        [ObservableProperty]
        private DateTime? endDate = DateTime.Today;

        public DashQueryViewModel()
        {
            
        }

        /// <summary>
        /// 查询按钮对应的命令方法：根据选择的开始/结束时间范围，查询生产记录数据并展示在DataGrid中
        /// 1. 处理时间参数空值（默认查近7天数据）
        /// 2. 修正结束时间为"结束日期+1天-1微秒"，确保包含结束日期当天所有数据
        /// 3. 从数据库查询符合时间范围的生产记录，按时间倒序加载到Records集合（绑定DataGrid展示）
        /// </summary>
        [RelayCommand]
        private async Task QueryAsync()
        {
            var start = StartDate ?? DateTime.Today.AddDays(-7);
            var end = EndDate ?? DateTime.Today;
            var endInclusive=end.AddDays(1).AddMicroseconds(-1);
            var list = await DataService.QueryRecordAsync(start, endInclusive);

            Records.Clear();
            //list.OrderByDescending(r => r.Time) 里的 r → 代表 list 集合里的每一个 ProductionRecord 对象（也就是每一条生产记录）。
            foreach (var r in list.OrderByDescending(r=>r.Time))
            {
                Records.Add(r);
            }
        }

        /// <summary>
        /// 导出CSV按钮对应的命令方法：将当前DataGrid中展示的生产记录导出为CSV文件
        /// 1. 校验Records集合是否有数据，无数据则直接返回
        /// 2. 弹出文件保存对话框，让用户选择保存路径和确认文件名（默认含时间戳避免重复）
        /// 3. 调用DataService将Records数据写入指定路径的CSV文件，异常时记录错误日志
        /// </summary>
        [RelayCommand]
        private async Task ExportAsync()
        {
            if (Records == null || Records.Count == 0) return;

            var dlg = new SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                FileName = $"ProductionRecords_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            //dlg.ShowDialog()：弹出 Windows 系统的 “另存为” 对话框（让用户选保存路径、确认文件名）；
            //!= true：判断用户是否取消了保存操作（比如点了对话框的「取消」按钮、关闭了对话框）；
            if (dlg.ShowDialog()!=true) return;
            try
            {
                //把内存中的生产记录数据，持久化成用户指定路径的 CSV 文件，完成导出功能。
                await DataService.ExportToCsvAsync(Records.ToList(), dlg.FileName);
            }
            catch (Exception ex)
            {
                LogService.Error($"导出生产记录失败", ex);
            }
        }
    }
}
 