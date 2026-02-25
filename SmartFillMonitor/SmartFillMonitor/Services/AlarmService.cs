using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SmartFillMonitor.Models;

namespace SmartFillMonitor.Services
{
    /// <summary>
    /// 报警业务核心服务类
    /// 职责：统一管理系统/设备的报警生命周期（触发、恢复、确认），提供报警数据的CRUD操作
    /// 设计：基于事件发布-订阅模式，解耦报警触发/恢复与UI更新逻辑
    /// </summary>
    public class AlarmService
    {
        /// <summary>
        /// 报警触发事件（发布-订阅模式）
        /// 订阅场景：UI层订阅该事件，收到通知后更新活动报警列表、播放报警提示音、弹窗提醒等
        /// 事件参数：触发的报警记录（AlarmRecord）
        /// </summary>
        public static event EventHandler<AlarmRecord>? AlarmTriggered;

        /// <summary>
        /// 报警恢复事件（发布-订阅模式）
        /// 订阅场景：UI层订阅该事件，收到通知后移除活动报警、关闭报警红灯、停止提示音等
        /// 事件参数：恢复的报警记录（AlarmRecord）
        /// </summary>
        public static event EventHandler<AlarmRecord>? AlarmRecovered;

        /// <summary>
        /// 触发新报警（核心方法）
        /// 业务逻辑：防重复触发 → 插入数据库 → 获取真实ID → 发布报警触发事件
        /// </summary>
        /// <param name="alarmRecord">待触发的报警记录（需包含报警码、消息、级别等核心信息）</param>
        /// <returns>异步任务</returns>
        public static async Task TriggerAlarmAsync(AlarmRecord alarmRecord)
        {
            try
            {
                // 防重复触发校验：同一报警码的活跃报警已存在时，直接返回（避免重复插入）
                //AnyAsync() 翻译过来就是「异步判断是否存在」
                bool isAlreadyActive = await DbProvider.Fsql.Select<AlarmRecord>()
                .Where(a => a.AlarmCode == alarmRecord.AlarmCode && a.IsActive).AnyAsync();
                if (isAlreadyActive) return;//已经存在报警了，直接返回             

                // 插入数据库（异步IO操作，执行在线程池线程，非UI线程），执行后生成数据库自增ID
                await DbProvider.Fsql.Insert(alarmRecord).ExecuteAffrowsAsync();

                // 查询数据库中该报警码的最新记录（目的：获取数据库生成的真实自增ID）
                //这段代码的作用是 “按 AlarmCode 找回刚插入的记录”，而这条记录里已经包含了数据库生成的 Id；
                var latesetRecord = await DbProvider.Fsql.Select<AlarmRecord>()
                    .Where(a => a.AlarmCode == alarmRecord.AlarmCode).FirstAsync();
                if (latesetRecord != null)
                {
                    // 替换为带真实ID的完整记录（原alarmRecord的ID为默认值0，需替换）
                    alarmRecord = latesetRecord;
                }

                // 记录报警触发日志（包含报警码和具体消息）
                LogService.Warn($"[报警触发]{alarmRecord.AlarmCode}:{alarmRecord.Message}");

                // 发布报警触发事件：通知所有订阅者（如UI层）有新报警产生
                // 事件回调会在非UI线程执行，订阅方需自行切换到UI线程更新界面
                AlarmTriggered?.Invoke(null, alarmRecord);
            }
            catch (Exception ex)
            {
                // 捕获并记录触发报警过程中的异常（含报警码，便于定位问题）
                LogService.Error($"触发报警异常{alarmRecord.AlarmCode}", ex);
            }
        }

        /// <summary>
        /// 恢复报警（设备故障解除时调用）
        /// 业务逻辑：查询活跃报警 → 标记为非活动 → 更新结束时间/持续时长 → 发布报警恢复事件
        /// </summary>
        /// <param name="alarmCode">待恢复的报警码</param>
        /// <returns>异步任务</returns>
        public static async Task RecoverAlarmAsync(AlarmCode alarmCode)
        {
            try
            {
                // 查询该报警码对应的活跃报警记录（无活跃报警则直接返回）
                var activeAlarm = await DbProvider.Fsql.Select<AlarmRecord>()
                .Where(a => a.AlarmCode == alarmCode && a.IsActive).FirstAsync();
                if (activeAlarm == null) return;//没有报警，忽略 

                // 更新报警状态：标记为非活动、记录结束时间、计算持续时长（秒）
                activeAlarm.IsActive = false;//非活动状态，从活跃列表移除
                activeAlarm.EndTime = DateTime.Now;//报警结束时间
                activeAlarm.DurationSeconds = (activeAlarm.EndTime - activeAlarm.StartTime).TotalSeconds;//报警持续时长

                // 只更新指定字段（IsActive/EndTime/DurationSeconds），避免全字段更新
                await DbProvider.Fsql.Update<AlarmRecord>()// 1. 初始化更新操作
                    .SetSource(activeAlarm)  // 2. 指定更新的数据源对象
                    .UpdateColumns(a => new { a.IsActive, a.EndTime, a.DurationSeconds }).ExecuteAffrowsAsync();//限定要更新的字段，ExecuteAffrowsAsync()表示执行更新并返回受影响行数

                // 记录报警恢复日志
                LogService.Info($"[报警恢复]{activeAlarm.AlarmCode}");

                // 发布报警恢复事件：通知订阅者（如UI层）该报警已恢复
                AlarmRecovered?.Invoke(null, activeAlarm);
            }
            catch (Exception ex)
            {
                // 捕获并记录恢复报警过程中的异常
                LogService.Error($"恢复报警异常{alarmCode}", ex);
            }
        }

