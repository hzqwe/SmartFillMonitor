// 引入基础系统库
using System;
using System.Collections.Generic;
using System.IO;          // 文件操作核心命名空间（File/Path等）
using System.Linq;
using System.Text;
using System.Text.Json;  // JSON序列化/反序列化命名空间
using System.Threading.Tasks; // 异步编程命名空间
using System.Windows;
using System.Windows.Controls;
using SmartFillMonitor.Models; // 引入配置模型类

namespace SmartFillMonitor.Services
{
    // 【核心作用】配置文件的读取、保存、备份管理类（工具类）
    // 所有文件IO、JSON处理逻辑都封装在这里，对外提供简洁的调用方法
    public class ConfigServices
    {
        // 配置文件名（固定）：最终生成的配置文件名为device-settings.json
        private const string SettingFileName = "device-settings.json";

        // IO锁：SemaphoreSlim(1,1) 表示"同时只允许1个线程访问文件"，解决多线程读写冲突
        // 第一个1=初始可用数，第二个1=最大可用数 → 本质是"互斥锁"
        private static readonly SemaphoreSlim IOLock = new SemaphoreSlim(1, 1);

        // 【工具方法】获取配置文件的完整路径
        // AppContext.BaseDirectory = 程序运行目录（比如bin/Debug/net6.0/）
        // Path.Combine = 拼接路径（自动处理斜杠，跨系统兼容）
        // SettingFileName：配置文件的文件名
        public static string GetSettingFilePath() => Path.Combine(AppContext.BaseDirectory, SettingFileName);

        // 【核心方法】异步加载配置文件
        // 返回值：DeviceSettings（配置模型对象），async Task<T> = 异步有返回值方法
        public static async Task<DeviceSettings> LoadSettingsAsync()
        {
            // 获取配置文件完整路径
            var path = GetSettingFilePath();
            // 初始化配置对象（默认null）
            DeviceSettings? settings = null;

            // 等待IO锁：进入"临界区"，确保当前只有一个线程操作文件
            // 如果其他线程正在读写文件，当前线程会等待，直到锁被释放
            //防止 “同时读写文件”（比如你快速点两次保存按钮）
            await IOLock.WaitAsync();
            
            try
            {
                // 第一步：判断配置文件是否存在
                if (File.Exists(path))
                {
                    try
                    {
                        // 异步读取文件所有文本内容（避免阻塞UI线程）
                        var json = await File.ReadAllTextAsync(path);

                        //序列化：对象 → 文本（为了保存 / 传输）；
                        //反序列化：文本 → 对象（为了程序使用）；

                        // JSON反序列化配置：PropertyNameCaseInsensitive=true → 属性名不区分大小写
                        // 比如JSON里的"portname"也能匹配模型里的"PortName"，提高兼容性
                        var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        // 把JSON字符串转成DeviceSettings对象（反序列化核心）
                        settings = JsonSerializer.Deserialize<DeviceSettings>(json, opt);
                         
                        // 如果反序列化成功（非null），直接返回配置对象
                        if (settings != null)
                        {
                            LogService.Info($"配置文件加载成功{path}");
                            return settings;
                        }
                    }
                    // 捕获JSON格式错误（比如手动改了配置文件导致格式乱了）
                    catch (JsonException jsonEx)
                    {
                        LogService.Error($"配置文件格式错误，将其设置为默认值：{jsonEx.Message}");
                        // 备份损坏的配置文件（避免数据丢失，方便排查问题）
                        BackCorruptFile(path);
                    }
                    // 捕获其他未知异常（比如文件权限不足）
                    catch (Exception ex)
                    {
                        // 此处可扩展：记录日志（比如Log.Error(ex, "加载配置失败")）
                        LogService.Error($"读取配置文件失败：{ex.Message}");
                    }
                }
                else
                {
                    // 文件不存在 → 后续会创建默认配置
                    LogService.Warn($"配置文件不存在{path}，将创建默认配置");
                }
            }
            finally
            {
                // 释放IO锁：无论是否报错，最终都会释放锁，避免死锁
                // finally块的特性：try里的代码无论是否抛出异常，finally都会执行
                IOLock.Release();
            }

            // 兜底逻辑：如果配置加载失败（文件不存在/反序列化失败）
            if (settings == null)
            {
                // 创建默认配置对象（使用DeviceSettings里的默认值：COM1、115200等）
                settings = new DeviceSettings();
                // 把默认配置保存到文件（首次启动程序时会执行）
                await SaveDeviceSettingsAsync(settings);
            }

            // 返回最终的配置对象（要么是加载的，要么是默认的）
            return settings;
        }

        // 【核心方法】异步保存配置文件
        // 参数：要保存的配置对象，返回值：bool（保存成功/失败）
        public static async Task<bool> SaveDeviceSettingsAsync(DeviceSettings settings)
        {
            // 入参校验：配置对象为null → 直接返回失败
            if (settings == null)
            {
                return false;
            }

            // 获取配置文件路径
            var path = GetSettingFilePath();
            // 临时文件路径：先写临时文件，再替换原文件（避免写文件时崩溃导致原文件损坏）
            var tempPath = path + ".tmp";

            // 加IO锁：确保单线程写文件
            // 加锁防止多线程同时写文件
            await IOLock.WaitAsync();
            try
            {
                // JSON序列化：把配置对象转成JSON字符串
                // WriteIndented=true → 格式化输出（JSON有换行和缩进，方便人工查看）
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

                // 第一步：写入临时文件（避免直接覆盖原文件）
                await File.WriteAllTextAsync(tempPath, json);

                // 第二步：原子性替换（操作系统级别的移动操作，保证完整性）
                // true → 如果目标文件已存在，直接覆盖
                //三个参数分别为：源文件路径（要移动/重命名的文件），目标文件路径（移动/重命名后的路径），是否覆盖已存在的目标文件
                File.Move(tempPath, path, true);

                // 保存成功 → 返回true
                LogService.Info("配置文件保存成功");
                return true;
            }
            catch (Exception ex)
            {
                // 保存失败 → 返回false（可扩展：记录异常日志）
                LogService.Error($"配置文件保存失败：{ex.Message}");
                return false;
            }
            finally
            {
                // 释放IO锁
                IOLock.Release();

                // 清理临时文件：无论保存成功/失败，都删除临时文件，避免残留
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception)
                    {
                        // 临时文件删除失败不影响主流程，忽略即可
                    }
                }
            }
        }

        // 【辅助方法】备份损坏的配置文件
        // 参数：原文件路径，作用：给损坏的文件加时间戳备份，方便排查问题
        public static void BackCorruptFile(string originalPath)
        {
            try
            {
                // 生成备份路径：原路径 + .corrupt + 时间戳（比如device-settings.json.corrupt20260205123000）
                // 时间戳格式：年-月-日-时-分-秒，避免备份文件重名
                var backupPath = originalPath + ".corrupt" + DateTime.Now.ToString("yyyyMMddHHmmss");

                // 拷贝原文件到备份路径，true=覆盖同名备份文件
                File.Copy(originalPath, backupPath, true);
                LogService.Warn($"已备份损坏的配置文件到：{backupPath}");
            }
            catch (Exception)
            {
                // 备份失败则抛出异常（上层可捕获）
                throw;
            }
        }
    }
}