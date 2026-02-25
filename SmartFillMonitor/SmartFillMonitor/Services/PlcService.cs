using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation.Peers;
using Modbus.Device;
using SmartFillMonitor.Models;

namespace SmartFillMonitor.Services
{
    /// <summary>
    /// PLC通讯服务，用于Modbus RTU串口通信读取数据
    /// 功能包括：
    /// 1.管理串口，连接，断开等操作
    /// 2.周期性轮询PLC的数据并且通过事件公布
    /// 3.提供一些命令接口给上层使用
    /// </summary>
    public class PlcService
    {
        //串口对象
        private static SerialPort? _serialPort;

        //NModbus4提供的Modbus master接口，用于读取/写入寄存器
        //给 PLC 发 Modbus 指令（读 / 写寄存器）的 “通信代理”
        private static IModbusMaster? _modbusMaster;

        //取消令牌源，用于停止后台轮询任务
        private static CancellationTokenSource? _cts;

        //异步锁：确保同时只有一个读写操作正在进行（读和写只能存在其一），防止串口冲突
        private static readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

        //Modbus 从站Id,默认为1
        private const byte SlaveId = 1;

        /// <summary>
        /// 当读取到新的数据的时候，触发该事件，用于UI层显示
        /// </summary>
        public static event EventHandler<DeviceState>? DataReceived;

        /// <summary>
        /// 当连接状态发送变化的时候，触发该事件（true表示已经连上,false表示断开了）
        /// </summary>
        public static event EventHandler<bool> ConnectionChanged;

        //只读属性，表示当前是否已经连接上PLC
        public static bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        private static bool _hasEverConnected; // 是否曾成功通信过

        /// <summary>
        /// 根据传入的系统设置，建立串口连接对象并且自动连接
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static async Task Initialize(DeviceSettings settings)
        {
            //先断开旧连接，防止资源泄露或者重复打开串口
            await DisConnectAsync();

            // if (settings is { AutoConnect: true})这是 C# 8.0 的「模式匹配语法糖」，等价于：
            //if (settings != null && settings.AutoConnect == true)

            if (settings is { AutoConnect: true})
            {
                _serialPort = new SerialPort
                {
                    PortName=settings.PortName,
                    BaudRate=settings.BaudRate,
                    DataBits=settings.DataBits,
                    Parity=ParseParity(settings.Parity),//字符串转枚举
                    StopBits=ParseStops(settings.StopBits),
                };
                //尝试连接
                await ConnectAsync();
            }
        }
        private static Parity ParseParity(string s)=>Enum.TryParse<Parity>(s,true,out var p) ? p : Parity.None;

        private static StopBits ParseStops(string s) => Enum.TryParse<StopBits>(s, true, out var stops) ? stops : StopBits.One;

        /// <summary>
        /// 打开串口并创建Modbus RTU通信对象，同时启动PLC数据后台轮询
        /// 核心逻辑：
        /// 1. 前置校验：避免空串口/重复连接
        /// 2. 串口初始化：创建ModbusMaster并设置超时
        /// 3. 启动轮询：开启后台线程周期性读取PLC数据
        /// 4. 异常处理：连接失败时触发状态变更+记录日志
        /// </summary>
        /// <returns>异步任务（无返回值）</returns>
        public static async Task ConnectAsync()
        {
            // 前置校验1：串口对象未初始化，直接返回（避免空引用）
            if (_serialPort == null) return;
            // 前置校验2：串口已连接，直接返回（避免重复打开串口）
            if (IsConnected) return;

            try
            {
                // 打开串口（独占式资源，同一时间只能被一个进程占用）
                _serialPort.Open();

                // 创建Modbus RTU主站通信对象（基于已打开的串口）
                _modbusMaster = ModbusSerialMaster.CreateRtu(_serialPort);
                // 设置读超时时间：1秒内未读取到数据则判定为通信超时
                _modbusMaster.Transport.ReadTimeout = 1000;
                // 设置写超时时间：1秒内未完成指令写入则判定为通信超时
                _modbusMaster.Transport.WriteTimeout = 1000;

                // 触发连接状态变更事件：通知UI/上层逻辑“已连接”
                //Invoke是 “触发事件” 的方法：
                //-第一个参数null：事件发送者（这里是静态类（里面的成员有static修饰），没有实例，所以传 null）；
                //-第二个参数true：事件参数（表示 “连接成功”，false 表示 “连接失败 / 断开”）
                
                // 记录连接成功日志（包含串口号，方便调试）
                LogService.Info($"PLC连接成功{_serialPort.PortName}");

                // 创建取消令牌源：用于后续停止后台轮询任务
                _cts = new CancellationTokenSource();
                // 启动后台轮询任务（火并忘记模式）：周期性读取PLC数据
                //Task.Run 是.NET 提供的 **“开启后台线程的快捷方式”**
                // Task.Run：将轮询逻辑放到线程池线程执行，不阻塞当前主线程
                //（）为匿名方法，不需要传参数
                _ = Task.Run(() => PollDataLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                // 连接失败：触发状态变更事件，通知上层“已断开”
                ConnectionChanged?.Invoke(null, false);
                // 记录连接失败日志（含串口号+异常详情，方便定位问题）
                LogService.Error($"连接串口失败{_serialPort?.PortName}", ex);
            }

            // 占位：保持async Task方法签名一致性
            // 因方法内无真正的异步等待操作（Open/CreateRtu都是同步），返回已完成的Task
            await Task.CompletedTask;
        }

        /// <summary>
        /// 安全断开并且释放所有资源（取消后台轮询）
        /// </summary>
        /// <returns></returns>
        public static async Task DisConnectAsync()
        {
            //取消后台实时查询任务
            // 先判空，再取消
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _cts.Dispose(); // 释放取消令牌源
                _cts = null;

            }

            //等待异步锁，防止并发冲突 
            //await _ioLock.WaitAsync(); 写在 “取消轮询后、销毁资源前”，是断开连接的唯一安全位置：
            //早了：会等轮询线程退出，导致断开变慢；
            //晚了：销毁资源时可能有读写操作干扰，引发异常；
            await _ioLock.WaitAsync();

            try
            {
                if (_serialPort!=null)
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }

                    _serialPort.Dispose();
                    _serialPort = null;
                }

