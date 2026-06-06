using System.Collections.Generic;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Authoring.Validation;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Validation
{
    public partial class FuseDefinitionValidatorDeeperTests
    {
        private static FuseDefinitionValidator NewValidator() => new FuseDefinitionValidator();

        private static FuseModDefinition MinimalValid() => new FuseModDefinition
        {
            Id = "pkg",
            Name = "Package",
            SchemaVersion = "1.0"
        };
    }
}
