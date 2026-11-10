
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;
using SimpleBase;

namespace Sentinel.Infrastructure.Authentication;

public sealed class Nep413Verifier
{
    private const uint Nep413Prefix = 0x8000019D;

    public bool Verify(
        string message,
        string recipient,
        string nonceBase64,
        string publicKey,
        string signatureBase64)
    {
        try
        {
            if (!publicKey.StartsWith(
                "ed25519:", StringComparison.Ordinal))
                return false;

            var keyBytes = Base58.Bitcoin.Decode(
                publicKey["ed25519:".Length..]);

            var nonce = Convert.FromBase64String(nonceBase64);
            var signature = Convert.FromBase64String(signatureBase64);

            if (keyBytes.Length != 32 ||
                nonce.Length != 32 ||
                signature.Length != 64)
                return false;

            using var payload = new MemoryStream();

            // NEP-413 domain-separation prefix
            WriteUInt32(payload, Nep413Prefix);

            // Borsh-serialized MessagePayload fields
            WriteString(payload, message);
            payload.Write(nonce);
            WriteString(payload, recipient);

            // Option<String> callbackUrl = None
            payload.WriteByte(0);

            var hash = SHA256.HashData(payload.ToArray());

            var algorithm = SignatureAlgorithm.Ed25519;

            var key = PublicKey.Import(
                algorithm,
                keyBytes,
                KeyBlobFormat.RawPublicKey);

            return algorithm.Verify(key, hash, signature);
        }
        catch (Exception ex) when (
            ex is FormatException ||
            ex is ArgumentException ||
            ex is CryptographicException)
        {
            return false;
        }
    }

    private static void WriteUInt32(
        Stream stream,
        uint value)
    {
        Span<byte> bytes = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes, value);

        stream.Write(bytes);
    }

    private static void WriteString(
        Stream stream,
        string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);

        WriteUInt32(stream, checked((uint)bytes.Length));

        stream.Write(bytes);
    }
}
