using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using mizmii.ReClassic.Auctions;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace mizmii.ReClassic.Repairs
{
    internal sealed class RepairList : MonoBehaviour
    {
        private const float HeaderHeight = 24f;
        private const float RowHeight = 30f;
        private const float Gap = 6f;
        private const int VisibleRows = 4;

        private static RepairList _current;

        private RectTransform _person;
        private Vector2 _originalOffsetMin;
        private RectTransform _panel;
        private RectTransform _content;
        private TMP_FontAsset _font;
        private Material _fontMaterial;
        private Trader _trader;
        private float _height;
        private float _nextTick;
        private float _nextRefresh;
        private readonly List<Row> _rows = new List<Row>();

        private sealed class Row
        {
            public RepairJobView Job;
            public RectTransform Rect;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Detail;
            public TextMeshProUGUI Time;
            public RectTransform Fill;
        }

        public static void Enable()
        {
            new DealScreenShowPatch().Enable();
            new DealScreenClosePatch().Enable();
            RepairClient.Updated += () =>
            {
                if (_current != null)
                {
                    _current.Rebuild();
                }
            };
        }

        private static RectTransform FindPerson(TraderDealScreen screen)
        {
            Transform node = screen._traderScroll != null ? screen._traderScroll.transform : null;
            while (node != null && node != screen.transform)
            {
                if (node.name == "Left Person")
                {
                    return node as RectTransform;
                }
                node = node.parent;
            }
            return null;
        }

        private static void Attach(TraderDealScreen screen, Trader trader)
        {
            RectTransform person = FindPerson(screen);
            if (person == null)
            {
                return;
            }
            RepairList list = person.GetComponent<RepairList>();
            if (list == null)
            {
                list = person.gameObject.AddComponent<RepairList>();
                list.Build(person);
            }
            list._trader = trader;
            _current = list;
            list.Rebuild();
            RepairClient.Refresh();
        }

        private void Build(RectTransform person)
        {
            _person = person;
            _originalOffsetMin = person.offsetMin;
            TextMeshProUGUI sample = person.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t.font != null);
            _font = sample != null ? sample.font : AuctionUi.Font;
            _fontMaterial = sample != null ? sample.fontSharedMaterial : AuctionUi.FontMaterial;

            _panel = AuctionUi.Rect("ReClassic Repairs", person);
            _panel.anchorMin = new Vector2(0f, 0f);
            _panel.anchorMax = new Vector2(1f, 0f);
            _panel.pivot = new Vector2(0.5f, 1f);
            Image back = _panel.gameObject.AddComponent<Image>();
            back.color = new Color32(14, 15, 15, 230);
            back.raycastTarget = true;
            Outline outline = _panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color32(70, 70, 66, 200);
            outline.effectDistance = new Vector2(1f, -1f);

            RectTransform header = AuctionUi.Rect("Header", _panel);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta = new Vector2(0f, HeaderHeight);
            Image headerBack = header.gameObject.AddComponent<Image>();
            headerBack.color = new Color32(42, 43, 41, 255);
            headerBack.raycastTarget = false;
            TextMeshProUGUI title = Label("Title", header, "IN REPAIR", 14f, AuctionUi.NativeText, TextAlignmentOptions.MidlineLeft);
            AuctionUi.Stretch(title.rectTransform, 10f, 0f, 10f, 0f);

            RectTransform viewport = AuctionUi.Rect("Viewport", _panel);
            AuctionUi.Stretch(viewport, 0f, HeaderHeight, 0f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            Image viewportHit = viewport.gameObject.AddComponent<Image>();
            viewportHit.color = new Color(0f, 0f, 0f, 0f);

            _content = AuctionUi.Rect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;

            ScrollRect scroll = _panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            _panel.gameObject.SetActive(false);
        }

        private TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI label = AuctionUi.Text(name, parent, text, size, color, alignment);
            if (_font != null)
            {
                label.font = _font;
                if (_fontMaterial != null)
                {
                    label.fontSharedMaterial = _fontMaterial;
                }
            }
            label.enableWordWrapping = false;
            return label;
        }

        private void Rebuild()
        {
            if (_person == null)
            {
                return;
            }
            List<RepairJobView> jobs = RepairTime.Active && _trader != null ? RepairClient.JobsFor(_trader.Id).ToList() : new List<RepairJobView>();
            foreach (Row row in _rows)
            {
                if (row.Rect != null)
                {
                    Destroy(row.Rect.gameObject);
                }
            }
            _rows.Clear();
            if (jobs.Count == 0)
            {
                Collapse();
                return;
            }
            for (int i = 0; i < jobs.Count; i++)
            {
                _rows.Add(MakeRow(jobs[i], i));
            }
            _content.sizeDelta = new Vector2(0f, jobs.Count * RowHeight);
            _height = HeaderHeight + Mathf.Min(jobs.Count, VisibleRows) * RowHeight;
            _person.offsetMin = new Vector2(_originalOffsetMin.x, _originalOffsetMin.y + _height + Gap);
            _panel.anchoredPosition = new Vector2(0f, -Gap);
            _panel.sizeDelta = new Vector2(0f, _height);
            _panel.gameObject.SetActive(true);
            Tick();
        }

        private Row MakeRow(RepairJobView job, int index)
        {
            var row = new Row { Job = job };
            row.Rect = AuctionUi.Rect("Repair " + index, _content);
            row.Rect.anchorMin = new Vector2(0f, 1f);
            row.Rect.anchorMax = new Vector2(1f, 1f);
            row.Rect.pivot = new Vector2(0.5f, 1f);
            row.Rect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            row.Rect.sizeDelta = new Vector2(0f, RowHeight);
            Image back = row.Rect.gameObject.AddComponent<Image>();
            back.color = index % 2 == 0 ? AuctionUi.NativePanel : new Color32(80, 80, 80, 35);
            back.raycastTarget = false;

            row.Name = Label("Name", row.Rect, RepairClient.ItemName(job.Template), 13f, AuctionUi.NativeText, TextAlignmentOptions.MidlineLeft);
            row.Name.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(row.Name.rectTransform, 0f, 0.5f, 10f, -4f);

            string detail = job.MaxDurability > 0 ? $"{job.Durability:0.#}/{job.MaxDurability:0.#}" : "";
            row.Detail = Label("Detail", row.Rect, detail, 12f, AuctionUi.NativeCaption, TextAlignmentOptions.MidlineRight);
            Anchor(row.Detail.rectTransform, 0.5f, 0.64f, 0f, -6f);

            RectTransform bar = AuctionUi.Rect("Bar", row.Rect);
            bar.anchorMin = new Vector2(0.66f, 0.5f);
            bar.anchorMax = new Vector2(0.84f, 0.5f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, 6f);
            Image barBack = bar.gameObject.AddComponent<Image>();
            barBack.color = new Color32(30, 31, 30, 255);
            barBack.raycastTarget = false;
            row.Fill = AuctionUi.Rect("Fill", bar);
            row.Fill.anchorMin = Vector2.zero;
            row.Fill.anchorMax = new Vector2(0f, 1f);
            row.Fill.pivot = new Vector2(0f, 0.5f);
            row.Fill.offsetMin = Vector2.zero;
            row.Fill.offsetMax = Vector2.zero;
            Image fill = row.Fill.gameObject.AddComponent<Image>();
            fill.color = new Color32(197, 195, 178, 220);
            fill.raycastTarget = false;

            row.Time = Label("Time", row.Rect, "", 13f, AuctionUi.NativeText, TextAlignmentOptions.MidlineRight);
            Anchor(row.Time.rectTransform, 0.85f, 1f, 0f, -10f);
            return row;
        }

        private static void Anchor(RectTransform rect, float from, float to, float left, float right)
        {
            rect.anchorMin = new Vector2(from, 0f);
            rect.anchorMax = new Vector2(to, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(right, 0f);
        }

        private void Collapse()
        {
            if (_panel != null)
            {
                _panel.gameObject.SetActive(false);
            }
            if (_person != null)
            {
                _person.offsetMin = _originalOffsetMin;
            }
        }

        private void Tick()
        {
            bool finished = false;
            foreach (Row row in _rows)
            {
                long left = row.Job.EndTime - RepairClient.Now;
                row.Time.text = left > 0 ? RepairClient.Duration(left) : "READY";
                row.Fill.anchorMax = new Vector2(RepairClient.Progress(row.Job), 1f);
                finished |= left <= 0;
            }
            if (finished && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 10f;
                RepairClient.Refresh();
            }
        }

        private void Update()
        {
            if (_rows.Count == 0 || Time.unscaledTime < _nextTick)
            {
                return;
            }
            _nextTick = Time.unscaledTime + 1f;
            Tick();
        }

        private void OnDisable()
        {
            Collapse();
            if (_current == this)
            {
                _current = null;
            }
        }

        private class DealScreenShowPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TraderDealScreen), nameof(TraderDealScreen.Show),
                    new[] { typeof(Trader), typeof(Profile), typeof(InventoryController), typeof(ETradeMode), typeof(ItemUiContext), typeof(QuestController), typeof(IEnumerable<Trader>) });
            }

            [PatchPostfix]
            private static void Postfix(TraderDealScreen __instance, Trader trader)
            {
                try
                {
                    Attach(__instance, trader);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Repair Time] Could not show pending repairs: {e}");
                }
            }
        }

        private class DealScreenClosePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(TraderDealScreen), nameof(TraderDealScreen.Close));
            }

            [PatchPrefix]
            private static void Prefix()
            {
                if (_current != null)
                {
                    _current.Collapse();
                    _current = null;
                }
            }
        }
    }
}
