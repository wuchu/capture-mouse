using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CaptureMouse;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Console.WriteLine("=== CaptureMouse 迭代一测试 ===");
        Console.WriteLine("1. VNC 连接测试");
        Console.WriteLine("2. 输入捕获测试");
        Console.WriteLine("3. 综合测试 (捕获+转发)");
        Console.WriteLine();
        Console.Write("选择测试模式 (1-3): ");

        var mode = Console.ReadLine();

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
            default:
                Console.WriteLine("无效选择");
                break;
        }
    }

    /// <summary>
    /// VNC 连接测试
    /// </summary>
    static void RunVncTest()
    {
        Console.WriteLine("\n=== VNC 连接测试 ===\n");

        using var vncClient = new VncClient();

        vncClient.ConnectionStateChanged += (s, connected) =>
        {
            Console.WriteLine($"[事件] 连接状态: {(connected ? "已连接" : "已断开")}");
        };

        vncClient.ErrorOccurred += (s, error) =>
        {
            Console.WriteLine($"[错误] {error}");
        };

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

            switch (input)
            {
                case "1":
                    ConnectToMac(vncClient);
                    break;
                case "2":
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
                    Console.WriteLine($"连接状态: {(vncClient.IsConnected ? "已连接" : "未连接")}");
                    if (vncClient.IsConnected)
                    {
                        Console.WriteLine($"屏幕尺寸: {vncClient.ScreenWidth}x{vncClient.ScreenHeight}");
                    }
                    break;
                case "0":
                    return;
                default:
                    Console.WriteLine("无效选择");
                    break;
            }
        }
    }

    /// <summary>
    /// 输入捕获测试
    /// </summary>
    static void RunInputCaptureTest()
    {
        Console.WriteLine("\n=== 输入捕获测试 ===");
        Console.WriteLine("按任意键或移动鼠标，观察输出...");
        Console.WriteLine("按 ESC 停止测试\n");

        using var inputCapture = new InputCapture();

        inputCapture.InputReceived += (s, e) =>
        {
            Console.WriteLine($"[输入] {e}");
        };

        if (!inputCapture.StartCapture())
        {
            Console.WriteLine("启动输入捕获失败");
            return;
        }

        Console.WriteLine("输入捕获已启动，开始测试...");

        // 运行消息循环
        Application.Run();
    }

    /// <summary>
    /// 综合测试：捕获 + 转发到 VNC
    /// </summary>
    static void RunIntegratedTest()
    {
        Console.WriteLine("\n=== 综合测试 (捕获+转发) ===\n");

        using var vncClient = new VncClient();
        using var inputCapture = new InputCapture();

        // 先连接 VNC
        ConnectToMac(vncClient);

        if (!vncClient.IsConnected)
        {
            Console.WriteLine("连接失败，无法继续测试");
            return;
        }

        // 设置输入转发
        bool isForwarding = false;

        Console.WriteLine("\n按 Ctrl+Alt+M 切换转发模式");
        Console.WriteLine("按 Ctrl+Alt+Q 退出\n");

        inputCapture.InputReceived += (s, e) =>
        {
            // 检查是否是切换快捷键
            if (e.Type == InputEventType.KeyDown && e.KeyCode == 0x4D) // M key
            {
                // 检查修饰键状态（简化处理）
                // 实际应该跟踪修饰键状态
            }

            if (!isForwarding)
            {
                Console.WriteLine($"[本地] {e}");
                return;
            }

            // 转发到 VNC
            try
            {
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
                Console.WriteLine($"[转发错误] {ex.Message}");
            }
        };

        // 启动捕获
        if (!inputCapture.StartCapture())
        {
            Console.WriteLine("启动输入捕获失败");
            return;
        }

        // 默认开始转发
        isForwarding = true;
        Console.WriteLine("已开始转发输入到 macOS");

        // 运行消息循环
        Application.Run();
    }

    #region Helper Methods

    static void ConnectToMac(VncClient vncClient)
    {
        if (vncClient.IsConnected)
        {
            Console.WriteLine("已经连接，请先断开");
            return;
        }

        Console.Write("输入 macOS IP 地址: ");
        var host = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(host))
        {
            Console.WriteLine("IP 地址不能为空");
            return;
        }

        Console.Write("输入 VNC 密码: ");
        var password = ReadPassword();
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("密码不能为空");
            return;
        }

        Console.WriteLine("正在连接...");

        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var success = vncClient.ConnectAsync(host, 5900, password, cts.Token).Result;

            if (success)
            {
                Console.WriteLine("连接成功!");
                Console.WriteLine($"屏幕尺寸: {vncClient.ScreenWidth}x{vncClient.ScreenHeight}");
            }
            else
            {
                Console.WriteLine("连接失败");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"连接异常: {ex.Message}");
        }
    }

    static void TestMouseMove(VncClient vncClient)
    {
        if (!vncClient.IsConnected)
        {
            Console.WriteLine("请先连接");
            return;
        }

        Console.WriteLine("发送鼠标移动测试 (从左上到右下)...");

        int steps = 20;
        for (int i = 0; i <= steps; i++)
        {
            int x = (vncClient.ScreenWidth * i) / steps;
            int y = (vncClient.ScreenHeight * i) / steps;
            vncClient.SendMouseMove(x, y);
            Thread.Sleep(50);
        }

        Console.WriteLine("鼠标移动测试完成");
    }

    static void TestMouseClick(VncClient vncClient)
    {
        if (!vncClient.IsConnected)
        {
            Console.WriteLine("请先连接");
            return;
        }

        Console.WriteLine("发送鼠标点击测试 (左键)...");

        int centerX = vncClient.ScreenWidth / 2;
        int centerY = vncClient.ScreenHeight / 2;
        vncClient.SendMouseMove(centerX, centerY);
        Thread.Sleep(100);
        vncClient.SendMouseButton(1, true);
        Thread.Sleep(100);
        vncClient.SendMouseButton(1, false);

        Console.WriteLine("鼠标点击测试完成");
    }

    static void TestKeyboard(VncClient vncClient)
    {
        if (!vncClient.IsConnected)
        {
            Console.WriteLine("请先连接");
            return;
        }

        Console.WriteLine("发送键盘测试 (输入 'ABC')...");

        vncClient.SendKey(0x61, true);  // 'a'
        Thread.Sleep(50);
        vncClient.SendKey(0x61, false);
        Thread.Sleep(100);

        vncClient.SendKey(0x62, true);  // 'b'
        Thread.Sleep(50);
        vncClient.SendKey(0x62, false);
        Thread.Sleep(100);

        vncClient.SendKey(0x63, true);  // 'c'
        Thread.Sleep(50);
        vncClient.SendKey(0x63, false);

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

