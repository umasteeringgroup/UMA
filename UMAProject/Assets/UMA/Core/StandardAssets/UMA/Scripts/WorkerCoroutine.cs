using System.Collections;
using System;

namespace UMA
{
	public abstract class WorkerCoroutine
	{
	    protected abstract void Start();
	    protected abstract IEnumerator workerMethod();
	    protected abstract void Stop();

		private IEnumerator workerInstance;
	    private WorkerCoroutine subWorker;
        public int TimeHint;
        public WorkerCoroutine lastWorker;
        public int lastWorkerCount = 0;

	    public void Cancel()
	    {
			Stop();
			workerInstance = null;
	        subWorker = null;
	    }
	    
		public bool Work()
	    {
            TimeHint = 0;
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
                        TimeHint = subWorker.TimeHint;
                        if (lastWorker == subWorker)
                        {
                            lastWorkerCount++;
                        }
                        else
                        {
                            lastWorkerCount = 1;
                        }
                        lastWorker = subWorker;
                        return false;
	                }
	            }

	            if (workerInstance == null)
	            {
	                workerInstance = workerMethod();
	                Start();
	            }

                bool done;
                try
                {
                    done = !workerInstance.MoveNext(); // we're done when we can't get next Enumerator element
                }
                catch(Exception e)
                {
                    throw new Exception("Exception in WorkerCoroutine: "+this, e);
                }
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
                        if (lastWorker == this)
                        {
                            lastWorkerCount++;
                        }
                        else
                        {
                            lastWorkerCount = 1;
                        }
                        lastWorker = this;

                        // returned null take a break
                        if (workerInstance.Current is int)
                        {
                            TimeHint = (int)workerInstance.Current;
                        }
                        return false;
	                }

	                // returned a subWorker, no time to rest
	                continue;
	            }
	        }
	    }
	}
}