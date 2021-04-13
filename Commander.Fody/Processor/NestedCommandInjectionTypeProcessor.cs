﻿using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Commander.Fody
{
    public partial class ModuleWeaver
    {
        private const TypeAttributes DefaultTypeNAttributesForCommand = TypeAttributes.SpecialName | TypeAttributes.NestedPrivate | TypeAttributes.BeforeFieldInit;
        private const MethodAttributes ConstructorDefaultMethodNAttributes =
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
        
        private const string OwnerFieldName = "_owner";
        private const string CanExecuteChangedFieldName = "CanExecuteChanged";

        private CommandData _command;
        private MethodDefinition _initializeMethod;

        public void NestedCommandInjectionTypeProcessor(CommandData command,
             MethodDefinition initializeMethod, TypeDefinition type)
        {
            _command = command ?? throw new ArgumentNullException("command");
            _initializeMethod = initializeMethod ?? throw new ArgumentNullException("initializeMethod");
            
            var constructor = InjectNestedCommandClass(type);
            var constructorRef = constructor.Resolve();
            AddCommandPropertyInitialization(type,constructorRef);
        }

        public CommandData Command
        {
            get { return _command; }
        }

        public MethodDefinition InitializeMethod
        {
            get { return _initializeMethod; }
        }

        public MethodDefinition InjectNestedCommandClass(TypeDefinition type)
        {
            var name = string.Format("<>__NestedCommandImplementationFor" + Command.CommandName);
            var commandType = new TypeDefinition(type.Namespace, name, DefaultTypeNAttributesForCommand)
            {
                BaseType = Assets.TypeReferences.Object
            };

            var field = commandType.AddField(type, OwnerFieldName);
            field.IsInitOnly = true;
            //field = commandType.AddField(Assets.TypeReferences.EventHandler, CanExecuteChangedFieldName);

            ImplementICommandInterface(commandType);

            var ctor = CreateConstructor(commandType);
            commandType.Methods.Add(ctor);
            type.NestedTypes.Add(commandType);
            return ctor;
        }

        public void AddCommandPropertyInitialization(TypeDefinition type,MethodReference commandConstructor)
        {
            var method = InitializeMethod;
            if (!method.Body.Variables.Any(vDef => vDef.VariableType.IsBoolean()))//&& vDef.Name == "isNull"))
            {
                var vDef = new VariableDefinition(type.Module.TypeSystem.Boolean);
                method.Body.Variables.Add(vDef);
            }

            // var returnInst = CommandInjectionTypeProcessor.GetOrCreateLastReturnInstruction(method);
            var instructions = method.Body.Instructions;
            Instruction blockEnd = Instruction.Create(OpCodes.Nop);

            // Null check
            // if (Command == null) { ... }
            instructions.Prepend(
                Instruction.Create(OpCodes.Nop),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Call, Command.CommandProperty.GetMethod),
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ceq),
                Instruction.Create(OpCodes.Ldc_I4_0),
                Instruction.Create(OpCodes.Ceq),
                Instruction.Create(OpCodes.Stloc_0),
                Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Brtrue_S, blockEnd),
                blockEnd
                );

            instructions.BeforeInstruction(inst => inst == blockEnd,
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Newobj, commandConstructor),
                Instruction.Create(OpCodes.Call, Command.CommandProperty.SetMethod),
                Instruction.Create(OpCodes.Nop),
                Instruction.Create(OpCodes.Nop)
                );

            Command.CommandInitializationInjected = true;
            Command.UsesNestedCommand = true;
        }

        internal MethodDefinition CreateConstructor(TypeDefinition type)
        {
            var ctor = new MethodDefinition(".ctor", ConstructorDefaultMethodNAttributes, Assets.TypeReferences.Void);
            var parameter = new ParameterDefinition("owner", ParameterAttributes.None, type);
            var field = type.Fields[0];

            ctor.Parameters.Add(parameter);
            var il = ctor.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Call, Assets.ObjectConstructorReference));
            il.Append(Instruction.Create(OpCodes.Nop));
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Stfld, field));
            il.Append(Instruction.Create(OpCodes.Nop));
            il.Append(Instruction.Create(OpCodes.Ret));
            return ctor;
        }

        internal void ImplementICommandInterface(TypeDefinition commandType)
        {
            commandType.Interfaces.Add(new InterfaceImplementation(Assets.TypeReferences.ICommand));
            Assets.AddCanExecuteChangedEvent(commandType);
            AddNCanExecuteMethod(commandType);
            AddNExecuteMethod(commandType);
        }           

        internal void AddNExecuteMethod(TypeDefinition commandType)
        {
            var field = commandType.Fields[0];
            var method = new MethodDefinition("Execute",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                MethodAttributes.Virtual, Assets.TypeReferences.Void)
            {
                Body = {InitLocals = true}
            };

            var commandParameter = new ParameterDefinition("parameter", ParameterAttributes.None, Assets.TypeReferences.Object);
            method.Parameters.Add(commandParameter);

            commandType.Methods.Add(method);

            var il = method.Body.GetILProcessor();
            var start = Instruction.Create(OpCodes.Nop);
            il.Append(start);
            foreach (var onExecuteMethod in Command.OnExecuteMethods)
            {
                il.Append(Instruction.Create(OpCodes.Ldarg_0));
                il.Append(Instruction.Create(OpCodes.Ldfld, field));   
                if (onExecuteMethod.Parameters.Count == 1)
                {
                    il.Append(Instruction.Create(OpCodes.Ldarg_1));
                    var parameter = onExecuteMethod.Parameters[0];
                    if (!parameter.ParameterType.FullNameMatches(Assets.TypeReferences.Object))
                    {
                        if (parameter.ParameterType.IsGenericInstance)
                        {

                        }
                        else
                        {
                            il.Append(Instruction.Create(OpCodes.Unbox_Any, parameter.ParameterType));
                        }
                    }
                }                
                if (onExecuteMethod.IsVirtual)
                {
                    il.Append(Instruction.Create(OpCodes.Callvirt, onExecuteMethod));
                }
                else
                {
                    il.Append(Instruction.Create(OpCodes.Call, onExecuteMethod));
                }
            }    
            il.Append(Instruction.Create(OpCodes.Nop));     
            il.Append(Instruction.Create(OpCodes.Ret));
        }


        internal void AddNCanExecuteMethod(TypeDefinition commandType)
        {
            var field = commandType.Fields[0];

            var method = new MethodDefinition("CanExecute",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                MethodAttributes.Virtual, Assets.TypeReferences.Boolean)
            {
                Body = {InitLocals = true}
            };

            var commandParameter = new ParameterDefinition("parameter", ParameterAttributes.None, Assets.TypeReferences.Object);
            method.Parameters.Add(commandParameter);

            var returnVariable = new VariableDefinition(Assets.TypeReferences.Boolean);
            method.Body.Variables.Add(returnVariable);

            commandType.Methods.Add(method);

            var il = method.Body.GetILProcessor();            
            il.Append(Instruction.Create(OpCodes.Nop));
            if (Command.CanExecuteMethods.Count == 0)
            {
                var returnBlock = Instruction.Create(OpCodes.Ldloc_0);
                il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
                il.Append(Instruction.Create(OpCodes.Stloc_0));
                il.Append(Instruction.Create(OpCodes.Br_S, returnBlock));                
                il.Append(returnBlock);
                il.Append(Instruction.Create(OpCodes.Ret));
            }
            else
            {
                var canExecuteMethod = Command.CanExecuteMethods.Single();
                var returnBlock = Instruction.Create(OpCodes.Nop);
                il.Append(Instruction.Create(OpCodes.Ldarg_0));
                il.Append(Instruction.Create(OpCodes.Ldfld, field));
                if (canExecuteMethod.Parameters.Count == 1)
                {
                    il.Append(Instruction.Create(OpCodes.Ldarg_1));
                    var parameter = canExecuteMethod.Parameters[0];
                    if (!parameter.ParameterType.FullNameMatches(Assets.TypeReferences.Object))
                    {
                        if (parameter.ParameterType.IsGenericInstance)
                        {

                        }
                        else
                        {
                            il.Append(Instruction.Create(OpCodes.Unbox_Any, parameter.ParameterType));
                        }
                    }
                }               
                if (canExecuteMethod.IsVirtual)
                {
                    il.Append(Instruction.Create(OpCodes.Callvirt, canExecuteMethod));
                }
                else
                {
                    il.Append(Instruction.Create(OpCodes.Call, canExecuteMethod));
                }
                il.Append(Instruction.Create(OpCodes.Stloc_0));
                il.Append(Instruction.Create(OpCodes.Br_S, returnBlock));
                il.Append(returnBlock);
                il.Append(Instruction.Create(OpCodes.Ldloc_0));
                il.Append(Instruction.Create(OpCodes.Ret));
            }            
        }
    }
}