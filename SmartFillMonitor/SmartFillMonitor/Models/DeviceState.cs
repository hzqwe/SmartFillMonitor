using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartFillMonitor.Models
{
    public class DeviceState
    {
        /// <summary>
        /// 用于描述设备数据状态
        /// </summary>
        public int ActualCount { get; set; }//当前产量

        public int TargetCount { get; set; }//总产量

        public double CurrentTemp { get; set; }//实时温度

        public double SettingTemp { get; set; }//设定温度

        public double RunningTime { get; set; }//运行时间
        //
        public string? DeviceStatus { get; set; }

        public double CurrentCycleTime { get; set; }//当前节拍

        public double LiquidLevel { get; set; }//当前液位

        public double StandardCycleTime { get; set; }//总节拍

        public bool ValueOpen { get; set; } //当前阀门状态

        public string BarCode { get; set; } =string.Empty;//二维码
    }
}
