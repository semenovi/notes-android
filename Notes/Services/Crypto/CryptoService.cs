using System.Security.Cryptography;
using System.Text;

namespace Notes.Services.Crypto;

public class CryptoService
{
  private byte[]? _masterKey;
  private byte[]? _salt;
  private const int KEY_SIZE = 32;
  private const int SALT_SIZE = 16;
  private const int ITERATIONS = 10000;

  public bool IsInitialized => _masterKey != null;

  public void Initialize(string password)
  {
    _salt = GenerateSalt();
    _masterKey = DeriveKey(password, _salt);
  }

  public byte[] Encrypt(byte[] data)
  {
    if (_masterKey == null)
      throw new InvalidOperationException("CryptoService is not initialized. Call Initialize first.");

    using (Aes aes = Aes.Create())
    {
      aes.Key = _masterKey;
      aes.GenerateIV();

      using (var encryptor = aes.CreateEncryptor())
      using (var ms = new MemoryStream())
      {
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
          cs.Write(data, 0, data.Length);
          cs.FlushFinalBlock();
        }

        return ms.ToArray();
      }
    }
  }

  public byte[] Decrypt(byte[] encryptedData)
  {
    if (_masterKey == null)
      throw new InvalidOperationException("CryptoService is not initialized. Call Initialize first.");

    using (Aes aes = Aes.Create())
    {
      byte[] iv = new byte[aes.IV.Length];
      Array.Copy(encryptedData, 0, iv, 0, iv.Length);

      using (var ms = new MemoryStream())
      {
        using (var decryptor = aes.CreateDecryptor(_masterKey, iv))
        using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
        {
          cs.Write(encryptedData, iv.Length, encryptedData.Length - iv.Length);
          cs.FlushFinalBlock();
        }

        return ms.ToArray();
      }
    }
  }

  public string GenerateOtp()
  {
    long timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
    return GenerateOtpForTimeStep(timeStep);
  }

  public bool ValidateOtp(string otp)
  {
    if (_masterKey == null)
      throw new InvalidOperationException("CryptoService is not initialized. Call Initialize first.");

    long currentTimeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

    for (int i = -1; i <= 1; i++)
    {
      string validOtp = GenerateOtpForTimeStep(currentTimeStep + i);
      if (validOtp == otp)
        return true;
    }

    return false;
  }

  private string GenerateOtpForTimeStep(long timeStep)
  {
    if (_masterKey == null)
      throw new InvalidOperationException("CryptoService is not initialized. Call Initialize first.");

    byte[] timeBytes = BitConverter.GetBytes(timeStep);
    if (BitConverter.IsLittleEndian)
      Array.Reverse(timeBytes);

    using (HMACSHA1 hmac = new HMACSHA1(_masterKey))
    {
      byte[] hash = hmac.ComputeHash(timeBytes);
      int offset = hash[hash.Length - 1] & 0xf;
      int binary =
          ((hash[offset] & 0x7f) << 24) |
          ((hash[offset + 1] & 0xff) << 16) |
          ((hash[offset + 2] & 0xff) << 8) |
          (hash[offset + 3] & 0xff);

      int password = binary % 1000000;
      return password.ToString("D6");
    }
  }

  private byte[] GenerateSalt()
  {
    byte[] salt = new byte[SALT_SIZE];
    using (var rng = RandomNumberGenerator.Create())
    {
      rng.GetBytes(salt);
    }
    return salt;
  }

  private byte[] DeriveKey(string password, byte[] salt)
  {
    using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, ITERATIONS, HashAlgorithmName.SHA256))
    {
      return pbkdf2.GetBytes(KEY_SIZE);
    }
  }
}