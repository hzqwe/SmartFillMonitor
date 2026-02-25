using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FreeSql.DataAnnotations;

namespace SmartFillMonitor.Models
{
    [Table(Name ="SystemLog",DisableSyncStructure =true)]
   public class SystemLog
    {
        //IsPrimary = true：标记为主键,IsIdentity = true：标记为自增列
        [Column(Name ="Id",IsPrimary =true,IsIdentity =true)]
        public int Id { get; set; }

        [Column(Name = "Timestamp")]
        public DateTime Timestamp { get; set; }

        [Column(Name = "Level",StringLength =50)]
        public string Level { get; set; }

        [Column(Name = "Exception", StringLength = 1000)]
        public string Exception { get; set; }

        [Column(Name = "RenderedMessage", StringLength = 50)]
        public string RenderedMessage { get; set; }

        [Column(Name = "Properties", StringLength = 1000)]
        public string Properties { get; set; }
    }
}
