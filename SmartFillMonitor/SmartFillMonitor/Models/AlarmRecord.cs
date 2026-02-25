using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using FreeSql.DataAnnotations;
using HandyControl.Controls;

namespace SmartFillMonitor.Models
{
    #region 数据库实体类型
    [Table(Name = "AlarmRecord")]
    public class AlarmRecord
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }
        //报警类型
        public AlarmCode AlarmCode { get; set; }
        //报警级别
        public AlarmServerity AlarmServerity { get; set; }
        //报警开始时间
        public DateTime StartTime { get; set; } = DateTime.Now;
        //报警恢复（设备解除故障）时间
        public DateTime EndTime { get; set; }
        //报警持续时间，单位秒
        public double? DurationSeconds { get; set; }
        //是否为活动报警（True代表故障，False代表已恢复）
        public bool IsActive { get; set; }
        //是否被人工确认
        public bool IsAcKnowledged { get; set; }
        //几点确认的？
        public DateTime? AckTime { get; set; }
        //确认操作人（记录用户名）
        public string? Ackuser { get; set; }
        //处理建议（通常从Enum获取）
        public string? Description { get; set; }
        //动态消息
        public string? Message { get; set; }


    }

    /// <summary>
    /// 报警级别
    /// </summary>
    public enum AlarmServerity
    {
        [Description("所有")]
        All = 0,
        [Description("提示")]
        Info = 1,
        [Description("警告")]
        Warning = 2,
        [Description("错误")]
        Error = 3,
        [Description("致命")]
        Critical = 4,

    }

    /// <summary>
    /// 报警类型
    /// </summary>
    public enum AlarmCode
    {
        [Description("无")]
        None = 0,
        [Description("原料桶液位过低")]
        LowLiquidLevel = 1,
        [Description("压缩空气压力偏低")]
        LowAirPresure = 2001,
        [Description("加热温度过高")]
        HighTemperature = 3001,
        [Description("PLC通信故障")]
        CommunicationError = 4001,
        [Description("系统内部错误")]
        SystemError = 5001,
    }
    #endregion

    #region
    //UI视图模型，用于显示在界面

    public class AlarmUiModel : INotifyPropertyChanged
    {
        private long _id;
        private string _code;
        private string _title;
        private string _timeStr;
        private string _description;

        public long Id
        {
            get => _id;
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged();
            }
        }

        public string Code
        {
            get => _code;
            set
            {
                if (_code == value) return;
                _code = value;
                OnPropertyChanged();
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title == value) return;
                _title = value;
                OnPropertyChanged();
            }
        }

        public string TimeStr
        {
            get => _timeStr;
            set
            {
                if (_timeStr == value) return;
                _timeStr = value;
                OnPropertyChanged();
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description == value) return;
                _description = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName]string? propertyName=null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static AlarmUiModel FormRecord(AlarmRecord record)
        {
            string title =record.AlarmCode.GetDescription(); 
            return new AlarmUiModel 
            {
                Id = record.Id,
                Code=$"E{(int)record.AlarmCode}",
                Title=title,
                Description=record.Description,
                TimeStr=record.StartTime.ToString("MM-dd HH:mm:ss"),
            };
        }

    }

    #endregion

    //以AlarmCode.HighTemperature为例，完整调用链路：
    //    1. record.AlarmCode = AlarmCode.HighTemperature（枚举值3001）
    //    2. 调用record.AlarmCode.GetDescription() → 进入你的扩展方法
    //    3. value = AlarmCode.HighTemperature → value.ToString() = "HighTemperature"
    //    4. GetField("HighTemperature") → 拿到这个枚举字段的元数据
    //    5. GetCustomAttribute → 找到字段上的[Description("加热温度过高")]
    //    6. attribute.Description = "加热温度过高" → 返回这个值
    //    7. FromRecord里的title = "加热温度过高" → 赋值给UI模型的Title属性

    public static class EnumExtensions
    {
        public static string GetDescription(this Enum value)
        {
            var filed=value.GetType().GetField(value.ToString());
            var attribute = Attribute.GetCustomAttribute(filed, typeof(DescriptionAttribute)) as DescriptionAttribute;
            return attribute?.Description??value.ToString();
        }
    }

}
