using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;

namespace Commander.Fody
{
    public partial class ModuleWeaver
    {
        // TODO: Eventually change this to be configurable
        private const string InitializerMethodName = "<Commander_Fody>InitializeCommands";

        public List<CommandData> Commands;

        public void CommandInjectionTypeProcessor(TypeDefinition type)
        {
            Commands = Assets.Commands.Values.Where(cmd => cmd.DeclaringType.FullName == type.FullName).ToList();
            if (type == null)
            {
                WriteInfo($"类型缺失.");
                return;
            }

            InjectCommandProperties(type);

            if (Commands.Count > 0)
            {
                InjectCommandInitialization(type);
            }            
        }        
   
        internal void InjectCommandProperties(TypeDefinition type)
        {
            WriteInfo($"InjectType: {type.FullName}.");

            var commandTypeReference = Assets.TypeReferences.ICommand;
            foreach (var commandData in Commands)
            {
                try
                {
                    if (type.TryAddCommandProperty(commandTypeReference, commandData.CommandName, out PropertyDefinition propertyDefinition))
                    {
                        WriteInfo($"Successfully added a property for command: {commandData.CommandName}.");
                    }
                    commandData.CommandProperty = propertyDefinition;
                }
                catch (Exception ex)
                {
                    WriteError($"Error while adding property {commandData.CommandName} to {type}: {ex}");
                }
            }
        }

        internal void InjectCommandInitialization(TypeDefinition type)
        {
            if (Commands.Count == 0)
            {
                WriteInfo($"Command initialization for type: {type.FullName} skipped since there were no commands to bind.");
                return;
            }            

            var initializeMethod = CreateCommandInitializerMethod(type);
            if (Assets.CommandImplementationConstructors.Count == 0 && Settings.FallbackToNestedCommands)
            {
                WriteInfo($"Opting for nested command injection for type: {type.FullName} since there were no eligible command implementations.");
                //Assets.Log.Info("Command initialization for type: {0} skipped since there were no eligible command implementations.", Type.FullName);
                InjectCommandInitializationWithNestedCommand(type,initializeMethod);             
            }
            else
            {
                InjectCommandInitializationWithDelegateCommand(type,initializeMethod);     
            }
            var wasCommandInitializationInjected = Commands.Any(x => x.CommandInitializationInjected);
            if (wasCommandInitializationInjected)
            {
                type.Methods.Add(initializeMethod);
                AddInitializationToConstructors(type,initializeMethod);
            }            
        }        

        public MethodDefinition CreateCommandInitializerMethod(TypeDefinition type)
        {
            var initializeMethod =
                type.Methods.FirstOrDefault(x => x.Name == InitializerMethodName);
            if (initializeMethod != null)
            {
                return initializeMethod;
            }

            initializeMethod = new MethodDefinition(
                InitializerMethodName
                , MethodAttributes.Private | MethodAttributes.SpecialName
                , Assets.TypeReferences.Void)
            {
                HasThis = true,
                Body = { InitLocals = true }
            };

            initializeMethod.Body.Instructions.Append(
                Instruction.Create(OpCodes.Ret)
                );

            return initializeMethod;
        }

        public bool TryAddCommandPropertyInitialization(TypeDefinition type,MethodDefinition initializeMethod, CommandData commandData)
        {
            if (!Assets.CommandImplementationConstructors.Any())
            {
                if (!Assets.DelegateCommandImplementationWasInjected)
                {
                    WriteInfo($"Skipped command initialization for command {commandData}, because there is no eligible command implementation to bind to.");
                    return false;
                }                
            }

            if (!initializeMethod.Body.Variables.Any(vDef => vDef.VariableType.IsBoolean()))// && vDef.Name == "isNull"))
            {
                var vDef = new VariableDefinition(type.Module.TypeSystem.Boolean);
                initializeMethod.Body.Variables.Add(vDef);
            }
            var instructions = initializeMethod.Body.Instructions;
            GetOrCreateLastReturnInstruction(initializeMethod);
            var instructionsToAdd = GetCommandInitializationInstructions(commandData).ToArray();
            instructions.Prepend(instructionsToAdd);
            commandData.CommandInitializationInjected = true;
            return true;
        }

