using System.Linq;
using Mono.Cecil;

namespace Commander.Fody
{
    public partial class ModuleWeaver
    {
        public void CleanReferences()
        {
            var referenceToRemove = ModuleDefinition.AssemblyReferences.FirstOrDefault(x => x.Name == "Commander");
            if (referenceToRemove == null)
            {
                WriteInfo("\tNo reference to 'Commander' found. References not modified.");
                return;
            }

            ModuleDefinition.AssemblyReferences.Remove(referenceToRemove);
            WriteInfo("\tRemoving reference to 'Commander'.");
        }
    }
}