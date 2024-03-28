namespace UMA
{
    //IDNAConverters dont need to have a DynamicUMADnaAsset (their names are hard coded)
    //IDynamicDNAConverters do have dna assets.
    //we could get rid of this shit if we finally ditched the legacy hard coded converters
    public interface IDynamicDNAConverter
	{
		DynamicUMADnaAsset dnaAsset { get; }
	}
}
