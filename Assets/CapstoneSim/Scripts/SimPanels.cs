// SimPanels.cs
// 화면 구성 (웹보드 규약 v1.2 의 표시 규칙을 따름: 상태는 상태 머신 v2 출력만 표시, 값 없으면 '–')
//   왼쪽     : ECG / PPG 파형 — 서버가 저장소 공통 전처리(prepare_channel, 100 Hz)를 거쳐 보낸 최근 6초
//   오른쪽 위 : AI 판정 — level(-1 측정 불가 / 0 정상 / 1 주의 / 2 졸음), 졸음 후보, p_used·임계값, 채널, 기준 수집 상태, 판정 이력
//   위 가운데 : HMI 배너 (주의 / 졸음) + 경고음
//   출처 표시 : 생체 = 기록 재생(recorded), CAN = 시뮬레이터 합성(simulated)
using UnityEngine;

namespace CapstoneSim
{
    public class SimPanels : MonoBehaviour
    {
        public BioReplay bio;
        public AiLink ai;
        public bool show = true;
        [Tooltip("졸음 확정 시 경고음")] public bool beep = true;

        const int GW = 520, GH = 110;
        Texture2D _texE, _texP, _white;
        Color32[] _pix;
        GUIStyle _title, _small, _tiny, _big, _mid, _banner, _badge;
        int _lastLevel = -9, _lastWaveN = -1;
        float[] _lastE;
        AudioSource _audio;
        AudioClip _beepLo, _beepHi;

        static readonly Color CGray = new Color(0.6f, 0.62f, 0.66f), CGreen = new Color(0.25f, 0.85f, 0.45f),
                              CAmber = new Color(1.0f, 0.68f, 0.15f), CRed = new Color(1.0f, 0.28f, 0.25f), CBlue = new Color(0.4f, 0.8f, 1f);
        static readonly Color32 Bg = new Color32(8, 14, 20, 210), Grid = new Color32(40, 70, 70, 255), Mid = new Color32(30, 50, 55, 255);
        static readonly Color32 EcgCol = new Color32(80, 255, 140, 255), PpgCol = new Color32(255, 120, 90, 255);

        static Color LevelColor(int lv) => lv == 0 ? CGreen : lv == 1 ? CAmber : lv == 2 ? CRed : CGray;

        void Start()
        {
            _texE = NewTex(); _texP = NewTex();
            _pix = new Color32[GW * GH];
            _white = Texture2D.whiteTexture;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _beepLo = Tone(660f, 0.25f); _beepHi = Tone(1100f, 0.12f);
            Clear(_texE); Clear(_texP);
        }

        static Texture2D NewTex() => new Texture2D(GW, GH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        static AudioClip Tone(float hz, float len)
        {
            int sr = 44100, n = (int)(sr * len);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Min(1f, i / 400f) * Mathf.Min(1f, (n - i) / 800f);
                d[i] = 0.25f * env * Mathf.Sin(2 * Mathf.PI * hz * i / sr);
            }
            var c = AudioClip.Create("beep" + hz, n, 1, sr, false);
            c.SetData(d, 0);
            return c;
        }

        void Update()
        {
            if (SimInput.HudTogglePressed()) show = !show;
            var r = Live ? ai.Latest : null;

            int lv = r != null && r.has_out ? r.level : -9;
            if (beep && _audio && lv != _lastLevel)
            {
                if (lv == 1) _audio.PlayOneShot(_beepLo);
                if (lv == 2) StartCoroutine(TripleBeep());
            }
            _lastLevel = lv;

            // 새 파형 패킷이 오면 다시 그림
            if (r != null && r.ecg_w != null && r.ecg_w.Length > 1 && (r.ecg_w != _lastE))
            {
                _lastE = r.ecg_w;
                Plot(_texE, r.ecg_w, r.wave_fs, EcgCol);
                Plot(_texP, r.ppg_w, r.wave_fs, PpgCol);
            }
        }

