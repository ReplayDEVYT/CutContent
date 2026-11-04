using BepInEx;
using BepInEx.Logging;

namespace mizmii.CutContent
{
    [BepInPlugin("mizmii.CutContent", "CutContent", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;

        private void Awake()
        {
            LogSource = Logger;
            LogSource.LogInfo("CutContent loaded!");

            // Enable your patches here. Each patch class inherits ModulePatch.
            // Uncomment the line below to enable the example patch:
            // new Patches.ExamplePatch().Enable();
        }
    }
}
