using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FreeSql.DataAnnotations;

namespace SmartFillMonitor.Models
{
    [Table(Name = "Users")]
    //给 UserName 加「唯一索引」后，数据库会强制保证：
    //用户名不能重复：插入 / 修改数据时，如果有两条记录的 UserName 相同（比如都叫 admin），数据库会直接报错，拒绝操作；
    //[Index(...)] FreeSql 的特性，告诉数据库：要给当前表创建一个索引
    //"idx_unique_username"	索引的名称（自定义，见名知意即可）：idx= 索引，unique= 唯一，username= 用户名
    //"UserName"	要创建索引的字段名：给 Users 表的 UserName 字段加索引
    //true	索引的唯一性标记：true= 唯一索引，false= 普通索引
    [Index("idx_unique_username","UserName",true)]
    public class User
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        [Column(StringLength = 50, IsIdentity = false)]
        public string UserName { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        [Column(StringLength = 50)]
        public string DisplayName { get; set; }

        /// <summary>
        /// 存储哈希后的密码
        /// </summary>
        [Column(StringLength = 128,IsNullable =false)]
        public string PasswordHash { get; set; }

        [Column(MapType =typeof(int))]
        public Role Role { get; set; }

        //是否禁用
        public bool IsDisabled { get; set; } = false;

        public DateTime CreatedAt { get; set; } =DateTime.Now;

        public DateTime? LastLoginTime { get; set; }

        //方便UI绑定，不映射到数据库
        [Column(IsIgnore = true)]

        //public string RoleName => ...，这是「计算属性」它的值不是手动赋值的，而是 “算出来的”。
        //只能读不能改
        //_ 是 “默认值” 
        public string RoleName => Role switch
        {
            Role.admin => "管理员",
            Role.Engineer => "工程师",
            Role.Operator => "操作员",
            _ => "未知",
        };
    }
    public enum Role
    {
        [Description("管理员")]
        admin = 0,
        [Description("工程师")]
        Engineer = 1,
        [Description("操作员")]
        Operator = 2,
    }
}
