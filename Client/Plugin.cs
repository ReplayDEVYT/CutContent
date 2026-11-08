using BepInEx;
using BepInEx.Logging;

namespace mizmii.ReClassic
{
    [BepInPlugin(Guid, "Re-Classic", "1.2.0")]
    [BepInDependency("xyz.drakia.searchopencontainers")]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.mizmii.reclassic";

        public static ManualLogSource LogSource;

        private void Awake()
        {
            LogSource = Logger;
            LogSource.LogInfo("Re-Classic loaded!");
            MigrateOldConfig();
            Sync.SettingsSync.Init(Config);

            Start("Effects", () => Effects.CustomEffects.Enable());
            Start("Unconsciousness", () => Consciousness.Consciousness.Enable(Config));
            Start("Interaction Focus", () =>
            {
                Patches.InteractionFocus.Enable(Config);
            });
            Start("Open Containers", () =>
            {
                Patches.NoContainerInventoryPatch.Bind(Config);
                new Patches.NoContainerInventoryPatch().Enable();
            });
            Start("PropPhysics", () =>
            {
                Patches.PropPhysics.Enable(Config);
            });
            Start("WorldInteractions", () =>
            {
                Patches.WorldInteractions.Enable(Config);
            });
            Start("DoorPhysics", () => Patches.DoorPhysics.Enable(Config));
            Start("Footsteps", () => Patches.Footsteps.Enable(Config));
            Start("Acoustics", () =>
            {
                Patches.Acoustics.Enable(Config);
            });
            Start("FragmentDamage", () =>
            {
                Patches.FragmentDamage.Enable(Config);
            });
            Start("AimPunch", () =>
            {
                Patches.AimPunch.Enable(Config);
            });
            Start("Movement", () =>
            {
                Patches.Movement.Enable(Config);
            });
            Start("WeaponHandling", () =>
            {
                Patches.WeaponHandling.Enable(Config);
            });
            Start("LegIK", () =>
            {
                Patches.LegIK.Enable(Config);
            });
            Start("Bobbing", () =>
            {
                Patches.Bobbing.Enable(Config);
            });
            Start("Visuals", () =>
            {
                Patches.Visuals.Enable(Config);
            });
            Start("Scopes", () =>
            {
                Patches.Scopes.Enable(Config);
            });
            Start("AlphaLighting", () =>
            {
                Patches.AlphaLighting.Enable(Config);
            });
            Start("Auctions", () =>
            {
                Auctions.Auctions.Enable(Config);
            });
            Start("Repair Time", () => Repairs.RepairTime.Enable(Config));
            Start("Body Search", () => Patches.BodySearch.Enable(Config));
            Start("Item Usage Bar", () => Patches.UsingBar.Enable(Config));
            Start("Looting Animations", () => Patches.LootAnimations.Enable(Config));
            Start("Raid Timers", () => Raid.RaidTimers.Enable(Config));
            Start("Flashlight Beam", () => Patches.FlashlightCookie.Enable(Config));
            Start("Grenade Quick Throw", () => Patches.QuickThrow.Enable(Config));
            Start("Map Guides", () => Patches.MapGuides.Enable(Config));
            Start("PMC Karma", () => Karma.PmcKarma.Enable(Config));
            Start("FeatureSettingsTab", () =>
            {
                Patches.FeatureSettingsTab.Enable();
            });
            Start("Fika", () =>
            {
                Sync.FikaSync.Init();
            });
            Start("WelcomePopup", () =>
            {
                Patches.WelcomePopup.Enable(Config);
            });
        }

        private static void Start(string feature, System.Action enable)
        {
            try
            {
                enable();
            }
            catch (System.Exception e)
            {
                LogSource.LogError($"Could not start {feature}: {e}");
            }
        }

        private void MigrateOldConfig()
        {
            try
            {
                bool reload = MoveFile(Guid + ".cfg", "mizmii.ReClassic.cfg", "mizmii.CutContent.cfg");
                MoveFile(Guid + ".PropFilters.json", "mizmii.ReClassic.PropFilters.json", "mizmii.CutContent.PropFilters.json");
                if (reload)
                {
                    Config.Reload();
                    LogSource.LogInfo("Moved the old Re-Classic settings over to " + Guid + ".cfg.");
                }
            }
            catch (System.Exception e)
            {
                LogSource.LogWarning($"Could not move the old Re-Classic settings: {e.Message}");
            }
        }

        private static bool MoveFile(string newName, params string[] oldNames)
        {
            string newPath = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, newName);
            if (System.IO.File.Exists(newPath))
            {
                return false;
            }
            foreach (string oldName in oldNames)
            {
                string oldPath = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, oldName);
                if (System.IO.File.Exists(oldPath))
                {
                    System.IO.File.Move(oldPath, newPath);
                    return true;
                }
            }
            return false;
        }
    }
}
