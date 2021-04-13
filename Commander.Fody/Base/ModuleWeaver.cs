using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Commander.Fody
{
    public partial class ModuleWeaver: BaseModuleWeaver
    {
        public Assets Assets;
        //public TypeDefinition Type;

        public ModuleWeaverSettings Settings = new ModuleWeaverSettings();

        public override void Execute()
        {
            Settings = new ModuleWeaverSettings(Config);
            Assets = new Assets(this);

            CommandAttributeScanner();
            DelegateCommandClassInjection();
            ProcessTypes();
            CleanReferences();
            CleanAttributes();
        }

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            yield return "mscorlib";
            yield return "System";
            yield return "System.Runtime";
            yield return "System.Runtime.Serialization";
            yield return "System.Core";
            yield return "netstandard";
            yield return "System.Collections";
            yield return "System.ObjectModel";
            yield return "System.Numerics";
            yield return "System.Data";
            yield return "System.Drawing";
            yield return "System.Xml";
            yield return "System.Xml.Linq";
            //TODO: remove when move to only netstandard2.0
            yield return "System.Diagnostics.Tools";
            yield return "System.Diagnostics.Debug";
        }

    }
}