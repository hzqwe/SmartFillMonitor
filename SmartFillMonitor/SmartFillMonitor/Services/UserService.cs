using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography; // 加密相关命名空间（SHA256哈希）
using System.Text;
using System.Threading.Tasks;
using SmartFillMonitor.Models;

namespace SmartFillMonitor.Services
{
    /// <summary>
    /// 用户服务类（静态类）：处理用户登录、登出、创建、初始化等核心逻辑
    /// 静态类特点：无需实例化，直接通过 UserService.XXX 调用方法
    /// </summary>
    public static class UserService
    {
        // 密码加盐的固定字符串（盐值：防止密码被暴力破解，相当于给密码加“干扰项”）
        // 注意：当前为固定盐，生产环境建议改为每个用户随机盐
        private const string StaticSalt = "MysuperSecretSalt_2026!@#";

        /// <summary>
        /// 登录状态改变事件：当用户登录/登出时触发，用于通知UI更新登录状态
        /// Action<User?>：事件委托，参数为当前登录用户（null表示登出）
        /// </summary>
        public static event Action<User?>? LoginStateChanged;

        // 私有字段：存储当前登录的用户（仅内部修改）
        private static User? _currentUser;

        /// <summary>
        /// 公开属性：获取当前登录用户（只读，外部无法直接赋值）
        /// 赋值逻辑封装在set中，赋值时自动触发登录状态事件
        /// </summary>
        public static User? CurrentUser
        {
            get => _currentUser; // 读取当前登录用户
            private set // 私有赋值逻辑（仅内部方法可修改）
            {
                // 只有用户状态真正变化时，才更新并触发事件
                if (_currentUser != value)
                {
                    _currentUser = value;
                    // 触发登录状态改变事件（通知UI更新）
                    LoginStateChanged?.Invoke(_currentUser);
                }
            }
        }

        /// <summary>
        /// 初始化默认用户（程序首次启动时调用，仅执行一次）
        /// 逻辑：检查数据库是否有用户，无则创建admin和engineer两个默认用户
        /// </summary>
        /// <returns>异步任务</returns>
        public static async Task InitalizeAsync()
        {
            try
            {
                // 检查Users表是否有任何用户数据
                bool hasUsers = await DbProvider.Fsql.Select<User>().AnyAsync();
                // 无用户时才初始化默认用户
                if (!hasUsers)
                {
                    var now = DateTime.Now;
                    // 构建默认用户列表
                    // 集合初始化器（List<User> { ... }） + 对象初始化器（new User() { ... }）；
                    var users = new List<User>
                    {
                        // 管理员用户：用户名admin，密码admin（哈希后存储）
                        new User()
                        {
                            UserName="admin",
                            PasswordHash=HashPassword("admin"), // 密码哈希处理
                            Role=Role.admin, // 角色：管理员
                            CreatedAt=now, // 创建时间
                        },
                        // 工程师用户：用户名engineer，密码engineer
                        new User()
                        {
                            UserName="engineer",
                            PasswordHash=HashPassword("engineer"),
                            Role=Role.Engineer, // 角色：工程师
                            CreatedAt=now,
                        },
                    };

                    // 批量插入默认用户到数据库，返回受影响行数
                    var affrows = await DbProvider.Fsql.Insert(users).ExecuteAffrowsAsync();
                    // 验证是否成功插入2个用户
                    if (affrows == 2)
                    {
                        LogService.Info("系统初始化：创建默认用户成功");
                    }
                    else
                    {
                        // 插入数量不符则抛异常（会被外层catch捕获并记录日志）
                        throw new Exception("创建失败");
                    }
                }
            }
            catch (Exception ex)
            {
                // 捕获初始化异常，记录错误日志
                LogService.Error("系统初始化用户失败", ex);
            }
        }

