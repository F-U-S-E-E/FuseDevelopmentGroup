using System;
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

        public class AudioRules
        {
            [Fact]
            public void NullWhistle_EmitsError()
            {
                var definition = MinimalValid();
                definition.Audio.Whistles["w"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.audio.whistle.required");
            }

            [Fact]
            public void Whistle_BlankNameOrClip_EmitsRequiredErrors()
            {
                var definition = MinimalValid();
                definition.Audio.Whistles["w"] = new FuseWhistleAudio { Name = null, Clip = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "audio.whistles.w.name" && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field == "audio.whistles.w.clip" && e.Code == "fuse.required");
            }

            [Fact]
            public void NullHorn_EmitsError()
            {
                var definition = MinimalValid();
                definition.Audio.Horns["h"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.audio.horn.required");
            }

            [Fact]
            public void Horn_WithNoLayers_EmitsError()
            {
                var definition = MinimalValid();
                definition.Audio.Horns["h"] = new FuseHornAudio { Name = "Horn", Layers = Array.Empty<FuseHornLayer>() };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.audio.horn.layers");
            }

            [Fact]
            public void Horn_LayerMissingFile_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.Audio.Horns["h"] = new FuseHornAudio
                {
                    Name = "Horn",
                    Layers = new[] { new FuseHornLayer { File = null } }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "audio.horns.h.layers[0].file" && e.Code == "fuse.required");
            }

            [Fact]
            public void Horn_LayerWithoutKeyframes_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Audio.Horns["h"] = new FuseHornAudio
                {
                    Name = "Horn",
                    Layers = new[] { new FuseHornLayer { File = "h.ogg", Keyframes = null } }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.audio.horn.keyframes.empty");
            }

            [Fact]
            public void NullBell_EmitsError()
            {
                var definition = MinimalValid();
                definition.Audio.Bells["b"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.audio.bell.required");
            }

            [Fact]
            public void Bell_BlankNameOrFile_EmitsRequiredErrors()
            {
                var definition = MinimalValid();
                definition.Audio.Bells["b"] = new FuseBellAudio { Name = null, File = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "audio.bells.b.name" && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field == "audio.bells.b.file" && e.Code == "fuse.required");
            }
        }
    }
}
