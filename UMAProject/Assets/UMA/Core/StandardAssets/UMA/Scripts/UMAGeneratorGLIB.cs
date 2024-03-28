namespace UMA
{
    /// <summary>
    /// This is a dummy class and should not be used
    /// It's here solely to solve issues when someone forgets to delete the UMA folder.
    /// </summary>
    public class UMAGeneratorGLIB : UMAGeneratorBase
    {
        public override void addDirtyUMA(UMAData umaToAdd)
        {
        }

        public override bool IsIdle()
        {
            return false;
        }

        public override int QueueSize()
        {
            return 0;
        }

        public override void removeUMA(UMAData umaToRemove)
        {
        }

        public override bool updatePending(UMAData umaToCheck)
        {
            return false;
        }

        public override bool updateProcessing(UMAData umaToCheck)
        {
            return false;
        }

        public override void Work()
        {
        }
    }
}