using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    /// <summary>
    /// Makes a side panel collapsible with a small tab on its inner edge: "‹" slides a left panel off
    /// the left of the screen ("›" brings it back), and mirrored for a right panel. The panel slides
    /// instead of being deactivated, so the owner's own SetActive logic (edit mode, say) still works;
    /// the tab is a child placed just outside the panel, so it stays on the screen edge while the
    /// panel is hidden.
    /// </summary>
    public sealed class CollapsiblePanel : MonoBehaviour
    {
        private const float TabWidth = 46f;
        private const float TabHeight = 90f;
        private const float Margin = 20f;

        private RectTransform _panel;
        private TMP_Text _label;
        private bool _left;
        private float _shift;

        public bool Collapsed { get; private set; }

        public Button Tab { get; private set; }

        /// <summary>Adds the collapse tab to a panel anchored to the left or right side of its canvas.</summary>
        public static CollapsiblePanel Attach(RectTransform panel, bool leftSide, TMP_FontAsset font)
        {
            var collapsible = panel.gameObject.AddComponent<CollapsiblePanel>();
            collapsible.Build(panel, leftSide, font);
            return collapsible;
        }

        public void SetCollapsed(bool collapsed)
        {
            if (collapsed == Collapsed)
            {
                return;
            }

            Collapsed = collapsed;
            var delta = new Vector2(collapsed ? _shift : -_shift, 0f);
            _panel.offsetMin += delta;
            _panel.offsetMax += delta;
            _label.text = Arrow();
        }

        private void Build(RectTransform panel, bool leftSide, TMP_FontAsset font)
        {
            _panel = panel;
            _left = leftSide;
            var width = panel.offsetMax.x - panel.offsetMin.x;
            _shift = (width + Margin) * (leftSide ? -1f : 1f);

            var tabObject = new GameObject("Collapse tab", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            tabObject.GetComponent<LayoutElement>().ignoreLayout = true; // panels with a layout group must not place it
            tabObject.transform.SetParent(panel, false);
            var rect = (RectTransform)tabObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(leftSide ? 1f : 0f, 1f);
            rect.pivot = new Vector2(leftSide ? 0f : 1f, 1f);
            rect.anchoredPosition = new Vector2(0f, -12f);
            rect.sizeDelta = new Vector2(TabWidth, TabHeight);
            var image = tabObject.GetComponent<Image>();
            image.color = new Color(0.05f, 0.07f, 0.09f, 0.96f);
            Tab = tabObject.GetComponent<Button>();
            Tab.targetGraphic = image;
            Tab.onClick.AddListener(() => SetCollapsed(!Collapsed));

            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(tabObject.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            _label = labelObject.AddComponent<TextMeshProUGUI>();
            _label.font = font != null ? font : TMP_Settings.defaultFontAsset;
            _label.fontSize = 36;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = Color.white;
            _label.raycastTarget = false;
            _label.text = Arrow();
        }

        // Points the way the panel will move: toward its edge to collapse, back to expand.
        private string Arrow() => (_left ^ Collapsed) ? "‹" : "›";
    }
}
