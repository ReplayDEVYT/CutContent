using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Consciousness
{
    internal sealed class RobberyReport
    {
        public int Weapons;
        public int PlatesTaken;
        public int PlatesSwapped;
        public int Items;
        public int Dropped;
        public int Parts;
        public readonly List<Item> StripFromHands = new List<Item>();
        public readonly List<Item> TakeFromHands = new List<Item>();
        public readonly List<Item> DropFromHands = new List<Item>();
        public List<Player> Robbers = new List<Player>();
        public bool Give;

        public bool Any => Weapons + PlatesTaken + PlatesSwapped + Items + Dropped + Parts > 0;

        public string Describe()
        {
            return "Some of your items have been stolen.";
        }
    }

    internal static class Robbery
    {
        private static readonly EquipmentSlot[] WeaponSlots = { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster };
        private static readonly EquipmentSlot[] ArmorSlots = { EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest, EquipmentSlot.Headwear };
        private static readonly EquipmentSlot[] LootSlots = { EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.Pockets };
        private static readonly EquipmentSlot[] CarrySlots = { EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.Pockets };
        private static readonly EquipmentSlot[] DroppableSlots = { EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.Headwear, EquipmentSlot.Earpiece, EquipmentSlot.FaceCover, EquipmentSlot.Eyewear, EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon };

        private static Player _lastAttacker;
        private static float _lastAttackTime;

        public static void Enable()
        {
            new AttackerPatch().Enable();
            new KnockedOutShotPatch().Enable();
        }

        public static Player RecentAttacker(float seconds)
        {
            if (_lastAttacker == null || Time.time - _lastAttackTime > seconds || _lastAttacker.HealthController == null || !_lastAttacker.HealthController.IsAlive)
            {
                return null;
            }
            return _lastAttacker;
        }

        public static void Forget()
        {
            _lastAttacker = null;
        }

        private static IEnumerable<Player> LocalBots()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                return Enumerable.Empty<Player>();
            }
            return world.AllAlivePlayersList.Where(p => p != null && p.IsAI && p.AIData?.BotOwner != null && p.HealthController != null && p.HealthController.IsAlive).ToList();
        }

        public static List<Player> Robbers(Vector3 origin)
        {
            if (!Consciousness.Robbing)
            {
                return new List<Player>();
            }
            float radius = Consciousness.RobberyRadius;
            return LocalBots().Where(b => (b.Position - origin).sqrMagnitude <= radius * radius).OrderBy(b => (b.Position - origin).sqrMagnitude).ToList();
        }

        public static int DespawnKillers(Player killer, Vector3 origin)
        {
            IBotGame game = Singleton<IBotGame>.Instance;
            if (game == null)
            {
                return 0;
            }
            var leaving = new HashSet<Player>();
            List<Player> bots = LocalBots().ToList();
            Vector3 centre = origin;
            if (killer != null && killer.IsAI && killer.AIData?.BotOwner != null)
            {
                leaving.Add(killer);
                centre = killer.Position;
                BotsGroup group = killer.AIData.BotOwner.BotsGroup;
                if (group != null)
                {
                    for (int i = 0; i < group.MembersCount; i++)
                    {
                        Player member = group.Member(i)?.GetPlayer;
                        if (member != null)
                        {
                            leaving.Add(member);
                        }
                    }
                }
            }
            else
            {
                Player nearest = Robbers(origin).FirstOrDefault();
                if (nearest == null)
                {
                    return 0;
                }
                leaving.Add(nearest);
                centre = nearest.Position;
            }
            float radius = Consciousness.FollowerRadius;
            foreach (Player bot in bots)
            {
                if ((bot.Position - centre).sqrMagnitude <= radius * radius || (bot.Position - origin).sqrMagnitude <= radius * radius)
                {
                    leaving.Add(bot);
                }
            }
            int gone = 0;
            foreach (Player bot in leaving)
            {
                if (bot == null || bot.AIData?.BotOwner == null || !bot.HealthController.IsAlive)
                {
                    continue;
                }
                try
                {
                    game.BotDespawn(bot.AIData.BotOwner);
                    gone++;
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Unconsciousness] Could not despawn {bot.Profile?.Nickname}: {e.Message}");
                }
            }
            return gone;
        }

        public static RobberyReport Rob(Player victim, List<Player> robbers, bool giveToBots)
        {
            var report = new RobberyReport { Robbers = robbers ?? new List<Player>(), Give = giveToBots };
            float chance = TheftChance(victim);
            if (chance <= 0f || victim?.InventoryController == null || robbers == null || (giveToBots && robbers.Count == 0))
            {
                return report;
            }
            var robber = new Robber(victim, robbers, giveToBots);
            robber.Weapons(chance, report);
            robber.Attachments(chance, report);
            robber.Plates(chance, report);
            robber.Loot(chance, report);
            return report;
        }

        public static bool InHands(Player victim, Item item)
        {
            return item != null && victim?.HandsController != null && victim.HandsController.Item == item;
        }

        public static KnifeComponent Melee(Player victim)
        {
            return victim?.Equipment?.GetSlot(EquipmentSlot.Scabbard)?.ContainedItem?.GetItemComponent<KnifeComponent>();
        }

        public static void HandOver(Player victim, RobberyReport report)
        {
            if (report == null || victim == null || !victim.HealthController.IsAlive)
            {
                return;
            }
            if (report.StripFromHands.Count > 0)
            {
                List<Player> strippers = report.Robbers.Where(r => r != null && r.HealthController != null && r.HealthController.IsAlive && r.AIData?.BotOwner != null).ToList();
                var stripper = new Robber(victim, strippers, report.Give && strippers.Count > 0);
                foreach (Item part in report.StripFromHands)
                {
                    if (part.CurrentAddress != null && !report.TakeFromHands.Any(w => part.GetAllParentItemsAndSelf().Contains(w)))
                    {
                        stripper.TakeOrRemove(part);
                    }
                }
            }
            if (report.TakeFromHands.Count + report.DropFromHands.Count == 0)
            {
                return;
            }
            KnifeComponent knife = Melee(victim);
            if (knife == null)
            {
                return;
            }
            try
            {
                victim.SetInHands(knife, result =>
                {
                    if (result.Failed)
                    {
                        Plugin.LogSource.LogWarning($"[Unconsciousness] Could not switch to the melee weapon: {result.Error}");
                        return;
                    }
                    List<Player> robbers = report.Robbers.Where(r => r != null && r.HealthController != null && r.HealthController.IsAlive && r.AIData?.BotOwner != null).ToList();
                    var robber = new Robber(victim, robbers, report.Give && robbers.Count > 0);
                    foreach (Item item in report.TakeFromHands)
                    {
                        if (item.CurrentAddress != null && !robber.Take(item, true))
                        {
                            robber.Drop(item);
                        }
                    }
                    foreach (Item item in report.DropFromHands)
                    {
                        if (item.CurrentAddress != null)
                        {
                            robber.Drop(item);
                        }
                    }
                });
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Unconsciousness] Could not hand over the weapon: {e.Message}");
            }
        }

        public static int DropGear(Player victim, RobberyReport report)
        {
            if (victim?.InventoryController == null || UnityEngine.Random.value >= Consciousness.DropChance)
            {
                return 0;
            }
            List<Item> candidates = DroppableSlots
                .Select(s => victim.Equipment.GetSlot(s).ContainedItem)
                .Where(i => i != null && !i.QuestItem && i.PinLockState != EItemPinLockState.Locked && (!InHands(victim, i) || Melee(victim) != null))
                .OrderBy(_ => UnityEngine.Random.value)
                .ToList();
            int count = Math.Min(candidates.Count, UnityEngine.Random.Range(1, 4));
            int dropped = 0;
            for (int i = 0; i < count; i++)
            {
                if (InHands(victim, candidates[i]))
                {
                    report?.DropFromHands.Add(candidates[i]);
                    dropped++;
                    continue;
                }
                try
                {
                    if (victim.InventoryController.TryThrowItem(candidates[i], null, true).Succeeded)
                    {
                        dropped++;
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Unconsciousness] Could not drop {candidates[i].ShortName.Localized()}: {e.Message}");
                }
            }
            return dropped;
        }

        public static float TheftChance(Player victim)
        {
            Item secure = victim?.Equipment?.GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem;
            double total = 0d;
            foreach (EquipmentSlot slotName in Enum.GetValues(typeof(EquipmentSlot)))
            {
                Item item = victim?.Equipment?.GetSlot(slotName)?.ContainedItem;
                if (item == null || item == secure)
                {
                    continue;
                }
                total += Value(item);
            }
            return 0.2f + 0.7f * (float)(total / (total + 400000d));
        }

        public static double Value(Item item)
        {
            return item.GetAllItems().Sum(i => Math.Max(0, i.Template.CreditsPrice) * (double)Math.Max(1, i.StackObjectsCount));
        }

        public static float PlateScore(Item plate)
        {
            ArmorComponent armor = plate?.GetItemComponent<ArmorComponent>();
            if (armor == null)
            {
                return 0f;
            }
            RepairableComponent repairable = plate.GetItemComponent<RepairableComponent>();
            float condition = repairable != null && repairable.MaxDurability > 0f ? repairable.Durability / repairable.MaxDurability : 1f;
            return armor.ArmorClass * (0.25f + 0.75f * condition);
        }

        private sealed class Robber
        {
            private readonly Player _victim;
            private readonly List<Player> _robbers;
            private readonly bool _give;
            private readonly InventoryController _victimController;
            private readonly HashSet<Item> _taken = new HashSet<Item>();

            public Robber(Player victim, List<Player> robbers, bool give)
            {
                _victim = victim;
                _robbers = robbers;
                _give = give;
                _victimController = victim.InventoryController;
            }

            public void Weapons(float chance, RobberyReport report)
            {
                foreach (EquipmentSlot slotName in WeaponSlots)
                {
                    Item weapon = _victim.Equipment.GetSlot(slotName).ContainedItem;
                    if (weapon == null || weapon.QuestItem)
                    {
                        continue;
                    }
                    double value = Value(weapon);
                    float odds = chance * (0.35f + 0.65f * (float)(value / (value + 60000d)));
                    if (UnityEngine.Random.value >= odds)
                    {
                        continue;
                    }
                    if (InHands(_victim, weapon))
                    {
                        if (Melee(_victim) != null)
                        {
                            report.TakeFromHands.Add(weapon);
                            report.Weapons++;
                        }
                        continue;
                    }
                    if (Take(weapon, true))
                    {
                        report.Weapons++;
                    }
                }
            }

            public void Plates(float chance, RobberyReport report)
            {
                foreach (EquipmentSlot slotName in ArmorSlots)
                {
                    Item carrier = _victim.Equipment.GetSlot(slotName).ContainedItem;
                    if (!(carrier is CompoundItem compound) || _taken.Contains(carrier))
                    {
                        continue;
                    }
                    foreach (Slot slot in compound.Slots.Where(s => s is ArmorSlot && !s.Locked).ToList())
                    {
                        Item plate = slot.ContainedItem;
                        float score = PlateScore(plate);
                        if (plate == null || score <= 0f)
                        {
                            continue;
                        }
                        float odds = chance * Mathf.Clamp01(0.2f + score / 7f);
                        if (UnityEngine.Random.value >= odds)
                        {
                            continue;
                        }
                        if (!_give)
                        {
                            if (Remove(plate))
                            {
                                report.PlatesTaken++;
                            }
                            continue;
                        }
                        if (TrySwapPlate(slot, plate, score, out bool swapped))
                        {
                            if (swapped)
                            {
                                report.PlatesSwapped++;
                            }
                            else
                            {
                                report.PlatesTaken++;
                            }
                        }
                    }
                }
            }

            public void Loot(float chance, RobberyReport report)
            {
                foreach (EquipmentSlot slotName in LootSlots)
                {
                    Item container = _victim.Equipment.GetSlot(slotName).ContainedItem;
                    if (container == null)
                    {
                        continue;
                    }
                    foreach (Item item in container.GetAllItems().ToList())
                    {
                        if (item == container || item.QuestItem || item.CurrentAddress == null || item.IsArmorMod())
                        {
                            continue;
                        }
                        if (item.GetAllParentItemsAndSelf().Any(parent => _taken.Contains(parent)))
                        {
                            continue;
                        }
                        double value = Value(item);
                        float odds = chance * (0.15f + 0.85f * (float)(value / (value + 40000d)));
                        if (UnityEngine.Random.value >= odds)
                        {
                            continue;
                        }
                        if (Take(item, false))
                        {
                            report.Items++;
                        }
                    }
                }
            }

            public void Attachments(float chance, RobberyReport report)
            {
                foreach (EquipmentSlot slotName in WeaponSlots)
                {
                    Item weapon = _victim.Equipment.GetSlot(slotName).ContainedItem;
                    if (weapon == null || _taken.Contains(weapon) || report.TakeFromHands.Contains(weapon))
                    {
                        continue;
                    }
                    bool held = InHands(_victim, weapon);
                    var chosen = new List<Item>();
                    foreach (Item part in weapon.GetAllItems().ToList())
                    {
                        if (part == weapon || !(part is Mod) || part.QuestItem || !(part.CurrentAddress?.Container is Slot slot) || slot.Required)
                        {
                            continue;
                        }
                        if (chosen.Any(c => part.GetAllParentItemsAndSelf().Contains(c)))
                        {
                            continue;
                        }
                        double value = Value(part);
                        float odds = chance * 0.6f * (0.1f + 0.9f * (float)(value / (value + 30000d)));
                        if (UnityEngine.Random.value < odds)
                        {
                            chosen.Add(part);
                        }
                    }
                    foreach (Item part in chosen)
                    {
                        if (held)
                        {
                            report.StripFromHands.Add(part);
                            report.Parts++;
                        }
                        else if (TakeOrRemove(part))
                        {
                            report.Parts++;
                        }
                    }
                }
            }

            public bool TakeOrRemove(Item item)
            {
                return Take(item, false);
            }

            public void Drop(Item item)
            {
                try
                {
                    _victimController.TryThrowItem(item, null, true);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Unconsciousness] Could not drop {item.ShortName.Localized()}: {e.Message}");
                }
            }

            public bool Take(Item item, bool weapon)
            {
                if (!_give)
                {
                    return Remove(item);
                }
                foreach (Player robber in _robbers)
                {
                    ItemAddress place = weapon ? WeaponPlace(robber, item) ?? GridPlace(robber, item) : GridPlace(robber, item);
                    if (place == null)
                    {
                        continue;
                    }
                    if (Remove(item) && Give(robber, item, place))
                    {
                        return true;
                    }
                    return false;
                }
                return false;
            }

            private bool TrySwapPlate(Slot victimSlot, Item plate, float score, out bool swapped)
            {
                swapped = false;
                foreach (Player robber in _robbers)
                {
                    foreach (EquipmentSlot slotName in ArmorSlots)
                    {
                        if (!(robber.Equipment.GetSlot(slotName).ContainedItem is CompoundItem carrier))
                        {
                            continue;
                        }
                        Slot robberSlot = carrier.Slots.FirstOrDefault(s => s is ArmorSlot && !s.Locked && s.ID == victimSlot.ID && s.CheckCompatibility(plate));
                        if (robberSlot == null)
                        {
                            continue;
                        }
                        Item theirs = robberSlot.ContainedItem;
                        if (theirs == null)
                        {
                            return Remove(plate) && Give(robber, plate, robberSlot.CreateItemAddress());
                        }
                        if (PlateScore(theirs) >= score || !victimSlot.CheckCompatibility(theirs))
                        {
                            continue;
                        }
                        InventoryController robberController = robber.InventoryController;
                        var release = ItemManipulator.DiscardWithoutRestrictions(theirs, robberController);
                        if (release.Failed)
                        {
                            continue;
                        }
                        release.Value.RaiseEvents(robberController, CommandStatus.Begin);
                        release.Value.RaiseEvents(robberController, CommandStatus.Succeed);
                        if (!Remove(plate))
                        {
                            Give(robber, theirs, robberSlot.CreateItemAddress());
                            return false;
                        }
                        Give(robber, plate, robberSlot.CreateItemAddress());
                        var add = ItemManipulator.AddWithoutRestrictions(theirs, victimSlot.CreateItemAddress(), _victimController);
                        if (add.Succeeded)
                        {
                            add.Value.RaiseEvents(_victimController, CommandStatus.Begin);
                            add.Value.RaiseEvents(_victimController, CommandStatus.Succeed);
                        }
                        swapped = true;
                        return true;
                    }
                }
                return false;
            }

            private bool Remove(Item item)
            {
                try
                {
                    var discard = ItemManipulator.Discard(item, _victimController, true);
                    if (discard.Failed)
                    {
                        return false;
                    }
                    _victimController.TryRunNetworkTransaction(discard);
                    _taken.Add(item);
                    return true;
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Unconsciousness] Could not take {item.ShortName.Localized()}: {e.Message}");
                    return false;
                }
            }

            private static bool Give(Player robber, Item item, ItemAddress place)
            {
                if (item.CurrentAddress != null)
                {
                    return true;
                }
                try
                {
                    InventoryController controller = robber.InventoryController;
                    var add = ItemManipulator.AddWithoutRestrictions(item, place, controller);
                    if (add.Failed)
                    {
                        return true;
                    }
                    add.Value.RaiseEvents(controller, CommandStatus.Begin);
                    add.Value.RaiseEvents(controller, CommandStatus.Succeed);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Unconsciousness] {robber.Profile?.Nickname} could not carry {item.ShortName.Localized()}: {e.Message}");
                }
                return true;
            }

            private static ItemAddress WeaponPlace(Player robber, Item weapon)
            {
                foreach (EquipmentSlot slotName in new[] { EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster })
                {
                    Slot slot = robber.Equipment.GetSlot(slotName);
                    if (slot != null && slot.ContainedItem == null && slot.CheckCompatibility(weapon))
                    {
                        return slot.CreateItemAddress();
                    }
                }
                return null;
            }

            private static ItemAddress GridPlace(Player robber, Item item)
            {
                foreach (EquipmentSlot slotName in CarrySlots)
                {
                    if (!(robber.Equipment.GetSlot(slotName).ContainedItem is CompoundItem container) || container.Grids == null)
                    {
                        continue;
                    }
                    foreach (Grid grid in container.Grids)
                    {
                        LocationInGrid location = grid.FindFreeSpace(item);
                        if (location != null)
                        {
                            return grid.CreateItemAddress(location);
                        }
                    }
                }
                return null;
            }
        }

        private class KnockedOutShotPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.Shot));
            }

            [PatchPrefix]
            private static bool Prefix(Player.FirearmController __instance)
            {
                Knockout knockout = Knockout.Active;
                if (knockout == null || knockout.Player == null || knockout.Player.HandsController != __instance)
                {
                    return true;
                }
                __instance.SetTriggerPressed(false);
                return false;
            }
        }

        private class AttackerPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.ApplyDamageInfo));
            }

            [PatchPrefix]
            private static void Prefix(Player __instance, DamageInfo damageInfo)
            {
                if (!__instance.IsYourPlayer)
                {
                    return;
                }
                try
                {
                    IPlayer attacker = damageInfo.Player?.iPlayer;
                    if (attacker == null || attacker.ProfileId == __instance.ProfileId)
                    {
                        return;
                    }
                    Player found = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(attacker.ProfileId);
                    if (found != null)
                    {
                        _lastAttacker = found;
                        _lastAttackTime = Time.time;
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
