using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Notes.Services.Security
{
    public class CryptoService
    {
        private byte[] _masterKey;
        private readonly int _iterations = 10000;
        private readonly int _keySize = 32; // 256 bits
        private readonly int _saltSize = 16;
        private readonly int _ivSize = 16;
        private readonly long _timeLimitSeconds = 30; // OTP time limit

        public bool IsInitialized => _masterKey != null;

        public void Initialize(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be empty", nameof(password));

            // Create a key derivation function to generate a strong key from the password
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var salt = new byte[_saltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            _masterKey = DeriveKeyFromPassword(password, salt);
        }

        public byte[] Encrypt(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            if (_masterKey == null)
                throw new InvalidOperationException("CryptoService not initialized. Call Initialize first.");

            // Generate a random IV
            byte[] iv = new byte[_ivSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }

            // Encrypt the data
            using (var aes = Aes.Create())
            {
                aes.Key = _masterKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] encryptedData = encryptor.TransformFinalBlock(data, 0, data.Length);
                    
                    // Combine IV and encrypted data
                    byte[] result = new byte[iv.Length + encryptedData.Length];
                    Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                    Buffer.BlockCopy(encryptedData, 0, result, iv.Length, encryptedData.Length);
                    
                    return result;
                }
            }
        }

        public byte[] Decrypt(byte[] encryptedData)
        {
            if (encryptedData == null || encryptedData.Length <= _ivSize)
                throw new ArgumentException("Encrypted data is invalid", nameof(encryptedData));

            if (_masterKey == null)
                throw new InvalidOperationException("CryptoService not initialized. Call Initialize first.");

            // Extract the IV from the encrypted data
            byte[] iv = new byte[_ivSize];
            Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);

            // Extract the actual encrypted data
            byte[] actualEncryptedData = new byte[encryptedData.Length - iv.Length];
            Buffer.BlockCopy(encryptedData, iv.Length, actualEncryptedData, 0, actualEncryptedData.Length);

            // Decrypt the data
            using (var aes = Aes.Create())
            {
                aes.Key = _masterKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(actualEncryptedData, 0, actualEncryptedData.Length);
                }
            }
        }

        public string GenerateOtp()
        {
            // Generate a TOTP (Time-Based One-Time Password)
            if (_masterKey == null)
                throw new InvalidOperationException("CryptoService not initialized. Call Initialize first.");

            // Get the current time in seconds and divide by the time limit
            long timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _timeLimitSeconds;
            
            // Convert the time step to bytes
            byte[] timeBytes = BitConverter.GetBytes(timeStep);
            
            // Combine with the master key and compute HMAC
            using (var hmac = new HMACSHA1(_masterKey))
            {
                byte[] hash = hmac.ComputeHash(timeBytes);
                
                // Get the offset (last 4 bits of the hash)
                int offset = hash[hash.Length - 1] & 0x0F;
                
                // Get 4 bytes starting at the offset
                int binary = ((hash[offset] & 0x7F) << 24) |
                             ((hash[offset + 1] & 0xFF) << 16) |
                             ((hash[offset + 2] & 0xFF) << 8) |
                             (hash[offset + 3] & 0xFF);
                
                // Generate a 6-digit code
                int otp = binary % 1000000;
                return otp.ToString("D6");
            }
        }

        public bool ValidateOtp(string otp)
        {
            if (string.IsNullOrEmpty(otp) || otp.Length != 6 || !int.TryParse(otp, out _))
                return false;

            // Check if the provided OTP matches the current one
            string currentOtp = GenerateOtp();
            
            // Also check the previous time step to allow for some clock drift
            long previousTimeStep = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _timeLimitSeconds) / _timeLimitSeconds;
            byte[] timeBytes = BitConverter.GetBytes(previousTimeStep);
            
            string previousOtp;
            using (var hmac = new HMACSHA1(_masterKey))
            {
                byte[] hash = hmac.ComputeHash(timeBytes);
                int offset = hash[hash.Length - 1] & 0x0F;
                int binary = ((hash[offset] & 0x7F) << 24) |
                             ((hash[offset + 1] & 0xFF) << 16) |
                             ((hash[offset + 2] & 0xFF) << 8) |
                             (hash[offset + 3] & 0xFF);
                int otpValue = binary % 1000000;
                previousOtp = otpValue.ToString("D6");
            }
            
            return otp == currentOtp || otp == previousOtp;
        }

        private byte[] DeriveKeyFromPassword(string password, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(password),
                salt,
                _iterations,
                HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(_keySize);
            }
        }
    }
}