        /// <summary>
        /// 人工确认报警（用户点击确认/复位按钮时调用）
        /// 业务逻辑：更新报警确认状态 → 记录确认人/确认时间 → 返回是否确认成功
        /// 防重复确认：仅当报警未确认时才执行更新
        /// </summary>
        /// <param name="alarmid">待确认的报警ID（数据库自增主键）</param>
        /// <param name="operatorName">确认人名称（登录用户名）</param>
        /// <returns>确认结果：true=成功，false=失败（如已确认/ID不存在）</returns>
        public static async Task<bool> AcknowledgeAlarmAsync(long alarmid, string operatorName)
        {
            try
            {
                // 更新报警确认状态：标记为已确认、记录确认时间、记录确认人
                // Where条件：仅更新指定ID且未确认的报警（防止重复确认）
                var result = await DbProvider.Fsql.Update<AlarmRecord>()
                    .Set(a => a.IsAcKnowledged, true)//标记为已确认
                    .Set(a => a.AckTime, DateTime.Now)//确认时间
                    .Set(a => a.Ackuser, operatorName)//确认人
                    .Where(a => a.Id == alarmid && !a.IsAcKnowledged)//防止重复确认
                    .ExecuteAffrowsAsync();//返回受影响行数

                // 受影响行数>0表示更新成功
                if (result > 0)
                {
                    LogService.Info($"报警已确认：ID{alarmid} by {operatorName}");
                    return true;
                }

                // 受影响行数=0表示更新失败（如已确认/ID不存在）
                return false;
            }
            catch (Exception ex)
            {
                // 捕获并记录确认报警过程中的异常，返回失败
                LogService.Error($"确认报警异常,ID={alarmid}", ex);
                return false;
            }
        }

        /// <summary>
        /// 获取当前活跃报警列表
        /// 筛选条件：IsActive=true（活跃报警）
        /// 排序规则：按触发时间降序（最新触发的报警排在前面）
        /// </summary>
        /// <returns>活跃报警记录列表</returns>
        public static async Task<List<AlarmRecord>> GetActiveAlarmsAsync()
        {
            return await DbProvider.Fsql.Select<AlarmRecord>()
                .Where(a => a.IsActive)//仅查询活跃报警
                .OrderByDescending(a => a.StartTime)//按触发时间降序排列
                .ToListAsync();
        }

        /// <summary>
        /// 分页查询历史报警（支持多条件过滤）
        /// 过滤条件：时间范围、报警级别
        /// 排序规则：按触发时间降序
        /// 返回值：当前页记录列表 + 符合条件的总记录数（用于分页控件显示）
        /// </summary>
        /// <param name="pageIndex">页码（从1开始）</param>
        /// <param name="pageSize">每页显示条数</param>
        /// <param name="startTime">查询开始时间（null=不限制）</param>
        /// <param name="endTime">查询结束时间（null=不限制）</param>
        /// <param name="alarmServerity">报警级别（All=不限制）</param>
        /// <returns>元组：当前页记录列表 + 总记录数</returns>
        public static async Task<(List<AlarmRecord> Item, long Total)> GetAlarmHistroyAsync(int pageIndex, int pageSize, DateTime? startTime = null, DateTime? endTime = null, AlarmServerity alarmServerity = AlarmServerity.All)
        {
            try
            {
                // 初始化基础查询
                var query = DbProvider.Fsql.Select<AlarmRecord>();

                // 时间过滤：开始时间（>=）
                if (startTime.HasValue)
                {
                    query = query.Where(w => w.StartTime >= startTime.Value);
                }

                // 时间过滤：结束时间（<=）
                if (endTime.HasValue)
                {
                    query = query.Where(w => w.StartTime <= endTime.Value);
                }

                // 级别过滤：仅查询>=指定级别的报警，All为0
                if (alarmServerity != AlarmServerity.All)
                {
                    query = query.Where(w => w.AlarmServerity >= alarmServerity);
                }

                // 查询符合条件的总记录数（用于分页）
                var total = await query.CountAsync();

                // 分页查询：按触发时间降序，跳过前(pageIndex-1)*pageSize条，取pageSize条
                var list = await query
                    .OrderByDescending(a => a.StartTime)
                    .Page(pageIndex, pageSize)
                    .ToListAsync();

                // 返回当前页数据和总记录数
                return (list, total);
            }
            catch (Exception ex)
            {
                // 异常时返回空列表+0条总数，避免上层崩溃
                LogService.Error("查询历史报警失败", ex);
                // 返回 “空报警列表 + 0 总条数” 的元组
                //作用：异常兜底，避免程序崩溃，保证上层代码能安全处理 “查询失败” 的情况
                return (new List<AlarmRecord>(), 0);
            }
        }
    }
}