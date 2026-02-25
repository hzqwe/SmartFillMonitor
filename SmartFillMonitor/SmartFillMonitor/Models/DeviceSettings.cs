using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartFillMonitor.Models
{
   public class DeviceSettings
    {
        //串口选项
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 115200;
        public int DataBits { get; set; } = 8;
        public string Parity { get; set; } = "None";
        public string StopBits { get; set; } = "One";

        //系统选项
        public bool AutoConnect { get; set; } = true;
        public bool AlarmSound { get; set; } = true;
        public bool DebugLogMode { get; set; } = false;
    }
}
