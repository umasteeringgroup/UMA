namespace UMA
{

    public interface IUMAIndexOptions
    {
        public bool ForceKeep { get; set; }
        public bool LabelLocalFiles { get; set; }
        public bool NoAutoAdd { get; set; }
    }
}