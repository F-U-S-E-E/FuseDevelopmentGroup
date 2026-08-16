using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using FUSE.Patches;
using HarmonyLib;
using UI.Menu;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseNewGameMapMenuPatchTests
    {
        [Fact]
        public void BuildPanelTranspiler_InsertsMapFieldAndRelabelsProgression()
        {
            var target = AccessTools.Method(
                typeof(NewGameMenu),
                "BuildPanelContent");
            var patchType = typeof(FuseNewGameMapMenuPatch);
            var transpiler = patchType.GetMethod(
                "BuildPanelContentTranspiler",
                BindingFlags.Static | BindingFlags.NonPublic);
            var addWorldField = patchType.GetMethod(
                "AddWorldField",
                BindingFlags.Static | BindingFlags.NonPublic);
            var original = PatchProcessor
                .GetOriginalInstructions(target)
                .ToList();

            var patched = (List<CodeInstruction>)transpiler.Invoke(
                null,
                new object[] { original });

            Assert.Contains(
                patched,
                instruction => instruction.Calls(addWorldField));
            Assert.Contains(
                patched,
                instruction =>
                    instruction.opcode == OpCodes.Ldstr &&
                    (string)instruction.operand == "Starting Progression");
        }
    }
}
