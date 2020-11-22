using ExCSS;
using Facebook.Yoga;
using Jint.Native;
using Jint.Native.Function;
using ReactUnity.EventHandlers;
using ReactUnity.Interop;
using ReactUnity.Layout;
using ReactUnity.StateHandlers;
using ReactUnity.StyleEngine;
using ReactUnity.Styling;
using ReactUnity.Styling.Types;
using ReactUnity.Types;
using ReactUnity.Visitors;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReactUnity.Components
{
    public class UnityComponent
    {
        private static HashSet<string> EmptyClassList = new HashSet<string>();

        public UnityUGUIContext Context { get; }
        public static NodeStyle TagDefaultStyle { get; } = new NodeStyle();
        public static YogaNode TagDefaultLayout { get; } = new YogaNode();
        public virtual NodeStyle DefaultStyle => TagDefaultStyle;
        public virtual YogaNode DefaultLayout => TagDefaultLayout;

        public GameObject GameObject { get; private set; }
        public RectTransform RectTransform { get; private set; }
        public ContainerComponent Parent { get; private set; }


        public FlexElement Flex { get; private set; }
        public YogaNode Layout { get; private set; }
        public NodeStyle Style { get; private set; }
        public StateStyles StateStyles { get; private set; }
        public ExpandoObject Inline { get; private set; } = new ExpandoObject();

        public BorderAndBackground BorderAndBackground { get; protected set; }
        public MaskAndImage MaskAndImage { get; protected set; }

        public Selectable Selectable { get; protected set; }
        public CanvasGroup CanvasGroup => GameObject.GetComponent<CanvasGroup>();
        public Canvas Canvas => GameObject.GetComponent<Canvas>();

        public bool IsPseudoElement = false;
        public string Tag { get; set; } = "";
        public string ClassName { get; set; } = "";
        public HashSet<string> ClassList { get; private set; }

        public string TextContent => new TextContentVisitor().Get(this);

        protected UnityComponent(RectTransform existing, UnityUGUIContext context)
        {
            Context = context;
            GameObject = existing.gameObject;
            RectTransform = existing;

            StateStyles = new StateStyles(this);
            Style = new NodeStyle(DefaultStyle, StateStyles);
            Layout = new YogaNode(DefaultLayout);
        }

        public UnityComponent(UnityUGUIContext context, string tag)
        {
            Tag = tag;
            Context = context;
            GameObject = new GameObject();
            RectTransform = GameObject.AddComponent<RectTransform>();

            RectTransform.anchorMin = Vector2.up;
            RectTransform.anchorMax = Vector2.up;
            RectTransform.pivot = Vector2.up;


            StateStyles = new StateStyles(this);
            Style = new NodeStyle(DefaultStyle, StateStyles);
            Layout = new YogaNode(DefaultLayout);

            Flex = GameObject.AddComponent<FlexElement>();
            Flex.Layout = Layout;
            Flex.Style = Style;
            Flex.Component = this;
        }

        public virtual void Destroy()
        {
            GameObject.DestroyImmediate(GameObject);
            Parent.Children.Remove(this);
            Parent.Layout.RemoveChild(Layout);
            Parent.ScheduleLayout();
        }

        public virtual void SetParent(ContainerComponent parent, UnityComponent insertBefore = null, bool insertAfter = false)
        {
            Parent = parent;
            RectTransform.SetParent(parent.Container, false);

            if (insertBefore == null)
            {
                parent.Children.Add(this);
                parent.Layout.AddChild(Layout);
            }
            else
            {
                var ind = parent.Children.IndexOf(insertBefore);
                if (insertAfter) ind++;

                parent.Children.Insert(ind, this);
                parent.Layout.Insert(ind, Layout);
                RectTransform.SetSiblingIndex(ind);
            }

            Style.Parent = parent.Style;
            ResolveStyle(true);

            Parent.ScheduleLayout();
        }


        public virtual void SetEventListener(string eventName, Callback fun)
        {
            var eventType = EventHandlerMap.GetEventType(eventName);
            if (eventType == null) throw new System.Exception($"Unknown event name specified, '{eventName}'");

            // Remove
            var handler = GameObject.GetComponent(eventType) as IEventHandler;
            handler?.ClearListeners();

            // No event to add
            if (fun == null) return;

            if (handler == null) handler = GameObject.AddComponent(eventType) as IEventHandler;

            Action<BaseEventData> callAction = (e) => fun.Call(e);
            handler.OnEvent += callAction;
        }

        public virtual void SetProperty(string propertyName, object value)
        {
            switch (propertyName)
            {
                case "name":
                    GameObject.name = value?.ToString();
                    return;
                case "className":
                    ClassName = value?.ToString();
                    ClassList = string.IsNullOrWhiteSpace(ClassName) ? EmptyClassList :
                        new HashSet<string>(ClassName.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
                    return;
                default:
                    throw new System.Exception($"Unknown property name specified, '{propertyName}'");
            }
        }

        public void ScheduleLayout(System.Action callback = null)
        {
            Context.scheduleLayout(callback);
        }

        public virtual void ResolveStyle(bool recursive = false)
        {
            var inlineStyles = RuleHelpers.GetRuleDic(Inline);
            var inlineLayouts = RuleHelpers.GetLayoutDic(Inline) ?? new List<LayoutValue>();

            var matchingRules = Context.StyleTree.GetMatchingRules(this, IsPseudoElement).ToList();
            Style.CssStyles = matchingRules.SelectMany(x => x.Data?.Rules).Append(inlineStyles).ToList();


            if (Style.CssLayouts != null)
                foreach (var item in Style.CssLayouts) item.SetDefault(Layout);
            Style.CssLayouts = matchingRules.Where(x => x.Data?.Layouts != null).SelectMany(x => x.Data?.Layouts).Concat(inlineLayouts).ToList();
            foreach (var item in Style.CssLayouts) item.Set(Layout);

            ApplyStyles();
            Style.MarkChangesSeen();
        }

        public virtual void ApplyLayoutStyles()
        {
            ResolveOpacityAndInteractable();
            SetOverflow();
            UpdateBackgroundGraphic(true, false);
        }

        public virtual void ApplyStyles()
        {
            ResolveTransform();
            ResolveOpacityAndInteractable();
            SetZOrder();
            SetOverflow();
            SetCursor();
            UpdateBackgroundGraphic(false, true);
        }

        protected void ResolveTransform()
        {
            // Reset rotation and scale before setting pivot
            RectTransform.localScale = Vector3.one;
            RectTransform.localRotation = Quaternion.identity;


            var pivot = Style.pivot;
            Vector3 deltaPosition = RectTransform.pivot - pivot;    // get change in pivot
            deltaPosition.Scale(RectTransform.rect.size);           // apply sizing
            deltaPosition.Scale(RectTransform.localScale);          // apply scaling
            deltaPosition = RectTransform.rotation * deltaPosition; // apply rotation

            RectTransform.pivot = pivot;                            // change the pivot
            RectTransform.localPosition -= deltaPosition;           // reverse the position change


            // Restore rotation and scale
            var scale = Style.scale;
            RectTransform.localScale = new Vector3(scale.x, scale.y, 1);
            RectTransform.localRotation = Quaternion.Euler(0, 0, Style.rotate);
        }

        protected void ResolveOpacityAndInteractable()
        {
            var opacity = Style.opacity;
            var hidden = Style.hidden;
            var none = Layout.Display == YogaDisplay.None;
            var interaction = Style.interaction;

            if (hidden || none) opacity = 0;
            if (none) interaction = InteractionType.Ignore;

            var isTransparent = opacity < 1;
            var isInvisible = opacity == 0;

            var hasInteraction = interaction == InteractionType.Always || (!isInvisible && interaction == InteractionType.WhenVisible);


            var group = CanvasGroup;
            // Group does not exist and there is no need for it, quit early
            if (!group && !isTransparent && hasInteraction) return;
            if (!group) group = GameObject.AddComponent<CanvasGroup>();

            group.alpha = opacity;
            group.interactable = hasInteraction;

            if (interaction == InteractionType.Ignore) group.blocksRaycasts = false;
            else if (isInvisible && interaction == InteractionType.WhenVisible) group.blocksRaycasts = false;
            else group.blocksRaycasts = true;
        }

        private void SetOverflow()
        {
            var mask = MaskAndImage;

            // Mask is not defined and there is no need for it
            if (Layout.Overflow == YogaOverflow.Visible && mask == null) return;

            if (mask == null) mask = MaskAndImage = new MaskAndImage(RectTransform);

            mask.SetEnabled(Layout.Overflow != YogaOverflow.Visible);
            mask.SetBorderRadius(Style.borderRadius);
        }

        private void SetCursor()
        {
            if (string.IsNullOrWhiteSpace(Style.cursor)) return;
            var handler = GameObject.GetComponent<CursorHandler>() ?? GameObject.AddComponent<CursorHandler>();
            handler.Cursor = Style.cursor;
        }

        protected bool HasBorderOrBackground()
        {
            if (BorderAndBackground != null) return true;

            var borderAny = Layout.BorderWidth > 0 || Layout.BorderLeftWidth > 0 || Layout.BorderRightWidth > 0
                || Layout.BorderTopWidth > 0 || Layout.BorderBottomWidth > 0
                || Layout.BorderStartWidth > 0 || Layout.BorderEndWidth > 0;
            if (borderAny) return true;

            if (Style.borderRadius > 0 && Style.borderColor.a > 0) return true;
            if (Style.backgroundColor.a > 0) return true;
            if (Style.backgroundImage != null) return true;
            if (Style.boxShadow != null) return true;

            return false;
        }

        public virtual BorderAndBackground UpdateBackgroundGraphic(bool updateLayout, bool updateStyle)
        {
            if (!HasBorderOrBackground()) return null;

            BorderAndBackground image = BorderAndBackground;

            if (image == null)
            {
                updateStyle = true;
                updateLayout = true;
                image = new BorderAndBackground(RectTransform);

                if (Selectable) Selectable.targetGraphic = image.Background.GetComponent<Image>();
                BorderAndBackground = image;
            }

            if (updateLayout)
            {
                image.SetBorderSize(Layout);
            }
            if (updateStyle)
            {
                var sprite = AssetReference<object>.GetSpriteFromObject(Style.backgroundImage, Context);
                image.SetBackgroundColorAndImage(Style.backgroundColor, sprite);
                image.SetBoxShadow(Style.boxShadow);

                MainThreadDispatcher.OnUpdate(() =>
                {
                    if (!GameObject) return;
                    var borderSprite = BorderGraphic.CreateBorderSprite(Style.borderRadius);
                    image.SetBorderImage(borderSprite);
                });

                image.SetBorderColor(Style.borderColor);
            }

            return image;
        }

        private void SetZOrder()
        {
            var z = Style.zOrder;
            Canvas canvas = Canvas;
            if (!canvas && z == 0) return;
            if (!canvas)
            {
                canvas = GameObject.AddComponent<Canvas>();
                GameObject.AddComponent<GraphicRaycaster>();
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = z;
        }

        public UnityComponent QuerySelector(string query)
        {
            var tree = new RuleTree<string>(Context.Parser);
            tree.AddSelector(query);
            return tree.GetMatchingChild(this);
        }

        public List<UnityComponent> QuerySelectorAll(string query)
        {
            var tree = new RuleTree<string>(Context.Parser);
            tree.AddSelector(query);
            return tree.GetMatchingChildren(this);
        }

        public virtual void Accept(UnityComponentVisitor visitor)
        {
            visitor.Visit(this);
        }
    }
}
