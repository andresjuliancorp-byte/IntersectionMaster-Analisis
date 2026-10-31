using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace IntersectionMaster.UI
{
    /// <summary>Utilidades para construir interfaz uGUI por código (sin prefabs ni escenas editadas a mano).</summary>
    public static class UIKit
    {
        public static readonly Color Bg = new Color32(24, 28, 36, 255);
        public static readonly Color PanelCol = new Color32(34, 40, 52, 240);
        public static readonly Color Accent = new Color32(61, 139, 253, 255);
        public static readonly Color Good = new Color32(46, 160, 90, 255);
        public static readonly Color Bad = new Color32(200, 60, 55, 255);
        public static readonly Color Neutral = new Color32(70, 80, 98, 255);
        public static readonly Color Dim = new Color32(52, 58, 72, 255);
        public static readonly Color Orange = new Color32(232, 119, 34, 255);
        public static readonly Color Dark = new Color(0.1f, 0.12f, 0.16f, 0.88f);

        static Font font;
        static Sprite white;

        public static Font DefaultFont
        {
            get
            {
                if (font == null)
                {
                    try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
                    if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (Exception) { } }
                    if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);
                }
                return font;
            }
        }

        static Sprite White
        {
            get
            {
                if (white == null)
                {
                    var t = new Texture2D(2, 2);
                    t.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                    t.Apply();
                    white = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
                }
                return white;
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax;
        }

        public static Image Panel(Transform parent, string name, Color color, bool raycast = true)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = White;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null)
        {
            var rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color ?? Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = false;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string text, Color color, Action onClick, int fontSize = 30)
        {
            var img = Panel(parent, "Btn_" + text, color);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            btn.colors = colors;
            var lab = Label(img.transform, text, fontSize);
            Stretch(lab.rectTransform, 6, 2, 6, 2);
            lab.resizeTextForBestFit = true;
            lab.resizeTextMinSize = 12;
            lab.resizeTextMaxSize = fontSize;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static void SetText(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static void SetColor(Button b, Color c)
        {
            if (b.targetGraphic != null) b.targetGraphic.color = c;
        }

        public static InputField Input(Transform parent, string initial, int maxChars, Action<string> onEnd)
        {
            var img = Panel(parent, "Input", new Color32(18, 22, 30, 255));
            var field = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, initial, 28, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 12, 4, 12, 4);
            text.raycastTarget = false;
            field.textComponent = text;
            field.text = initial;
            field.characterLimit = maxChars;
            field.targetGraphic = img;
            field.onEndEdit.AddListener(s => { if (onEnd != null) onEnd(s); });
            return field;
        }

        public static VerticalLayoutGroup VLayout(RectTransform rt, float spacing, int padding, bool expandW = true)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = expandW; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HLayout(RectTransform rt, float spacing, int padding)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            return h;
        }

        public static GridLayoutGroup Grid(RectTransform rt, Vector2 cell, float spacing, int padding, int cols)
        {
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cell;
            g.spacing = new Vector2(spacing, spacing);
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = cols;
            g.childAlignment = TextAnchor.UpperLeft;
            return g;
        }

        public static LayoutElement Size(Component c, float prefW = -1, float prefH = -1, float flexW = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (prefW >= 0) le.preferredWidth = prefW;
            if (prefH >= 0) le.preferredHeight = prefH;
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }

        /// <summary>Lista desplazable vertical; devuelve el contenido donde añadir filas.</summary>
        public static RectTransform ScrollList(Transform parent, out RectTransform scrollRoot)
        {
            var root = Panel(parent, "Scroll", new Color(0, 0, 0, 0.18f));
            var sr = root.gameObject.AddComponent<ScrollRect>();
            var viewport = Rect("Viewport", root.transform);
            Stretch(viewport);
            var mask = viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var v = VLayout(content, 10, 10);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30f;
            scrollRoot = root.rectTransform;
            return content;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static bool PointerOverUi(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            return results.Count > 0;
        }
    }
}
