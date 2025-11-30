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
        public UMAData umaData;

        public int OwnerIndex
        {
            // position of DNA in index, created at initialization
            get;
            private set;
        }

        protected UMADnaBase Owner;  // owning DNA class. Used to set the DNA by index


        public DnaSetter(DNAInstance instance, DNAGroup group, UMAData umaData)
        {
            Name = instance.Name;
            Value = instance.Value;
            Category = group.DNAArea;
            dnaInstance = instance;
            OwnerIndex = -1;
            Owner = null;
            this.umaData = umaData;
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
            umaData = null;
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
                if (dnaInstance.parentGroup.MaxTotalValue > 0.0001f)
                {
                    // Enforce MaxTotalValue by reducing other non-zero instances equally
                    if (umaData != null && umaData.dnaInstanceCollection != null)
                    {
                        var group = dnaInstance.parentGroup;
                        var instances = umaData.dnaInstanceCollection.GetDNAInstancesByGroup(group);
                        if (instances != null && instances.Count > 0)
                        {
                            float total = 0.0f;
                            int otherNonZero = 0;
                            for (int i = 0; i < instances.Count; i++)
                            {
                                var inst = instances[i];
                                if (inst == null || !inst.enabled)
                                {
                                    continue;
                                }
                                total += inst.Value;
                                if (inst != dnaInstance && inst.Value > 0.0f)
                                {
                                    otherNonZero++;
                                }
                            }

                            float maxTotal = group.MaxTotalValue;
                            if (total > maxTotal && otherNonZero > 0)
                            {
                                float over = total - maxTotal;
                                float reductionPer = over / (float)otherNonZero;

                                for (int i = 0; i < instances.Count; i++)
                                {
                                    var inst = instances[i];
                                    if (inst == null || !inst.enabled)
                                    {
                                        continue;
                                    }
                                    if (inst == dnaInstance)
                                    {
                                        continue;
                                    }
                                    if (inst.Value > 0.0f)
                                    {
                                        float newVal = inst.Value - reductionPer;
                                        if (newVal < 0.0f)
                                        {
                                            newVal = 0.0f;
                                        }
                                        if (newVal > 1.0f)
                                        {
                                            newVal = 1.0f;
                                        }
                                        inst.Value = newVal;
                                    }
                                }
                            }
                        }
                    }

                }
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