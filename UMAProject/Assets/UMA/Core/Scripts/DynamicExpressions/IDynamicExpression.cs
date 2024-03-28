using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UMA;

namespace UMA
{
    public interface IDynamicExpression
    {
        public abstract void Initialize(UMAData umadata);
        public abstract void PreProcess(UMAData umadata);
        public abstract void Process(UMAData umadata);
    }
}
