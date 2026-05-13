using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CaptureMouse;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // 首先初始化日志（在任何其他操作之前）
        try
        {
            Logger.Initialize();
            Logger.Info("程序入口点 - Main 方法开始");
        }
        catch (Exception ex)
        {
            // 如果日志初始化失败，尝试写入文件
            try
            {
                File.WriteAllText("startup_error.log", $"日志初始化失败: {ex}\n");
            }
            catch { }
            Console.WriteLine($"日志初始化失败: {ex.Message}");
        }

        // 设置未处理异常处理器
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Fatal("AppDomain 未处理异常", ex ?? new Exception("未知异常"));
            
            // 写入崩溃日志文件
            try
            {
                var crashLog = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                File.WriteAllText(crashLog, 
                    $"=== 程序崩溃 ===\n" +
                    $"时间: {DateTime.Now}\n" +
                    $"异常: {ex?.GetType().Name}\n" +
                    $"消息: {ex?.Message}\n" +
                    $"堆栈: {ex?.StackTrace}\n");
            }
            catch { }
        };

        Application.ThreadException += (s, e) =>
        {
            Logger.Fatal("Application 线程异常", e.Exception);
        };

        try
        {
            Logger.Info("设置控制台编码...");
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            
            Logger.Info("检查 Windows 版本...");
            if (Environment.OSVersion.Version.Major < 10)
            {
                Logger.Error("需要 Windows 10 或更高版本");
                Console.WriteLine("错误: 需要 Windows 10 或更高版本");
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
                return;
            }

            Logger.Info("启动主程序...");
            RunMainMenu();
        }
        catch (Exception ex)
        {
            Logger.Fatal("主程序异常", ex);
            Console.WriteLine($"\n!!! 程序异常 !!!");
            Console.WriteLine($"错误: {ex.Message}");
            Console.WriteLine($"\n日志文件: {Logger.GetLogFilePath() ?? "未知"}");
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
        finally
        {
            Logger.Info("程序退出");
        }
    }

    static void RunMainMenu()
    {
        Logger.Info("显示主菜单");
        
        Console.WriteLine("=== CaptureMouse 调试版本 ===");
        Console.WriteLine($"日志文件: {Logger.GetLogFilePath()}");
        Console.WriteLine();
        Console.WriteLine("1. VNC 连接测试");
        Console.WriteLine("2. 输入捕获测试");
        Console.WriteLine("3. 综合测试 (捕获+转发)");
        Console.WriteLine("0. 退出");
        Console.WriteLine();
        Console.Write("选择测试模式 (0-3): ");

        var mode = Console.ReadLine();
        Logger.Info($"用户选择: {mode}");

        switch (mode)
        {
            case "1":
                RunVncTest();
                break;
            case "2":
                RunInputCaptureTest();
                break;
            case "3":
                RunIntegratedTest();
                break;
            case "0":
                Logger.Info("用户选择退出");
                break;
            default:
                Logger.Warning($"无效选择: {mode}");
                Console.WriteLine("无效选择");
                break;
        }
    }

    static void RunVncTest()
    {
        Logger.Info("=== VNC 连接测试开始 ===");
        Console.WriteLine("\n=== VNC 连接测试 ===\n");

        VncClient? vncClient = null;
        
        try
        {
            Logger.Debug("创建 VncClient 实例");
            vncClient = new VncClient();
            
            vncClient.ConnectionStateChanged += (s, connected) =>
            {
                Logger.Info($"VNC 连接状态变更: {connected}");
                Console.WriteLine($"[事件] 连接状态: {(connected ? "已连接" : "已断开")}");
            };

            vncClient.ErrorOccurred += (s, error) =>
            {
                Logger.Error($"VNC 错误: {error}");
                Console.WriteLine($"[错误] {error}");
            };

            VncTestMenu(vncClient);
        }
        catch (Exception ex)
        {
            Logger.Error("VNC 测试异常", ex);
            Console.WriteLine($"VNC 测试异常: {ex.Message}");
        }
        finally
        {
            Logger.Info("释放 VncClient");
            vncClient?.Dispose();
            Logger.Info("=== VNC 连接测试结束 ===");
        }
    }

    static void VncTestMenu(VncClient vncClient)
    {
        while (true)
        {
            Console.WriteLine("\n命令菜单:");
            Console.WriteLine("1. 连接到 macOS");
            Console.WriteLine("2. 断开连接");
            Console.WriteLine("3. 发送鼠标移动测试");
            Console.WriteLine("4. 发送鼠标点击测试");
            Console.WriteLine("5. 发送键盘按键测试");
            Console.WriteLine("6. 查看连接状态");
            Console.WriteLine("0. 返回主菜单");
            Console.WriteLine();
            Console.Write("选择: ");

            var input = Console.ReadLine();
            Logger.Debug($"VNC 菜单选择: {input}");

            try
            {
                switch (input)
                {
                    case "1":
                        ConnectToMac(vncClient);
                        break;
                    case "2":
                        Logger.Info("用户选择断开连接");
                        vncClient.Disconnect();
                        Console.WriteLine("已断开连接");
                        break;
                    case "3":
                        TestMouseMove(vncClient);
                        break;
                    case "4":
                        TestMouseClick(vncClient);
                        break;
                    case "5":
                        TestKeyboard(vncClient);
                        break;
                    case "6":
                        Logger.Info($"查询状态: Connected={vncClient.IsConnected}");
                        Console.WriteLine($"连接状态: {(vncClient.IsConnected ? "已连接" : "未连接")}");
                        if (vncClient.IsConnected)
                        {
                            Console.WriteLine($"屏幕尺寸: {vncClient.ScreenWidth}x{vncClient.ScreenHeight}");
                        }
                        break;
                    case "0":
                        Logger.Info("返回主菜单");
                        return;
                    default:
                        Logger.Warning($"无效选择: {input}");
                        Console.WriteLine("无效选择");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"菜单操作异常 (选项 {input})", ex);
                Console.WriteLine($"操作失败: {ex.Message}");
            }
        }
    }

    static void RunInputCaptureTest()
    {
        Logger.Info("=== 输入捕获测试开始 ===");
        Console.WriteLine("\n=== 输入捕获测试 ===");
        Console.WriteLine("按任意键或移动鼠标，观察输出...");
        Console.WriteLine("按 ESC 停止测试\n");

        InputCapture? inputCapture = null;
        
        try
        {
            Logger.Debug("创建 InputCapture 实例");
            inputCapture = new InputCapture();
            
            inputCapture.InputReceived += (s, e) =>
            {
                Logger.Debug($"输入事件: {e.Type}");
                Console.WriteLine($"[输入] {e}");
            };

            Logger.Info("启动输入捕获...");
            if (!inputCapture.StartCapture())
            {
                Logger.Error("启动输入捕获失败");
                Console.WriteLine("启动输入捕获失败");
                return;
            }

            Logger.Info("输入捕获已启动，运行消息循环");
            Console.WriteLine("输入捕获已启动，开始测试...");
            
            // 运行消息循环
            Application.Run();
        }
        catch (Exception ex)
        {
            Logger.Error("输入捕获测试异常", ex);
            Console.WriteLine($"输入捕获异常: {ex.Message}");
        }
        finally
        {
            Logger.Info("释放 InputCapture");
            inputCapture?.Dispose();
            Logger.Info("=== 输入捕获测试结束 ===");
        }
    }

    static void RunIntegratedTest()
    {
        Logger.Info("=== 综合测试开始 ===");
        Console.WriteLine("\n=== 综合测试 (捕获+转发) ===\n");

        VncClient? vncClient = null;
        InputCapture? inputCapture = null;
        
        try
        {
            Logger.Debug("创建 VncClient 实例");
            vncClient = new VncClient();
            
            Logger.Debug("创建 InputCapture 实例");
            inputCapture = new InputCapture();

            // 先连接 VNC
            Logger.Info("尝试连接 macOS");
            ConnectToMac(vncClient);

            if (!vncClient.IsConnected)
            {
                Logger.Error("连接失败，无法继续测试");
                Console.WriteLine("连接失败，无法继续测试");
                return;
            }

            // 设置输入转发
            bool isForwarding = false;

            Console.WriteLine("\n按 Ctrl+Alt+M 切换转发模式");
            Console.WriteLine("按 Ctrl+Alt+Q 退出\n");

            inputCapture.InputReceived += (s, e) =>
            {
                if (!isForwarding)
                {
                    Logger.Debug($"[本地] {e.Type}");
                    Console.WriteLine($"[本地] {e}");
                    return;
                }

                // 转发到 VNC
                try
                {
                    Logger.Debug($"[转发] {e.Type}");
                    switch (e.Type)
                    {
                        case InputEventType.MouseMove:
                            vncClient.SendMouseMove(e.X, e.Y);
                            break;
                        case InputEventType.MouseButtonDown:
                            vncClient.SendMouseButton(e.Button, true);
                            break;
                        case InputEventType.MouseButtonUp:
                            vncClient.SendMouseButton(e.Button, false);
                            break;
                        case InputEventType.KeyDown:
                            vncClient.SendKey(e.KeySym, true);
                            break;
                        case InputEventType.KeyUp:
                            vncClient.SendKey(e.KeySym, false);
                            break;
                    }
                    Console.WriteLine($"[转发] {e}");
                }
                catch (Exception ex)
                {
                    Logger.Error("转发失败", ex);
                    Console.WriteLine($"[转发错误] {ex.Message}");
                }
            };

            // 启动捕获
            Logger.Info("启动输入捕获...");
            if (!inputCapture.StartCapture())
            {
                Logger.Error("启动输入捕获失败");
                Console.WriteLine("启动输入捕获失败");
                return;
            }

            // 默认开始转发
            isForwarding = true;
            Logger.Info("已开始转发输入到 macOS");
            Console.WriteLine("已开始转发输入到 macOS");

            // 运行消息循环
            Application.Run();
        }
        catch (Exception ex)
        {
            Logger.Error("综合测试异常", ex);
            Console.WriteLine($"综合测试异常: {ex.Message}");
        }
        finally
        {
            Logger.Info("释放资源");
            inputCapture?.Dispose();
            vncClient?.Dispose();
            Logger.Info("=== 综合测试结束 ===");
        }
    }

    #region Helper Methods

    static void ConnectToMac(VncClient vncClient)
    {
        Logger.Info("ConnectToMac 开始");
        
        if (vncClient.IsConnected)
        {
            Logger.Warning("已经连接，请先断开");
            Console.WriteLine("已经连接，请先断开");
            return;
        }

        Console.Write("输入 macOS IP 地址: ");
        var host = Console.ReadLine();
        Logger.Info($"用户输入 IP: {host}");
        
        if (string.IsNullOrWhiteSpace(host))
        {
            Logger.Warning("IP 地址为空");
            Console.WriteLine("IP 地址不能为空");
            return;
        }

        Console.Write("输入 macOS 用户名: ");
        var username = Console.ReadLine();
        Logger.Info($"用户输入用户名: {username}");

        Console.Write("输入 macOS 密码: ");
        var password = ReadPassword();
        
        if (string.IsNullOrWhiteSpace(password))
        {
            Logger.Warning("密码为空");
            Console.WriteLine("密码不能为空");
            return;
        }

        Logger.Info("正在连接...");
        Console.WriteLine("正在连接...");

        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Logger.Debug("调用 ConnectAsync...");
            var success = vncClient.ConnectAsync(host, 5900, username ?? "", password, cts.Token).Result;

            if (success)
            {
                Logger.Info($"连接成功! 屏幕尺寸: {vncClient.ScreenWidth}x{vncClient.ScreenHeight}");
                Console.WriteLine("连接成功!");
                Console.WriteLine($"屏幕尺寸: {vncClient.ScreenWidth}x{vncClient.ScreenHeight}");
            }
            else
            {
                Logger.Error("连接失败");
                Console.WriteLine("连接失败");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("连接异常", ex);
            Console.WriteLine($"连接异常: {ex.Message}");
        }
        
        Logger.Info("ConnectToMac 结束");
    }

    static void TestMouseMove(VncClient vncClient)
    {
        Logger.Info("TestMouseMove 开始");
        
        if (!vncClient.IsConnected)
        {
            Logger.Warning("未连接，无法测试");
            Console.WriteLine("请先连接");
            return;
        }

        Console.WriteLine("发送鼠标移动测试 (从左上到右下)...");

        int steps = 20;
        for (int i = 0; i <= steps; i++)
        {
            int x = (vncClient.ScreenWidth * i) / steps;
            int y = (vncClient.ScreenHeight * i) / steps;
            Logger.Debug($"移动鼠标到 ({x}, {y})");
            vncClient.SendMouseMove(x, y);
            Thread.Sleep(50);
        }

        Logger.Info("鼠标移动测试完成");
        Console.WriteLine("鼠标移动测试完成");
    }

    static void TestMouseClick(VncClient vncClient)
    {
        Logger.Info("TestMouseClick 开始");
        
        if (!vncClient.IsConnected)
        {
            Logger.Warning("未连接，无法测试");
            Console.WriteLine("请先连接");
            return;
        }

        Console.WriteLine("发送鼠标点击测试 (左键)...");

        int centerX = vncClient.ScreenWidth / 2;
        int centerY = vncClient.ScreenHeight / 2;
        
        Logger.Debug($"移动鼠标到中心 ({centerX}, {centerY})");
        vncClient.SendMouseMove(centerX, centerY);
        Thread.Sleep(100);
        
        Logger.Debug("按下左键");
        vncClient.SendMouseButton(1, true);
        Thread.Sleep(100);
        
        Logger.Debug("释放左键");
        vncClient.SendMouseButton(1, false);

        Logger.Info("鼠标点击测试完成");
        Console.WriteLine("鼠标点击测试完成");
    }

    static void TestKeyboard(VncClient vncClient)
    {
        Logger.Info("TestKeyboard 开始");
        
        if (!vncClient.IsConnected)
        {
            Logger.Warning("未连接，无法测试");
            Console.WriteLine("请先连接");
            return;
        }

        Console.WriteLine("发送键盘测试 (输入 'ABC')...");

        Logger.Debug("发送按键 'a'");
        vncClient.SendKey(0x61, true);
        Thread.Sleep(50);
        vncClient.SendKey(0x61, false);
        Thread.Sleep(100);

        Logger.Debug("发送按键 'b'");
        vncClient.SendKey(0x62, true);
        Thread.Sleep(50);
        vncClient.SendKey(0x62, false);
        Thread.Sleep(100);

        Logger.Debug("发送按键 'c'");
        vncClient.SendKey(0x63, true);
        Thread.Sleep(50);
        vncClient.SendKey(0x63, false);

        Logger.Info("键盘测试完成");
        Console.WriteLine("键盘测试完成");
    }

    static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }
            }
            else
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        return password.ToString();
    }

    #endregion
}
