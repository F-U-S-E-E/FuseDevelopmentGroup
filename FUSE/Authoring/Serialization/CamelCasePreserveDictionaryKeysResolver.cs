using Newtonsoft.Json.Serialization;
using System;
using System.Reflection;

namespace FUSE.Authoring.Serialization
{
    /// <summary>
    /// Contract resolver that applies CamelCase naming to property names,
    /// but preserves the original case for dictionary keys (which are often IDs that are case-sensitive).
    /// </summary>
    internal sealed class CamelCasePreserveDictionaryKeysResolver : CamelCasePropertyNamesContractResolver
    {
        protected override JsonDictionaryContract CreateDictionaryContract(Type objectType)
        {
            var contract = base.CreateDictionaryContract(objectType);

            // Don't apply naming strategy to dictionary keys
            // This preserves the original key casing for ID lookups
            contract.DictionaryKeyResolver = (key) => key;

            return contract;
        }
    }
}