        bool Live => ai != null && ai.Connected && ai.Latest != null && ai.SessionAccepted;

        System.Collections.IEnumerator TripleBeep()
        {
            for (int i = 0; i < 3; i++) { _audio.PlayOneShot(_beepHi); yield return new WaitForSecondsRealtime(0.18f); }
        }

        void Clear(Texture2D tex)
        {
            for (int i = 0; i < _pix.Length; i++) _pix[i] = Bg;
            tex.SetPixels32(_pix); tex.Apply(false);
        }

        /// <summary>표본을 GW 열로 나눠 각 열의 최소~최대를 세로선으로 그림 (min-max 다운샘플링). 범위는 ±4 (z 단위, 자동)</summary>
        void Plot(Texture2D tex, float[] buf, float fs, Color32 col)
        {
            for (int i = 0; i < _pix.Length; i++) _pix[i] = Bg;
            int n = buf == null ? 0 : buf.Length;
            if (fs > 0)
                for (float s = fs; s < n; s += fs)
                {
                    int x = (int)(s * GW / n);
                    for (int y = 0; y < GH; y++) _pix[y * GW + x] = Grid;
                }
            for (int x = 0; x < GW; x++) _pix[(GH / 2) * GW + x] = Mid;
            if (n < 2) { tex.SetPixels32(_pix); tex.Apply(false); return; }
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < n; i++) { if (buf[i] < mn) mn = buf[i]; if (buf[i] > mx) mx = buf[i]; }
            float pad = (mx - mn) * 0.1f + 1e-3f, lo = mn - pad, span = Mathf.Max(1e-3f, mx - mn + 2 * pad);
            int prevY = -1;
            for (int x = 0; x < GW; x++)
            {
                int a = (int)((long)x * n / GW), b = Mathf.Max(a + 1, (int)((long)(x + 1) * n / GW));
                b = Mathf.Min(b, n);
                float cmin = float.MaxValue, cmax = float.MinValue;
                for (int i = a; i < b; i++) { if (buf[i] < cmin) cmin = buf[i]; if (buf[i] > cmax) cmax = buf[i]; }
                int y0 = Mathf.Clamp((int)((cmin - lo) / span * (GH - 1)), 0, GH - 1);
                int y1 = Mathf.Clamp((int)((cmax - lo) / span * (GH - 1)), 0, GH - 1);
                if (prevY >= 0) { y0 = Mathf.Min(y0, prevY); y1 = Mathf.Max(y1, prevY); }
                for (int y = y0; y <= y1; y++)
                {
                    _pix[y * GW + x] = col;
                    if (x + 1 < GW) _pix[y * GW + x + 1] = col;
                }
                prevY = Mathf.Clamp((int)((buf[b - 1] - lo) / span * (GH - 1)), 0, GH - 1);
            }
            tex.SetPixels32(_pix);
            tex.Apply(false);
        }

