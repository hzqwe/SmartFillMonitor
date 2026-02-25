using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SmartFillMonitor.Models;
using CsvHelper;
using System.IO;
using System.Globalization;

namespace SmartFillMonitor.Services
{
    public static class DataService
    {
        public static async Task SaveProductionRecordAsync(ProductionRecord record)
        {
            await DbProvider.Fsql.Insert(record).ExecuteAffrowsAsync();
        }

        public static async Task<List<ProductionRecord>> QueryRecordAsync(DateTime start,DateTime end)
        {
            return await DbProvider.Fsql.Select<ProductionRecord>()
                .Where(r=>r.Time>=start&&r.Time<=end)
                .ToListAsync();
        }

        public static async Task ExportToCsvAsync(List<ProductionRecord> records,string filePath)
        {
            //打开 / 创建你指定路径（filePath）的 CSV 文件
            await using var writer=new StreamWriter(filePath);
            //CultureInfo.InvariantCulture：强制用 “通用格式”（比如小数点是.，日期格式统一），避免不同系统（中文 / 英文）导出的 CSV 格式乱码。
            await using var csv = new CsvWriter(writer,CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(records);
        }
    }
}
