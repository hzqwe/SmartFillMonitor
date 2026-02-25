using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartFillMonitor.Services
{
   public static class DbProvider
    {
        private static readonly object _lock = new object();//确保线程安全,加锁可以保证同一时间只有一个线程执行锁内的代码，避免重复初始化。

        //{ get;private set; }表示读取权限是公开的,修改权限是私有的
        public static IFreeSql Fsql { get;private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="connectionString">数据库连接字符串</param>
        /// <param name="data">数据库类型</param>
        public static void Initalize(string connectionString,FreeSql.DataType data=FreeSql.DataType.Sqlite)
        {
            if (Fsql != null) return;//已经初始化过了
            lock (_lock)
            {
                if (Fsql != null) return;

                if (Fsql == null)//双重检查锁定
                {
                    Fsql = new FreeSql.FreeSqlBuilder()
                        .UseConnectionString(FreeSql.DataType.Sqlite, connectionString)// 指定数据库类型和连接字符串
                        .UseAdoConnectionPool(true) // 启用数据库连接池（复用连接，减少创建/销毁连接的开销，提升性能）
                        // SQL 执行监控方法
                        .UseMonitorCommand(
                            cmd =>
                            {
                               //sql执行前
                            },
                            (cmd, traceLog) =>
                            {
                                // SQL执行后的回调：打印执行的SQL语句和日志，方便调试
                                Console.WriteLine($"[SQL]: {cmd.CommandText}\r\n->{traceLog}");
                            })//监视SQl命令执行
                        .UseAutoSyncStructure(true) // 自动同步实体类和数据库表结构（比如新增实体字段，会自动在数据库表中加列，开发阶段很方便）
                        .UseLazyLoading(true) // 启用延迟加载（访问实体的导航属性时，自动从数据库加载关联数据，比如 User.Order 会自动查订单表）
                        .Build();// 构建并返回 IFreeSql 实例
                }
            }
        }
    }
}
