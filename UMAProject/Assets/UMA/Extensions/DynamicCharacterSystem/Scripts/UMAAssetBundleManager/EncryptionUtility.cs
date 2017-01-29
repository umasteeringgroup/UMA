using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace UMAAssetBundleManager
{
	public static class EncryptionUtil
	{
		public static byte[] Key;
		public static byte[] IV;

		public static string EncodeFileName(string text, string salt = "")
		{
			var textBytes = Encoding.UTF8.GetBytes(text);
			var res = Convert.ToBase64String(textBytes);
			return res.ToLower();
		}
		
		public static bool PasswordValid(string password)
		{
			if (password.Length < 16)
			{
				Debug.LogWarning("An encryption Key must be 16 characters long or more");
				return false;
			}
			else
				return true;
		}

		public static string GenerateRandomPW(int length = 16)
		{
			byte[] b = new byte[length];
			RandomNumberGenerator rg = RandomNumberGenerator.Create();
			rg.GetBytes(b);
			return Convert.ToBase64String(b);
		}

		public static void GenerateKeyAndIV(string password)
		{
			if (password.Length < 16)
			{
				throw new CryptographicException("Error - Password must be 16 characters long or greater");
			}
			AesManaged myAlg = new AesManaged();
			byte[] salt = Encoding.ASCII.GetBytes(password.Substring(password.Length - 16));
			Rfc2898DeriveBytes key = new Rfc2898DeriveBytes(password, salt);
			if (Key == null)
			{
				Debug.LogWarning("Key was Null");
				Key = key.GetBytes(myAlg.KeySize / 8);
			}
			IV = key.GetBytes(myAlg.BlockSize / 8);
		}

		public static byte[] GenerateKey(string password)
		{
			if (password.Length < 16)
			{
				throw new CryptographicException("Error - Password must be 16 characters long or greater");
			}
			AesManaged myAlg = new AesManaged();
			byte[] salt = Encoding.ASCII.GetBytes(password.Substring(password.Length - 16));
			Rfc2898DeriveBytes key = new Rfc2898DeriveBytes(password, salt);
			return key.GetBytes(myAlg.KeySize / 8);
		}

		public static byte[] GenerateIV(string password)
		{
			if (password.Length < 16)
			{
				throw new CryptographicException("Error - Password must be 16 characters long or greater");
			}
			AesManaged myAlg = new AesManaged();
			byte[] salt = Encoding.ASCII.GetBytes(password.Substring(password.Length - 16));
			Rfc2898DeriveBytes key = new Rfc2898DeriveBytes(password, salt);
			return key.GetBytes(myAlg.BlockSize / 8);
		}

		private static byte[] CryptoTransform(ICryptoTransform cryptoTransform, byte[] data)
		{
			using (var memoryStream = new MemoryStream())
			{
				using (var cryptoStream = new CryptoStream(memoryStream, cryptoTransform, CryptoStreamMode.Write))
				{
					cryptoStream.Write(data, 0, data.Length);
					cryptoStream.FlushFinalBlock();
					byte[] retBytes = memoryStream.ToArray();
					memoryStream.Close();
					return retBytes;
				}
			}
		}
		//we dont want this editor only
#if UNITY_EDITOR
		//used in the editor to encrypt assetbundles
		public static byte[] Encrypt(byte[] value, ref byte[] IVout)
		{
			var pass = UMAABMSettings.GetEncryptionPassword();
			if (String.IsNullOrEmpty(pass))
			{
				throw new Exception("[EncryptUtil] could not perform any encryption because not encryption password was set in UMAAssetBundleManager.");
			}
			IVout = GenerateIV(pass);
			var thisKey = GenerateKey(pass);
			SymmetricAlgorithm algorithm = new AesManaged();
			ICryptoTransform transform = algorithm.CreateEncryptor(thisKey, IVout);
			return CryptoTransform(transform, value);
		}
#endif

		public static string Encrypt(byte[] value, byte[] Key, byte[] IV)
		{
			SymmetricAlgorithm algorithm = new AesManaged();
			ICryptoTransform transform = algorithm.CreateEncryptor(Key, IV);
			var encrypted = CryptoTransform(transform, value);
			return Convert.ToBase64String(encrypted);
		}

		//with the bundle decryption we will send an IV but generate a key based on the current password
		public static byte[] Decrypt(byte[] value, string password, byte[] thisIV)
		{
			if(password == "")
			{
				throw new Exception("[EncryptUtil] No password was provided for decryption");
			}
			var thisKey = GenerateKey(password);
			SymmetricAlgorithm algorithm = new AesManaged();
			ICryptoTransform transform = algorithm.CreateDecryptor(thisKey, thisIV);

			return CryptoTransform(transform, value);
		}
		
		public static byte[] Decrypt(string text, byte[] Key, byte[] IV)
		{
			var data = Convert.FromBase64String(text);
			SymmetricAlgorithm algorithm = new AesManaged();
			ICryptoTransform transform = algorithm.CreateDecryptor(Key, IV);
			return CryptoTransform(transform, data);
		}
	}
}
