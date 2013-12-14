using UnityEngine;
using System.Collections;
using System.Collections.Generic;



namespace UMA
{
	public abstract class TextureProcessBaseCoroutine : WorkerCoroutine
	{
	    public abstract void Prepare(UMAData _umaData, UMAGeneratorBase _umaGenerator);
	}
}