// SimTheme.cs
// 메인 메뉴와 주행 화면이 함께 쓰는 IMGUI 디자인 시스템 (차량 계기판 스타일: 어두운 유리 패널 + 시안 강조색).
//   · 해상도 독립: 세로 1080 기준 가상 좌표로 그리고 GUI.matrix 로 확대/축소 (Begin/End)
//   · 글꼴: Windows 의 맑은 고딕을 동적 글꼴로 사용 (없으면 Unity 기본 글꼴)
//   · 그리기 도우미: Panel, Fill(둥근 모서리), Button, Chip, Bar, Radio, Segmented, Toggle, TextField, Timeline
using System.Collections.Generic;
using UnityEngine;

namespace CapstoneSim
{
    public static class SimTheme
    {
        // ---------------------------------------------------------------- 색
        public static readonly Color PanelBg = new Color(0.035f, 0.05f, 0.075f, 0.82f);
        public static readonly Color PanelHi = new Color(0.08f, 0.11f, 0.15f, 0.92f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Text = new Color(0.91f, 0.93f, 0.96f);
        public static readonly Color Sub = new Color(0.56f, 0.61f, 0.69f);
        public static readonly Color Dim = new Color(0.36f, 0.41f, 0.48f);
        public static readonly Color Accent = new Color(0.24f, 0.78f, 1f);          // 시안
        public static readonly Color Berry = new Color(0.69f, 0.42f, 1f);           // 브랜드 보조색
        public static readonly Color Green = new Color(0.24f, 0.86f, 0.59f);
        public static readonly Color Amber = new Color(1f, 0.70f, 0.25f);
        public static readonly Color Red = new Color(1f, 0.30f, 0.37f);
        public static readonly Color Gray = new Color(0.54f, 0.58f, 0.65f);

        public static Color LevelColor(int lv) => lv == 0 ? Green : lv == 1 ? Amber : lv == 2 ? Red : Gray;
        public static Color A(Color c, float a) { c.a = a; return c; }

        // ---------------------------------------------------------------- 좌표계
        /// <summary>현재 배율 (실제 픽셀 / 가상 픽셀)</summary>
        public static float S { get; private set; } = 1f;
        /// <summary>가상 화면 크기 (세로 1080 기준)</summary>
        public static float VW { get; private set; } = 1920f;
        public static float VH { get; private set; } = 1080f;

        /// <summary>OnGUI 첫 줄에서 호출. 반환값을 End 에 넘김</summary>
        public static Matrix4x4 Begin()
        {
            var old = GUI.matrix;
            S = Mathf.Clamp(Screen.height / 1080f, 0.5f, 2.5f);
            VW = Screen.width / S; VH = Screen.height / S;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(S, S, 1f));
            Init();
            return old;
        }
        public static void End(Matrix4x4 old) => GUI.matrix = old;

        // ---------------------------------------------------------------- 리소스
        static Font _font;
        static bool _init;
        static Texture2D _gradH, _gradV;
        static readonly Dictionary<long, GUIStyle> Styles = new Dictionary<long, GUIStyle>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _init = false; _fontTried = false; _font = null; _gradH = _gradV = null; Styles.Clear(); }

