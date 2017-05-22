using UnityEngine;
using System.Collections.Generic;


#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace UMA.AssetBundles
{
	public static class EncryptionUtil
	{
		public static byte[] Encrypt(byte[] pwd, byte[] data) 
		{
				int a, i, j, k, tmp;
				int[] key, box;
				byte[] cipher;

				key = new int[256];
				box = new int[256];
				cipher = new byte[data.Length];

				for (i = 0; i < 256; i++) {
					key[i] = pwd[i % pwd.Length];
					box[i] = i;
				}
				for (j = i = 0; i < 256; i++) {
					j = (j + box[i] + key[i]) % 256;
					tmp = box[i];
					box[i] = box[j];
					box[j] = tmp;
				}
				for (a = j = i = 0; i < data.Length; i++) {
					a++;
					a %= 256;
					j += box[a];
					j %= 256;
					tmp = box[a];
					box[a] = box[j];
					box[j] = tmp;
					k = box[((box[a] + box[j]) % 256)];
					cipher[i] = (byte)(data[i] ^ k);
				}
				return cipher;
		}

		public static byte[] Decrypt(byte[] pwd, byte[] data) 
		{
			return Encrypt(pwd, data);
		}


		public static byte[] Decrypt(byte[] EncryptedData, string Pwd, byte[] IV)
		{
			if(Pwd == "")
			{
				throw new Exception("[EncryptUtil] No password was provided for decryption");
			}
			return Decrypt(BuildKey(Pwd,IV),EncryptedData);
		}


		#if UNITY_EDITOR
		public static byte[] Encrypt(byte[] value, ref byte[] IVout)
		{
			var pass = UMAABMSettings.GetEncryptionPassword();
			if (String.IsNullOrEmpty(pass))
			{
				throw new Exception("[EncryptUtil] could not perform any encryption because not encryption password was set in UMAAssetBundleManager.");
			}

			IVout = GenerateIV();
			return Encrypt(BuildKey(pass,IVout),value);
		}
		#endif

		public static byte[] BuildKey(string pw, byte[] IV)
		{
			byte[] pwb = Encoding.ASCII.GetBytes(pw);
			return Combine(pwb,IV);
		}

		public static byte[] Combine(byte[] first, byte[] second)
		{
			byte[] ret = new byte[first.Length + second.Length];
			Buffer.BlockCopy(first, 0, ret, 0, first.Length);
			Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
			return ret;
		}

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

		public static byte[] GenerateIV()
		{
			string IV = GenerateRandomPW(10);
			return Encoding.ASCII.GetBytes(IV);
		}

		public static string GenerateRandomPW(int length = 16)
		{
			byte[] b = new byte[length];
			RandomNumberGenerator rg = RandomNumberGenerator.Create();
			rg.GetBytes(b);
			return Convert.ToBase64String(b);
		}



/*
		public static byte[] Key;
		public static byte[] IV;




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
	*/
	}
}
