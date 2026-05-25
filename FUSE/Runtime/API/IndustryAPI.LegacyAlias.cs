using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class IndustryAPI
    {

        private static string NormalizeLegacyIndustryReference(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return id;
            }

            const string colliePrefix = "collie-";
            return id.StartsWith(colliePrefix, StringComparison.OrdinalIgnoreCase)
                ? id.Substring(colliePrefix.Length)
                : id;
        }

        private static bool IndustryMatchesLegacyReference(Industry industry, string reference)
        {
            if (industry == null || string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            return string.Equals(industry.name, reference, StringComparison.OrdinalIgnoreCase) ||
                   LooseIdEquals(industry.identifier, reference) ||
                   LooseIdEquals(industry.name, reference);
        }

        private static bool LooseIdEquals(string left, string right)
        {
            var normalizedLeft = NormalizeLooseId(left);
            return normalizedLeft.Length > 0 &&
                   string.Equals(normalizedLeft, NormalizeLooseId(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLooseId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }
    }
}
