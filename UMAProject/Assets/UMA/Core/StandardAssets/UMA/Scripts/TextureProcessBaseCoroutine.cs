namespace UMA
{
	/// <summary>
	/// Base class for texture processing coroutines.
	/// </summary>
	public abstract class TextureProcessBaseCoroutine : WorkerCoroutine
	{
	    public abstract void Prepare(UMAData _umaData, UMAGeneratorBase _umaGenerator);
	}
}