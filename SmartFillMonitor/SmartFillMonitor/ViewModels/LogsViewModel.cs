using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel; // MVVM工具包：实现属性通知（ObservableProperty）
using CommunityToolkit.Mvvm.Input; // MVVM工具包：实现命令绑定（RelayCommand）
using FreeSql; // FreeSql ORM框架：操作数据库
using SmartFillMonitor.Models; // 实体模型（SystemLog）
using SmartFillMonitor.Services; // 日志服务（LogService）

namespace SmartFillMonitor.ViewModels
{
    // ObservableObject：MVVM工具包基类，提供INotifyPropertyChanged，实现"属性变化→UI更新"
    public partial class LogsViewModel : ObservableObject
    {
        #region 1. 页面绑定的属性（UI ↔ 后台双向绑定）
        // ObservableProperty：自动生成带通知的属性（含Set方法、PropertyChanged事件）
        // 日志查询起始日期（默认2026-02-01，覆盖数据库所有历史日志）
        [ObservableProperty]
        private DateTime _startDate = new DateTime(2026, 2, 1);

        // 日志查询结束日期（默认"今天+1天-1秒"，即今天23:59:59，包含今天所有日志）
        [ObservableProperty]
        private DateTime _endDate = DateTime.Today.AddDays(1).AddSeconds(-1);

        // 当前选中的日志级别（默认All：不筛选级别）
        [ObservableProperty]
        private string _selectedLevel = "All";

        // 日志级别下拉框数据源（UI绑定的可选值）
        public ObservableCollection<string> LogLevels { get; } = new ObservableCollection<string>
        {
            "All",      // 全部级别
            "Debug",    // 调试日志
            "Information", // 信息日志
            "Warning",  // 警告日志
            "Error"     // 错误日志
        };

        // 搜索文本（按日志内容模糊查询）
        [ObservableProperty]
        private string _searchText = "";

        // 数据库操作忙碌状态（用于控制按钮禁用/加载动画）
        [ObservableProperty]
        private bool _isBusy;

        // 符合条件的日志总条数（分页用：显示"共X条"）
        [ObservableProperty]
        private int _totalCount;

        // 当前页码（默认第1页）
        [ObservableProperty]
        private int _pageIndex = 1;

        // 每页显示固定条数（常量：每页50条，避免魔法数字）
        private const int pageSize = 50;

        // 绑定到UI的日志列表（ObservableCollection：集合变化→UI自动更新）
        [ObservableProperty]
        private ObservableCollection<SystemLog> _logs = new ObservableCollection<SystemLog>();
        #endregion

        #region 2. 构造函数（ViewModel初始化时执行）
        public LogsViewModel()
        {
            // 异步加载日志（_= 表示"火并忘"，不阻塞构造函数）构造函数专属写法
            // 程序启动/ViewModel创建时，自动加载第一页日志
            _ = LoadLogsAsync();
        }
        #endregion

        #region 3. 命令（UI按钮绑定的操作）
        // RelayCommand：MVVM工具包命令，绑定到UI按钮的Command属性
        // 上一页命令
        [RelayCommand]
        private async Task PreviousPageAsync()
        {
            // 边界判断：已经是第1页，不执行
            if (PageIndex <= 1) return;
            PageIndex--; // 页码减1
            await LoadLogsAsync(); // 重新加载当前页数据
        }

        // 下一页命令
        [RelayCommand]
        private async Task NextPageAsync()
        {
            // 边界判断：当前页数据不足50条 → 已是最后一页，不执行
            // 假设数据库里总共有 68 条 日志：
            // 第 1 页：显示 50 条（Logs.Count = 50）→ 50 < 50？不成立 → 可以点 “下一页”；
            // 第 2 页：显示 18 条（Logs.Count = 18）→ 18 < 50？成立 → 直接 return，不让点 “下一页”（避免页码无限增加，查空数据）。
            if (Logs.Count < pageSize) return;
            PageIndex++; // 页码加1
            await LoadLogsAsync(); // 重新加载当前页数据
        }

        // 搜索/查询命令（点击查询按钮执行）
        [RelayCommand]
        private async Task SearchAsync()
        {
            PageIndex = 1; // 搜索时重置为第1页
            await LoadLogsAsync(); // 按新条件重新加载数据
        }

