﻿using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace Commander.Fody
{
    public partial class ModuleWeaver
    {
        private readonly List<string> _propertyAttributeNames = new List<string>
        {
            "Commander.OnCommandAttribute",
            "Commander.OnCommandCanExecuteAttribute",
        };

        void ProcessType(TypeDefinition type)
        {
            RemoveAttributes(type.CustomAttributes);
            foreach (var method in type.Methods)
            {
                RemoveAttributes(method.CustomAttributes);
            }            
        }

        void RemoveAttributes(Collection<CustomAttribute> customAttributes)
        {
            var attributes = customAttributes
                .Where(attribute => _propertyAttributeNames.Contains(attribute.Constructor.DeclaringType.FullName));

            foreach (var customAttribute in attributes.ToList())
            {
                customAttributes.Remove(customAttribute);
            }
        }

        public void CleanAttributes()
        {
            foreach (var type in ModuleDefinition.GetTypes())
            {
                ProcessType(type);
            }
        }
    }
}