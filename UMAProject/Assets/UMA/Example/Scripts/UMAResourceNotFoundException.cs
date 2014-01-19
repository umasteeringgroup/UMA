using System;

namespace UMA
{
	public class UMAResourceNotFoundException : Exception
	{
		public UMAResourceNotFoundException(string message)
			: base(message)
		{
		}
	}
}