        /// <summary>
        /// 验证用户登录凭证（用户名+密码）
        /// </summary>
        /// <param name="userName">用户名</param>
        /// <param name="password">明文密码</param>
        /// <returns>验证结果：true=登录成功，false=登录失败</returns>
        public static async Task<bool> AuthenticateAsync(string userName, string password)
        {
            // 先校验用户名/密码是否为空（空则直接返回失败）
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            {
                return false;
            }

            try
            {
                // 根据用户名查询用户（FirstAsync：无数据时会抛异常，后续优化可改用FirstOrDefaultAsync）
                var user = await DbProvider.Fsql.Select<User>()
                    .Where(u => u.UserName == userName)
                    .FirstAsync();

                // 理论上FirstAsync无数据会抛异常，此判断为冗余但保留
                if (user == null)  return false; 

                // 将用户输入的明文密码哈希（和数据库存储的哈希规则一致）
                var inputHash = HashPassword(password);
                // 对比哈希值（Ordinal：严格大小写匹配，保证哈希对比准确）
                bool isValid = string.Equals(inputHash, user.PasswordHash, StringComparison.Ordinal);

                if (isValid)
                {
                    // 登录成功：更新当前登录用户，触发登录状态事件
                    CurrentUser = user;
                    LogService.Info($"用户登录成功：{userName}");
                }
                else
                {
                    // 密码错误：记录警告日志
                    LogService.Warn($"用户{userName}尝试登录失败，密码错误");
                }
                return isValid;
            }
            catch (Exception ex)
            {
                // 捕获查询/验证异常（如用户名不存在），记录错误日志并返回失败
                LogService.Error("用户登录验证失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 用户登出，清空当前程序里的 “登录状态”，相当于点击了退出登录
        /// </summary>
        /// <returns>已完成的异步任务（无返回值）</returns>
        public static Task LogoutAsync()
        {
            // 有登录用户时才处理
            if (CurrentUser != null)
            {
                // 记录登出日志
                LogService.Info($"用户{CurrentUser.UserName}登出");
                // 清空当前登录用户，触发登录状态事件（UI会感知到登出）
                CurrentUser = null;
            }
            // 返回已完成的任务（异步方法无耗时操作时的标准写法）
            //Task.CompletedTask 是啥？
            //它是.NET 提供的 “空任务”“已完成的任务”，相当于告诉调用方：
            //“这个异步方法已经干完所有活了，任务状态是‘完成’，你不用等了。”
            return Task.CompletedTask;
        }

        /// <summary>
        /// 创建新用户并保存到数据库
        /// </summary>
        /// <param name="username">用户名（唯一）</param>
        /// <param name="password">明文密码（会自动哈希）</param>
        /// <param name="role">用户角色（管理员/工程师/操作员）</param>
        /// <param name="displayName">显示名称（可选）</param>
        /// <returns>异步任务</returns>
        /// <exception cref="ArgumentNullException">用户名/密码为空时抛出</exception>
        /// <exception cref="InvalidOperationException">用户名已存在时抛出</exception>
        public static async Task CreateUserAsync(string username, string password, Role role, string displayName = "")
        {
            // 校验用户名/密码不能为空（为空则抛异常）
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                throw new ArgumentNullException("用户名或密码不能为空");
            }

            // 检查用户名是否已存在（AnyAsync：返回是否存在）
            bool exits = await DbProvider.Fsql.Select<User>()
                .Where(u => u.UserName == username)
                .AnyAsync();

            // 用户名已存在则抛异常
            if (exits) throw new InvalidOperationException($"用户{username}已存在");

            // 构建新用户对象
            var user = new User
            {
                UserName = username,
                PasswordHash = HashPassword(password), // 密码哈希处理
                Role = role, // 分配角色
                CreatedAt = DateTime.Now, // 创建时间
            };

            // 将新用户插入数据库
            await DbProvider.Fsql.Insert(user).ExecuteAffrowsAsync();
            // 记录创建成功日志
            LogService.Info($"创建新用户：{username}");
        }

        /// <summary>
        /// 获取所有用户列表（按用户名升序排序）
        /// </summary>
        /// <returns>用户列表</returns>
        public static async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                // 查询所有用户，按用户名排序，返回列表
                return await DbProvider.Fsql.Select<User>()
                    .OrderBy(u => u.UserName)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                // 捕获异常，记录日志后重新抛出（让调用方感知错误）
                LogService.Error("获取用户列表失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 密码哈希处理（SHA256 + 固定盐）
        /// 流程：明文密码 + 盐值 → 转字节数组 → SHA256哈希 → 转十六进制字符串
        /// </summary>
        /// <param name="password">明文密码</param>
        /// <returns>哈希后的十六进制字符串（小写）</returns>
        private static string HashPassword(string password)
        {
            // 密码为空则返回空字符串
            if (string.IsNullOrEmpty(password)) return string.Empty;

            // 密码 + 固定盐值（增加密码破解难度）
            string raw = password + StaticSalt;

            // 创建SHA256哈希对象（using自动释放资源）
            using var sha = SHA256.Create();
            // 将字符串转UTF8字节数组，计算哈希值
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));

            // 哈希字节数组转十六进制字符串（x2：每个字节转2位十六进制）
            var sb = new StringBuilder(bytes.Length * 2);
            //创建字符串拼接器（3个字节×2=6位），遍历每个字节转十六进制，
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2")); // x2：小写十六进制，如 0a、1b
            }
            // 返回最终哈希字符串
            return sb.ToString();
        }
    }
}