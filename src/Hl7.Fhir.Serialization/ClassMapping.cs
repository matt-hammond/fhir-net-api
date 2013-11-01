﻿using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hl7.Fhir.Serialization
{
    public enum FhirModelConstruct
    {
        PrimitiveType,
        ComplexType,
        Resource
    }


    public class ClassMapping : ICloneable
    {
        internal const string RESOURCENAME_SUFFIX = "Resource";

        public FhirModelConstruct ModelConstruct;

        public string Name { get; private set; }
        public string Profile { get; private set; }
        
        public Type ImplementingType { get; private set; }
   //     public Type PrimitiveType { get; private set; }

        private Func<string, object> _primitiveParsingFunction;

        public object Clone()
        {
            var result = new ClassMapping();

            result.ModelConstruct = this.ModelConstruct;
            result.Name = this.Name;
            result.Profile = this.Profile;
            result._elements = this._elements;
            result.ImplementingType = this.ImplementingType;

            result._primitiveParsingFunction = buildPrimitiveParserInvoker(result.ImplementingType);

            return result;
        }


        public object Parse(string value)
        {
            if (ModelConstruct == FhirModelConstruct.PrimitiveType)
            {
                return _primitiveParsingFunction(value);
            }
            else
                throw Error.InvalidOperation("Can only invoke Parse on a primitive mapped class");
        }
   

        public ClassMapping CloseGenericMapping(params Type[] genericArgs)
        {
            if (ImplementingType.ContainsGenericParameters)
            {
                var closedMapping = (ClassMapping)this.Clone();
                closedMapping.ImplementingType = ImplementingType.MakeGenericType(genericArgs);

                return closedMapping;
            }
            else
            {
                Message.Info("Called CloseGenericMapping on already closed generic {0}", ImplementingType.Name);
                return null;
            }
        }

        // Elements indexed by uppercase name for access speed
        private Dictionary<string, PropertyMapping> _elements = new Dictionary<string, PropertyMapping>();

        public IEnumerable<PropertyMapping> Elements
        {
            get
            {
                return _elements.Values;
            }
        }

        internal void AddElements(IEnumerable<PropertyMapping> elements)
        {
            foreach(var element in elements)
            {
                _elements.Add(element.Name.ToUpperInvariant(), element);
            }
        }

        internal PropertyMapping FindMappedPropertyForElement(string name)
        {
            var normalizedName = name.ToUpperInvariant();

            PropertyMapping prop = null;

            bool success = _elements.TryGetValue(normalizedName, out prop);

            // Direct success
            if (success) return prop;
            
            // Not found, maybe a polymorphic name
            // TODO: specify possible polymorhpic variations using attributes
            // to speedup look up & aid validation
            return Elements.SingleOrDefault(p => p.MatchesSuffixedName(name));            
        }

        public static ClassMapping CreateForResource(Type t)
        {
            var result = new ClassMapping();
            result.ModelConstruct = FhirModelConstruct.Resource;
            result.Name = getMappedResourceName(t);
            result.Profile = getProfile(t);
            result.ImplementingType = t;

            return result;
        }


        public static ClassMapping CreateForComplexType(Type t)
        {
            var result = new ClassMapping();
            result.ModelConstruct = FhirModelConstruct.ComplexType;
            result.Name = getMappedComplexTypeName(t);
            result.Profile = null;  // No support for profiled datatypes
            result.ImplementingType = t;

            return result;
        }

        public static ClassMapping CreateForFhirPrimitive(Type t)
        {
            var result = new ClassMapping();
            result.ModelConstruct = FhirModelConstruct.PrimitiveType;
            result.Name = getMappedPrimitiveTypeName(t);
            result.Profile = null;  // No support for profiled datatypes
            result.ImplementingType = t;
            result._primitiveParsingFunction = buildPrimitiveParserInvoker(t);
            
            return result;
        }

        private static Func<string,object> buildPrimitiveParserInvoker(Type implementingType)
        {
            // Now determine actual .NET primitive used for the ImplementingType's Value property
            //var valueProperty = ReflectionHelper.FindPublicProperty(result.ImplementingType, "Value");
            //if(valueProperty == null) throw Error.InvalidOperation("Expected a Value property on the mapped primitive class {0}", result.ImplementingType.Name);
            //result.PrimitiveType = valueProperty.PropertyType;
            if (implementingType.IsEnum)
            {
                return input => invokeEnumParser(input, implementingType);
            }
            else
            {
                var parseMethod = ReflectionHelper.FindPublicStaticMethod(implementingType, "Parse", typeof(string));
                if (parseMethod == null) throw Error.InvalidOperation("Expected a static Parse(string) function on the mapped primitive class {0}", implementingType.Name);

                return input => parseMethod.Invoke(null, new object[] { input });
            }
        }


        private static object invokeEnumParser(string input, Type enumType)
        {
            object result = null;
            bool success = EnumHelper.TryParseEnum(input, enumType, out result);

            if (!success)
                throw Error.InvalidOperation("Parsing of enum failed");

            return result;
        }

        private static string getProfile(Type type)
        {
            var attr = (FhirResourceAttribute)Attribute.GetCustomAttribute(type, typeof(FhirResourceAttribute));

            return attr != null ? attr.Profile : null;
        }

        private static string getMappedResourceName(Type type)
        {
            var attr = (FhirResourceAttribute)Attribute.GetCustomAttribute(type, typeof(FhirResourceAttribute));

            if (attr != null)
            {
                return attr.Name;
            }                
            else
            {
                var name = type.Name;
                if (name.EndsWith(RESOURCENAME_SUFFIX))
                    name = name.Substring(0, name.Length - RESOURCENAME_SUFFIX.Length);

                return name;
            }
        }


        private static string getMappedComplexTypeName(Type type)
        {
            var attr = (FhirComplexTypeAttribute)Attribute.GetCustomAttribute(type, typeof(FhirComplexTypeAttribute));

            if (attr != null)
                return attr.Name;
            else
                return type.Name;
        }

        private static string getMappedPrimitiveTypeName(Type type)
        {
            var attr = (FhirPrimitiveTypeAttribute)Attribute.GetCustomAttribute(type, typeof(FhirPrimitiveTypeAttribute));

            if (attr != null)
                return attr.Name;
            else
                return type.Name;
        }

        public static bool IsFhirResource(Type type)
        {
            return typeof(Resource).IsAssignableFrom(type)
                    || hasResourceNameSuffix(type)
                    || type.IsDefined(typeof(FhirResourceAttribute),true);
        }

        private static bool hasResourceNameSuffix(Type type)
        {
            // This means it *ends* in Resource, not just "Resource"
            return type.Name.EndsWith(ClassMapping.RESOURCENAME_SUFFIX) && ClassMapping.RESOURCENAME_SUFFIX != type.Name;
        }

        public static bool IsFhirComplexType(Type type)
        {
            return typeof(ComplexElement).IsAssignableFrom(type)
                || type.IsDefined(typeof(FhirComplexTypeAttribute), true);
        }

        public static bool IsFhirPrimitive(Type type)
        {
            return typeof(PrimitiveElement).IsAssignableFrom(type)
                || type.IsDefined(typeof(FhirPrimitiveTypeAttribute), true)
                || type.IsDefined(typeof(FhirEnumerationAttribute), false);
        }    

    }
}