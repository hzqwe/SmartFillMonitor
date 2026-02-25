using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace SmartFillMonitor.Services
{
    public static class LogService
    {
        //以下是一些日志级别
        public static void Info(string message)=>Log.Information(message);

        public static void Warn(string message) => Log.Warning(message);

        public static void Debug(string message) => Log.Debug(message);

        //Verbose是 Serilog 的最低级日志级别（比 Debug 还低），用于记录 “最详细的调试信息”
        public static void Verbose(string message) => Log.Verbose(message);

        public static void Fatal(string message) => Log.Fatal(message);

        public static void Fatal(string message,Exception ex=null) => Log.Fatal(ex,message);

        public static void Error(string message,Exception ex=null) => Log.Error(ex,message);
    }
}
