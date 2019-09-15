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
					c = char.ToUpper(c);
				sb.Append(c);
			}
			return sb.ToString().Split('|');
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
                              c = char.ToUpper(c);
                        sb.Append(c);
                  }
                  return sb.ToString();
            }
      }
}