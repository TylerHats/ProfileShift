using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProfileShift.Core
{
    /// <summary>
    /// Portable AES-256-GCM encryption using a user-supplied passphrase.
    /// Used instead of DPAPI (which is machine-bound) so that credential
    /// exports can be restored on a different machine/user/domain.
    ///
    /// File format: [16-byte salt][12-byte nonce][ciphertext][16-byte auth tag]
    /// Key derivation: PBKDF2-SHA256, 100,000 iterations, 32-byte key
    /// </summary>
    public static class PortableEncryption
    {
        private const int SaltLength = 16;
        private const int NonceLength = 12;
        private const int TagLength = 16;
        private const int KeyLength = 32; // AES-256
        private const int Iterations = 100_000;

        /// <summary>
        /// Encrypts plaintext bytes with AES-256-GCM using a passphrase-derived key.
        /// Returns the complete encrypted blob (salt + nonce + ciphertext + tag).
        /// </summary>
        public static byte[] Encrypt(byte[] plaintext, string passphrase)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceLength);
            byte[] key = DeriveKey(passphrase, salt);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagLength];

            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            // Combine: [salt][nonce][ciphertext][tag]
            byte[] result = new byte[SaltLength + NonceLength + ciphertext.Length + TagLength];
            Buffer.BlockCopy(salt, 0, result, 0, SaltLength);
            Buffer.BlockCopy(nonce, 0, result, SaltLength, NonceLength);
            Buffer.BlockCopy(ciphertext, 0, result, SaltLength + NonceLength, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, SaltLength + NonceLength + ciphertext.Length, TagLength);

            return result;
        }

        /// <summary>
        /// Decrypts an AES-256-GCM encrypted blob using a passphrase-derived key.
        /// Throws CryptographicException if the passphrase is wrong.
        /// </summary>
        public static byte[] Decrypt(byte[] encryptedBlob, string passphrase)
        {
            if (encryptedBlob.Length < SaltLength + NonceLength + TagLength)
                throw new CryptographicException("Encrypted data is too short to be valid.");

            byte[] salt = new byte[SaltLength];
            byte[] nonce = new byte[NonceLength];
            Buffer.BlockCopy(encryptedBlob, 0, salt, 0, SaltLength);
            Buffer.BlockCopy(encryptedBlob, SaltLength, nonce, 0, NonceLength);

            int ciphertextLength = encryptedBlob.Length - SaltLength - NonceLength - TagLength;
            byte[] ciphertext = new byte[ciphertextLength];
            byte[] tag = new byte[TagLength];
            Buffer.BlockCopy(encryptedBlob, SaltLength + NonceLength, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(encryptedBlob, SaltLength + NonceLength + ciphertextLength, tag, 0, TagLength);

            byte[] key = DeriveKey(passphrase, salt);
            byte[] plaintext = new byte[ciphertextLength];

            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }

        /// <summary>
        /// Encrypts plaintext bytes and writes to a file.
        /// </summary>
        public static void EncryptToFile(byte[] plaintext, string passphrase, string filePath)
        {
            byte[] encrypted = Encrypt(plaintext, passphrase);
            File.WriteAllBytes(filePath, encrypted);
        }

        /// <summary>
        /// Reads an encrypted file and decrypts it.
        /// Throws CryptographicException if the passphrase is wrong.
        /// </summary>
        public static byte[] DecryptFromFile(string filePath, string passphrase)
        {
            byte[] encrypted = File.ReadAllBytes(filePath);
            return Decrypt(encrypted, passphrase);
        }

        private static byte[] DeriveKey(string passphrase, byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(passphrase),
                salt,
                Iterations,
                HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(KeyLength);
        }
    }
}
