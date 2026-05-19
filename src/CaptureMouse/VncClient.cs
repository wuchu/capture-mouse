using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CaptureMouse;

/// <summary>
/// VNC 客户端实现 - 仅支持键鼠事件发送
/// </summary>
public class VncClient : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly object _lock = new();
    private bool _disposed;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    /// <summary>
    /// 当前鼠标 X 坐标（VNC 坐标系 0-65535）
    /// </summary>
    private ushort _currentVncX;

    /// <summary>
    /// 当前鼠标 Y 坐标（VNC 坐标系 0-65535）
    /// </summary>
    private ushort _currentVncY;

    /// <summary>
    /// 当前鼠标按键状态掩码
    /// </summary>
    private byte _currentButtonMask;

    /// <summary>
    /// 认证用户名（macOS 用户名）
    /// </summary>
    private string _username = "";

    public bool IsConnected => _tcpClient?.Connected ?? false;
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 远程端主动断开连接时触发（macOS 端关闭屏幕共享等）
    /// Windows 端应在此事件中恢复键鼠控制权
    /// </summary>
    public event EventHandler? RemoteDisconnected;

    /// <summary>
    /// 连接到 VNC 服务器
    /// </summary>
    public async Task<bool> ConnectAsync(string host, int port, string username, string password, CancellationToken ct = default)
    {
        try
        {
            _username = username;
            Logger.Info($"正在连接到 {host}:{port}...");
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port, ct);
            _stream = _tcpClient.GetStream();
            Logger.Info("TCP 连接已建立");

            // 1. 协议版本协商
            if (!await NegotiateVersionAsync(ct))
            {
                ErrorOccurred?.Invoke(this, "协议版本协商失败");
                return false;
            }

            // 2. 安全认证
            if (!await AuthenticateAsync(password, ct))
            {
                ErrorOccurred?.Invoke(this, "认证失败");
                return false;
            }

            // 3. 客户端初始化
            if (!await ClientInitAsync(ct))
            {
                ErrorOccurred?.Invoke(this, "客户端初始化失败");
                return false;
            }

            // 4. 服务器初始化
            if (!await ServerInitAsync(ct))
            {
                ErrorOccurred?.Invoke(this, "服务器初始化失败");
                return false;
            }

            // 5. 设置像素格式和编码
            await SetPixelFormatAsync(ct);
            await SetEncodingsAsync(ct);

            // 6. 启动服务器消息接收循环（防止 TCP 缓冲区满）
            StartReceiveLoop();

            Logger.Info($"连接成功! 屏幕尺寸: {ScreenWidth}x{ScreenHeight}");
            ConnectionStateChanged?.Invoke(this, true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"连接错误: {ex.Message}", ex);
            ErrorOccurred?.Invoke(this, $"连接错误: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        Logger.Info("断开 VNC 连接");

        // 停止接收循环
        _receiveCts?.Cancel();
        try
        {
            _receiveTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        _receiveCts?.Dispose();
        _receiveCts = null;
        _receiveTask = null;

        lock (_lock)
        {
            _stream?.Close();
            _tcpClient?.Close();
            _stream = null;
            _tcpClient = null;
        }

        ConnectionStateChanged?.Invoke(this, false);
    }

    /// <summary>
    /// 发送鼠标移动事件
    /// </summary>
    /// <param name="x">Windows 屏幕坐标 X</param>
    /// <param name="y">Windows 屏幕坐标 Y</param>
    public void SendMouseMove(int x, int y)
    {
        if (!IsConnected) return;
        if (ScreenWidth <= 0 || ScreenHeight <= 0) return;

        lock (_lock)
        {
            if (_stream == null) return;

            // 将 Windows 像素坐标映射到 VNC 坐标 (0-65535)
            _currentVncX = (ushort)Math.Clamp((long)x * 65535 / ScreenWidth, 0, 65535);
            _currentVncY = (ushort)Math.Clamp((long)y * 65535 / ScreenHeight, 0, 65535);

            WriteMouseEvent(_currentButtonMask, _currentVncX, _currentVncY);
        }
    }

    /// <summary>
    /// 发送鼠标按键事件
    /// </summary>
    /// <param name="button">VNC 按键掩码: 1=左键, 2=中键, 4=右键</param>
    /// <param name="pressed">是否按下</param>
    public void SendMouseButton(int button, bool pressed)
    {
        if (!IsConnected) return;

        lock (_lock)
        {
            if (_stream == null) return;

            if (pressed)
                _currentButtonMask |= (byte)button;
            else
                _currentButtonMask &= (byte)~button;

            WriteMouseEvent(_currentButtonMask, _currentVncX, _currentVncY);
        }
    }

    /// <summary>
    /// 发送鼠标滚轮事件
    /// </summary>
    /// <param name="delta">滚轮增量</param>
    public void SendMouseWheel(int delta)
    {
        if (!IsConnected) return;

        lock (_lock)
        {
            if (_stream == null) return;

            if (delta > 0)
            {
                WriteMouseEvent((byte)(_currentButtonMask | 8), _currentVncX, _currentVncY);  // scroll up
                WriteMouseEvent(_currentButtonMask, _currentVncX, _currentVncY);
            }
            else if (delta < 0)
            {
                WriteMouseEvent((byte)(_currentButtonMask | 16), _currentVncX, _currentVncY); // scroll down
                WriteMouseEvent(_currentButtonMask, _currentVncX, _currentVncY);
            }
        }
    }

    /// <summary>
    /// 发送键盘事件
    /// </summary>
    public void SendKey(uint keySym, bool pressed)
    {
        if (!IsConnected) return;
        if (keySym == 0) return; // 忽略未知按键

        lock (_lock)
        {
            if (_stream == null) return;

            byte[] buffer = new byte[8];
            buffer[0] = 4; // KeyEvent message type
            buffer[1] = pressed ? (byte)1 : (byte)0;
            buffer[2] = 0; // padding
            buffer[3] = 0; // padding
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4), keySym);

            try
            {
                _stream.Write(buffer);
            }
            catch (IOException ex)
            {
                Logger.Error("发送键盘事件失败", ex);
            }
        }
    }

    /// <summary>
    /// 写入鼠标事件到流（调用者必须持有 _lock）
    /// </summary>
    private void WriteMouseEvent(byte buttonMask, ushort x, ushort y)
    {
        byte[] buffer = new byte[6];
        buffer[0] = 5; // PointerEvent message type
        buffer[1] = buttonMask;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), x);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), y);

        try
        {
            _stream!.Write(buffer);
        }
        catch (IOException ex)
        {
            Logger.Error("发送鼠标事件失败", ex);
        }
    }

    #region 协议握手

    /// <summary>
    /// 协议版本协商
    /// </summary>
    private async Task<bool> NegotiateVersionAsync(CancellationToken ct)
    {
        Logger.Debug("开始协议版本协商");

        // 读取服务器版本 (12 bytes: "RFB xxx.yyy\n")
        byte[] serverVersion = await ReadExactAsync(12, ct);
        string versionStr = Encoding.ASCII.GetString(serverVersion);
        Logger.Info($"服务器版本: {versionStr.Trim()}");

        // 发送客户端版本 (支持 3.8)
        byte[] clientVersion = Encoding.ASCII.GetBytes("RFB 003.008\n");
        await _stream!.WriteAsync(clientVersion, ct);
        Logger.Debug("已发送客户端版本: RFB 003.008");

        return true;
    }

    /// <summary>
    /// 安全认证
    /// </summary>
    private async Task<bool> AuthenticateAsync(string password, CancellationToken ct)
    {
        Logger.Debug("开始安全认证");

        // 读取安全类型数量
        byte[] securityCountBuffer = await ReadExactAsync(1, ct);
        int securityCount = securityCountBuffer[0];

        if (securityCount == 0)
        {
            // 读取失败原因长度和原因
            byte[] reasonLenBuffer = await ReadExactAsync(4, ct);
            int reasonLen = BinaryPrimitives.ReadInt32BigEndian(reasonLenBuffer);
            if (reasonLen < 0 || reasonLen > 65536)
            {
                Logger.Error($"无效的拒绝原因长度: {reasonLen}");
                ErrorOccurred?.Invoke(this, "服务器发送了无效的拒绝原因");
                return false;
            }
            byte[] reasonBuffer = await ReadExactAsync(reasonLen, ct);
            string reason = Encoding.UTF8.GetString(reasonBuffer);
            Logger.Error($"服务器拒绝连接: {reason}");
            ErrorOccurred?.Invoke(this, $"服务器拒绝连接: {reason}");
            return false;
        }

        // 读取支持的认证类型
        byte[] securityTypes = await ReadExactAsync(securityCount, ct);
        Logger.Info($"服务器支持的认证类型: {BitConverter.ToString(securityTypes)}");

        // 检查支持的认证类型（按优先级）
        bool supportsAppleAuth = Array.Exists(securityTypes, t => t == 30);  // Apple DH
        bool supportsVncAuth = Array.Exists(securityTypes, t => t == 2);    // Standard VNC
        bool supportsNone = Array.Exists(securityTypes, t => t == 1);       // None

        if (supportsAppleAuth)
        {
            // Apple VNC 认证 (Type 30) - macOS Screen Sharing 默认
            Logger.Debug("选择 Apple VNC 认证 (类型 30)");
            await _stream!.WriteAsync(new byte[] { 30 }, ct);

            return await AppleVncAuth.AuthenticateAsync(_stream!, _username, password, ct);
        }
        else if (supportsVncAuth)
        {
            // 标准VNC 认证 (Type 2)
            Logger.Debug("选择 VNC 认证 (类型 2)");
            await _stream!.WriteAsync(new byte[] { 2 }, ct);

            // VNC 认证挑战-响应
            byte[] challenge = await ReadExactAsync(16, ct);
            byte[] response = EncryptVncPassword(password, challenge);
            await _stream!.WriteAsync(response, ct);

            // 读取认证结果
            byte[] resultBuffer = await ReadExactAsync(4, ct);
            uint result = BinaryPrimitives.ReadUInt32BigEndian(resultBuffer);

            if (result == 0)
            {
                Logger.Info("VNC 认证成功");
                return true;
            }
            else
            {
                Logger.Error($"VNC 认证失败，结果码: {result}");
                return false;
            }
        }
        else if (supportsNone)
        {
            // 无需认证 (类型 1)
            Logger.Debug("选择无认证 (类型 1)");
            await _stream!.WriteAsync(new byte[] { 1 }, ct);
            return true;
        }
        else
        {
            Logger.Error("服务器不支持的认证类型");
            ErrorOccurred?.Invoke(this, "服务器不支持的认证类型");
            return false;
        }
    }

    /// <summary>
    /// VNC 密码加密 (DES)
    /// </summary>
    private static byte[] EncryptVncPassword(string password, byte[] challenge)
    {
        // VNC 密码最多 8 个字符
        byte[] key = new byte[8];
        byte[] passwordBytes = Encoding.ASCII.GetBytes(password);
        Array.Copy(passwordBytes, key, Math.Min(passwordBytes.Length, 8));

        // 反转密钥位 (VNC 特殊处理)
        for (int i = 0; i < 8; i++)
        {
            key[i] = ReverseBits(key[i]);
        }

        // 使用 DES 加密
        using var des = DES.Create();
        des.Key = key;
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;

        using var encryptor = des.CreateEncryptor();
        byte[] response = new byte[16];
        encryptor.TransformBlock(challenge, 0, 16, response, 0);

        // 清除密钥材料
        Array.Clear(key, 0, key.Length);
        Array.Clear(passwordBytes, 0, passwordBytes.Length);

        return response;
    }

    /// <summary>
    /// 反转字节位
    /// </summary>
    private static byte ReverseBits(byte b)
    {
        byte result = 0;
        for (int i = 0; i < 8; i++)
        {
            result |= (byte)(((b >> i) & 1) << (7 - i));
        }
        return result;
    }

    /// <summary>
    /// 客户端初始化
    /// </summary>
    private async Task<bool> ClientInitAsync(CancellationToken ct)
    {
        Logger.Debug("发送 ClientInit (共享模式)");
        // 发送共享标志 (1 = 共享, 0 = 独占)
        await _stream!.WriteAsync(new byte[] { 1 }, ct);
        return true;
    }

    /// <summary>
    /// 服务器初始化
    /// </summary>
    private async Task<bool> ServerInitAsync(CancellationToken ct)
    {
        Logger.Debug("读取 ServerInit");

        // 读取服务器初始化消息
        byte[] initBuffer = await ReadExactAsync(24, ct);

        ScreenWidth = BinaryPrimitives.ReadUInt16BigEndian(initBuffer.AsSpan(0, 2));
        ScreenHeight = BinaryPrimitives.ReadUInt16BigEndian(initBuffer.AsSpan(2, 4));
        int nameLength = BinaryPrimitives.ReadInt32BigEndian(initBuffer.AsSpan(20, 4));

        Logger.Info($"服务器屏幕尺寸: {ScreenWidth}x{ScreenHeight}");

        // 读取桌面名称
        if (nameLength > 0 && nameLength < 4096) // 安全检查
        {
            byte[] nameBuffer = await ReadExactAsync(nameLength, ct);
            string name = Encoding.UTF8.GetString(nameBuffer);
            Logger.Info($"桌面名称: {name}");
        }

        return true;
    }

    /// <summary>
    /// 设置像素格式
    /// </summary>
    private async Task SetPixelFormatAsync(CancellationToken ct)
    {
        Logger.Debug("设置像素格式");

        // 消息格式: [type=0][pad x3][pixelformat 16bytes] = 20 bytes
        byte[] format = new byte[20];
        format[0] = 0; // SetPixelFormat
        format[1] = 0; // padding
        format[2] = 0; // padding
        format[3] = 0; // padding

        // 像素格式 (16 bytes)
        format[4] = 32;  // bits per pixel
        format[5] = 24;  // depth
        format[6] = 0;   // big endian flag
        format[7] = 1;   // true color flag
        // red max (2 bytes, big endian) = 255
        format[8] = 0;
        format[9] = 255;
        // green max (2 bytes, big endian) = 255
        format[10] = 0;
        format[11] = 255;
        // blue max (2 bytes, big endian) = 255
        format[12] = 0;
        format[13] = 255;
        // red shift
        format[14] = 16;
        // green shift
        format[15] = 8;
        // blue shift
        format[16] = 0;
        // padding (3 bytes) - 已默认为0

        await _stream!.WriteAsync(format, ct);
    }

    /// <summary>
    /// 设置编码
    /// </summary>
    private async Task SetEncodingsAsync(CancellationToken ct)
    {
        Logger.Debug("设置编码");

        // 消息格式: [type=2][pad][num_encodings x2][encodings x4] 
        byte[] encodings = new byte[8];
        encodings[0] = 2; // SetEncodings
        encodings[1] = 0; // padding
        encodings[2] = 0; // number of encodings (high byte)
        encodings[3] = 1; // number of encodings (low byte) = 1

        // Raw encoding (0) - 我们不关心画面，但需要设置一个编码
        encodings[4] = 0;
        encodings[5] = 0;
        encodings[6] = 0;
        encodings[7] = 0;

        await _stream!.WriteAsync(encodings, ct);
    }

    #endregion

    #region 服务器消息接收

    /// <summary>
    /// 启动服务器消息接收循环
    /// 必须持续读取服务器消息，否则 TCP 缓冲区满后无法发送
    /// </summary>
    private void StartReceiveLoop()
    {
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(ReceiveLoop, _receiveCts.Token);
    }

    /// <summary>
    /// 接收服务器消息循环
    /// </summary>
    private async Task ReceiveLoop()
    {
        Logger.Debug("服务器消息接收循环已启动");

        var token = _receiveCts?.Token ?? CancellationToken.None;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_stream == null || _tcpClient?.Connected != true)
                    break;

                // 读取消息类型
                byte[] msgTypeBuf = await ReadExactAsync(1, token);
                byte msgType = msgTypeBuf[0];

                switch (msgType)
                {
                    case 0: // FramebufferUpdate
                        await HandleFramebufferUpdate(token);
                        break;
                    case 1: // SetColourMapEntries
                        await HandleSetColourMapEntries(token);
                        break;
                    case 2: // Bell
                        Logger.Debug("服务器: Bell");
                        break;
                    case 3: // ServerCutText
                        await HandleServerCutText(token);
                        break;
                    default:
                        Logger.Warning($"未知服务器消息类型: {msgType}");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消（本地主动断开），忽略
            Logger.Debug("接收循环被取消（本地断开）");
            return;
        }
        catch (IOException ex)
        {
            // macOS 端主动断开连接
            Logger.Info($"远程端断开连接: {ex.Message}");
            OnRemoteDisconnected();
            return;
        }
        catch (Exception ex)
        {
            // 其他异常也视为断连
            Logger.Error("接收循环异常，视为远程断开", ex);
            OnRemoteDisconnected();
            return;
        }

        // 循环正常退出但非取消，说明连接已断
        if (!token.IsCancellationRequested)
        {
            Logger.Info("连接已断开（循环退出）");
            OnRemoteDisconnected();
        }
    }

    /// <summary>
    /// 处理 FramebufferUpdate 消息（丢弃画面数据）
    /// </summary>
    private async Task HandleFramebufferUpdate(CancellationToken ct)
    {
        // 读取 padding + number of rectangles
        byte[] header = await ReadExactAsync(3, ct);
        ushort numRects = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1));

        for (int i = 0; i < numRects; i++)
        {
            // 读取矩形头 (12 bytes: x, y, width, height, encoding)
            byte[] rectHeader = await ReadExactAsync(12, ct);
            int encoding = BinaryPrimitives.ReadInt32BigEndian(rectHeader.AsSpan(8));

            // 根据编码类型跳过数据
            long dataSize = GetEncodingDataSize(encoding, rectHeader);
            if (dataSize < 0)
            {
                Logger.Warning($"不支持的编码类型 {encoding}，无法确定数据大小，断开连接");
                throw new IOException($"Unsupported encoding: {encoding}");
            }
            if (dataSize > 0)
            {
                await SkipBytesAsync(dataSize, ct);
            }
        }
    }

    /// <summary>
    /// 获取编码数据大小
    /// </summary>
    private long GetEncodingDataSize(int encoding, byte[] rectHeader)
    {
        ushort width = BinaryPrimitives.ReadUInt16BigEndian(rectHeader.AsSpan(4));
        ushort height = BinaryPrimitives.ReadUInt16BigEndian(rectHeader.AsSpan(6));

        return encoding switch
        {
            0 => (long)width * height * 4,  // Raw: width * height * bytesPerPixel (long to avoid overflow)
            1 => 4,                          // CopyRect: srcX(2) + srcY(2)
            -239 => width * height * 4,      // Cursor pseudo-encoding: pixel data + mask
            _ => -1                          // Unknown encoding: cannot determine size, must disconnect
        };
    }

    /// <summary>
    /// 处理 SetColourMapEntries 消息
    /// </summary>
    private async Task HandleSetColourMapEntries(CancellationToken ct)
    {
        byte[] header = await ReadExactAsync(5, ct);
        ushort numColors = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(3));
        await SkipBytesAsync(numColors * 6, ct);
    }

    /// <summary>
    /// 处理 ServerCutText 剪贴板消息
    /// </summary>
    private async Task HandleServerCutText(CancellationToken ct)
    {
        byte[] header = await ReadExactAsync(7, ct);
        int textLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(3));
        if (textLength > 0 && textLength < 1024 * 1024) // 安全检查
        {
            await SkipBytesAsync(textLength, ct);
        }
    }

    /// <summary>
    /// 跳过指定字节数
    /// </summary>
    private async Task SkipBytesAsync(long count, CancellationToken ct)
    {
        if (count <= 0) return;

        byte[] buffer = new byte[Math.Min(count, 8192)];
        long remaining = count;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(remaining, buffer.Length);
            int read = await _stream!.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) throw new IOException("连接已关闭");
            remaining -= read;
        }
    }

    #endregion

    /// <summary>
    /// 读取指定字节数
    /// </summary>
    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        byte[] buffer = new byte[count];
        int totalRead = 0;

        while (totalRead < count)
        {
            int read = await _stream!.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (read == 0) throw new IOException("连接已关闭");
            totalRead += read;
        }

        return buffer;
    }

    /// <summary>
    /// 远程端断开连接时的处理
    /// 清理连接并触发事件，让 Windows 端恢复键鼠控制权
    /// </summary>
    private void OnRemoteDisconnected()
    {
        Logger.Warning("!!! 远程端断开连接，恢复本地键鼠控制权 !!!");

        // 清理连接资源（不触发 Cancel，因为已经断开了）
        lock (_lock)
        {
            _stream?.Close();
            _tcpClient?.Close();
            _stream = null;
            _tcpClient = null;
        }

        // 重置按键状态
        _currentButtonMask = 0;

        // 触发事件
        ConnectionStateChanged?.Invoke(this, false);
        RemoteDisconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
    }
}