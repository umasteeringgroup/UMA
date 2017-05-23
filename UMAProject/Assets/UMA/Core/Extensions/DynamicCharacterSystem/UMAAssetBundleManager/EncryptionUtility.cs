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

		public static void DebugLog(string s)
		{
			string fname = System.IO.Path.Combine(Application.dataPath,"debuglog.txt");
			System.IO.File.AppendAllText(fname,s+Environment.NewLine);
			Debug.Log(s);
		}

		public static void LogHash(byte[] data)
		{
			using(SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider())
			{
				string hash = Convert.ToBase64String(sha1.ComputeHash(data));
				DebugLog("Hash is: "+hash);
			}
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
			DebugLog("Decrypting Assetbundle with PWD "+Pwd + " IV " + Encoding.ASCII.GetString(IV)+ " Size is: "+EncryptedData.Length);
			byte[] decrypted = Decrypt(BuildKey(Pwd,IV),EncryptedData);
			LogHash(decrypted);
			return decrypted;
		}


		#if UNITY_EDITOR
		public static byte[] Encrypt(byte[] value, ref byte[] IVout)
		{
			var pass = UMAABMSettings.GetEncryptionPassword();
			if (String.IsNullOrEmpty(pass))
			{
				throw new Exception("[EncryptUtil] could not perform any encryption because not encryption password was set in UMAAssetBundleManager.");
			}

			IVout = GenerateIV(pass);

			DebugLog("Encrypting Assetbundle with PWD "+pass + " IV " + Encoding.ASCII.GetString(IVout)+ " Size is: "+value.Length);
			LogHash(value);
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

		public static byte[] GenerateIV(string pass)
		{
			var textBytes = Encoding.UTF8.GetBytes(pass+"_umldata");
			var res = Convert.ToBase64String(textBytes);

			return Encoding.ASCII.GetBytes(res.Substring(0,8));
		}

		public static string GenerateRandomPW(int length = 16)
		{
			byte[] b = new byte[length];
			RandomNumberGenerator rg = RandomNumberGenerator.Create();
			rg.GetBytes(b);
			return Convert.ToBase64String(b);
		}
	}

}
