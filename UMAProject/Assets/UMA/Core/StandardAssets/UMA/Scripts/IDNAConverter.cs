using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	public interface IDNAConverter
	{
		System.Type DNAType { get; }

		string name { get; }

		string DisplayValue { get; }

		int DNATypeHash { get; }

		DNAConvertDelegate PreApplyDnaAction { get; }

		DNAConvertDelegate ApplyDnaAction { get; }

		void Prepare();

	}
}