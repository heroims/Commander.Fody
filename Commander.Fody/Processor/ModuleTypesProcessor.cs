using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace Commander.Fody
{
    public partial class ModuleWeaver
    {

        public void ProcessTypes()
        {
            var typesToProcess = Settings.GetTypesToProcess(this);

            ProcessTypes(typesToProcess);
        }        

        public void ProcessTypes(IEnumerable<TypeDefinition> types)
        {
            foreach (var type in types)
            {
                try
                {
                    CommandInjectionTypeProcessor(type);
                }
                catch (Exception ex)
                {
                    WriteError(ex.ToString());
                }
            }
        }        
    }
}