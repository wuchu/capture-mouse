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

    public bool IsConnected => _tcpClient?.Connected ?? false;
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 连接到 VNC 服务器
    /// </summary>
    public async Task<bool> ConnectAsync(string host, int port, string password, CancellationToken ct = default)
    {
        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port, ct);
            _stream = _tcpClient.GetStream();

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

            ConnectionStateChanged?.Invoke(this, true);
            return true;
        }
        catch (Exception ex)
        {
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
    public void SendMouseMove(int x, int y)
    {
        if (!IsConnected) return;

        // VNC 使用 16 位无符号整数表示坐标 (0-65535)
        ushort vncX = (ushort)((x * 65535) / ScreenWidth);
        ushort vncY = (ushort)((y * 65535) / ScreenHeight);

        SendMouseEvent(0, vncX, vncY);
    }

    /// <summary>
    /// 发送鼠标按键事件
    /// </summary>
    public void SendMouseButton(int button, bool pressed)
    {
        if (!IsConnected) return;

        // button: 1=左键, 2=中键, 4=右键
        byte buttonMask = pressed ? (byte)button : (byte)0;
        SendMouseEvent(buttonMask, 0, 0);
    }

    /// <summary>
    /// 发送键盘事件
    /// </summary>
    public void SendKey(uint keySym, bool pressed)
    {
        if (!IsConnected) return;

        lock (_lock)
        {
            if (_stream == null) return;

            byte[] buffer = new byte[8];
            buffer[0] = 4; // KeyEvent message type
            buffer[1] = pressed ? (byte)1 : (byte)0;
            buffer[2] = 0; // padding
            buffer[3] = 0; // padding
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4), keySym);

            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// 发送鼠标事件（内部）
    /// </summary>
    private void SendMouseEvent(byte buttonMask, ushort x, ushort y)
    {
        lock (_lock)
        {
            if (_stream == null) return;

            byte[] buffer = new byte[6];
            buffer[0] = 5; // PointerEvent message type
            buffer[1] = buttonMask;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), x);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), y);

            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// 协议版本协商
    /// </summary>
    private async Task<bool> NegotiateVersionAsync(CancellationToken ct)
    {
        // 读取服务器版本 (12 bytes: "RFB xxx.yyy\n")
        byte[] serverVersion = await ReadExactAsync(12, ct);
        string versionStr = Encoding.ASCII.GetString(serverVersion);
        Console.WriteLine($"Server version: {versionStr.Trim()}");

        // 发送客户端版本 (支持 3.8)
        byte[] clientVersion = Encoding.ASCII.GetBytes("RFB 003.008\n");
        await _stream!.WriteAsync(clientVersion, ct);

        return true;
    }

    /// <summary>
    /// 安全认证
    /// </summary>
    private async Task<bool> AuthenticateAsync(string password, CancellationToken ct)
    {
        // 读取安全类型数量
        byte[] securityCountBuffer = await ReadExactAsync(1, ct);
        int securityCount = securityCountBuffer[0];

        if (securityCount == 0)
        {
            // 读取失败原因长度和原因
            byte[] reasonLenBuffer = await ReadExactAsync(4, ct);
            int reasonLen = BinaryPrimitives.ReadInt32BigEndian(reasonLenBuffer);
            byte[] reasonBuffer = await ReadExactAsync(reasonLen, ct);
            string reason = Encoding.UTF8.GetString(reasonBuffer);
            ErrorOccurred?.Invoke(this, $"服务器拒绝连接: {reason}");
            return false;
        }

        // 读取支持的认证类型
        byte[] securityTypes = await ReadExactAsync(securityCount, ct);
        Console.WriteLine($"Security types: {BitConverter.ToString(securityTypes)}");

        // 选择 VNC 认证 (类型 2)
        if (!Array.Exists(securityTypes, t => t == 2))
        {
            ErrorOccurred?.Invoke(this, "服务器不支持 VNC 认证");
            return false;
        }

        // 发送选择的认证类型
        await _stream!.WriteAsync(new byte[] { 2 }, ct);

        // VNC 认证挑战-响应
        byte[] challenge = await ReadExactAsync(16, ct);
        byte[] response = EncryptVncPassword(password, challenge);
        await _stream!.WriteAsync(response, ct);

        // 读取认证结果
        byte[] resultBuffer = await ReadExactAsync(4, ct);
        uint result = BinaryPrimitives.ReadUInt32BigEndian(resultBuffer);

        return result == 0; // 0 = 成功
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
        // 发送共享标志 (1 = 共享, 0 = 独占)
        await _stream!.WriteAsync(new byte[] { 1 }, ct);
        return true;
    }

    /// <summary>
    /// 服务器初始化
    /// </summary>
    private async Task<bool> ServerInitAsync(CancellationToken ct)
    {
        // 读取服务器初始化消息
        byte[] initBuffer = await ReadExactAsync(24, ct);

        ScreenWidth = BinaryPrimitives.ReadUInt16BigEndian(initBuffer.AsSpan(0, 2));
        ScreenHeight = BinaryPrimitives.ReadUInt16BigEndian(initBuffer.AsSpan(2, 2));
        int nameLength = BinaryPrimitives.ReadInt32BigEndian(initBuffer.AsSpan(20, 4));

        Console.WriteLine($"Screen size: {ScreenWidth}x{ScreenHeight}");

        // 读取桌面名称
        if (nameLength > 0)
        {
            byte[] nameBuffer = await ReadExactAsync(nameLength, ct);
            string name = Encoding.UTF8.GetString(nameBuffer);
            Console.WriteLine($"Desktop name: {name}");
        }

        return true;
    }

    /// <summary>
    /// 设置像素格式
    /// </summary>
    private async Task SetPixelFormatAsync(CancellationToken ct)
    {
        byte[] format = new byte[20];
        format[0] = 0; // SetPixelFormat
        format[1] = 0; // padding
        format[2] = 0; // padding
        format[3] = 0; // padding
        format[4] = 32; // bits per pixel
        format[5] = 24; // depth
        format[6] = 0; // big endian
        format[7] = 1; // true color
        format[8] = 0; // red max (256)
        format[9] = 255;
        format[10] = 0; // green max (256)
        format[11] = 255;
        format[12] = 0; // blue max (256)
        format[13] = 255;
        format[14] = 16; // red shift
        format[15] = 8; // green shift
        format[16] = 0; // blue shift
        // padding

        await _stream!.WriteAsync(format, ct);
    }

    /// <summary>
    /// 设置编码
    /// </summary>
    private async Task SetEncodingsAsync(CancellationToken ct)
    {
        byte[] encodings = new byte[8];
        encodings[0] = 2; // SetEncodings
        encodings[1] = 0; // padding
        encodings[2] = 0; // number of encodings (0)
        encodings[3] = 1; // 1 encoding

        // Raw encoding (0)
        encodings[4] = 0;
        encodings[5] = 0;
        encodings[6] = 0;
        encodings[7] = 0;

        await _stream!.WriteAsync(encodings, ct);
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
    }
}