        // 导出日志命令（导出所有符合条件的日志为CSV文件）
        [RelayCommand]
        private async Task ExportAsync()
        {
            // 1. 构建查询条件（和加载日志用同一个查询逻辑，保证数据一致）
            var query = BuildQuery();
            // 2. 查询所有符合条件的日志（不分页，按时间倒序）
            var alldata = await query.OrderByDescending(x => x.Timestamp).ToListAsync();

            // 3. 无数据时提示
            if (alldata.Count == 0)
            {
                MessageBox.Show("没有数据可导出");
                return;
            }

            // 4. 构建CSV内容（CSV：逗号分隔值，Excel/记事本可打开）
            // 第一行：CSV表头
            var lines = new List<string> { "时间,等级,内容,异常" };
            // 后续行：日志数据（遍历所有日志，格式化每行内容）
            //如果你直接写：我想吃"苹果" → 纸条会误以为 “我想吃” 是内容，后面的 "苹果" 是多余的；
            //你必须写：我想吃\"苹果\" → 告诉纸条：“这个 " 是内容的一部分，不是纸条的边缘”。

            //replace里面的引号表示将一个引号替换成两个引号，由于有多个引号，所以需要使用转义字符\来区分字符串中的引号和代码中的引号。也就是说"\""其实只有一个引号，而"\"\""表示两个引号
            lines.AddRange(alldata.Select(x => $"{x.Timestamp:yyyy-MM-dd HH:mm:ss},{x.Level},\"{x.RenderedMessage.Replace("\"", "\"\"")}\",\"{x.Exception?.Replace("\n", "")}\""));

            // 生成导出文件名称（含时间戳，避免文件名重复）
            var path = $"Logs_Export_{DateTime.Now:yyyyMMddHHmmss}.csv";
            // 将CSV内容写入文件（UTF8编码，避免中文乱码）
            await File.WriteAllLinesAsync(path, lines, Encoding.UTF8);
            // 弹出提示框，告知用户导出成功并显示文件完整路径
            MessageBox.Show($"日志已导出到文件：{Path.GetFullPath(path)}");
        }
        #endregion

        #region 4. 核心方法：加载日志数据（分页+筛选）
        // 异步加载日志（async/await：不阻塞UI线程）
        private async Task LoadLogsAsync()
        {
            // 防重复点击：如果正在加载，直接返回
            if (IsBusy) return;
            IsBusy = true; // 标记为忙碌（UI可显示加载动画/禁用按钮）

            try
            {
                // 1. 构建查询条件（调用BuildQuery，拼接筛选条件）
                ISelect<SystemLog> query = BuildQuery();
                // 2. 查询符合条件的总条数（用于分页计算）
                TotalCount = (int)await query.CountAsync();
                // 3. 分页查询数据：按时间倒序 → 取第PageIndex页，每页pageSize条
                var data = await query.OrderByDescending(x => x.Timestamp)
                    .Page(PageIndex, pageSize) // FreeSql分页方法：Page(页码, 每页条数)
                    .ToListAsync();
                // 4. 更新UI绑定的日志列表（ObservableCollection自动通知UI更新）
                Logs = new ObservableCollection<SystemLog>(data);
            }
            catch (Exception ex)
            {
                // 异常处理：记录错误日志，方便排查
                LogService.Error("加载日志失败", ex);
            }
            finally
            {
                IsBusy = false; // 无论成功/失败，都标记为非忙碌
            }
        }
        #endregion

        #region 5. 辅助方法：构建查询条件（拼接筛选逻辑）
        // 构建FreeSql查询对象（拼接所有筛选条件：日期+级别+搜索文本）
        private FreeSql.ISelect<SystemLog> BuildQuery()
        {
            // 1. 创建基础查询：查询SystemLog表
            ISelect<SystemLog> query = DbProvider.Fsql.Select<SystemLog>();

            // 2. 日期筛选：只按日期（yyyy-MM-dd）筛选，忽略时间
            string start = StartDate.ToString("yyyy-MM-dd");
            string end = EndDate.ToString("yyyy-MM-dd");
            // SQLite函数：date(Timestamp) → 提取时间戳的日期部分
            // 注意：双引号包裹字段名，And前加空格（避免SQL语法错误）
            //这里的转义字符跟上面的csv同一个逻辑
            query = query.Where($"date(\"Timestamp\")>=date('{start}') And date(\"Timestamp\")<=date('{end}')");

            // 3. 搜索文本筛选：非空时，按日志内容模糊匹配
            if (!string.IsNullOrEmpty(SearchText))
            {
                query = query.Where(x => x.RenderedMessage.Contains(SearchText));
            }

            // 4. 日志级别筛选：不是All时，按级别模糊匹配（可改为==严格匹配）
            if (SelectedLevel != "All" && !string.IsNullOrEmpty(SelectedLevel))
            {
                query = query.Where(x => x.Level.Contains(SelectedLevel));
            }

            // 返回拼接好条件的查询对象
            return query;
        }
        #endregion
    }
}