        public static Instruction GetOrCreateLastReturnInstruction(MethodDefinition initializeMethod)
        {
            var instructions = initializeMethod.Body.Instructions;
            Instruction returnInst;
            if (instructions.Count == 0)
            {
                returnInst = Instruction.Create(OpCodes.Ret);
                instructions.Add(returnInst);
            }
            else
            {
                returnInst = instructions.GetLastInstructionWhere(inst => inst.OpCode == OpCodes.Ret);
                if (returnInst == null)
                {
                    returnInst = Instruction.Create(OpCodes.Ret);
                    instructions.Add(returnInst);
                }
            }
            return returnInst;
        }

        public void InjectCommandInitializationWithNestedCommand(TypeDefinition type,MethodDefinition initializeMethod)
        {
            foreach (var commandData in Commands)
            {
                try
                {
                    NestedCommandInjectionTypeProcessor(commandData, initializeMethod, type);
                }
                catch (Exception ex)
                {
                    WriteError(ex.ToString());                    
                }
            }
        }

        public void InjectCommandInitializationWithDelegateCommand(TypeDefinition type, MethodDefinition initializeMethod)
        {
            foreach (var commandData in Commands)
            {
                try
                {
                    WriteInfo($"Trying to add initialization for command: {commandData.CommandName}.");
                    if (TryAddCommandPropertyInitialization(type,initializeMethod, commandData))
                    {
                        WriteInfo($"Successfully added initialization for command: {commandData.CommandName}.");
                    }
                    else
                    {
                        WriteWarning($"Failed to add initialization for command: {commandData.CommandName}.");
                    }
                }
                catch (Exception ex)
                {
                    WriteError(ex.ToString());
                }
            }
        }

        public void AddInitializationToConstructors(TypeDefinition type,MethodDefinition initMethod)
        {
            var constructors =
                from constructor in type.GetConstructors()
                where !constructor.IsStatic
                select constructor;

            foreach (var constructor in constructors)
            {
                var instructions = constructor.Body.Instructions;
                var returnInst = instructions.GetLastInstructionWhere(inst => inst.OpCode == OpCodes.Ret);
                instructions.BeforeInstruction(inst => inst == returnInst
                    , Instruction.Create(OpCodes.Nop)
                    , Instruction.Create(OpCodes.Ldarg_0)
                    , Instruction.Create(OpCodes.Call, initMethod)
                );
            }
        }

        private IEnumerable<Instruction> GetCommandInitializationInstructions(CommandData commandData)
        {
            Instruction blockEnd = Instruction.Create(OpCodes.Nop);
            //// Null check
            //// if (Command == null) { ... }
            yield return Instruction.Create(OpCodes.Nop);
            yield return Instruction.Create(OpCodes.Ldarg_0);
            yield return Instruction.Create(OpCodes.Call, commandData.CommandProperty.GetMethod);
            yield return Instruction.Create(OpCodes.Ldnull);
            yield return Instruction.Create(OpCodes.Ceq);
            yield return Instruction.Create(OpCodes.Ldc_I4_0);
            yield return Instruction.Create(OpCodes.Ceq);
            yield return Instruction.Create(OpCodes.Stloc_0);
            yield return Instruction.Create(OpCodes.Ldloc_0);
            yield return Instruction.Create(OpCodes.Brtrue_S, blockEnd);

            foreach (var instruction in GetSetCommandInstructions(commandData))
            {
                yield return instruction;
            }

            // BlockEnd is the end of the if (Command == null) {} block (i.e. think the closing brace)
            yield return blockEnd;          
        }

