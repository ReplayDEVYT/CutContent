using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace mizmii.CutContent.Patches
{
    // All patches must inherit ModulePatch from SPT.Reflection.
    // Enable this patch in Plugin.cs with: new Patches.ExamplePatch().Enable();
    internal class ExamplePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Target the method you want to patch.
            // This example patches Player.Jump() as a demonstration.
            return AccessTools.Method(typeof(Player), nameof(Player.Jump));
        }

        [PatchPrefix]
        static bool Prefix()
        {
            // Prefix runs BEFORE the original method.
            // Return true  -> original code will still execute.
            // Return false -> original code is skipped.

            Plugin.LogSource.LogWarning("Player is about to jump!");
            return true;
        }

        [PatchPostfix]
        static void Postfix()
        {
            // Postfix runs AFTER the original method has executed.
            Plugin.LogSource.LogInfo("Player just jumped!");
        }
    }
}
