using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CaptureMouse;

/// <summary>
/// Apple VNC 认证实现 (Security Type 30)
/// macOS Screen Sharing 使用的专有认证协议
/// 基于 Diffie-Hellman 密钥交换 + AES-128-ECB 加密
/// 
/// 协议流程:
/// 1. Server 发送: [2B g_len][g][2B p_len][p][2B server_key_len][server_key]
/// 2. Client 发送: [2B client_key_len][client_key][128B encrypted_creds]
/// 3. Server 发送: [4B auth_result]
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
            // Step 1: 读取服务器 DH 参数 (generator, prime, public key)
            Logger.Debug("读取服务器 DH 参数...");

            // 读取 generator g
            byte[] gLenBuf = new byte[2];
            await ReadExactAsync(stream, gLenBuf, ct);
            int gLen = BinaryPrimitives.ReadUInt16BigEndian(gLenBuf);
            Logger.Debug($"DH generator 长度: {gLen}");

            byte[] gBytes = new byte[gLen];
            await ReadExactAsync(stream, gBytes, ct);
            BigInteger g = new BigInteger(gBytes, isUnsigned: true);
            Logger.Debug($"DH generator 已读取");

            // 读取 prime p
            byte[] pLenBuf = new byte[2];
            await ReadExactAsync(stream, pLenBuf, ct);
            int pLen = BinaryPrimitives.ReadUInt16BigEndian(pLenBuf);
            Logger.Debug($"DH prime 长度: {pLen}");

            byte[] pBytes = new byte[pLen];
            await ReadExactAsync(stream, pBytes, ct);
            BigInteger p = new BigInteger(pBytes, isUnsigned: true);
            Logger.Debug($"DH prime 已读取");

            // 读取服务器公钥
            byte[] serverKeyLenBuf = new byte[2];
            await ReadExactAsync(stream, serverKeyLenBuf, ct);
            int serverKeyLen = BinaryPrimitives.ReadUInt16BigEndian(serverKeyLenBuf);
            Logger.Debug($"服务器 DH 公钥长度: {serverKeyLen}");

            byte[] serverPublicKeyBytes = new byte[serverKeyLen];
            await ReadExactAsync(stream, serverPublicKeyBytes, ct);
            BigInteger serverPublicKey = new BigInteger(serverPublicKeyBytes, isUnsigned: true);
            Logger.Debug("服务器 DH 公钥已读取");

            // Step 2: 生成客户端 DH 密钥对
            Logger.Debug("生成客户端 DH 密钥对...");
            byte[] privateKey = new byte[pLen];
            RandomNumberGenerator.Fill(privateKey);
            BigInteger a = new BigInteger(privateKey, isUnsigned: true);
            
            // 确保私钥在合理范围内
            if (a.Sign <= 0) a = BigInteger.Abs(a);
            if (a >= p) a = a % (p - 1) + 1;

            // 计算客户端公钥: A = g^a mod p
            BigInteger clientPublicKey = BigInteger.ModPow(g, a, p);
            byte[] clientPublicKeyBytes = clientPublicKey.ToByteArray(isUnsigned: true);
            Logger.Debug($"客户端 DH 公钥长度: {clientPublicKeyBytes.Length}");

            // 计算共享密钥: K = serverPublicKey^a mod p
            BigInteger sharedSecret = BigInteger.ModPow(serverPublicKey, a, p);
            byte[] sharedSecretBytes = sharedSecret.ToByteArray(isUnsigned: true);
            Logger.Debug($"共享密钥长度: {sharedSecretBytes.Length}");

            // Step 3: 从共享密钥派生 AES 密钥
            byte[] aesKey = DeriveAesKey(sharedSecretBytes);
            Logger.Debug($"AES 密钥已派生, 长度: {aesKey.Length * 8} bits");

            // Step 4: 加密凭据 (128 bytes)
            byte[] encryptedCredentials = EncryptCredentials(username, password, aesKey);
            Logger.Debug($"凭据已加密, 长度: {encryptedCredentials.Length}");

            // Step 5: 发送客户端 DH 公钥
            byte[] clientKeyLenBuf = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(clientKeyLenBuf, (ushort)clientPublicKeyBytes.Length);
            await stream.WriteAsync(clientKeyLenBuf, ct);
            await stream.WriteAsync(clientPublicKeyBytes, ct);
            Logger.Debug("已发送客户端 DH 公钥");

            // Step 6: 发送加密的凭据
            await stream.WriteAsync(encryptedCredentials, ct);
            Logger.Debug("已发送加密凭据");

            // Step 7: 读取认证结果
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
                Logger.Error($"Apple VNC 认证失败, 结果码: {result}");
                // 读取失败原因（如果有）
                try
                {
                    byte[] reasonLenBuf = new byte[4];
                    await ReadExactAsync(stream, reasonLenBuf, ct);
                    int reasonLen = BinaryPrimitives.ReadInt32BigEndian(reasonLenBuf);
                    if (reasonLen > 0 && reasonLen < 4096)
                    {
                        byte[] reasonBuf = new byte[reasonLen];
                        await ReadExactAsync(stream, reasonBuf, ct);
                        string reason = Encoding.UTF8.GetString(reasonBuf);
                        Logger.Error($"失败原因: {reason}");
                    }
                }
                catch { }
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Apple VNC 认证异常", ex);
            return false;
        }
    }

    /// <summary>
    /// 从 DH 共享密钥派生 AES-128 密钥
    /// Apple VNC 使用 MD5(sharedSecret) 作为 AES 密钥
    /// </summary>
    private static byte[] DeriveAesKey(byte[] sharedSecret)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(sharedSecret);
    }

    /// <summary>
    /// 使用 AES 加密凭据
    /// 
    /// Apple VNC 凭据格式 (128 bytes):
    /// - 前 64 字节: 用户名加密块 (64 bytes 明文 = 最多63字符用户名 + null padding)
    /// - 后 64 字节: 密码加密块 (64 bytes 明文 = 最多63字符密码 + null padding)
    /// 
    /// 每个块使用 AES-128-ECB 独立加密
    /// </summary>
    private static byte[] EncryptCredentials(string username, string password, byte[] aesKey)
    {
        // 准备用户名明文块 (64 bytes)
        byte[] usernameBlock = new byte[64];
        byte[] usernameBytes = Encoding.UTF8.GetBytes(username ?? "");
        Array.Copy(usernameBytes, 0, usernameBlock, 0, Math.Min(usernameBytes.Length, 63));

        // 准备密码明文块 (64 bytes)
        byte[] passwordBlock = new byte[64];
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password ?? "");
        Array.Copy(passwordBytes, 0, passwordBlock, 0, Math.Min(passwordBytes.Length, 63));

        // AES-128-ECB 加密
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