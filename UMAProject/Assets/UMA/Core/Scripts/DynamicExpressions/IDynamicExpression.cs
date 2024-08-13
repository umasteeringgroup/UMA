namespace UMA
{
    public interface IDynamicExpression
    {
        public abstract void Initialize(UMAData umadata);
        public abstract void PreProcess(UMAData umadata);
        public abstract void Process(UMAData umadata);
    }
}
