using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CaptureMouse;

/// <summary>
/// Apple VNC 认证实现 (Security Type 30)
/// macOS Screen Sharing 使用的专有认证协议
/// 基于 Diffie-Hellman 密钥交换 + AES-128-CBC 加密
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
            // Step 1: 读取服务器的 DH 公钥
            Logger.Debug("读取服务器 DH 公钥...");
            byte[] serverKeyLenBuf = new byte[2];
            await ReadExactAsync(stream, serverKeyLenBuf, ct);
            int serverKeyLen = BinaryPrimitives.ReadUInt16BigEndian(serverKeyLenBuf);
            Logger.Debug($"服务器 DH 公钥长度: {serverKeyLen}");

            if (serverKeyLen <= 0 || serverKeyLen > 8192)
            {
                Logger.Error($"无效的服务器公钥长度: {serverKeyLen}");
                return false;
            }

            byte[] serverPublicKey = new byte[serverKeyLen];
            await ReadExactAsync(stream, serverPublicKey, ct);
            Logger.Debug("已读取服务器 DH 公钥");

            // Step 2: 生成客户端 DH 密钥对
            Logger.Debug("生成客户端 DH 密钥对...");
            using var dh = new DiffieHellman();
            byte[] clientPublicKey = dh.GeneratePublicKey();
            byte[] sharedSecret = dh.ComputeSharedSecret(serverPublicKey);
            Logger.Debug($"客户端 DH 公钥长度: {clientPublicKey.Length}, 共享密钥长度: {sharedSecret.Length}");

            // Step 3: 从共享密钥派生 AES 密钥
            byte[] aesKey = DeriveAesKey(sharedSecret);
            Logger.Debug($"AES 密钥已派生, 长度: {aesKey.Length * 8} bits");

            // Step 4: 加密凭据
            byte[] encryptedCredentials = EncryptCredentials(username, password, aesKey);
            Logger.Debug($"凭据已加密, 长度: {encryptedCredentials.Length}");

            // Step 5: 发送客户端 DH 公钥
            byte[] clientKeyLenBuf = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(clientKeyLenBuf, (ushort)clientPublicKey.Length);
            await stream.WriteAsync(clientKeyLenBuf, ct);
            await stream.WriteAsync(clientPublicKey, ct);
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
    /// 使用 MD5 哈希共享密钥的前 16 字节
    /// </summary>
    private static byte[] DeriveAesKey(byte[] sharedSecret)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(sharedSecret);
    }

    /// <summary>
    /// 使用 AES-128-CBC 加密凭据
    /// 格式: [32 bytes username, null-padded] [32 bytes password, null-padded]
    /// IV: 全零
    /// </summary>
    private static byte[] EncryptCredentials(string username, string password, byte[] aesKey)
    {
        // 准备凭据明文 (64 bytes)
        byte[] plaintext = new byte[64];
        byte[] usernameBytes = Encoding.UTF8.GetBytes(username ?? "");
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password ?? "");

        Array.Copy(usernameBytes, 0, plaintext, 0, Math.Min(usernameBytes.Length, 32));
        Array.Copy(passwordBytes, 0, plaintext, 32, Math.Min(passwordBytes.Length, 32));

        // AES-128-CBC 加密, IV = 全零
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = new byte[16]; // 全零 IV
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();
        byte[] encrypted = new byte[64];
        encryptor.TransformBlock(plaintext, 0, 64, encrypted, 0);

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

    /// <summary>
    /// 使用 Windows CNG API 实现的 Diffie-Hellman 密钥交换
    /// 使用 RFC 3526 MODP Group 14 (2048-bit) 参数
    /// </summary>
    private class DiffieHellman : IDisposable
    {
        // RFC 3526 MODP Group 14 (2048-bit) 素数
        private static readonly byte[] ModpGroup14Prime = HexToBytes(
            "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
            "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
            "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
            "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
            "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
            "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
            "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
            "670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
            "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9" +
            "DE2BCBF6955817183995497CEA956AE515D2261898FA0510" +
            "15728E5A8AACAA68FFFFFFFFFFFFFFFF");

        private const int Generator = 2;

        private byte[]? _privateKey;
        private byte[]? _publicKey;

        /// <summary>
        /// 生成 DH 公钥
        /// </summary>
        public byte[] GeneratePublicKey()
        {
            // 生成随机私钥 (256 bytes = 2048 bits)
            _privateKey = new byte[256];
            RandomNumberGenerator.Fill(_privateKey);

            // 计算公钥: g^privateKey mod p
            _publicKey = ModPow(Generator, _privateKey, ModpGroup14Prime);
            return _publicKey;
        }

        /// <summary>
        /// 计算共享密钥
        /// </summary>
        public byte[] ComputeSharedSecret(byte[] serverPublicKey)
        {
            if (_privateKey == null) throw new InvalidOperationException("请先生成公钥");
            return ModPow(serverPublicKey, _privateKey, ModpGroup14Prime);
        }

        /// <summary>
        /// 大数模幂运算: base^exp mod modulus
        /// </summary>
        private static byte[] ModPow(int baseVal, byte[] exp, byte[] modulus)
        {
            // 使用 .NET 的 BigInteger 进行模幂运算
            var b = new System.Numerics.BigInteger(baseVal);
            var e = new System.Numerics.BigInteger(exp, isUnsigned: true);
            var m = new System.Numerics.BigInteger(modulus, isUnsigned: true);

            var result = System.Numerics.BigInteger.ModPow(b, e, m);

            // 转换回字节数组，补齐到与模数相同长度
            byte[] resultBytes = result.ToByteArray(isUnsigned: true);
            if (resultBytes.Length < modulus.Length)
            {
                byte[] padded = new byte[modulus.Length];
                Array.Copy(resultBytes, 0, padded, modulus.Length - resultBytes.Length, resultBytes.Length);
                return padded;
            }
            return resultBytes;
        }

        /// <summary>
        /// 大数模幂运算 (字节数组版本)
        /// </summary>
        private static byte[] ModPow(byte[] baseVal, byte[] exp, byte[] modulus)
        {
            var b = new System.Numerics.BigInteger(baseVal, isUnsigned: true);
            var e = new System.Numerics.BigInteger(exp, isUnsigned: true);
            var m = new System.Numerics.BigInteger(modulus, isUnsigned: true);

            var result = System.Numerics.BigInteger.ModPow(b, e, m);

            byte[] resultBytes = result.ToByteArray(isUnsigned: true);
            if (resultBytes.Length < modulus.Length)
            {
                byte[] padded = new byte[modulus.Length];
                Array.Copy(resultBytes, 0, padded, modulus.Length - resultBytes.Length, resultBytes.Length);
                return padded;
            }
            return resultBytes;
        }

        private static byte[] HexToBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        public void Dispose()
        {
            if (_privateKey != null)
            {
                Array.Clear(_privateKey, 0, _privateKey.Length);
                _privateKey = null;
            }
        }
    }
}