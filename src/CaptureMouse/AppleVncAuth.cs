using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CaptureMouse;

/// <summary>
/// Apple VNC 认证实现 (Security Type 30)
/// macOS Screen Sharing 使用的专有认证协议
/// 
/// 当前状态: 协议逆向分析中
/// 先读取服务器原始数据做 hex dump，再根据分析结果实现
/// </summary>
public static class AppleVncAuth
{
    /// <summary>
    /// 执行 Apple VNC 认证 (Type 30)
    /// </summary>
    public static async Task<bool> AuthenticateAsync(NetworkStream stream, string username, string password, CancellationToken ct)
    {
        Logger.Info("开始 Apple VNC 认证 (Type 30)...");

        try
        {
            // 读取服务器发送的原始数据（最多 1024 字节）
            // 使用短超时，因为服务器应该在选中认证类型后立即发送
            byte[] rawData = new byte[1024];
            int totalRead = 0;
            
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(5));
            
            try
            {
                while (totalRead < rawData.Length)
                {
                    int read = await stream.ReadAsync(
                        rawData.AsMemory(totalRead, rawData.Length - totalRead), 
                        readCts.Token);
                    if (read == 0) break;
                    totalRead += read;
                    
                    // 短暂等待看是否还有更多数据
                    await Task.Delay(100, readCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 超时是正常的，服务器数据可能已经发完
                Logger.Debug($"读取超时，已读取 {totalRead} 字节");
            }

            if (totalRead == 0)
            {
                Logger.Error("服务器未发送任何数据");
                return false;
            }

            // Hex dump 原始数据
            Logger.Info($"=== Apple VNC Type 30 原始数据 ({totalRead} 字节) ===");
            DumpHex(rawData, totalRead);

            // 分析数据结构
            Logger.Info("=== 协议分析 ===");
            
            if (totalRead >= 2)
            {
                ushort val0 = BinaryPrimitives.ReadUInt16BigEndian(rawData);
                Logger.Info($"[0-1] BE uint16 = {val0} (0x{val0:X4})");
            }
            if (totalRead >= 4)
            {
                ushort val2 = BinaryPrimitives.ReadUInt16BigEndian(rawData.AsSpan(2));
                Logger.Info($"[2-3] BE uint16 = {val2} (0x{val2:X4})");
            }
            if (totalRead >= 6)
            {
                ushort val4 = BinaryPrimitives.ReadUInt16BigEndian(rawData.AsSpan(4));
                Logger.Info($"[4-5] BE uint16 = {val4} (0x{val4:X4})");
            }

            // 尝试常见的格式:
            // 格式A: [2B g_len][g_data][2B p_len][p_data][2B key_len][key_data]
            // 格式B: [4B total_len][data...]
            // 格式C: [2B key_len][key_data]  (只有服务器公钥)
            
            if (totalRead >= 4)
            {
                ushort firstLen = BinaryPrimitives.ReadUInt16BigEndian(rawData);
                
                if (firstLen == 2 && totalRead >= 4)
                {
                    // 格式A: g_len=2, g_data是接下来的2字节
                    Logger.Info("推测格式A: [2B g_len=2][2B g][2B p_len][p_data]...");
                    
                    BigInteger g = new BigInteger(rawData.AsSpan(2, 2), isUnsigned: true);
                    Logger.Info($"  g = {g}");
                    
                    if (totalRead >= 6)
                    {
                        ushort pLen = BinaryPrimitives.ReadUInt16BigEndian(rawData.AsSpan(4));
                        Logger.Info($"  p_len = {pLen}");
                        
                        if (pLen > 0 && pLen < 1024 && totalRead >= 6 + pLen + 2)
                        {
                            byte[] pBytes = rawData.AsSpan(6, pLen).ToArray();
                            BigInteger p = new BigInteger(pBytes, isUnsigned: true);
                            Logger.Info($"  p 已读取 ({pLen} 字节), p 前8字节: {BitConverter.ToString(pBytes, 0, Math.Min(8, pLen))}");
                            
                            int keyLenOffset = 6 + pLen;
                            ushort keyLen = BinaryPrimitives.ReadUInt16BigEndian(rawData.AsSpan(keyLenOffset));
                            Logger.Info($"  server_key_len = {keyLen}");
                            
                            if (keyLen > 0 && keyLen < 1024 && totalRead >= keyLenOffset + 2 + keyLen)
                            {
                                byte[] serverKeyBytes = rawData.AsSpan(keyLenOffset + 2, keyLen).ToArray();
                                BigInteger serverKey = new BigInteger(serverKeyBytes, isUnsigned: true);
                                Logger.Info($"  server_key 已读取 ({keyLen} 字节)");
                                
                                // 执行 DH 密钥交换
                                return await PerformDHExchange(stream, g, p, pLen, serverKey, username, password, ct);
                            }
                            else
                            {
                                Logger.Warning($"  server_key_len={keyLen} 异常或数据不足 (需要 {keyLenOffset + 2 + keyLen} 字节，只有 {totalRead})");
                            }
                        }
                        else
                        {
                            Logger.Warning($"  p_len={pLen} 异常或数据不足");
                        }
                    }
                }
                else if (firstLen > 32 && firstLen < 1024 && totalRead >= firstLen + 4)
                {
                    // 格式C: 可能只有 [2B key_len][key_data]
                    Logger.Info($"推测格式C: [2B key_len={firstLen}][key_data]...");
                    
                    // 但需要 DH 参数，可能 hardcoded
                    Logger.Warning("此格式需要 hardcoded DH 参数，暂未实现");
                }
                else
                {
                    Logger.Info($"无法识别格式, firstLen={firstLen}");
                }
            }

            Logger.Error("Apple VNC Type 30 协议分析失败，请将上述日志发送给开发者");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("Apple VNC 认证异常", ex);
            return false;
        }
    }

    /// <summary>
    /// 执行 DH 密钥交换和认证
    /// </summary>
    private static async Task<bool> PerformDHExchange(NetworkStream stream, BigInteger g, BigInteger p, int pLen, BigInteger serverPublicKey, string username, string password, CancellationToken ct)
    {
        Logger.Info("开始 DH 密钥交换...");

        // 生成客户端私钥
        byte[] privateKeyBytes = new byte[pLen];
        RandomNumberGenerator.Fill(privateKeyBytes);
        BigInteger a = new BigInteger(privateKeyBytes, isUnsigned: true);
        if (a.Sign <= 0) a = BigInteger.Abs(a);
        if (a >= p) a = a % (p - BigInteger.One) + BigInteger.One;

        // 计算客户端公钥: A = g^a mod p
        BigInteger clientPublicKey = BigInteger.ModPow(g, a, p);
        byte[] clientPublicKeyBytes = clientPublicKey.ToByteArray(isUnsigned: true);
        Logger.Debug($"客户端公钥长度: {clientPublicKeyBytes.Length}");

        // 计算共享密钥: K = serverPublicKey^a mod p
        BigInteger sharedSecret = BigInteger.ModPow(serverPublicKey, a, p);
        byte[] sharedSecretBytes = sharedSecret.ToByteArray(isUnsigned: true);
        Logger.Debug($"共享密钥长度: {sharedSecretBytes.Length}");

        // 派生 AES 密钥: MD5(sharedSecret)
        byte[] aesKey;
        using (var md5 = MD5.Create())
        {
            aesKey = md5.ComputeHash(sharedSecretBytes);
        }
        Logger.Debug($"AES 密钥已派生 ({aesKey.Length * 8} bits)");

        // 加密凭据 (128 bytes = 用户名64B + 密码64B, AES-128-ECB)
        byte[] encryptedCredentials = EncryptCredentials(username, password, aesKey);
        Logger.Debug($"凭据已加密 ({encryptedCredentials.Length} 字节)");

        // 发送客户端公钥: [2B key_len][key_data]
        byte[] clientKeyLenBuf = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(clientKeyLenBuf, (ushort)clientPublicKeyBytes.Length);
        await stream.WriteAsync(clientKeyLenBuf, ct);
        await stream.WriteAsync(clientPublicKeyBytes, ct);
        Logger.Debug("已发送客户端公钥");

        // 发送加密凭据
        await stream.WriteAsync(encryptedCredentials, ct);
        Logger.Debug("已发送加密凭据");

        // 读取认证结果
        byte[] resultBuf = new byte[4];
        await ReadExactAsync(stream, resultBuf, ct);
        uint result = BinaryPrimitives.ReadUInt32BigEndian(resultBuf);

        if (result == 0)
        {
            Logger.Info("Apple VNC 认证成功!");
            return true;
        }
        else
        {
            Logger.Error($"Apple VNC 认证失败, 结果码: {result} (0x{result:X8})");
            
            // 尝试读取失败原因
            try
            {
                byte[] reasonLenBuf = new byte[4];
                using var reasonCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                reasonCts.CancelAfter(TimeSpan.FromSeconds(2));
                await ReadExactAsync(stream, reasonLenBuf, reasonCts.Token);
                int reasonLen = BinaryPrimitives.ReadInt32BigEndian(reasonLenBuf);
                if (reasonLen > 0 && reasonLen < 4096)
                {
                    byte[] reasonBuf = new byte[reasonLen];
                    await ReadExactAsync(stream, reasonBuf, reasonCts.Token);
                    string reason = Encoding.UTF8.GetString(reasonBuf);
                    Logger.Error($"失败原因: {reason}");
                }
            }
            catch { }
            
            return false;
        }
    }

    /// <summary>
    /// AES-128-ECB 加密凭据 (128 bytes)
    /// </summary>
    private static byte[] EncryptCredentials(string username, string password, byte[] aesKey)
    {
        byte[] usernameBlock = new byte[64];
        byte[] passwordBlock = new byte[64];
        
        byte[] usernameBytes = Encoding.UTF8.GetBytes(username ?? "");
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password ?? "");
        
        Array.Copy(usernameBytes, 0, usernameBlock, 0, Math.Min(usernameBytes.Length, 63));
        Array.Copy(passwordBytes, 0, passwordBlock, 0, Math.Min(passwordBytes.Length, 63));

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        byte[] encrypted = new byte[128];
        using (var encryptor = aes.CreateEncryptor())
        {
            encryptor.TransformBlock(usernameBlock, 0, 64, encrypted, 0);
            encryptor.TransformBlock(passwordBlock, 0, 64, encrypted, 64);
        }

        return encrypted;
    }

    /// <summary>
    /// Hex dump 输出
    /// </summary>
    private static void DumpHex(byte[] data, int length)
    {
        int offset = 0;
        while (offset < length)
        {
            int lineLen = Math.Min(16, length - offset);
            var hex = BitConverter.ToString(data, offset, lineLen);
            var ascii = new StringBuilder();
            for (int i = 0; i < lineLen; i++)
            {
                byte b = data[offset + i];
                ascii.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            Logger.Info($"{offset:X4}: {hex,-47} {ascii}");
            offset += 16;
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) throw new IOException("连接已关闭");
            totalRead += read;
        }
    }
}