                //_modbusMaster：读 / 写寄存器的通信代理
                if (_modbusMaster!=null)
                {
                    _modbusMaster.Dispose();
                    _modbusMaster = null;
                }
            }
            finally
            {
                //释放锁
                _ioLock.Release();
                //通知订阅者已断开
                ConnectionChanged?.Invoke(null,false);
                _hasEverConnected = false; // 重置标记
            }
        }

        /// <summary>
        /// 后台轮询循环，持续读取PLC状态并且通过DataReceived事件公布数据
        /// </summary>
        private static async Task PollDataLoop(CancellationToken token)
        {
            int errCount = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {

                    //先检查串口是否处于 “已连接” 状态；
                    if (!IsConnected)
                    {
                        await Task.Delay(1000,token);
                        continue;
                    }
                    var state = await ReadStateAsync();

                    // 首次成功读取，触发连接成功事件
                    if (!_hasEverConnected)
                    {
                        _hasEverConnected = true;
                        ConnectionChanged?.Invoke(null, true);
                    }

                    errCount = 0;
                    // 触发事件，把DeviceState数据传给UI
                    DataReceived?.Invoke(null, state);
                    //Task.Delay(200, token)  创建延迟任务
                    //切记，不是 “必须等 1000ms”
                    //而是没取消时：等 1000ms 后重试读 PLC；
                    //取消时：“紧急刹车”，立刻抛出异常，轮询直接退出，一秒都不等。
                    //token 取消令牌，如果调用_cts.Cancel()（断开连接），延迟会立刻终止，抛出OperationCanceledException，循环直接退出
                    await Task.Delay(200,token);//200ms轮询间隔
                }
                catch(OperationCanceledException)
                {
                    break;//收到取消请求，跳出循环
                }
                catch (Exception ex)
                {
                    errCount++;
                    // 确保锁释放（ReadStateAsync异常时可能没释放）
                    if (_ioLock.CurrentCount == 0)
                    {
                        _ioLock.Release();
                    }
                    if (errCount >=3)
                    {
                        LogService.Warn($"PLC通信异常：{ex.Message}");
                        ConnectionChanged?.Invoke(null, false);
                        _hasEverConnected = false; // 重置标记，下次成功时再次触发 true
                        errCount = 0;
                    }

                    await Task.Delay(1000,token);//等待一段时间再重试
                }
            }
        }

        /// <summary>
        /// 从PLC读取当前设备状态并且封装为DeviceState对象返回
        /// </summary>
        /// <returns></returns>
        public static async Task<DeviceState> ReadStateAsync()
        {
            //_ioLock 是用来保护串口读写操作的，ReadStateAsync 是直接操作串口的方法，必须加锁；
            //而 PollDataLoop 只是 “调度员”（只调用读数据方法），不直接操作串口，所以不用加锁。
            //异步等待获取串口操作的互斥锁，核心作用是保证同一时间只有一个线程能操作串口（读 / 写），避免多个线程同时读写串口引发冲突、异常。
            await _ioLock.WaitAsync();
            try
            {
                if (_modbusMaster == null) throw new InvalidOperationException("未连接");

                //1,读取数值区（假设读取10个寄存器）
                //读PLC里「0号地址开始的10个寄存器」——存的是数字
                ushort[] registers = await _modbusMaster.ReadHoldingRegistersAsync(SlaveId, 0, 10);
                //2.读取条码区（假设从地址10开始读取，长度为10的寄存器）
                //定义条码读取的地址和长度（固定值，PLC里约定好的）

                const ushort barcodeStart = 10;
                const ushort barcodeLength = 10;
                string barcode = string.Empty;

                try
                {
                    //异步读取 PLC 中指定地址段的保持寄存器数据，并把读取到的数值存到barcodeRes这个 16 位无符号整数数组里，专门用来获取条码相关的数据。
                    // 读PLC里「10号地址开始的10个寄存器」——存的是条码的字符编码
                    ushort[] barcodeRes = await _modbusMaster.ReadHoldingRegistersAsync(SlaveId, barcodeStart, barcodeLength);
                    // 将PLC寄存器中读取到的ushort类型数字数组（条码字符编码），转换为实际的条码字符串
                    // 比如PLC里存的[65,66,67]（ASCII码）→ 转换为"ABC"，[48,49,50]→转换为"012"
                    barcode = ConvertRegistersToString(barcodeRes);

                }
                catch (Exception ex)
                {
                    LogService.Warn($"读取条码失败{ex.Message}");
                    
                }

                return new DeviceState
                {
                    ActualCount = registers[ModbusConfigHelper.ActualCount],
                    TargetCount = registers[ModbusConfigHelper.TargetCount],
                    //温度和时间类数据假设需要除以100得到实际值
                    CurrentTemp = registers[ModbusConfigHelper.CurrentTemp]/100.0,
                    SettingTemp = registers[ModbusConfigHelper.SettingTemp]/100.0,
                    RunningTime = registers[ModbusConfigHelper.RunningTime]/100.0,
                    CurrentCycleTime = registers[ModbusConfigHelper.CurrentCycleTime]/100.0,
                    StandardCycleTime = registers[ModbusConfigHelper.StandardCycleTime]/100.0,
                    LiquidLevel = registers[ModbusConfigHelper.LiquidLevel]/100.0,
                    ValueOpen = registers[ModbusConfigHelper.ValueOpen] == 1,//数字1表示打开阀门，0为关闭阀门
                    BarCode = barcode,
                };
            }
            finally
            {
                _ioLock.Release(); 
            }
        }

        private static string ConvertRegistersToString(ushort[] regs)
        {
            //空检查
            if (regs == null || regs.Length == 0) return string.Empty;

            List<byte> bytes = new List<byte>();
            foreach (ushort reg in regs)
            {
                //如果寄存器为0，常用设备将0作为字符串的结束,直接结束解析
                if(reg == 0) break;

                byte high=(byte)(reg >> 8);//high为寄存器的高8位（高字节）
                byte low=(byte)(reg & 0xFF);//low为寄存器的低8位（低字节）

                if(high!=0) bytes.Add(high);
                if(low!=0) bytes.Add(low);
            }
            //按照ASCII解码字节序列化为字符串，并且去掉两端留白
            return Encoding.ASCII.GetString(bytes.ToArray()).Trim();
        }

        /// <summary>
        /// 向PLC写入命令
        /// </summary>
        /// <param name="command"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static async Task WriteCommandAsync(string command,bool value)
        {
            //指令→地址映射：根据启停指令，确定要写的PLC线圈地址
            //如果收到的指令是 "Start"（启动），就把要写入的 PLC 寄存器地址设为 1；
            //如果指令不是 "Start"（比如 "Stop" 停止），就把寄存器地址设为 2；
            ushort address = command == "Start" ? (ushort)1 : (ushort)2;
            await _ioLock.WaitAsync();
            try
            {
                if (_modbusMaster == null) return;
                //    value：true=触发指令（比如写true到地址1，设备启动），false=取消指令
                await _modbusMaster.WriteSingleCoilAsync(SlaveId, address, value);
                LogService.Info($"写入指令{command}={value}");
            }
            catch (Exception ex)
            {
                LogService.Error($"写入指令失败：{command}={value}", ex);        
            }
            finally
            {
                _ioLock.Release();//最终释放IO锁 
            }
        }
        /// <summary>
        /// 获得计算机可用串口列表
        /// </summary>
        /// <returns></returns>
        public static string[] GetAvaliblePorts()=>SerialPort.GetPortNames();

    }
}
