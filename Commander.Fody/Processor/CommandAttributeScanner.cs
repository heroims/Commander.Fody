﻿using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Commander.Fody
{
    public partial class ModuleWeaver
    {

        public void CommandAttributeScanner()
        {
            foreach (var @class in Assets.AllClasses)
            {
                ScanForOnCommandAttribute(@class);
                ScanForOnCommandCanExecuteAttribute(@class);
            }
        }

        public IEnumerable<MethodDefinition> FindOnCommandMethods(TypeDefinition type)
        {
            return type.Methods.Where(method => 
                method.CustomAttributes.ContainsAttribute(
                    Settings.OnCommandAttributeName, Settings.MatchAttributesByFullName));
        }

        public IEnumerable<MethodDefinition> FindCommandCanExecuteMethods(TypeDefinition type)
        {
            return type.Methods.Where(method => 
                method.CustomAttributes.ContainsAttribute(
                    Settings.OnCommandCanExecuteAttributeName, Settings.MatchAttributesByFullName));
        }

        public bool IsValidOnExecuteMethod(MethodDefinition method)
        {
            return method.ReturnType == Assets.TypeReferences.Void
                && (!method.HasParameters
                    || (method.Parameters.Count == 1
                        && !method.Parameters[0].IsOut));
        }

        public bool IsValidCanExecuteMethod(MethodDefinition method)
        {
            return method.ReturnType == Assets.TypeReferences.Boolean
                && (!method.HasParameters
                    || (method.Parameters.Count == 1
                        && !method.Parameters[0].IsOut
                        && method.Parameters[0].ParameterType.Matches(Assets.TypeReferences.Object)));
        }

        internal void ScanForOnCommandAttribute(TypeDefinition type)
        {
            var methods = FindOnCommandMethods(type);
            foreach (var method in methods)
            {
                if (!IsValidOnExecuteMethod(method))
                {
                    WriteWarning($"Method: {method} is not a valid OnExecute method for ICommand binding.." );
                    WriteWarning($"Method: {method} parameter info:");
                    for (int index = 0; index < method.Parameters.Count; index++)
                    {
                        var parameter = method.Parameters[index];
                        WriteInfo($"Parameter[{index}]: {parameter}");
                    }
                    continue;
                }

                // Find OnCommand methods where name is given
                var attributes =
                    method.CustomAttributes
                        .Where(x => x.IsCustomAttribute(Settings.OnCommandAttributeName, Settings.MatchAttributesByFullName))
                        .Where(x => x.HasConstructorArguments
                            && x.ConstructorArguments.First().Type.FullNameMatches(Assets.TypeReferences.String));

                foreach (var attribute in attributes)
                {
                    var commandName = (string)attribute.ConstructorArguments[0].Value;
                    WriteInfo($"Found OnCommand method {method} for command {commandName} on type {type.Name}");
                    var command = Assets.Commands.GetOrAdd(commandName, name => new CommandData(type, name));
                    command.OnExecuteMethods.Add(method);
                }
            }
        }

        internal void ScanForOnCommandCanExecuteAttribute(TypeDefinition type)
        {
            var methods = FindCommandCanExecuteMethods(type);
            foreach (var method in methods)
            {
                if (!IsValidCanExecuteMethod(method))
                {
                    WriteWarning($"Method: {method} is not a valid CanExecute method for ICommand binding." );
                    WriteWarning($"Method: {method} parameter info:" );
                    for (int index = 0; index < method.Parameters.Count; index++)
                    {
                        var parameter = method.Parameters[index];
                        WriteInfo($"Parameter[{index}]: {parameter}");
                    }
                    continue;
                }

                // Find OnCommandCanExecute methods where name is given
                var attributes =
                    method.CustomAttributes
                        .Where(x => x.IsCustomAttribute(Settings.OnCommandCanExecuteAttributeName, Settings.MatchAttributesByFullName))
                        .Where(x => x.HasConstructorArguments
                            && x.ConstructorArguments.First().Type.FullNameMatches(Assets.TypeReferences.String));

                foreach (var attribute in attributes)
                {
                    var commandName = (string)attribute.ConstructorArguments[0].Value;
                    WriteInfo($"Found OnCommandCanExecute method {method} for command {commandName} on type {type.Name}" );
                    var command = Assets.Commands.GetOrAdd(commandName, name => new CommandData(type, name));
                    command.CanExecuteMethods.Add(method);
                }
            }
        }  
    }
}