using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FreeSql.DataAnnotations;


namespace SmartFillMonitor.Models
{
    [Table(Name ="ProductionRecords")]
    public class ProductionRecord
    {
        //Id
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }
        //时间戳

        public DateTime Time { get; set; } = DateTime.Now;

        //批次号
        [Column(StringLength =50)]

        public string? BatchNo { get; set; }

        public double SettingTemp { get; set; }//设定温度

        public double ActualTemp { get; set; }//实际温度

        public double FillWeight { get; set; }//灌装重量/容量

        public int ActualCount { get; set; }//实际产量

        public int TargetCount { get; set; }//目标产量

        public bool IsNG { get; set; }//是否NG

        //NG原因
        [Column(StringLength = 100)]
        public string? NgReason { get; set; }

        public double CycleTime { get; set; }//这一产品花费时间

        //操作员
        [Column(StringLength = 100)]
        public string? Operator { get; set; }

    }
}
