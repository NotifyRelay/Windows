using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace NotifyRelay.Helpers;

public class EcdhHelper
{
    // ECDH key pair generator
    public static AsymmetricCipherKeyPair GetKeyPair()
    {
        var ecParams = SecNamedCurves.GetByName("secp256r1");
        var ecDomainParameters = new ECDomainParameters(ecParams.Curve, ecParams.G, ecParams.N, ecParams.H);

        var keyPairGenerator = new ECKeyPairGenerator();
        var keyGenParams = new ECKeyGenerationParameters(ecDomainParameters, new SecureRandom());
        keyPairGenerator.Init(keyGenParams);
        return keyPairGenerator.GenerateKeyPair();
    }

    public static byte[] DeriveKey(string remotePublicKeyBase64, byte[] privateKey)
    {
        // Reconstruct the key pair
        var ecParams = SecNamedCurves.GetByName("secp256r1");
        var ecDomainParameters = new ECDomainParameters(ecParams.Curve, ecParams.G, ecParams.N, ecParams.H);

        var privateKeyParameters = new ECPrivateKeyParameters(
            new Org.BouncyCastle.Math.BigInteger(1, privateKey),
            ecDomainParameters);
        byte[] rawPointBytes = Convert.FromBase64String(remotePublicKeyBase64);
        var point = ecParams.Curve.DecodePoint(rawPointBytes);
        var publicKeyParameters = new ECPublicKeyParameters(point,
            new ECDomainParameters(ecParams.Curve, ecParams.G, ecParams.N, ecParams.H));

        // 1. ECDH 密钥协商
        var agreement = AgreementUtilities.GetBasicAgreement("ECDH");
        agreement.Init(privateKeyParameters);
        var sharedSecret = agreement.CalculateAgreement(publicKeyParameters);
        var sharedSecretBytes = sharedSecret.ToByteArrayUnsigned();

        // 2. HKDF-Extract: HMAC-SHA256(salt=zeros(32), ikm=ECDH_shared_secret)
        var salt = new byte[32]; // 全零
        byte[] prk;
        using (var hmac = new HMACSHA256(salt))
        {
            prk = hmac.ComputeHash(sharedSecretBytes);
        }

        // 3. HKDF-Expand: 派生 32 字节密钥
        //    info = "NotifyRelay-ECDH-v1"（与 Android 端一致，同一字符串两端相同）
        var infoBytes = Encoding.UTF8.GetBytes("NotifyRelay-ECDH-v1");

        using var hmacExpand = new HMACSHA256(prk);
        hmacExpand.Initialize();
        hmacExpand.TransformBlock(infoBytes, 0, infoBytes.Length, infoBytes, 0);
        hmacExpand.TransformFinalBlock([1], 0, 1);
        var okm = hmacExpand.Hash ?? [];

        return okm.Take(32).ToArray();
    }

    public static string GenerateNonce()
    {
        var nonce = new byte[32];
        new SecureRandom().NextBytes(nonce);
        return Convert.ToBase64String(nonce);
    }

    public static string GenerateProof(byte[] sharedSecret, string nonce)
    {
        var hmac = new Org.BouncyCastle.Crypto.Macs.HMac(new Sha256Digest());
        hmac.Init(new KeyParameter(sharedSecret));

        var nonceBytes = Convert.FromBase64String(nonce);
        hmac.BlockUpdate(nonceBytes, 0, nonceBytes.Length);

        var proof = new byte[hmac.GetMacSize()];
        hmac.DoFinal(proof, 0);

        return Convert.ToBase64String(proof);
    }

    public static bool VerifyProof(byte[] sharedSecret, string nonce, string proof)
    {
        var expectedProof = GenerateProof(sharedSecret, nonce);
        return expectedProof == proof;
    }

    /// <summary>
    /// 从密钥对获取 Base64 编码的公钥（未压缩点 65 字节）
    /// </summary>
    public static string GetPublicKeyBase64(AsymmetricCipherKeyPair keyPair)
    {
        var publicKey = (ECPublicKeyParameters)keyPair.Public;
        var encodedPoint = publicKey.Q.GetEncoded(false);
        return Convert.ToBase64String(encodedPoint);
    }

    /// <summary>
    /// ECDH 密钥协商 + HKDF-SHA256 派生，返回 32 字节
    /// </summary>
    public static byte[] DeriveSharedSecret(AsymmetricCipherKeyPair keyPair, string remotePublicKeyBase64)
    {
        var privateKey = (ECPrivateKeyParameters)keyPair.Private;
        var privateKeyBytes = privateKey.D.ToByteArrayUnsigned();
        return DeriveKey(remotePublicKeyBase64, privateKeyBytes);
    }

    /// <summary>
    /// 从 byte[] 私钥重建密钥对并协商
    /// </summary>
    public static byte[] DeriveSharedSecretFromPrivate(byte[] privateKeyBytes, string remotePublicKeyBase64)
    {
        return DeriveKey(remotePublicKeyBase64, privateKeyBytes);
    }

    /// <summary>
    /// 检测密钥格式：true=ECDH（非UUID格式），false=旧UUID格式
    /// </summary>
    public static bool IsEcdhFormat(string publicKey)
    {
        if (string.IsNullOrEmpty(publicKey)) return false;
        if (publicKey.Length == 32 && Regex.IsMatch(publicKey, "^[0-9a-fA-F]{32}$")) return false;
        return true;
    }

    /// <summary>
    /// 序列化私钥为 byte[]（用于存储）
    /// </summary>
    public static byte[] SerializePrivateKey(AsymmetricCipherKeyPair keyPair)
    {
        var privateKey = (ECPrivateKeyParameters)keyPair.Private;
        return privateKey.D.ToByteArrayUnsigned();
    }

    /// <summary>
    /// 从 byte[] 私钥 + 公钥 Base64 反序列化密钥对
    /// </summary>
    public static AsymmetricCipherKeyPair DeserializeKeyPair(byte[] privateKeyBytes, string publicKeyBase64)
    {
        var ecParams = SecNamedCurves.GetByName("secp256r1");
        var ecDomainParameters = new ECDomainParameters(ecParams.Curve, ecParams.G, ecParams.N, ecParams.H);

        var privateKeyParameters = new ECPrivateKeyParameters(
            new BigInteger(1, privateKeyBytes),
            ecDomainParameters);

        byte[] rawPointBytes = Convert.FromBase64String(publicKeyBase64);
        var point = ecParams.Curve.DecodePoint(rawPointBytes);
        var publicKeyParameters = new ECPublicKeyParameters(point, ecDomainParameters);

        return new AsymmetricCipherKeyPair(publicKeyParameters, privateKeyParameters);
    }

    /// <summary>
    /// Generates a random password with 12 characters containing uppercase letters, 
    /// lowercase letters, numbers, and special characters
    /// </summary>
    public static string GenerateRandomPassword()
    {
        const string allowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                                    "abcdefghijklmnopqrstuvwxyz" +
                                    "0123456789" +
                                    "!@#$%^&*";

        return new string(Enumerable.Range(1, 12)
            .Select(_ => allowedChars[Random.Shared.Next(allowedChars.Length)])
            .ToArray());
    }
}
