using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace mizmii.ReClassic.Patches
{
    internal static class BodySearch
    {
        private const string Section = "Gameplay - Body Search";
        private const string BodySound = "looting_body_extended";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _searchTime;
        private static readonly HashSet<string> Searched = new HashSet<string>();
        private static GameWorld _world;
        private static Search _active;

        private sealed class Search
        {
            public GamePlayerOwner Owner;
            public LootItem Body;
            public Action Open;
            public float Start;
            public float Duration;
            public Coroutine Watcher;
            public bool Planted;
            public BetterSource Sound;
            public bool WasStanding;
        }

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Interaction", "Timed Body Search", true,
                "The first time you search a body you crouch down and go through their rig and pockets. The cursor turns into a spinner with a progress bar under it, and the loot screen opens once it's full. Moving cancels it. Each body only has to be searched once.",
                Section, "Timed Body Search");
            _searchTime = config.Bind(Section, "Body Search Time", 3f,
                ConfigHelper.Advanced("Seconds it takes to search a body.", new AcceptableValueRange<float>(0.5f, 20f)));
            new SearchActionPatch().Enable();
            new KeepSearchingPatch().Enable();
        }

        public static float Progress => _active == null ? 0f : Mathf.Clamp01((Time.time - _active.Start) / Mathf.Max(0.01f, _active.Duration));

        public static bool Running => _active != null;

        private static bool IsBody(LootItem lootItem)
        {
            return lootItem is Corpse || lootItem?.ItemOwner?.RootItem is InventoryEquipment;
        }

        private static string BodyId(LootItem body)
        {
            return body?.ItemOwner?.RootItem?.Id ?? body?.ItemId;
        }

        private static bool AlreadySearched(LootItem body)
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world != _world)
            {
                _world = world;
                Searched.Clear();
            }
            string id = BodyId(body);
            return id != null && Searched.Contains(id);
        }

        private static void Begin(GamePlayerOwner owner, LootItem body, Action open)
        {
            Player player = owner?.Player;
            if (_active != null)
            {
                if (_active.Owner == owner)
                {
                    Cancel(_active);
                }
                return;
            }
            if (player == null || body == null || player.CurrentManagedState is PlantPlayerState)
            {
                return;
            }
            if (AlreadySearched(body))
            {
                open();
                return;
            }
            var search = new Search
            {
                Owner = owner,
                Body = body,
                Open = open,
                Start = Time.time,
                Duration = _searchTime.Value,
                WasStanding = !player.IsInPronePose && player.MovementContext != null && player.MovementContext.PoseLevel > 0.5f
            };
            _active = search;
            SearchCursor.Begin(owner);
            search.Sound = PlaySound(player);
            try
            {
                player.CurrentManagedState.Plant(true, player.IsInPronePose, search.Duration, success => Finish(search, success));
                search.Planted = player.CurrentManagedState is PlantPlayerState;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Body Search] Could not crouch to search: {e.Message}");
            }
            search.Watcher = owner.StartCoroutine(Watch(search));
        }

        private static void Cancel(Search search)
        {
            Player player = search.Owner != null ? search.Owner.Player : null;
            if (search.Planted && player != null && player.CurrentManagedState is PlantPlayerState)
            {
                player.CurrentManagedState.Cancel();
            }
            Finish(search, false);
        }

        private static BetterSource PlaySound(Player player)
        {
            try
            {
                AudioClip clip = Singleton<GUISounds>.Instance?.GetLootingClip(BodySound);
                BetterAudio audio = Singleton<BetterAudio>.Instance;
                if (clip == null || audio == null)
                {
                    return null;
                }
                return audio.PlayAtPoint(player.Position + Vector3.up * 0.5f, clip, 15f, BetterAudio.AudioSourceGroupType.Character, 15, 1f, EOcclusionTest.None, audio.ClientPlayerMovementMixer, true);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Body Search] Could not play the looting sound: {e.Message}");
                return null;
            }
        }

        private static IEnumerator Watch(Search search)
        {
            Player player = search.Owner.Player;
            Vector3 start = player.Position;
            while (_active == search)
            {
                yield return null;
                if (_active != search)
                {
                    yield break;
                }
                bool moved = Time.time - search.Start > 0.15f && player != null && player.InputDirection.sqrMagnitude > 0.01f;
                if (player == null || !player.HealthController.IsAlive || search.Body == null || moved || (player.Position - start).sqrMagnitude > 0.36f)
                {
                    search.Watcher = null;
                    Cancel(search);
                    yield break;
                }
                if (!search.Planted && Time.time >= search.Start + search.Duration)
                {
                    search.Watcher = null;
                    Finish(search, true);
                    yield break;
                }
            }
        }

        private static void Finish(Search search, bool success)
        {
            if (_active != search)
            {
                return;
            }
            _active = null;
            if (search.Watcher != null && search.Owner != null)
            {
                search.Owner.StopCoroutine(search.Watcher);
                search.Watcher = null;
            }
            SearchCursor.End();
            if (search.Sound != null && !success)
            {
                search.Sound.Stop(0.25f);
            }
            Player player = search.Owner != null ? search.Owner.Player : null;
            if (!success || player == null || !player.HealthController.IsAlive || search.Body == null || !search.Body.gameObject.activeInHierarchy)
            {
                return;
            }
            string id = BodyId(search.Body);
            if (id != null)
            {
                Searched.Add(id);
            }
            if (search.WasStanding)
            {
                search.Owner.StartCoroutine(StayCrouched(player));
            }
            try
            {
                search.Open();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Body Search] Could not open the body: {e}");
            }
        }

        private static IEnumerator StayCrouched(Player player)
        {
            yield return null;
            if (player != null && player.MovementContext != null && !player.IsInPronePose)
            {
                player.MovementContext.SetPoseLevel(0f, true);
            }
        }

        private class KeepSearchingPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(GamePlayerOwner), nameof(GamePlayerOwner.TranslateCommand));
            }

            [PatchPrefix]
            private static bool Prefix(GamePlayerOwner __instance, ECommand command, ref InputNode.ETranslateResult __result)
            {
                if (_active == null || _active.Owner != __instance || command != ECommand.EndInteracting)
                {
                    return true;
                }
                __result = InputNode.ETranslateResult.Block;
                return false;
            }
        }

        private class SearchActionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableActions),
                    new[] { typeof(GamePlayerOwner), typeof(LootItem) });
            }

            [PatchPostfix]
            private static void Postfix(AvailableInteractionState __result, GamePlayerOwner owner, LootItem lootItem)
            {
                if (!_enabled.Value || __result?.Actions == null || !IsBody(lootItem))
                {
                    return;
                }
                foreach (InteractionAction action in __result.Actions)
                {
                    if (action.Name != "Search" || action.Action == null)
                    {
                        continue;
                    }
                    Action open = action.Action;
                    action.Action = () => Begin(owner, lootItem, open);
                }
            }
        }
    }

    internal sealed class SearchCursor : MonoBehaviour
    {
        private static SearchCursor _instance;
        private static Sprite _spinner;
        private static bool _spinnerSearched;
        private static bool _logged;

        private ActionPanel _panel;
        private GamePlayerOwner _owner;
        private Sprite _savedSprite;
        private Vector2 _savedSize;
        private Image _fill;
        private bool _useFillAmount;
        private Vector2 _fillAnchorMax;
        private bool _running;

        public static void Begin(GamePlayerOwner owner)
        {
            try
            {
                ActionPanel panel = FindObjectsOfType<ActionPanel>().FirstOrDefault(p => p.isActiveAndEnabled) ?? FindObjectOfType<ActionPanel>();
                if (panel == null || panel._pointer == null || panel._progressBar == null)
                {
                    owner.ShowObjectivesPanel("Searching...", 1f);
                    return;
                }
                SearchCursor cursor = panel.GetComponent<SearchCursor>() ?? panel.gameObject.AddComponent<SearchCursor>();
                _instance = cursor;
                cursor.Start(panel, owner);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Body Search] Could not show the search cursor: {e.Message}");
            }
        }

        public static void End()
        {
            if (_instance != null)
            {
                _instance.Stop();
            }
        }

        private static Sprite Spinner()
        {
            if (_spinnerSearched)
            {
                return _spinner;
            }
            _spinnerSearched = true;
            Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            _spinner = sprites.FirstOrDefault(s => s != null && s.name == "spinner")
                ?? sprites.Where(s => s != null && s.name.IndexOf("spinner", StringComparison.OrdinalIgnoreCase) >= 0 && s.name != "spinner_big")
                    .OrderBy(s => s.rect.width).FirstOrDefault()
                ?? sprites.FirstOrDefault(s => s != null && s.name == "spinner_big");
            Plugin.LogSource.LogInfo(_spinner != null ? $"[Body Search] Cursor spinner: {_spinner.name}" : "[Body Search] No spinner sprite found.");
            return _spinner;
        }

        private void Start(ActionPanel panel, GamePlayerOwner owner)
        {
            if (_running)
            {
                Restore();
            }
            _panel = panel;
            _owner = owner;
            Image pointer = panel._pointer.Image;
            _savedSprite = pointer.sprite;
            _savedSize = pointer.rectTransform.sizeDelta;
            Sprite spinner = Spinner();
            if (spinner != null)
            {
                pointer.sprite = spinner;
                pointer.rectTransform.sizeDelta = new Vector2(16f, 16f);
                pointer.preserveAspect = true;
            }
            FindFill(panel._progressBar);
            _running = true;
            Apply(0f);
        }

        private void FindFill(Image bar)
        {
            Image[] images = bar.GetComponentsInChildren<Image>(true);
            if (!_logged)
            {
                _logged = true;
                Plugin.LogSource.LogInfo("[Body Search] Progress bar: " + string.Join(", ", images.Select(i => $"{i.name}({i.type}, {(i.sprite != null ? i.sprite.name : "no sprite")})")));
            }
            _fill = images.FirstOrDefault(i => i.type == Image.Type.Filled);
            _useFillAmount = _fill != null;
            if (_fill == null)
            {
                _fill = images.Where(i => i != bar).LastOrDefault() ?? bar;
                if (_fill.sprite != null)
                {
                    _fill.type = Image.Type.Filled;
                    _fill.fillMethod = Image.FillMethod.Horizontal;
                    _fill.fillOrigin = 0;
                    _useFillAmount = true;
                }
            }
            _fillAnchorMax = _fill.rectTransform.anchorMax;
        }

        private void Apply(float progress)
        {
            if (_panel == null)
            {
                return;
            }
            _panel._interactionButtonsContainer.gameObject.SetActive(false);
            _panel._itemPanel.SetActive(false);
            _panel._errorPanel.SetActive(false);
            _panel._progressBar.gameObject.SetActive(true);
            if (_useFillAmount)
            {
                _fill.fillAmount = progress;
            }
            else if (_fill != _panel._progressBar)
            {
                _fill.rectTransform.anchorMax = new Vector2(Mathf.Lerp(_fill.rectTransform.anchorMin.x, _fillAnchorMax.x, progress), _fillAnchorMax.y);
            }
            Image pointer = _panel._pointer.Image;
            pointer.color = Color.white;
            pointer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -360f * (Time.unscaledTime % 1f));
        }

        private void LateUpdate()
        {
            if (_running)
            {
                Apply(BodySearch.Progress);
            }
        }

        private void Stop()
        {
            if (_running)
            {
                Restore();
            }
        }

        private void Restore()
        {
            _running = false;
            if (_panel == null)
            {
                return;
            }
            Image pointer = _panel._pointer.Image;
            pointer.sprite = _savedSprite;
            pointer.rectTransform.sizeDelta = _savedSize;
            pointer.rectTransform.localRotation = Quaternion.identity;
            if (_fill != null)
            {
                if (_useFillAmount)
                {
                    _fill.fillAmount = 1f;
                }
                else
                {
                    _fill.rectTransform.anchorMax = _fillAnchorMax;
                }
            }
            _panel._progressBar.gameObject.SetActive(false);
            try
            {
                if (_owner != null)
                {
                    _panel.AvailableInteractionStateChangedHandler(_owner.AvailableInteractionState.Value);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Body Search] Could not restore the interaction menu: {e.Message}");
            }
        }
    }
}
