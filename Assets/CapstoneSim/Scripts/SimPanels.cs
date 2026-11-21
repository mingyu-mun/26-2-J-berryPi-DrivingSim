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
        public BioSource bio;
        public AiLink ai;
        public SimBootstrap sim;
        public bool show = true;
        [Tooltip("졸음 확정 시 경고음")] public bool beep = true;

        const int GW = 640, GH = 120;
        Texture2D _texE, _texP;
        Color32[] _pix;
        int _lastLevel = -9, _lastWaveN = -1;
        float[] _lastE;
        AudioSource _audio;
        AudioClip _beepLo, _beepHi;

        static readonly Color32 Bg = new Color32(6, 10, 15, 0), Grid = new Color32(255, 255, 255, 14), Mid = new Color32(255, 255, 255, 22);
        static readonly Color32 EcgCol = new Color32(80, 255, 150, 255), PpgCol = new Color32(255, 125, 95, 255);

        void Start()
        {
            _texE = NewTex(); _texP = NewTex();
            _pix = new Color32[GW * GH];
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

        static string V(float v, string fmt) => v < 0 ? "–" : v.ToString(fmt);
        static string MmSs(float s) => SimTheme.MmSs(s);

        void OnGUI()
        {
            if (!show) return;
            var m = SimTheme.Begin();
            DrawBio();
            DrawAi();
            DrawBanner();
            SimTheme.End(m);
        }

        // ---------------------------------------------------------------- 왼쪽 위: 생체 신호
        void DrawBio()
        {
            var r = Live ? ai.Latest : null;
            var P = new Rect(40, 40, 680, 410);
            SimTheme.Panel(P, 18f);
            float x = P.x + 22, y = P.y + 18, w = P.width - 44;
            SimTheme.Label(new Rect(x, y, 300, 24), "BIOSIGNAL", 13, SimTheme.Accent, true);
            bool liveSrc = bio != null && bio.IsLive;
            string badge = liveSrc ? "실측 라이브" : "기록 재생";
            float bw = SimTheme.TextWidth(badge, 12, true) + 22;
            SimTheme.Chip(new Rect(P.xMax - 22 - bw, y, bw, 24), badge, liveSrc ? SimTheme.Green : SimTheme.Accent);
            float cw = SimTheme.TextWidth("CAN 시뮬레이터 합성", 12, true) + 22;
            SimTheme.Chip(new Rect(P.xMax - 30 - bw - cw, y, cw, 24), "CAN 시뮬레이터 합성", SimTheme.Amber);
            if (bio == null || !bio.Loaded)
            {
                SimTheme.Label(new Rect(x, y + 40, w, 60), bio ? bio.Status : "입력 없음", 16, SimTheme.Sub, false, TextAnchor.UpperLeft, true);
                return;
            }
            bool wave = r != null && r.ecg_w != null && r.ecg_w.Length > 1;
            y += 36;
            WaveRow(x, y, w, "ECG", r != null && r.has_out ? $"HR <b>{V(r.hr_ecg, "F0")}</b> bpm   ·   이상 간격 {V(r.q_ecg, "F2")}" : "HR –", _texE, wave);
            y += 156;
            WaveRow(x, y, w, "PPG", r != null && r.has_out ? $"PR <b>{V(r.hr_ppg, "F0")}</b> bpm   ·   이상 간격 {V(r.q_ppg, "F2")}" : "PR –", _texP, wave);
            y += 158;
            // 아래: 재생 위치 (기록) 또는 수신 품질 (실측)
            if (bio is BioReplay br)
            {
                float dur = Mathf.Max(1f, br.DurationSec);
                var tl = new Rect(x, y + 4, w, 6);
                SimTheme.Fill(tl, new Color(1, 1, 1, 0.08f), 3f);
                foreach (var g in br.Segments)
                {
                    Color c = g.label.Contains("졸음") ? SimTheme.Red : g.label.Contains("정상") ? SimTheme.Green : SimTheme.Gray;
                    SimTheme.Fill(new Rect(tl.x + tl.width * g.start / dur, tl.y, Mathf.Max(1, tl.width * (g.end - g.start) / dur - 2), tl.height), SimTheme.A(c, 0.55f), 3f);
                }
                float px = tl.x + tl.width * Mathf.Clamp01(br.FileTimeSec / dur);
                SimTheme.Fill(new Rect(px - 2, tl.y - 5, 4, 16), Color.white, 2f);
                string lab = string.IsNullOrEmpty(br.SegmentLabel) ? "" : $"   ·   참고 라벨(자기보고) <b>{br.SegmentLabel}</b>";
                SimTheme.Label(new Rect(x, y + 14, w, 22), $"{br.SessionName}   {MmSs(br.FileTimeSec)} / {MmSs(br.DurationSec)}   ·   ECG {br.FsEcg:F0} Hz · PPG {br.FsPpg:F0} Hz{lab}", 13, SimTheme.Sub);
            }
            else if (bio is LiveBioSource lb)
                SimTheme.Label(new Rect(x, y + 4, w, 30), $"{lb.Device}   ·   {lb.PacketRate:F0} 패킷/s   ·   손실 {lb.Lost}   ·   흔들림 {lb.JitterMs:F1} ms   ·   마지막 수신 {lb.LastAgeSec:F1} s 전", 13, lb.Receiving ? SimTheme.Sub : SimTheme.Amber);
        }

        void WaveRow(float x, float y, float w, string name, string info, Texture2D tex, bool wave)
        {
            SimTheme.Label(new Rect(x, y, 60, 24), name, 17, SimTheme.Text, true);
            SimTheme.Label(new Rect(x + 60, y, w - 60, 24), info, 14, SimTheme.Sub);
            var tr = new Rect(x, y + 30, w, 116);
            SimTheme.Fill(tr, new Color(0.02f, 0.035f, 0.05f, 0.85f), 10f);
            GUI.DrawTexture(tr, tex);
            if (!wave) SimTheme.Label(tr, "전처리 파형 대기 중 (AI 서버 연결 후 표시)", 13, SimTheme.Dim, false, TextAnchor.MiddleCenter);
        }

        // ---------------------------------------------------------------- 오른쪽 위: AI 판정
        void DrawAi()
        {
            const float PW = 480;
            var P = new Rect(SimTheme.VW - PW - 40, 40, PW, 520);
            SimTheme.Panel(P, 18f);
            float x = P.x + 24, y = P.y + 18, w = PW - 48;
            SimTheme.Label(new Rect(x, y, 300, 24), "DRIVER STATE · AI", 13, SimTheme.Accent, true);
            SimTheme.Label(new Rect(x, y, w, 24), "S1-EP + 상태 머신 v2 · 이 PC CPU", 12, SimTheme.Dim, false, TextAnchor.MiddleRight);
            if (ai == null) return;
            var r = ai.Latest;
            y += 34;
            if (!Live)
            {
                SimTheme.Label(new Rect(x, y, w, 40), "연결 대기", 30, SimTheme.Text, true);
                SimTheme.Label(new Rect(x, y + 44, w, 60), ai.ServerStatus, 15, SimTheme.Sub, false, TextAnchor.UpperLeft, true);
                return;
            }

            bool ready = r.base_status == "ready" || r.base_status == "retry_required" && r.base_a >= 0;
            if (!r.has_out)
            {
                string head, sub; float prog;
                if (r.base_status == "waiting")
                {
                    head = "착석 대기"; prog = Mathf.Clamp01(r.t_rel / Mathf.Max(1, r.base_wait));
                    sub = $"측정 시작 후 {r.base_wait:F0}초는 기준에 쓰지 않습니다  ·  {r.t_rel:F0} / {r.base_wait:F0} s";
                }
                else if (!ready)
                {
                    head = "개인 기준 수집"; prog = Mathf.Clamp01(r.clean_windows / (float)Mathf.Max(1, r.required_clean));
                    sub = $"깨끗한 30초 창 {r.clean_windows} / {r.required_clean}  ·  경과 {MmSs(r.t_rel)} (최대 {MmSs(r.base_max)})";
                }
                else
                {
                    head = "첫 판정 대기"; prog = 1f;
                    sub = $"기준 {BaseText(r)} · 창 끝 +{r.decision_delay - 30:F0}초 후 첫 출력";
                }
                SimTheme.Label(new Rect(x, y, w, 52), head, 38, SimTheme.Text, true);
                y += 62;
                SimTheme.Bar(new Rect(x, y, w, 10), prog, SimTheme.Accent);
                SimTheme.Label(new Rect(x, y + 16, w, 40), sub, 14, SimTheme.Sub, false, TextAnchor.UpperLeft, true);
                y += 64;
            }
            else
            {
                var c = SimTheme.LevelColor(r.level);
                // 상태 표시: 큰 글자 + 왼쪽 색 막대
                SimTheme.Fill(new Rect(x, y + 6, 5, 50), c, 2.5f);
                SimTheme.Label(new Rect(x + 18, y, 220, 62), string.IsNullOrEmpty(r.level_name) ? "–" : r.level_name, 46, c, true);
                float bx = x + 18 + SimTheme.TextWidth(r.level_name ?? "", 46, true) + 18;
                if (r.candidate) { SimTheme.Chip(new Rect(bx, y + 18, 150, 26), "졸음 후보 · 확정 대기", SimTheme.Amber); bx += 158; }
                if (r.frozen) SimTheme.Chip(new Rect(bx, y + 18, 80, 26), "판정 보류", SimTheme.Gray);
                if (r.level == 2 && !string.IsNullOrEmpty(r.confirmation_source))
                    SimTheme.Label(new Rect(x + 18, y + 56, w, 18), $"확정 근거: {(r.confirmation_source == "persistence" ? "지속 조건" : r.confirmation_source)}", 12, SimTheme.Sub);
                y += 80;
                // p_used 막대 + 임계값
                SimTheme.Label(new Rect(x, y, 120, 22), "졸음 확률", 14, SimTheme.Sub, true);
                SimTheme.Label(new Rect(x, y, w, 22), V(r.p_used, "F2"), 20, SimTheme.Text, true, TextAnchor.MiddleRight);
                var bar = new Rect(x, y + 30, w, 12);
                Color pc = r.p_used >= r.th_on ? SimTheme.Red : r.p_used >= r.th_off ? SimTheme.Amber : SimTheme.Green;
                SimTheme.Bar(bar, Mathf.Max(0, r.p_used), pc);
                SimTheme.Fill(new Rect(bar.x + bar.width * r.th_on - 1, bar.y - 5, 2, 22), Color.white);
                SimTheme.Fill(new Rect(bar.x + bar.width * r.th_off - 1, bar.y - 3, 2, 18), new Color(1, 1, 1, 0.5f));
                y += 52;
                SimTheme.Label(new Rect(x, y, w, 20), $"채널 <b>{r.channel}</b>   신뢰도 {V(r.confidence, "F2")}   점수 {V(r.score, "F2")}   on {r.th_on:F2} · off {r.th_off:F2}", 13, SimTheme.Sub);
                SimTheme.Label(new Rect(x, y + 20, w, 20), $"p_EP {V(r.p_ep, "F2")}   p_PPG {V(r.p_ppg, "F2")}   q_ECG {V(r.q_ecg, "F2")}   q_PPG {V(r.q_ppg, "F2")}", 13, SimTheme.Sub);
                y += 48;
            }

            // 판정 이력
            SimTheme.Label(new Rect(x, y, w, 18), "판정 이력 · 최근 15분 (10초 간격)", 12, SimTheme.Sub, true);
            var g = new Rect(x, y + 22, w, 74);
            SimTheme.Fill(g, new Color(1, 1, 1, 0.035f), 8f);
            SimTheme.Fill(new Rect(g.x, g.y + g.height * (1 - r.th_on), g.width, 1), new Color(1, 1, 1, 0.35f));
            SimTheme.Fill(new Rect(g.x, g.y + g.height * (1 - r.th_off), g.width, 1), new Color(1, 1, 1, 0.15f));
            if (r.hist_t != null && r.hist_t.Length > 0)
            {
                int n = r.hist_t.Length, maxN = 90, s0 = Mathf.Max(0, n - maxN);
                for (int i = s0; i < n; i++)
                {
                    float px = g.x + 4 + (g.width - 8) * (i - s0 + 0.5f) / maxN, p = r.hist_p[i];
                    if (p < 0) { SimTheme.Fill(new Rect(px - 1.5f, g.yMax - 4, 3, 3), SimTheme.Gray); continue; }
                    SimTheme.Fill(new Rect(px - 2, g.y + 3 + (g.height - 6) * (1 - Mathf.Clamp01(p)) - 2, 4, 4), SimTheme.LevelColor(r.hist_lv[i]), 2f);
                }
            }
            y += 106;
            if (r.z_names != null && r.z_names.Length > 0)
            {
                var sb = new System.Text.StringBuilder("개인 기준 대비 큰 변화  ");
                for (int i = 0; i < r.z_names.Length; i++) sb.Append($"<b>{r.z_names[i]}</b> {(r.z_vals[i] >= 0 ? "+" : "")}{r.z_vals[i]:F1}   ");
                SimTheme.Label(new Rect(x, y, w, 20), sb.ToString(), 12, SimTheme.Sub);
            }
            if (r.base_source != null && r.base_source.StartsWith("file") && r.feat_ready >= 0 && r.feat_ready < 1f)
            {
                SimTheme.Label(new Rect(x, y + 22, w, 20), $"장기(5분) 특징 창 채우는 중 {r.feat_ready * 100f:F0}% — 그동안 판정이 보수적입니다", 12, SimTheme.Amber);
            }

            // 상태 표시줄
            float fy = P.yMax - 70;
            SimTheme.Fill(new Rect(x, fy - 8, w, 1), SimTheme.Line);
            string modeTxt = r.mode == "exact" ? "batch 동일(exact)" : "실시간(causal)";
            string pre = r.preroll > 0 ? $" · 앞 {r.preroll:F0}s 빠른 입력" : "";
            SimTheme.Label(new Rect(x, fy, w, 18), $"{modeTxt}{pre} · 기준 {BaseText(r)} · 창 {r.n_windows}개", 12, SimTheme.Sub);
            SimTheme.Label(new Rect(x, fy + 20, w, 18), $"입력 {(r.ecg_rx ? "●" : "○")} 생체  {(r.can_rx ? "●" : "○")} CAN   ·   마지막 창 끝 {MmSs(r.win_end)} ({V(r.out_age, "F0")}s 전)   ·   처리 {V(r.proc_ms, "F0")} ms", 12, SimTheme.Sub);
            SimTheme.Label(new Rect(x, fy + 40, w, 18), "can_drowsy = null (검증된 CAN 규칙 없음)" + (string.IsNullOrEmpty(r.error) ? "" : $"   ⚠ {r.error}"), 12, string.IsNullOrEmpty(r.error) ? SimTheme.Dim : SimTheme.Amber);
        }

        static string BaseText(AiUi r)
        {
            string src = r.base_source != null && r.base_source.StartsWith("file:") ? r.base_source.Substring(5) + " " : "";
            return r.base_a >= 0 ? $"{src}{r.base_a:F0}~{r.base_b:F0}s{(r.base_forced ? "(강제)" : "")}" : r.base_status;
        }

        // ---------------------------------------------------------------- 위 가운데: HMI 배너
        void DrawBanner()
        {
            if (!Live || !ai.Latest.has_out) return;
            int lv = ai.Latest.level;
            if (lv < 1) return;
            float left = 40 + 680 + 24, right = SimTheme.VW - 40 - 480 - 24, w = right - left;
            if (w < 360) { left = SimTheme.VW * 0.5f - 300; w = 600; }
            var r = new Rect(left, 40, w, 64);
            bool drowsy = lv == 2;
            float pulse = drowsy ? 0.75f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 6f) : 0.85f;
            SimTheme.Fill(r, SimTheme.A(drowsy ? new Color(0.45f, 0.05f, 0.08f) : new Color(0.4f, 0.25f, 0.02f), 0.9f * pulse), 16f);
            SimTheme.Outline(r, SimTheme.A(drowsy ? SimTheme.Red : SimTheme.Amber, 0.8f), 1.5f, 16f);
            SimTheme.Label(new Rect(r.x + 20, r.y + 8, r.width - 40, 20), drowsy ? "DROWSINESS DETECTED" : "CAUTION", 12, drowsy ? SimTheme.Red : SimTheme.Amber, true);
            SimTheme.Label(new Rect(r.x + 20, r.y + 26, r.width - 40, 32), drowsy ? "졸음이 감지되었습니다 — 안전한 곳에서 휴식하세요" : "졸음 징후가 보입니다 — 주의하세요", w < 560 ? 17 : 21, SimTheme.Text, true);
        }
    }
}