        void InitStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _tiny = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _mid = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            _banner = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _badge = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }

        void Badge(Rect r, string text, Color c)
        {
            Box(r, new Color(c.r, c.g, c.b, 0.85f));
            var o = GUI.contentColor; GUI.contentColor = Color.black; GUI.Label(r, text, _badge); GUI.contentColor = o;
        }

        static string V(float v, string fmt) => v < 0 ? "–" : v.ToString(fmt);
        static string MmSs(float s) => s < 0 ? "–" : $"{(int)s / 60:00}:{(int)s % 60:00}";

        void OnGUI()
        {
            if (!show) return;
            InitStyles();
            DrawWaveforms();
            DrawAiPanel();
            DrawBanner();
        }

        void DrawWaveforms()
        {
            float x = 10, y = 70;
            var r = Live ? ai.Latest : null;
            Box(new Rect(x - 4, y - 4, GW + 8, GH * 2 + 96), new Color(0, 0, 0, 0.38f));
            if (bio == null || !bio.Loaded)
            {
                GUI.Label(new Rect(x, y, GW, 40), bio ? bio.Status : "BioReplay 없음", _small);
                return;
            }
            bool wave = r != null && r.ecg_w != null && r.ecg_w.Length > 1;
            GUI.Label(new Rect(x, y, 260, 22), "ECG", _title);
            GUI.Label(new Rect(x + 60, y + 2, GW - 60, 20),
                r != null && r.has_out ? $"HR {V(r.hr_ecg, "F0")} bpm   이상간격 q {V(r.q_ecg, "F2")}   (최근 30초 창)" : "HR –", _small);
            GUI.DrawTexture(new Rect(x, y + 22, GW, GH), _texE);
            y += GH + 26;
            GUI.Label(new Rect(x, y, 260, 22), "PPG", _title);
            GUI.Label(new Rect(x + 60, y + 2, GW - 60, 20),
                r != null && r.has_out ? $"PR {V(r.hr_ppg, "F0")} bpm   이상간격 q {V(r.q_ppg, "F2")}" : "PR –", _small);
            GUI.DrawTexture(new Rect(x, y + 22, GW, GH), _texP);
            if (!wave) GUI.Label(new Rect(x + 10, y - GH / 2 - 10, GW, 20), "전처리 파형 대기 중 (AI 서버 연결 후 표시)", _small);
            y += GH + 26;
            GUI.Label(new Rect(x, y, GW, 18),
                $"기록 재생 {bio.SessionName}  {MmSs(bio.FileTimeSec)} / {MmSs(bio.DurationSec)}   ECG {bio.FsEcg:F0} Hz · PPG {bio.FsPpg:F0} Hz 원신호 → 서버 전처리 100 Hz", _tiny);
            y += 18;
            string lab = string.IsNullOrEmpty(bio.SegmentLabel) ? "" : $"참고 라벨(자기보고): {bio.SegmentLabel}";
            Badge(new Rect(x, y + 2, 112, 18), "생체: 기록 재생", CBlue);
            Badge(new Rect(x + 118, y + 2, 132, 18), "CAN: 시뮬레이터 합성", CAmber);
            GUI.Label(new Rect(x + 258, y, GW - 258, 20), lab, _small);
        }

        void DrawAiPanel()
        {
            const float W = 400;
            float x = Screen.width - W - 10, y = 10, h = 318;
            Box(new Rect(x, y, W, h), new Color(0.02f, 0.05f, 0.08f, 0.8f));
            GUI.Label(new Rect(x + 12, y + 6, W, 22), "AI 운전자 상태", _title);
            GUI.Label(new Rect(x + 130, y + 8, W - 140, 20), "S1-EP 학생 모델 + 상태 머신 v2 · 이 PC CPU 추론", _tiny);
            if (ai == null) { GUI.Label(new Rect(x + 12, y + 40, W - 24, 40), "AiLink 없음", _small); return; }
            var r = ai.Latest;
            float cy = y + 32;
            if (!Live)
            {
                GUI.Label(new Rect(x + 12, cy, W - 24, 80), $"연결 안 됨\n{ai.ServerStatus}", _mid);
                return;
            }

            // ---- 기준 수집 (상태 머신 v2 2장) ----
            bool ready = r.base_status == "ready" || r.base_status == "retry_required" && r.base_a >= 0;
            if (!r.has_out)
            {
                string head, sub; float prog;
                if (r.base_status == "waiting")
                {
                    head = "착석 대기"; prog = Mathf.Clamp01(r.t_rel / Mathf.Max(1, r.base_wait));
                    sub = $"측정 시작 후 {r.base_wait:F0}초는 기준에 쓰지 않음  ({r.t_rel:F0}/{r.base_wait:F0}s)";
                }
                else if (!ready)
                {
                    head = "기준 수집 중"; prog = Mathf.Clamp01(r.clean_windows / (float)Mathf.Max(1, r.required_clean));
                    sub = $"깨끗한 30초 창 {r.clean_windows}/{r.required_clean}  ·  경과 {MmSs(r.t_rel)} (최대 {MmSs(r.base_max)})";
                }
                else
                {
                    head = "첫 판정 대기"; prog = 1f;
                    sub = $"기준 {r.base_a:F0}~{r.base_b:F0}s 확정 · 첫 창 판정 중 (창 끝 +{r.decision_delay - 30:F0}s 후 출력)";
                }
                GUI.Label(new Rect(x + 12, cy, W - 24, 40), head, _big);
                cy += 46;
                Box(new Rect(x + 12, cy, W - 24, 12), new Color(1, 1, 1, 0.15f));
                Box(new Rect(x + 12, cy, (W - 24) * prog, 12), CBlue);
                GUI.Label(new Rect(x + 12, cy + 14, W - 24, 20), sub, _small);
                cy += 40;
            }
            else
            {
                var c = LevelColor(r.level);
                var old = GUI.contentColor; GUI.contentColor = c;
                GUI.Label(new Rect(x + 12, cy, 200, 44), string.IsNullOrEmpty(r.level_name) ? "–" : r.level_name, _big);
                GUI.contentColor = old;
                float bx = x + 150;
                if (r.candidate) { Badge(new Rect(bx, cy + 8, 150, 20), "졸음 후보 · 확정 대기", CAmber); bx += 156; }
                if (r.frozen) { Badge(new Rect(bx, cy + 8, 70, 20), "판정 보류", CGray); bx += 76; }
                if (r.level == 2 && !string.IsNullOrEmpty(r.confirmation_source))
                    GUI.Label(new Rect(x + 150, cy + 26, W - 162, 18), $"확정 근거: {(r.confirmation_source == "persistence" ? "지속 조건" : r.confirmation_source)}", _tiny);
                cy += 46;

                // p_used 막대 + 임계값 (th_on / th_off)
                float bw = W - 120;
                GUI.Label(new Rect(x + 12, cy - 2, 90, 18), "졸음 확률", _small);
                Box(new Rect(x + 92, cy + 2, bw, 12), new Color(1, 1, 1, 0.12f));
                if (r.p_used >= 0) Box(new Rect(x + 92, cy + 2, bw * Mathf.Clamp01(r.p_used), 12), r.p_used >= r.th_on ? CRed : r.p_used >= r.th_off ? CAmber : CGreen);
                Box(new Rect(x + 92 + bw * r.th_on, cy - 1, 2, 18), Color.white);
                Box(new Rect(x + 92 + bw * r.th_off, cy - 1, 1, 18), new Color(1, 1, 1, 0.6f));
                GUI.Label(new Rect(x + 96 + bw, cy - 2, 40, 18), V(r.p_used, "F2"), _small);
                cy += 18;
                GUI.Label(new Rect(x + 12, cy, W - 24, 18),
                    $"채널 {r.channel}   신뢰도 {V(r.confidence, "F2")}   점수(중앙값) {V(r.score, "F2")}   on {r.th_on:F2} / off {r.th_off:F2}", _tiny);
                cy += 16;
                GUI.Label(new Rect(x + 12, cy, W - 24, 18),
                    $"p_EP {V(r.p_ep, "F2")}   p_PPG {V(r.p_ppg, "F2")}   q_ECG {V(r.q_ecg, "F2")}   q_PPG {V(r.q_ppg, "F2")}", _tiny);
                cy += 18;
            }

            // ---- 판정 이력 (최근 창들의 p_used, 점 색 = level) ----
            GUI.Label(new Rect(x + 12, cy, W - 24, 16), "판정 이력 — 최근 15분, 10초 간격 (점 색 = 상태, 선 = on/off 임계값)", _tiny);
            float gx = x + 12, gw = W - 24, gh = 60, gy = cy + 16;
            Box(new Rect(gx, gy, gw, gh), new Color(1, 1, 1, 0.06f));
            Box(new Rect(gx, gy + gh * (1 - r.th_on), gw, 1), new Color(1, 1, 1, 0.45f));
            Box(new Rect(gx, gy + gh * (1 - r.th_off), gw, 1), new Color(1, 1, 1, 0.2f));
            if (r.hist_t != null && r.hist_t.Length > 0)
            {
                int n = r.hist_t.Length, maxN = 90;
                for (int i = Mathf.Max(0, n - maxN); i < n; i++)
                {
                    float p = r.hist_p[i];
                    float px = gx + gw * (i - (n - Mathf.Min(n, maxN)) + 0.5f) / maxN;
                    if (p < 0) { Box(new Rect(px - 1, gy + gh - 3, 3, 3), CGray); continue; }
                    Box(new Rect(px - 1.5f, gy + gh * (1 - Mathf.Clamp01(p)) - 1.5f, 3, 3), LevelColor(r.hist_lv[i]));
                }
            }
            cy = gy + gh + 4;

            // ---- 근거: 기준 대비 크게 벗어난 RR 특징 ----
            if (r.z_names != null && r.z_names.Length > 0)
            {
                var sb = new System.Text.StringBuilder("개인 기준 대비 큰 변화(z, ±5 상한): ");
                for (int i = 0; i < r.z_names.Length; i++) sb.Append($"{r.z_names[i]} {(r.z_vals[i] >= 0 ? "+" : "")}{r.z_vals[i]:F1}  ");
                GUI.Label(new Rect(x + 12, cy, W - 24, 18), sb.ToString(), _tiny);
            }
            cy += 18;

            // ---- 상태 표시줄 ----
            string modeTxt = r.mode == "exact" ? "batch 동일 계산(exact)" : "실시간 계산(causal)";
            string pre = r.preroll > 0 ? $" · 앞 {r.preroll:F0}s 빠른 입력" : "";
            string bas = r.base_a >= 0 ? $"기준 {r.base_a:F0}~{r.base_b:F0}s{(r.base_forced ? "(강제)" : "")}" : $"기준 {r.base_status}";
            GUI.Label(new Rect(x + 12, y + h - 52, W - 24, 18), $"{modeTxt}{pre} · {bas} · 창 {r.n_windows}개", _tiny);
            GUI.Label(new Rect(x + 12, y + h - 36, W - 24, 18),
                $"입력 ECG/PPG {(r.ecg_rx ? "●" : "○")}  CAN {(r.can_rx ? "●" : "○")}   마지막 창 끝 {MmSs(r.win_end)} ({V(r.out_age, "F0")}s 전)  처리 {V(r.proc_ms, "F0")} ms", _tiny);
            GUI.Label(new Rect(x + 12, y + h - 20, W - 24, 18),
                $"can_drowsy = null (검증된 CAN 규칙 없음)  ·  세션 {r.session}" + (string.IsNullOrEmpty(r.error) ? "" : $"  ⚠ {r.error}"), _tiny);
        }

        void DrawBanner()
        {
            if (!Live || !ai.Latest.has_out) return;
            int lv = ai.Latest.level;
            if (lv < 1) return;
            string msg = lv == 2 ? "졸음이 감지되었습니다 — 안전한 곳에서 휴식하세요" : "주의 — 졸음 징후가 보입니다";
            var c = lv == 2 ? new Color(0.8f, 0.12f, 0.08f, 0.85f) : new Color(0.85f, 0.55f, 0.05f, 0.75f);
            if (lv == 2 && (int)(Time.realtimeSinceStartup * 2) % 2 == 0) c.a = 0.6f;
            float left = GW + 30, right = Screen.width - 420, w = right - left, y = 64;
            if (w < 360) { left = 10; w = Screen.width - 20; y = Screen.height - 220; }
            var r = new Rect(left, y, w, 44);
            Box(r, c);
            var bs = new GUIStyle(_banner) { fontSize = w < 560 ? 16 : 21 };
            GUI.Label(r, msg, bs);
        }
    }
}
