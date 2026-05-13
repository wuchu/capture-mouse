using System;
using System.IO;
using System.Text;
using System.Threading;

namespace CaptureMouse;

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

/// <summary>
/// 日志记录器
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static string? _logFile;
    private static bool _initialized = false;

    /// <summary>
    /// 初始化日志系统
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        try
        {
            // 日志文件放在可执行文件同级目录的 logs 文件夹下
            var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            var exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            var logDir = Path.Combine(exeDir, "logs");

            Directory.CreateDirectory(logDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _logFile = Path.Combine(logDir, $"CaptureMouse_{timestamp}.log");

            // 写入启动标记
            Log(LogLevel.Info, "=== CaptureMouse 启动 ===");
            Log(LogLevel.Info, $"日志文件: {_logFile}");
            Log(LogLevel.Info, $"操作系统: {Environment.OSVersion}");
            Log(LogLevel.Info, $"CLR 版本: {Environment.Version}");
            Log(LogLevel.Info, $"工作目录: {Environment.CurrentDirectory}");
            Log(LogLevel.Info, $"命令行: {Environment.CommandLine}");

            _initialized = true;
        }
        catch (Exception ex)
        {
            // 如果日志初始化失败，输出到控制台
            Console.WriteLine($"日志初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 记录调试信息
    /// </summary>
    public static void Debug(string message)
    {
        Log(LogLevel.Debug, message);
    }

    /// <summary>
    /// 记录信息
    /// </summary>
    public static void Info(string message)
    {
        Log(LogLevel.Info, message);
    }

    /// <summary>
    /// 记录警告
    /// </summary>
    public static void Warning(string message)
    {
        Log(LogLevel.Warning, message);
    }

    /// <summary>
    /// 记录错误
    /// </summary>
    public static void Error(string message)
    {
        Log(LogLevel.Error, message);
    }

    /// <summary>
    /// 记录错误及异常
    /// </summary>
    public static void Error(string message, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine(message);
        sb.AppendLine($"异常: {ex.GetType().Name}");
        sb.AppendLine($"消息: {ex.Message}");
        sb.AppendLine($"堆栈: {ex.StackTrace}");

        if (ex.InnerException != null)
        {
            sb.AppendLine($"内部异常: {ex.InnerException.Message}");
        }

        Log(LogLevel.Error, sb.ToString());
    }

    /// <summary>
    /// 记录致命错误
    /// </summary>
    public static void Fatal(string message, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("!!! 致命错误 !!!");
        sb.AppendLine(message);
        sb.AppendLine($"异常: {ex.GetType().Name}");
        sb.AppendLine($"消息: {ex.Message}");
        sb.AppendLine($"堆栈: {ex.StackTrace}");

        if (ex.InnerException != null)
        {
            sb.AppendLine($"内部异常: {ex.InnerException.Message}");
        }

        Log(LogLevel.Fatal, sb.ToString());

        // 确保致命错误被写入
        Flush();
    }

    /// <summary>
    /// 记录未处理异常
    /// </summary>
    public static void UnhandledException(Exception ex)
    {
        Fatal("未处理的异常", ex);
    }

    /// <summary>
    /// 获取日志文件路径
    /// </summary>
    public static string? GetLogFilePath()
    {
        return _logFile;
    }

    /// <summary>
    /// 刷新日志缓冲区
    /// </summary>
    public static void Flush()
    {
        // 日志立即写入，无需刷新
    }

    private static void Log(LogLevel level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var threadId = Thread.CurrentThread.ManagedThreadId;
        var levelStr = level.ToString().ToUpper();
        var logLine = $"{timestamp} [{levelStr}] [{threadId}] {message}";

        lock (_lock)
        {
            // 输出到控制台
            Console.WriteLine(logLine);

            // 写入文件
            if (_logFile != null)
            {
                try
                {
                    File.AppendAllText(_logFile, logLine + Environment.NewLine);
                }
                catch
                {
                    // 忽略文件写入错误
                }
            }
        }
    }
}
