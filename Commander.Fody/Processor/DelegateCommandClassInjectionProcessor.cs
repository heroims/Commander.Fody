﻿using System.Linq;
using System.Linq.Expressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Commander.Fody
{
    public partial class ModuleWeaver
    {
        public const string GeneratedCommandClassNamespace = "";
        public const string GeneratedCommandClassName = "<Commander_Fody>__DelegateCommand";
        public const TypeAttributes DefaultTypeDAttributesForCommand = TypeAttributes.SpecialName | TypeAttributes.BeforeFieldInit;
        public const MethodAttributes ConstructorDefaultMethodDAttributes =
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;


        public void DelegateCommandClassInjection()
        {          
            if (ShouldGenerateDelegateCommand())
            {
                WriteInfo("DelegateCommand class generation is enabled.");
                var commandClass = GenerateClass();
                //foreach (var constructor in commandClass.GetConstructors())
                //{
                //    Assets.CommandImplementationConstructors.Add(constructor);
                //}
                WriteInfo("DelegateCommand class generation is finish.");

            }
        }

        public bool ShouldGenerateDelegateCommand()
        {
            return Assets.CommandImplementationConstructors == null 
                || Assets.CommandImplementationConstructors.Count == 0;
        }

        public TypeDefinition GenerateClass()
        {
            var commandClass = new TypeDefinition(
                GeneratedCommandClassNamespace
                , GeneratedCommandClassName
                , DefaultTypeDAttributesForCommand
                , Assets.TypeReferences.Object);

            commandClass.Interfaces.Add(new InterfaceImplementation(Assets.TypeReferences.ICommand));

            var genericParameter = new GenericParameter("TParameter", commandClass);
            commandClass.GenericParameters.Add(genericParameter);


            AddFields(commandClass);
            AddConstructors(commandClass);
            AddDExecuteMethod(commandClass);
            AddDCanExecuteMethod(commandClass);
            Assets.AddCanExecuteChangedEvent(commandClass);

            ModuleDefinition.Types.Add(commandClass);
            Assets.DelegateCommandImplementationWasInjected = true;
            return commandClass;
        }

        internal void AddConstructors(TypeDefinition commandClass)
        {
            var mainConstructor = AddActionAndPredicateConstructor(commandClass);
            AddActionOnlyConstructor(commandClass, mainConstructor);
        }

        internal MethodDefinition AddActionOnlyConstructor(TypeDefinition commandClass, MethodDefinition invokedConstructor)
        {
            var constructor = new MethodDefinition(".ctor", ConstructorDefaultMethodDAttributes, Assets.TypeReferences.Void);

            // Prepare type for parameter
            var actionGenericInstanceType = new GenericInstanceType(Assets.TypeReferences.ActionOfT);
            actionGenericInstanceType.GenericArguments.Add(commandClass.GenericParameters[0]);

            var actionParameter = new ParameterDefinition("execute", ParameterAttributes.None, actionGenericInstanceType);
            constructor.Parameters.Add(actionParameter);

            var il = constructor.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Call, invokedConstructor));            
            il.Append(Instruction.Create(OpCodes.Nop));
            il.Append(Instruction.Create(OpCodes.Ret));
            commandClass.Methods.Add(constructor);
            return constructor;
        }

        internal MethodDefinition AddActionAndPredicateConstructor(TypeDefinition commandClass)
        {
            var executeField = commandClass.Fields.Single(x => x.Name == "_execute");
            var canExecuteField = commandClass.Fields.Single(x => x.Name == "_canExecute");
            var constructor = new MethodDefinition(".ctor", ConstructorDefaultMethodDAttributes, Assets.TypeReferences.Void)
            {
                Body = {InitLocals = true}
            };

            // Setup parameters
            // Prepare type for parameter
            var actionGenericInstanceType = new GenericInstanceType(Assets.TypeReferences.ActionOfT);
            actionGenericInstanceType.GenericArguments.Add(commandClass.GenericParameters[0]);

            var predicateGenericInstanceType = new GenericInstanceType(Assets.TypeReferences.PredicateOfT);
            predicateGenericInstanceType.GenericArguments.Add(commandClass.GenericParameters[0]);

            var actionParameter = new ParameterDefinition("execute", ParameterAttributes.None, actionGenericInstanceType);
            constructor.Parameters.Add(actionParameter);

            var predicateParameter = new ParameterDefinition("canExecute", ParameterAttributes.None, predicateGenericInstanceType);
            constructor.Parameters.Add(predicateParameter);

            // Setup variables
            var isNullVariable = new VariableDefinition(Assets.TypeReferences.Boolean);
            constructor.Body.Variables.Add(isNullVariable);

            var il = constructor.Body.GetILProcessor();
            var assignmentBlock = il.Create(OpCodes.Nop);
            // Call base ... base()
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Call, Assets.ObjectConstructorReference));

            // Check execute for null
            // if (execute == null) {
            // ...
            // }
            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Ceq));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ceq));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Brtrue_S, assignmentBlock));

            // Throw ArgumentNullException 
            // throw new ArgumentNullException("execute");
            il.Append(il.Create(OpCodes.Ldstr,"execute"));
            il.Append(il.Create(OpCodes.Newobj, Assets.ArgumentNullExceptionConstructorReference));
            il.Append(il.Create(OpCodes.Throw));

            il.Append(assignmentBlock);
            // assign to _execute field
            // this._execute = execute;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Stfld, executeField));

            // assign to _canExecute field
            // this._canExecute = canExecute;
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldarg_2));
            il.Append(Instruction.Create(OpCodes.Stfld, canExecuteField));

            // return
            il.Append(Instruction.Create(OpCodes.Nop));
            il.Append(Instruction.Create(OpCodes.Ret));
            commandClass.Methods.Add(constructor);
            return constructor;
        }

        internal void AddFields(TypeDefinition commandClass)
        {
            var predicateGenericInstanceType = new GenericInstanceType(Assets.TypeReferences.PredicateOfT);
            predicateGenericInstanceType.GenericArguments.Add(commandClass.GenericParameters[0]);

            var canExecuteField = new FieldDefinition(
                "_canExecute"
                , FieldAttributes.Private | FieldAttributes.InitOnly
                , predicateGenericInstanceType);

            commandClass.Fields.Add(canExecuteField);

            var actionGenericInstanceType = new GenericInstanceType(Assets.TypeReferences.ActionOfT);
            actionGenericInstanceType.GenericArguments.Add(commandClass.GenericParameters[0]);

            var executeField = new FieldDefinition(
                "_execute"
                , FieldAttributes.Private | FieldAttributes.InitOnly
                , actionGenericInstanceType);

            commandClass.Fields.Add(executeField);

            var canExecuteChangedField = new FieldDefinition(
                "CanExecuteChanged"
                , FieldAttributes.Private
                , Assets.TypeReferences.EventHandler);

            commandClass.Fields.Add(canExecuteChangedField);
        }

        internal void AddDExecuteMethod(TypeDefinition commandClass)
        {
            var field = commandClass.Fields.Single(x => x.Name == "_execute");
            var method = new MethodDefinition("Execute",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                MethodAttributes.Virtual, Assets.TypeReferences.Void)
            {
                Body = { InitLocals = true }
            };

            var commandParameter = new ParameterDefinition("parameter", ParameterAttributes.None, Assets.TypeReferences.Object);
            method.Parameters.Add(commandParameter);

            commandClass.Methods.Add(method);
            
            // Get MethodReference for Action<T>::Invoke(T);
            var genericType = new GenericInstanceType(commandClass);
            genericType.GenericArguments.Add(commandClass.GenericParameters[0]);
            var tParameter = genericType.GenericArguments[0];
            var invoker = Assets.ActionOfTInvokeReference.MakeHostInstanceGeneric(tParameter);

            var il = method.Body.GetILProcessor();
            var start = Instruction.Create(OpCodes.Nop);
            il.Append(start);
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, field));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Unbox_Any, tParameter));
            il.Append(Instruction.Create(OpCodes.Callvirt, invoker));
            il.Append(Instruction.Create(OpCodes.Nop));
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        internal void AddDCanExecuteMethod(TypeDefinition commandClass)
        {
            var field = commandClass.Fields.Single(x => x.Name == "_canExecute");

            var method = new MethodDefinition("CanExecute",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                MethodAttributes.Virtual, Assets.TypeReferences.Boolean)
            {
                Body = { InitLocals = true }
            };

            var commandParameter = new ParameterDefinition("parameter", ParameterAttributes.None, Assets.TypeReferences.Object);
            method.Parameters.Add(commandParameter);

            var returnVariable = new VariableDefinition(Assets.TypeReferences.Boolean);
            method.Body.Variables.Add(returnVariable);

            commandClass.Methods.Add(method);

            // Get MethodReference for Predicate<T>::Invoke(T);
            var genericType = new GenericInstanceType(commandClass);
            genericType.GenericArguments.Add(commandClass.GenericParameters[0]);
            var tParameter = genericType.GenericArguments[0];
            var invoker = Assets.PredicateOfTInvokeReference.MakeHostInstanceGeneric(tParameter);

            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Nop));
            
            var returnBlock = Instruction.Create(OpCodes.Ldloc_0);
            var isNullBlock = il.Create(OpCodes.Ldc_I4_1);
            var storeAndBranchBlock = il.Create(OpCodes.Nop);

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, field));
            il.Append(il.Create(OpCodes.Brfalse_S, isNullBlock));

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, field));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Unbox_Any, tParameter));
            il.Append(Instruction.Create(OpCodes.Callvirt, invoker));
            il.Append(il.Create(OpCodes.Br_S, storeAndBranchBlock));

            il.Append(isNullBlock);
            il.Append(storeAndBranchBlock);
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Br_S, returnBlock));
            il.Append(returnBlock);
            il.Append(Instruction.Create(OpCodes.Ret));
        }
    }
}