namespace UMA
{
    #region DNASETTER
    /// <summary>
    /// A DnaSetter is used to set a specific piece of DNA on the avatar
    /// that it is pulled from.
    /// </summary>
    public class DnaSetter 
    {
        public string Name; // The name of the DNA.
        public float Value; // Current value of the DNA.
        public string Category;
        DNAInstance dnaInstance;
        public int OwnerIndex
        {
            // position of DNA in index, created at initialization
            get;
            private set;
        }

        protected UMADnaBase Owner;  // owning DNA class. Used to set the DNA by index


        public DnaSetter(DNAInstance instance, DNAGroup group)
        {
            Name = instance.Name;
            Value = instance.Value;
            Category = group.DNAArea;
            dnaInstance = instance;
            OwnerIndex = -1;
            Owner = null;
        }


        /// <summary>
        /// Construct a DnaSetter
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="ownerIndex"></param>
        /// <param name="owner"></param>
        /// <param name="category"></param>
        public DnaSetter(string name, float value, int ownerIndex, UMADnaBase owner, string category)
        {
            Name = name;
            Value = value;
            OwnerIndex = ownerIndex;
            Owner = owner;
            Category = category;
        }

        /// <summary>
        /// Set the current DNA value. You will need to rebuild the character to see 
        /// the results change.
        /// </summary>
        public void Set(float val)
        {
            Value = val;
            if (dnaInstance != null)
            {
                dnaInstance.Value = val;
            }
            else
            {
                Owner.SetValue(OwnerIndex, val);
            }
        }

        /// <summary>
        /// Set the current DNA value. You will need to rebuild the character to see 
        /// the results change.
        /// </summary>
        public void Set()
        {
            Set(Value);
        }

        /// <summary>
        /// Gets the current DNA value.
        /// </summary>
        public float Get()
        {
            if (dnaInstance != null)
            {
                return dnaInstance.Value;
            }
            else
            {
                return Owner.GetValue(OwnerIndex);
            }
        }
    }
}
#endregion