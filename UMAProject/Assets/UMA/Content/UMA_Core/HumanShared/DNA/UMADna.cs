using System;
using System.Collections.Generic;
using System.Text;

namespace UMA
{
    public abstract partial class UMADna : UMADnaBase
    {
		public static UMADnaBase LoadInstance(System.String className, System.String data)
		{
			UMADnaBase oldRecipeLoad = OldRecipeUtils.LoadInstance(className, data);
			if (oldRecipeLoad != null)
				return oldRecipeLoad;

			if (className == "DynamicUMADna")
				return DynamicUMADna.LoadInstance(data);

			return null;
		}

		public static System.String SaveInstance(UMADnaBase instance)
		{
			if (instance is DynamicUMADna)
				return DynamicUMADna.SaveInstance(instance as DynamicUMADna);
			
			System.String oldRecipeSave = null; //OldRecipeUtils.SaveInstance(instance);

			return oldRecipeSave;
		}

    }
}
