using System.Security.Cryptography;
using System.Text;

namespace Notes.Services.Crypto;

public static class SyncCryptoHelper
{
  // Derives a 256-bit AES key from the shared API token.
  // All devices with the same token produce the same key automatically.
  public static byte[] DeriveKeyFromToken(string apiToken)
  {
    byte[] ikm = Encoding.UTF8.GetBytes(apiToken);
    byte[] salt = Encoding.UTF8.GetBytes("notes-sync-v1");
    return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt);
  }

  public static byte[] AesEncrypt(byte[] data, byte[] key)
  {
    using var aes = Aes.Create();
    aes.Key = key;
    aes.GenerateIV();
    using var ms = new MemoryStream();
    ms.Write(aes.IV, 0, aes.IV.Length);
    using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
    {
      cs.Write(data, 0, data.Length);
      cs.FlushFinalBlock();
    }
    return ms.ToArray();
  }

  public static byte[] AesDecrypt(byte[] encryptedData, byte[] key)
  {
    using var aes = Aes.Create();
    var iv = new byte[aes.BlockSize / 8];
    Array.Copy(encryptedData, 0, iv, 0, iv.Length);
    using var ms = new MemoryStream();
    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(key, iv), CryptoStreamMode.Write))
    {
      cs.Write(encryptedData, iv.Length, encryptedData.Length - iv.Length);
      cs.FlushFinalBlock();
    }
    return ms.ToArray();
  }
}
