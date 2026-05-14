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
/// 协议格式 (通过 hex dump 逆向分析):
/// Server → Client: [2B g][2B p_len][p_len bytes p][p_len bytes server_key]
/// Client → Server: [2B key_len][key_len bytes client_key][128B encrypted_creds]
/// Server → Client: [4B auth_result]
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
            // Step 1: 读取 generator (2 bytes, big-endian)
            byte[] gBuf = new byte[2];
            await ReadExactAsync(stream, gBuf, ct);
            int g = BinaryPrimitives.ReadUInt16BigEndian(gBuf);
            Logger.Info($"DH generator g = {g}");

            // Step 2: 读取 prime 长度 (2 bytes, big-endian)
            byte[] pLenBuf = new byte[2];
            await ReadExactAsync(stream, pLenBuf, ct);
            int pLen = BinaryPrimitives.ReadUInt16BigEndian(pLenBuf);
            Logger.Info($"DH prime 长度 = {pLen} bytes ({pLen * 8} bits)");

            if (pLen <= 0 || pLen > 1024)
            {
                Logger.Error($"无效的 prime 长度: {pLen}");
                return false;
            }

            // Step 3: 读取 prime p (pLen bytes)
            byte[] pBytes = new byte[pLen];
            await ReadExactAsync(stream, pBytes, ct);
            BigInteger p = new BigInteger(pBytes, isUnsigned: true);
            Logger.Info($"DH prime 已读取 ({pLen} bytes)");

            // Step 4: 读取服务器公钥 (pLen bytes, 长度等于 prime 长度)
            byte[] serverKeyBytes = new byte[pLen];
            await ReadExactAsync(stream, serverKeyBytes, ct);
            BigInteger serverPublicKey = new BigInteger(serverKeyBytes, isUnsigned: true);
            Logger.Info($"服务器 DH 公钥已读取 ({pLen} bytes)");

            // Step 5: 执行 DH 密钥交换
            return await PerformDHExchange(stream, g, p, pLen, serverPublicKey, username, password, ct);
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
    private static async Task<bool> PerformDHExchange(NetworkStream stream, int g, BigInteger p, int pLen, BigInteger serverPublicKey, string username, string password, CancellationToken ct)
    {
        Logger.Info("开始 DH 密钥交换...");

        BigInteger gBig = new BigInteger(g);

        // 生成客户端私钥 (与 prime 同长度)
        byte[] privateKeyBytes = new byte[pLen];
        RandomNumberGenerator.Fill(privateKeyBytes);
        BigInteger a = new BigInteger(privateKeyBytes, isUnsigned: true);
        if (a.Sign <= 0) a = BigInteger.Abs(a);
        if (a >= p) a = a % (p - BigInteger.One) + BigInteger.One;

        // 计算客户端公钥: A = g^a mod p
        BigInteger clientPublicKey = BigInteger.ModPow(gBig, a, p);
        byte[] clientPublicKeyBytes = PadToLength(clientPublicKey.ToByteArray(isUnsigned: true), pLen);
        Logger.Info($"客户端公钥长度: {clientPublicKeyBytes.Length}");

        // 计算共享密钥: K = serverPublicKey^a mod p
        BigInteger sharedSecret = BigInteger.ModPow(serverPublicKey, a, p);
        byte[] sharedSecretBytes = sharedSecret.ToByteArray(isUnsigned: true);
        Logger.Info($"共享密钥长度: {sharedSecretBytes.Length}");

        // 派生 AES 密钥: MD5(sharedSecret)
        byte[] aesKey;
        using (var md5 = MD5.Create())
        {
            aesKey = md5.ComputeHash(sharedSecretBytes);
        }
        Logger.Info($"AES 密钥已派生 ({aesKey.Length * 8} bits)");

        // 加密凭据 (128 bytes = 用户名64B + 密码64B, AES-128-ECB)
        byte[] encryptedCredentials = EncryptCredentials(username, password, aesKey);
        Logger.Info($"凭据已加密 ({encryptedCredentials.Length} bytes)");

        // 发送客户端公钥: [2B key_len][key_data]
        byte[] clientKeyLenBuf = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(clientKeyLenBuf, (ushort)clientPublicKeyBytes.Length);
        await stream.WriteAsync(clientKeyLenBuf, ct);
        await stream.WriteAsync(clientPublicKeyBytes, ct);
        Logger.Info($"已发送客户端公钥 ({clientPublicKeyBytes.Length} bytes)");

        // 发送加密凭据
        await stream.WriteAsync(encryptedCredentials, ct);
        Logger.Info($"已发送加密凭据 ({encryptedCredentials.Length} bytes)");

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
    /// 将字节数组填充到指定长度（前补零）
    /// </summary>
    private static byte[] PadToLength(byte[] data, int targetLength)
    {
        if (data.Length >= targetLength) return data;
        byte[] padded = new byte[targetLength];
        Array.Copy(data, 0, padded, targetLength - data.Length, data.Length);
        return padded;
    }

    /// <summary>
    /// AES-128-ECB 加密凭据 (128 bytes)
    /// 用户名和密码各占 64 字节明文块，独立用 AES-128-ECB 加密
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