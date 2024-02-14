using System;
using System.Text;

namespace UMA.CharacterSystem
{
      public static class UMAExtensions
      {
            public static int WordCount(this String str)
            {
                  return str.Split(new char[] { ' ', '.', '?' }, 
                  StringSplitOptions.RemoveEmptyEntries).Length;
            }
        public static string ToTitleCase(this String str)
        {
            char[] sep = { ' ' };

            string[] words = str.Split(sep,StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.Length > 2)
                {
                    string s1 = word.Substring(0, 1).ToUpper();
                    string s2 = word.Substring(1, word.Length - 1).ToLower();
                    sb.Append(s1);
                    sb.Append(s2);
                }
                else
                {
                    sb.Append(word.ToUpper());
                }
            }
            return sb.ToString();
        }

		public static string[] SplitCamelCase(this String str)
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < str.Length; i++)
			{
				char c = str[i];
				if (i > 0 && char.IsUpper(c))
				{
					sb.Append('|');
				}
				if (i == 0)
                {
                    c = char.ToUpper(c);
                }

                sb.Append(c);
			}
			return sb.ToString().Split('|');
		}

        public static string MenuCamelCase(this String str)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (i > 0 && char.IsUpper(c))
                {
                    sb.Append('/');
                }
                if (i == 0)
                {
                    c = char.ToUpper(c);
                }

                sb.Append(c);
            }
            return sb.ToString();
        }
    

    public static string BreakupCamelCase(this String str)
            {
                  StringBuilder sb = new StringBuilder();   
                  for (int i=0;i<str.Length;i++)
                  {
                        char c = str[i];
                        if (i > 0 && char.IsUpper(c))
                        {
                              sb.Append(' ');
                        }
                        if (i==0)
                {
                    c = char.ToUpper(c);
                }

                sb.Append(c);
                  }
                  return sb.ToString();
            }
      }
}