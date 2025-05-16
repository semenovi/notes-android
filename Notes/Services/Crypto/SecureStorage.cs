namespace Notes.Services.Crypto;

public class SecureStorage
{
  private readonly CryptoService _cryptoService;
  private readonly Dictionary<string, byte[]> _secureData = new Dictionary<string, byte[]>();

  public SecureStorage(CryptoService cryptoService)
  {
    _cryptoService = cryptoService;
  }

  public void SaveSecureData(string key, byte[] data)
  {
    if (!_cryptoService.IsInitialized)
      throw new InvalidOperationException("CryptoService is not initialized.");

    byte[] encryptedData = _cryptoService.Encrypt(data);
    _secureData[key] = encryptedData;
  }

  public byte[] GetSecureData(string key)
  {
    if (!_cryptoService.IsInitialized)
      throw new InvalidOperationException("CryptoService is not initialized.");

    if (!_secureData.TryGetValue(key, out byte[] encryptedData))
      throw new KeyNotFoundException($"Key {key} not found in secure storage.");

    return _cryptoService.Decrypt(encryptedData);
  }

  public bool DeleteSecureData(string key)
  {
    return _secureData.Remove(key);
  }

  public bool ContainsKey(string key)
  {
    return _secureData.ContainsKey(key);
  }
}