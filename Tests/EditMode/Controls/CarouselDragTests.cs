using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // 拖动行为的可靠 EditMode 验证：拖动定位是纯几何（屏幕指针 → viewport 本地坐标映射 +
    // anchoredPosition），EditMode 下 GoTo(animated:true) 走 instant 分支，所以翻页结果当帧落定，
    // 无需 PlayMode / 计时。指针通过 e.position 驱动（与 ScrollRect 一致；拖动读 position 而非 delta）。
    public class CarouselDragTests
    {
        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown]
        public void TearDown() { StateTintReactor.TestForceInstant = false; UI.ResetForTests(); }

        private static Carousel Open(string attrs)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='200x100' {attrs}><Image/><Image/><Image/></Carousel>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Carousel>("car");
        }

        private static Carousel OpenCards(string attrs, string cards)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='200x100' {attrs}>{cards}</Carousel>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Carousel>("car");
        }

        private static RectTransform Viewport(CarouselView v) => (RectTransform)v.StripRect.parent;

        // viewport 本地坐标点 → 屏幕点（overlay: world==screen px；含 lossyScale）。
        private static Vector2 ScreenAt(CarouselView v, Vector2 local)
            => RectTransformUtility.WorldToScreenPoint(null, Viewport(v).TransformPoint(local));

        private static PointerEventData Ev(Vector2 pos, Vector2 delta)
            => new(EventSystem.current) { position = pos, delta = delta };

        // 在 viewport 本地坐标系里拖 (dx,dy)：begin@0 → drag@(dx,dy) → end。翻页当帧落定。
        private static void DragLocal(CarouselView view, float dx, float dy = 0f)
        {
            var p0 = ScreenAt(view, Vector2.zero);
            var p1 = ScreenAt(view, new Vector2(dx, dy));
            ((IBeginDragHandler)view).OnBeginDrag(Ev(p0, Vector2.zero));
            ((IDragHandler)view).OnDrag(Ev(p1, p1 - p0));
            ((IEndDragHandler)view).OnEndDrag(Ev(p1, p1 - p0));
        }

        [Test]
        public void Drag_Tracks_Pointer_One_To_One_In_Local_Space()
        {
            // 像素级跟手：手指（屏幕）移动多少，被抓住的卡片就在本地坐标里跟随多少，
            // 不随 CanvasScaler.scaleFactor 变快/变慢。这里把 canvas 放大 2x（= scaleFactor 2）
            // 让屏幕像素 != 本地单位，旧实现直接把屏幕 delta 当本地位移会快一倍 → 卡红。
            var car = Open("interval='0'");
            var view = car.GameObject.GetComponent<CarouselView>();
            var canvas = car.GameObject.GetComponentInParent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;       // 阻止 scaler 把我们设的 scale 重置回去
            canvas.transform.localScale = new Vector3(2f, 2f, 1f);

            var viewport = Viewport(view);
            Assert.AreEqual(2f, viewport.lossyScale.x, 0.001f,
                "precondition: canvas scaled 2x so screen px != local units");

            var p0 = new Vector2(400f, 300f);
            var p1 = new Vector2(320f, 300f);   // 屏幕左移 80px → 本地应是 40
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, p0, null, out var l0);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, p1, null, out var l1);
            float expectedLocalDx = l1.x - l0.x;   // ≈ -40 (= -80 / 2)

            ((IBeginDragHandler)view).OnBeginDrag(Ev(p0, p1 - p0));
            ((IDragHandler)view).OnDrag(Ev(p1, p1 - p0));

            var card0 = (RectTransform)view.StripRect.GetChild(0);
            Assert.AreEqual(expectedLocalDx, card0.anchoredPosition.x, 0.5f,
                "grabbed card tracks the finger 1:1 in local space (not scaled by CanvasScaler.scaleFactor)");
        }

        [Test]
        public void Drag_Left_Past_Threshold_Advances()
        {
            var car = Open("interval='0'");           // 200px 视口；阈值 0.2*200 = 40
            DragLocal(car.GameObject.GetComponent<CarouselView>(), -120f);
            Assert.AreEqual(1, car.Current, "drag left past threshold advances one page");
        }

        [Test]
        public void Small_Drag_Snaps_Back()
        {
            var car = Open("interval='0'");
            DragLocal(car.GameObject.GetComponent<CarouselView>(), -10f);   // < 阈值
            Assert.AreEqual(0, car.Current, "small drag returns to current");
        }

        [Test]
        public void Drag_Cannot_Exceed_One_Page()
        {
            var car = Open("interval='0'");
            var view = car.GameObject.GetComponent<CarouselView>();
            var p0 = ScreenAt(view, Vector2.zero);
            var pFar = ScreenAt(view, new Vector2(-10000f, 0f));   // 远超一页
            ((IBeginDragHandler)view).OnBeginDrag(Ev(p0, Vector2.zero));
            ((IDragHandler)view).OnDrag(Ev(pFar, pFar - p0));
            // 拖动时可见滚动被钳进 ±1 页：不能滑到很远的页再回弹到相邻页。
            Assert.LessOrEqual(Mathf.Abs(view.ScrollForTests), 1.0001f,
                "drag is clamped to ±1 page");
        }

        [Test]
        public void Diagonal_Drag_Still_Scrolls_Horizontally()
        {
            // 先竖后横的手势（旧的首帧主轴锁会把整段拖动锁死）：只看 X 分量，仍应翻页。
            var car = Open("interval='0'");
            var view = car.GameObject.GetComponent<CarouselView>();
            var p0 = ScreenAt(view, Vector2.zero);
            var pUp = ScreenAt(view, new Vector2(0f, 80f));        // 首帧纯竖直
            var pLeft = ScreenAt(view, new Vector2(-120f, 80f));   // 再往左，过阈值
            ((IBeginDragHandler)view).OnBeginDrag(Ev(p0, Vector2.zero));
            ((IDragHandler)view).OnDrag(Ev(pUp, pUp - p0));
            ((IDragHandler)view).OnDrag(Ev(pLeft, pLeft - pUp));
            ((IEndDragHandler)view).OnEndDrag(Ev(pLeft, Vector2.zero));
            Assert.AreEqual(1, car.Current,
                "a drag starting vertical then going horizontal still advances (no first-frame axis lock-out)");
        }

        [Test]
        public void Peek_Drag_Threshold_Scales_With_Card_Stride()
        {
            // 视口 200，卡宽 100，spacing 0 → 步距 100、阈值 0.2*100 = 20。
            // 拖 -30：> 卡步距阈值（翻页），但 < 旧视口阈值 0.2*200=40（旧逻辑会回弹）。
            var car = OpenCards("fill='false' spacing='0' interval='0'",
                "<Frame size='100x80'/><Frame size='100x80'/><Frame size='100x80'/>");
            DragLocal(car.GameObject.GetComponent<CarouselView>(), -30f);
            Assert.AreEqual(1, car.Current, "drag threshold uses card stride (100), not viewport width (200)");
        }

        [Test]
        public void Vertical_Drag_Forwarded_To_Parent()
        {
            var car = Open("interval='0'");
            var parentGo = car.GameObject.transform.parent.gameObject;
            var recorder = parentGo.AddComponent<DragRecorder>();
            var view = car.GameObject.GetComponent<CarouselView>();

            DragLocal(view, 0f, 80f);   // 纯竖直 — 不属于轮播（X 位移 0），但整段转发给父级

            Assert.AreEqual(0, car.Current, "vertical drag: carousel must not advance");
            Assert.GreaterOrEqual(recorder.Begins, 1, "parent should receive OnBeginDrag");
            Assert.GreaterOrEqual(recorder.Drags, 1, "parent should receive OnDrag");
            Assert.GreaterOrEqual(recorder.Ends, 1, "parent should receive OnEndDrag");
        }
    }

    // 录制器：挂在父 GameObject 上，统计收到的拖动转发次数。
    internal class DragRecorder : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int Begins;
        public int Drags;
        public int Ends;

        public void OnBeginDrag(PointerEventData e) => Begins++;
        public void OnDrag(PointerEventData e) => Drags++;
        public void OnEndDrag(PointerEventData e) => Ends++;
    }
}