        static void Init()
        {
            if (_init && _gradH != null) return;
            _init = true;
            if (!_fontTried)
            {
                _fontTried = true;
                try { _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Segoe UI", "Arial" }, 16); }
                catch { _font = null; }
            }
            _gradH = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < 256; i++) _gradH.SetPixel(i, 0, new Color(1, 1, 1, 1f - i / 255f));
            _gradH.Apply();
            _gradV = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < 256; i++) _gradV.SetPixel(0, i, new Color(1, 1, 1, i / 255f));   // 아래(0)=투명 → 위=불투명
            _gradV.Apply();
            Styles.Clear();
        }
        static bool _fontTried;

        /// <summary>글자 스타일 (크기·굵기·정렬·줄바꿈 별로 캐시)</summary>
        public static GUIStyle T(int size, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
        {
            long key = size * 1000L + (bold ? 100 : 0) + (int)anchor * 2 + (wrap ? 1 : 0);
            if (!Styles.TryGetValue(key, out var st) || st == null)
            {
                st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = anchor, wordWrap = wrap, richText = true, clipping = TextClipping.Clip };
                st.padding = new RectOffset(0, 0, 0, 0);
                st.margin = new RectOffset(0, 0, 0, 0);
                if (_font) st.font = _font;
                Styles[key] = st;
            }
            return st;
        }

        public static void Label(Rect r, string text, int size, Color c, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
        {
            var st = T(size, bold, anchor, wrap);
            st.normal.textColor = c;
            GUI.Label(r, text, st);
        }

        public static float TextWidth(string text, int size, bool bold = false) => T(size, bold).CalcSize(new GUIContent(text)).x;

        // ---------------------------------------------------------------- 도형
        public static void Fill(Rect r, Color c, float radius = 0f)
        {
            if (c.a <= 0f) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
        }
        public static void Outline(Rect r, Color c, float width = 1f, float radius = 0f)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, width, radius);
        }
        /// <summary>유리 패널: 어두운 반투명 바탕 + 얇은 테두리 + 위쪽 하이라이트</summary>
        public static void Panel(Rect r, float radius = 14f, float alpha = 0.82f)
        {
            Fill(r, A(PanelBg, alpha), radius);
            Outline(r, Line, 1f, radius);
            Fill(new Rect(r.x + radius, r.y + 1, r.width - 2 * radius, 1), new Color(1, 1, 1, 0.06f));
        }
        /// <summary>왼쪽(불투명)→오른쪽(투명) 그라데이션</summary>
        public static void GradientH(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _gradH); GUI.color = o; }
        /// <summary>아래(불투명)→위(투명) 그라데이션 — 화면 아래 어둡게</summary>
        public static void GradientUp(Rect r, Color c)
        {
            var o = GUI.color; GUI.color = c;
            GUI.DrawTextureWithTexCoords(r, _gradV, new Rect(0, 1, 1, -1));
            GUI.color = o;
        }

        /// <summary>섹션 머리말: 작은 대문자 라벨 + 강조 막대</summary>
        public static void Header(Rect r, string text, Color? c = null)
        {
            Fill(new Rect(r.x, r.y + r.height * 0.5f - 7, 3, 14), c ?? Accent, 1.5f);
            Label(new Rect(r.x + 12, r.y, r.width - 12, r.height), text, 13, Sub, true);
        }

        // ---------------------------------------------------------------- 컨트롤
        public enum Kind { Normal, Primary, Ghost, Danger }

        static bool Hover(Rect r) => r.Contains(Event.current.mousePosition);

        public static bool Button(Rect r, string text, int size = 17, Kind kind = Kind.Normal, bool enabled = true)
        {
            bool hov = enabled && Hover(r);
            Color bg, fg = Text, line = Line;
            switch (kind)
            {
                case Kind.Primary: bg = A(Accent, hov ? 1f : 0.88f); fg = new Color(0.02f, 0.06f, 0.1f); line = A(Accent, 0f); break;
                case Kind.Danger: bg = A(Red, hov ? 0.35f : 0.2f); line = A(Red, 0.5f); break;
                case Kind.Ghost: bg = hov ? new Color(1, 1, 1, 0.07f) : new Color(1, 1, 1, 0f); break;
                default: bg = hov ? new Color(1, 1, 1, 0.12f) : new Color(1, 1, 1, 0.06f); break;
            }
            if (!enabled) { bg = new Color(1, 1, 1, 0.04f); fg = Dim; line = Line; }
            Fill(r, bg, 10f);
            if (line.a > 0) Outline(r, line, 1f, 10f);
            Label(r, text, size, fg, true, TextAnchor.MiddleCenter);
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>큰 카드형 버튼 (메인 메뉴 모드 선택)</summary>
        public static bool Card(Rect r, string tag, string title, string desc, string chip, Color chipColor, bool selected)
        {
            bool hov = Hover(r);
            Fill(r, selected ? new Color(0.07f, 0.13f, 0.19f, 0.92f) : A(PanelBg, hov ? 0.9f : 0.78f), 16f);
            Outline(r, selected ? A(Accent, 0.9f) : hov ? new Color(1, 1, 1, 0.18f) : Line, selected ? 1.5f : 1f, 16f);
            if (selected) Fill(new Rect(r.x, r.y + 18, 4, r.height - 36), Accent, 2f);
            Label(new Rect(r.x + 28, r.y + 18, r.width - 56, 20), tag, 13, selected ? Accent : Sub, true);
            Label(new Rect(r.x + 28, r.y + 40, r.width - 56, 40), title, 28, Text, true);
            Label(new Rect(r.x + 28, r.y + 84, r.width - 100, r.height - 96), desc, 15, Sub, false, TextAnchor.UpperLeft, true);
            if (!string.IsNullOrEmpty(chip))
            {
                float w = TextWidth(chip, 12, true) + 20;
                Chip(new Rect(r.xMax - w - 20, r.y + 18, w, 22), chip, chipColor);
            }
            Label(new Rect(r.xMax - 44, r.y + r.height * 0.5f - 14, 24, 28), "›", 30, hov || selected ? Accent : Dim, true, TextAnchor.MiddleCenter);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static void Chip(Rect r, string text, Color c, bool solid = false)
        {
            Fill(r, solid ? c : A(c, 0.16f), r.height * 0.5f);
            if (!solid) Outline(r, A(c, 0.55f), 1f, r.height * 0.5f);
            Label(r, text, Mathf.RoundToInt(r.height * 0.55f), solid ? new Color(0.03f, 0.05f, 0.08f) : c, true, TextAnchor.MiddleCenter);
        }

        /// <summary>상태 점 + 글자</summary>
        public static void Dot(Rect r, string text, Color c, int size = 14)
        {
            Fill(new Rect(r.x, r.y + r.height * 0.5f - 4, 8, 8), c, 4f);
            Label(new Rect(r.x + 14, r.y, r.width - 14, r.height), text, size, Sub);
        }

        public static void Bar(Rect r, float v, Color c, Color? bg = null)
        {
            Fill(r, bg ?? new Color(1, 1, 1, 0.08f), r.height * 0.5f);
            if (v > 0f) Fill(new Rect(r.x, r.y, Mathf.Max(r.height, r.width * Mathf.Clamp01(v)), r.height), c, r.height * 0.5f);
        }

        public static bool Radio(Rect r, string title, string sub, bool on, bool enabled = true)
        {
            bool hov = enabled && Hover(r);
            Fill(r, on ? new Color(0.07f, 0.13f, 0.19f, 0.9f) : hov ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0.025f), 10f);
            Outline(r, on ? A(Accent, 0.8f) : Line, 1f, 10f);
            var c = new Rect(r.x + 14, r.y + 16, 18, 18);
            Outline(c, on ? Accent : Sub, 2f, 9f);
            if (on) Fill(new Rect(c.x + 5, c.y + 5, 8, 8), Accent, 4f);
            Label(new Rect(r.x + 44, r.y + 9, r.width - 54, 22), title, 16, enabled ? Text : Dim, true);
            if (!string.IsNullOrEmpty(sub)) Label(new Rect(r.x + 44, r.y + 32, r.width - 54, r.height - 38), sub, 13, Sub, false, TextAnchor.UpperLeft, true);
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static int Segmented(Rect r, string[] items, int sel, bool[] enabled = null)
        {
            Fill(r, new Color(1, 1, 1, 0.05f), 10f);
            Outline(r, Line, 1f, 10f);
            float w = r.width / items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                var c = new Rect(r.x + i * w + 3, r.y + 3, w - 6, r.height - 6);
                bool en = enabled == null || enabled[i];
                bool hov = Hover(c);
                if (i == sel) Fill(c, A(Accent, 0.9f), 8f);
                else if (hov) Fill(c, new Color(1, 1, 1, 0.06f), 8f);
                Label(c, items[i], 14, i == sel ? new Color(0.02f, 0.06f, 0.1f) : en ? Text : Dim, true, TextAnchor.MiddleCenter);
                if (GUI.Button(c, GUIContent.none, GUIStyle.none)) sel = i;
            }
            return sel;
        }

        public static bool Toggle(Rect r, string text, bool v, string sub = null)
        {
            var sw = new Rect(r.x, r.y + 6, 44, 24);
            Fill(sw, v ? A(Accent, 0.9f) : new Color(1, 1, 1, 0.12f), 12f);
            Fill(new Rect(v ? sw.xMax - 21 : sw.x + 3, sw.y + 3, 18, 18), Color.white, 9f);
            Label(new Rect(r.x + 56, r.y + 2, r.width - 56, 22), text, 16, Text, true);
            if (!string.IsNullOrEmpty(sub)) Label(new Rect(r.x + 56, r.y + 24, r.width - 56, r.height - 24), sub, 13, Sub, false, TextAnchor.UpperLeft, true);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) v = !v;
            return v;
        }

        public static string TextField(Rect r, string label, string value)
        {
            Label(new Rect(r.x, r.y, r.width, 18), label, 12, Sub, true);
            var f = new Rect(r.x, r.y + 20, r.width, r.height - 20);
            Fill(f, new Color(1, 1, 1, 0.06f), 8f);
            Outline(f, Line, 1f, 8f);
            var st = T(16, false, TextAnchor.MiddleLeft);
            var ts = new GUIStyle(GUI.skin.textField) { font = st.font, fontSize = 16, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 0, 0) };
            ts.normal.background = ts.focused.background = ts.hover.background = null;
            ts.normal.textColor = ts.focused.textColor = ts.hover.textColor = Text;
            return GUI.TextField(f, value ?? "", ts);
        }

        /// <summary>재생 시작 위치 막대: 라벨 구간을 색으로 칠하고, 클릭·드래그로 위치를 고름</summary>
        public static float Timeline(Rect r, float value, float max, IList<(float s, float e, string label)> segs, int id)
        {
            Fill(r, new Color(1, 1, 1, 0.06f), 8f);
            if (max > 0 && segs != null)
                foreach (var g in segs)
                {
                    float x0 = r.x + r.width * Mathf.Clamp01(g.s / max), x1 = r.x + r.width * Mathf.Clamp01(g.e / max);
                    Color c = g.label.Contains("졸음") ? Red : g.label.Contains("주의") || g.label.Contains("피로") ? Amber : g.label.Contains("정상") ? Green : Gray;
                    Fill(new Rect(x0 + 1, r.yMax - 8, Mathf.Max(1, x1 - x0 - 2), 5), A(c, 0.85f), 2.5f);
                }
            var e = Event.current;
            int ctl = GUIUtility.GetControlID(id, FocusType.Passive, r);
            if ((e.type == EventType.MouseDown && r.Contains(e.mousePosition)) || (e.type == EventType.MouseDrag && GUIUtility.hotControl == ctl))
            {
                GUIUtility.hotControl = ctl;
                value = Mathf.Clamp01((e.mousePosition.x - r.x) / r.width) * max;
                e.Use();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == ctl) { GUIUtility.hotControl = 0; e.Use(); }
            float hx = r.x + r.width * (max > 0 ? Mathf.Clamp01(value / max) : 0f);
            Fill(new Rect(r.x, r.y + r.height * 0.5f - 3, hx - r.x, 4), A(Accent, 0.7f), 2f);
            Fill(new Rect(hx - 8, r.y + r.height * 0.5f - 9, 16, 16), Color.white, 8f);
            Outline(new Rect(hx - 8, r.y + r.height * 0.5f - 9, 16, 16), Accent, 2f, 8f);
            return value;
        }

        public static string MmSs(float s) => s < 0 ? "–" : $"{(int)s / 60:00}:{(int)s % 60:00}";
    }
}