        internal IEnumerable<Instruction> GetSetCommandInstructions(CommandData command)
        {
            var commandConstructors = Assets.CommandImplementationConstructors;
            if (Assets.DelegateCommandImplementationWasInjected)
            {
                var delegateCommandType = Assets.ModuleDefinition.GetType(GeneratedCommandClassName);
                commandConstructors = commandConstructors.Concat(delegateCommandType.GetConstructors()).ToList();
            }
            if (command.OnExecuteMethods.Count > 0)
            {
                MethodReference commandConstructor;
                var onExecuteMethod = command.OnExecuteMethods[0];
                var canExecuteMethod = command.CanExecuteMethods.FirstOrDefault();
                if (canExecuteMethod == null)
                {
                    commandConstructor = commandConstructors.FirstOrDefault(mf=>mf.Parameters.Count == 1);
                    commandConstructor = GetConstructorResolved(commandConstructor, onExecuteMethod);
                    yield return Instruction.Create(OpCodes.Ldarg_0);
                    yield return Instruction.Create(OpCodes.Ldarg_0);
                    yield return Instruction.Create(OpCodes.Ldftn, onExecuteMethod);
                    yield return Instruction.Create(OpCodes.Newobj, GetActionConstructorForExecuteMethod(onExecuteMethod, commandConstructor));
                    yield return Instruction.Create(OpCodes.Newobj, commandConstructor);
                    yield return Instruction.Create(OpCodes.Call, command.CommandProperty.SetMethod);
                    yield return Instruction.Create(OpCodes.Nop);
                    yield return Instruction.Create(OpCodes.Nop);
                }
                else
                {
                    commandConstructor = commandConstructors.OrderByDescending(mf => mf.Parameters.Count).First();
                    commandConstructor = GetConstructorResolved(commandConstructor, onExecuteMethod);
                    yield return Instruction.Create(OpCodes.Nop);
                    yield return Instruction.Create(OpCodes.Ldarg_0);
                    yield return Instruction.Create(OpCodes.Ldarg_0);
                    yield return Instruction.Create(OpCodes.Ldftn, command.OnExecuteMethods.Single());
                    yield return Instruction.Create(OpCodes.Newobj, GetActionConstructorForExecuteMethod(onExecuteMethod, commandConstructor));
                    yield return Instruction.Create(OpCodes.Ldarg_0);
                    yield return Instruction.Create(OpCodes.Ldftn, command.CanExecuteMethods.Single());
                    yield return Instruction.Create(OpCodes.Newobj, GetPredicateConstructorForCanExecuteMethod(canExecuteMethod, commandConstructor));
                    yield return Instruction.Create(OpCodes.Newobj, commandConstructor);
                    yield return Instruction.Create(OpCodes.Call, command.CommandProperty.SetMethod);
                    yield return Instruction.Create(OpCodes.Nop);
                    yield return Instruction.Create(OpCodes.Nop);
                }
            }
        }

        private MethodReference GetConstructorResolved(MethodReference commandConstructor, MethodDefinition method)
        {
            var parameterType = method.HasParameters
                ? method.Parameters[0].ParameterType
                : Assets.TypeReferences.Object;

            var commandType = commandConstructor.DeclaringType;
            bool isGeneric = commandType.HasGenericParameters;
            if (commandType.HasGenericParameters)
            {
                var commandTypeResolved = commandConstructor.DeclaringType.MakeGenericInstanceType(parameterType);
                commandType = commandTypeResolved;
                var resolvedConstructor =
                    commandType.GetElementType()
                        .Resolve()
                        .GetConstructors()
                        .First(c => c.Parameters.Count == commandConstructor.Parameters.Count);
                commandConstructor = resolvedConstructor;
            }

            if (isGeneric)
            {
                if (method.HasParameters)
                {
                    commandConstructor =
                        commandConstructor.MakeHostInstanceGeneric(method.Parameters[0].ParameterType);
                }
                else
                {
                    commandConstructor = commandConstructor.MakeHostInstanceGeneric(Assets.TypeReferences.Object);
                }
            }
            return commandConstructor;
        }

        private MethodReference GetActionConstructorForExecuteMethod(MethodReference method, MethodReference targetConstructor)
        {
            if (method.Parameters.Count > 1)
            {
                throw new Exception(
                    string.Format("Cannot generate command initialization for method {0}, because the method has too many parameters."
                    ,method));
            }

            if (method.HasParameters)
            {
                // Action<TCommandParameter> where TCommandParameter = method.Parameters[0].ParameterType
                return Assets.ActionOfTConstructorReference.MakeHostInstanceGeneric(method.Parameters[0].ParameterType);
            }

            // Action()
            return Assets.ActionConstructorReference;
        }

        private MethodReference GetPredicateConstructorForCanExecuteMethod(MethodReference method, MethodReference targetConstructor)
        {
            if (method.Parameters.Count > 1)
            {
                throw new Exception(
                    $"Cannot generate command initialization for 'CanExecute' method {method}, because the method has too many parameters.");
            }

            if (targetConstructor.Parameters.Count != 2)
            {
                throw new Exception(
                    $"Cannot generate command initialization for 'CanExecute' method {method}, because the method has the wrong signature.");
            }

            var targetParameter = targetConstructor.Parameters[1];
            if (method.HasParameters)
            {
                var methodParameter = method.Parameters[0];             
                return Assets.PredicateOfTConstructorReference.MakeHostInstanceGeneric(methodParameter.ParameterType);
            }

            if (targetParameter.ParameterType.Name != "Func`1")
            {
                throw new Exception(
                    $"Cannot generate command initialization for 'CanExecute' method {method}, because the method has the wrong signature.");
            }
           
            // Action()
            return Assets.FuncOfBoolConstructorReference;
        }
    }
}