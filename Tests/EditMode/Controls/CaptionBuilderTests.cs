using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // Drives CaptionBuilder directly (no UI.Open): a hand-built host RT, the two layout modes.
    // Spec 2026-08-31-collapsible-design §6 (the零件 extracted from TabMenu) / §5.3.
    public class CaptionBuilderTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        private RectTransform MakeHost(float w, float h)
        {
            _root = new GameObject("host", typeof(RectTransform));
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        // Edges of a caption node in host space — pivot- and anchor-agnostic, so a test states
        // "where does this sit" without re-deriving the anchoring the builder chose.
        private static Bounds InHost(RectTransform host, RectTransform node)
            => RectTransformUtility.CalculateRelativeRectTransformBounds(host, node);

        private static Sprite MakeSprite()
        {
            var tex = new Texture2D(4, 4);
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        }

        // ── Inline mode (TabMenu): caret trails the text ────────────────────────────────────

        [Test]
        public void Inline_places_icon_label_and_arrow_left_to_right()
        {
            var host = MakeHost(300f, 44f);
            var caption = new CaptionBuilder(host, arrowAtRight: false,
                padX: 4f, gap: 6f, iconSize: 24f, arrowSize: 16f, fontSize: 24f);
            caption.SetIconSprite(MakeSprite());
            caption.SetArrowSprite(MakeSprite());
            caption.SetText("Guild");

            var icon = InHost(host, caption.Icon.rectTransform);
            var label = InHost(host, caption.Label.rectTransform);
            var arrow = InHost(host, caption.Arrow.rectTransform);

            Assert.AreEqual(4f, icon.min.x, 0.01f, "icon starts at padX");
            Assert.AreEqual(24f, icon.size.x, 0.01f);
            Assert.AreEqual(4f + 24f + 6f, label.min.x, 0.01f, "label follows icon + gap");
            Assert.AreEqual(label.max.x + 6f, arrow.min.x, 0.01f, "caret trails the text by one gap");
            Assert.AreEqual(16f, arrow.size.x, 0.01f);
        }

        [Test]
        public void Inline_collapses_the_icon_slot_when_there_is_no_icon()
        {
            var host = MakeHost(300f, 44f);
            var caption = new CaptionBuilder(host, arrowAtRight: false,
                padX: 4f, gap: 6f, iconSize: 24f, arrowSize: 16f, fontSize: 24f);
            caption.SetArrowSprite(MakeSprite());
            caption.SetText("Guild");

            Assert.IsFalse(caption.Icon.enabled);
            Assert.AreEqual(4f, InHost(host, caption.Label.rectTransform).min.x, 0.01f);
        }

        [Test]
        public void Inline_content_width_matches_the_placed_geometry()
        {
            var host = MakeHost(300f, 44f);
            var caption = new CaptionBuilder(host, arrowAtRight: false,
                padX: 4f, gap: 6f, iconSize: 24f, arrowSize: 16f, fontSize: 24f);
            caption.SetIconSprite(MakeSprite());
            caption.SetArrowSprite(MakeSprite());
            caption.SetText("Guild");

            var arrow = InHost(host, caption.Arrow.rectTransform);
            Assert.AreEqual(arrow.max.x + 4f, caption.ContentWidth(), 0.01f,
                "ContentWidth is the closed form of the same row, plus the trailing padX");
        }

        // ── Pinned mode (Collapsible): caret hugs the right edge ────────────────────────────

        [Test]
        public void Pinned_arrow_sits_padX_from_the_right_edge()
        {
            var host = MakeHost(150f, 24f);
            var caption = new CaptionBuilder(host, arrowAtRight: true,
                padX: 12f, gap: 8f, iconSize: 24f, arrowSize: 16f, fontSize: 12f);
            caption.SetArrowSprite(MakeSprite());
            caption.SetText("任务");

            var arrow = InHost(host, caption.Arrow.rectTransform);
            Assert.AreEqual(150f - 12f, arrow.max.x, 0.01f, "arrow's right edge is padX from the host's");
            Assert.AreEqual(16f, arrow.size.x, 0.01f);
        }

        [Test]
        public void Pinned_label_fills_the_width_left_of_the_arrow_zone()
        {
            var host = MakeHost(150f, 24f);
            var caption = new CaptionBuilder(host, arrowAtRight: true,
                padX: 12f, gap: 8f, iconSize: 24f, arrowSize: 16f, fontSize: 12f);
            caption.SetArrowSprite(MakeSprite());
            caption.SetText("任务");

            var label = InHost(host, caption.Label.rectTransform);
            Assert.AreEqual(12f, label.min.x, 0.01f, "label starts at padX");
            Assert.AreEqual(150f - caption.ArrowZoneWidth, label.max.x, 0.01f,
                "and stops where the arrow zone begins");
            Assert.AreEqual(12f + 16f + 8f, caption.ArrowZoneWidth, 0.01f,
                "arrow zone = padX + arrowSize + gap");
        }

        [Test]
        public void Pinned_label_takes_the_whole_width_when_the_arrow_is_hidden()
        {
            var host = MakeHost(150f, 24f);
            var caption = new CaptionBuilder(host, arrowAtRight: true,
                padX: 12f, gap: 8f, iconSize: 24f, arrowSize: 16f, fontSize: 12f);
            caption.SetArrowSprite(MakeSprite());
            caption.SetText("任务");
            caption.SetArrowSprite(null);

            Assert.IsFalse(caption.Arrow.enabled);
            Assert.AreEqual(0f, caption.ArrowZoneWidth, 0.01f);
            Assert.AreEqual(150f - 12f, InHost(host, caption.Label.rectTransform).max.x, 0.01f);
        }

        [Test]
        public void Pinned_layout_follows_the_host_growing()
        {
            var host = MakeHost(150f, 24f);
            var caption = new CaptionBuilder(host, arrowAtRight: true,
                padX: 12f, gap: 8f, iconSize: 24f, arrowSize: 16f, fontSize: 12f);
            caption.SetArrowSprite(MakeSprite());
            caption.SetText("任务");

            host.sizeDelta = new Vector2(400f, 24f);

            // No relayout call: pinned mode is anchor-driven precisely so a host resize needs none.
            Assert.AreEqual(400f - 12f, InHost(host, caption.Arrow.rectTransform).max.x, 0.01f);
            Assert.AreEqual(400f - caption.ArrowZoneWidth, InHost(host, caption.Label.rectTransform).max.x, 0.01f);
        }

        [Test]
        public void Pinned_icon_shifts_the_label_right()
        {
            var host = MakeHost(150f, 24f);
            var caption = new CaptionBuilder(host, arrowAtRight: true,
                padX: 12f, gap: 8f, iconSize: 24f, arrowSize: 16f, fontSize: 12f);
            caption.SetArrowSprite(MakeSprite());
            caption.SetIconSprite(MakeSprite());
            caption.SetText("任务");

            Assert.AreEqual(12f, InHost(host, caption.Icon.rectTransform).min.x, 0.01f);
            Assert.AreEqual(12f + 24f + 8f, InHost(host, caption.Label.rectTransform).min.x, 0.01f);
        }

        [Test]
        public void Metric_changes_relayout()
        {
            var host = MakeHost(150f, 24f);
            var caption = new CaptionBuilder(host, arrowAtRight: true,
                padX: 12f, gap: 8f, iconSize: 24f, arrowSize: 16f, fontSize: 12f);
            caption.SetArrowSprite(MakeSprite());
            caption.SetText("任务");

            caption.ArrowSize = 24f;

            var arrow = InHost(host, caption.Arrow.rectTransform);
            Assert.AreEqual(24f, arrow.size.x, 0.01f);
            Assert.AreEqual(150f - 12f, arrow.max.x, 0.01f);
        }
    }

    // The activate-for-measurement helper lifted out of TabMenu (spec §6). A TMP added under an
    // inactive parent never runs Awake and reports 0 forever; measuring has to switch the subtree on.
    public class InactiveMeasureTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
        }

        [Test]
        public void Activates_an_inactive_object_and_restores_it()
        {
            _go = new GameObject("subject");
            _go.SetActive(false);

            var activated = InactiveMeasure.ActivateIfNeeded(_go);

            Assert.IsTrue(activated);
            Assert.IsTrue(_go.activeSelf, "switched on for the measurement");

            InactiveMeasure.Restore(_go, activated);
            Assert.IsFalse(_go.activeSelf, "and back off afterwards");
        }

        [Test]
        public void Leaves_an_already_active_object_alone()
        {
            _go = new GameObject("subject");

            var activated = InactiveMeasure.ActivateIfNeeded(_go);

            Assert.IsFalse(activated);
            InactiveMeasure.Restore(_go, activated);
            Assert.IsTrue(_go.activeSelf, "an already-active object is never switched off");
        }

        [Test]
        public void Tolerates_a_null_object()
        {
            Assert.IsFalse(InactiveMeasure.ActivateIfNeeded(null));
            Assert.DoesNotThrow(() => InactiveMeasure.Restore(null, true));
        }
    }
}
