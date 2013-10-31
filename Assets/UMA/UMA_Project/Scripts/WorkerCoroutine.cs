//	============================================================
//	Name:		WorkerCoroutine
//	Author: 	Joen Joensen (@UnLogick)
//	============================================================

using UnityEngine;
using System.Collections;

public abstract class WorkerCoroutine
{
    protected abstract void Start();
    protected abstract IEnumerator workerMethod();
    protected abstract void Stop();

	private IEnumerator workerInstance;
    private WorkerCoroutine subWorker;

    public void Cancel()
    {
		Stop();
		workerInstance = null;
        subWorker = null;
    }
    
	public bool Work()
    {
        while (true)
        {
            // process nested work jobs first
            if (subWorker != null)
            {
                if (subWorker.Work())
                {
                    subWorker = null;
                }
                else
                {
                    // subWorker requested pause
                    return false;
                }
            }

            if (workerInstance == null)
            {
                workerInstance = workerMethod();
                Start();
            }

            bool done = !workerInstance.MoveNext(); // we're done when we can't get next Enumerator element
            if (done)
            {
                // we truly are done
                Stop();
                workerInstance = null;
                return true;
            }
            else
            {
                subWorker = workerInstance.Current as WorkerCoroutine;
                if (subWorker == null)
                {
                    // returned null take a break
                    return false;
                }

                // returned a subWorker, no time to rest
                continue;
            }
        }
    }

}
