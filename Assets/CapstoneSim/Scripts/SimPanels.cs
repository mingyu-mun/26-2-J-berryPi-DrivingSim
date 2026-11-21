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

        const int GW = 560, GH = 120;
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

        // ---------------------------------------------------------------- 왼쪽 위: 생체 신호 + 기준 대비
        const float BioW = 800f;
        static readonly Color BaseCol = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color EcgC = new Color(0.31f, 1f, 0.59f), PpgC = new Color(1f, 0.49f, 0.37f);

        void DrawBio()
        {
            var r = Live ? ai.Latest : null;
            var P = new Rect(40, 40, BioW, 622);
            SimTheme.Panel(P, 18f);
            float x = P.x + 22, y = P.y + 18, w = P.width - 44;
            SimTheme.Label(new Rect(x, y, 300, 24), "BIOSIGNAL · 개인 기준 대비", 13, SimTheme.Accent, true);
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
            bool has = r != null && r.has_out;
            y += 36;
            WaveRow(x, y, w, "ECG", has ? $"HR <b>{V(r.hr_ecg, "F0")}</b> bpm   ·   이상 간격 {V(r.q_ecg, "F2")}" : "HR –", _texE, wave,
                    r?.ecg_tb, r?.ecg_tn, r != null ? r.ecg_r : -9, r != null ? r.ecg_amp : -1, EcgC, "R-peak");
            y += 160;
            WaveRow(x, y, w, "PPG", has ? $"PR <b>{V(r.hr_ppg, "F0")}</b> bpm   ·   이상 간격 {V(r.q_ppg, "F2")}" : "PR –", _texP, wave,
                    r?.ppg_tb, r?.ppg_tn, r != null ? r.ppg_r : -9, r != null ? r.ppg_amp : -1, PpgC, "맥파 정점");
            y += 160;
            DrawTachogram(new Rect(x, y, w, 128), r);
            y += 136;
            SimTheme.Label(new Rect(x, y, w, 18), "기준 대비 이탈 z  =  (현재 − 개인 기준 중앙값) / 산포     |z| ≥ 2 주황 · ≥ 3 빨강", 12, SimTheme.Dim);
            y += 22;
            DrawDeviationChips(x, y, w, r);
            y += 40;
            // 재생 위치 (기록) 또는 수신 품질 (실측)
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

        void WaveRow(float x, float y, float w, string name, string info, Texture2D tex, bool wave,
                     float[] tb, float[] tn, float corr, float amp, Color col, string anchorName)
        {
            const float TW = 230;
            float ww = w - TW - 12;
            SimTheme.Label(new Rect(x, y, 60, 24), name, 17, SimTheme.Text, true);
            SimTheme.Label(new Rect(x + 60, y, ww - 60, 24), info, 14, SimTheme.Sub);
            var tr = new Rect(x, y + 30, ww, 120);
            SimTheme.Fill(tr, new Color(0.02f, 0.035f, 0.05f, 0.85f), 10f);
            GUI.DrawTexture(tr, tex);
            if (!wave) SimTheme.Label(tr, "전처리 파형 대기 중 (AI 서버 연결 후 표시)", 13, SimTheme.Dim, false, TextAnchor.MiddleCenter);
            SimTheme.Label(new Rect(tr.x + 10, tr.y + 4, 200, 16), "최근 6초 · 공통 전처리", 11, SimTheme.Dim);

            // 박동 형태: 기준 구간 중앙값 박동(흰색) vs 현재 30초 창 중앙값 박동(채널 색)
            var br = new Rect(x + ww + 12, y + 30, TW, 120);
            SimTheme.Fill(br, new Color(0.02f, 0.035f, 0.05f, 0.85f), 10f);
            SimTheme.Label(new Rect(x + ww + 12, y, TW, 24), "박동 형태", 13, SimTheme.Sub, true);
            bool hb = tb != null && tb.Length > 3, hn = tn != null && tn.Length > 3;
            if (!hb && !hn) { SimTheme.Label(br, "기준 확정 후 표시", 12, SimTheme.Dim, false, TextAnchor.MiddleCenter); return; }
            float lo = float.MaxValue, hi = float.MinValue;
            if (hb) foreach (var v in tb) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            if (hn) foreach (var v in tn) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            float pad = (hi - lo) * 0.12f + 1e-3f; lo -= pad; hi += pad;
            var plot = new Rect(br.x + 8, br.y + 8, br.width - 16, br.height - 30);
            int n = hb ? tb.Length : tn.Length;
            float pre = name == "ECG" ? (ai.Latest != null ? ai.Latest.ecg_tpre : 0.25f) : (ai.Latest != null ? ai.Latest.ppg_tpre : 0.45f);
            float ax = plot.x + plot.width * Mathf.Clamp01(pre * 100f / Mathf.Max(1, n - 1));
            for (float yy = plot.y; yy < plot.yMax; yy += 6) SimTheme.Fill(new Rect(ax, yy, 1, 3), new Color(1, 1, 1, 0.15f));
            if (hb) Curve(plot, tb, lo, hi, BaseCol, 2f);
            if (hn) Curve(plot, tn, lo, hi, col, 2.5f);
            // 수치: 형태 상관 r, 진폭 비 (기준 = 1.00)
            bool off = (corr > -2 && corr < 0.9f) || (amp > 0 && (amp < 0.7f || amp > 1.3f));
            string m = corr > -2 ? $"형태 r {corr:F2}   진폭 {amp:F2}×" : "형태 –";
            SimTheme.Label(new Rect(br.x + 10, br.yMax - 22, br.width - 20, 18), m, 12, off ? SimTheme.Amber : SimTheme.Sub, true);
            // 범례 (제목 줄 오른쪽)
            SimTheme.Fill(new Rect(br.xMax - 92, y + 11, 14, 3), BaseCol);
            SimTheme.Label(new Rect(br.xMax - 74, y + 3, 30, 18), "기준", 11, SimTheme.Dim);
            SimTheme.Fill(new Rect(br.xMax - 44, y + 11, 14, 3), col);
            SimTheme.Label(new Rect(br.xMax - 26, y + 3, 30, 18), "현재", 11, SimTheme.Dim);
            SimTheme.Label(new Rect(ax + 3, plot.y - 2, 80, 14), anchorName, 10, SimTheme.Dim);
        }

        /// <summary>연속 점을 이어 그림 (구간마다 세로로 채운 짧은 막대)</summary>
        static void Curve(Rect r, float[] d, float lo, float hi, Color c, float th)
        {
            int n = d.Length;
            if (n < 2) return;
            float span = Mathf.Max(1e-6f, hi - lo);
            for (int i = 0; i < n - 1; i++)
            {
                float x0 = r.x + r.width * i / (n - 1), x1 = r.x + r.width * (i + 1) / (n - 1);
                float y0 = r.yMax - (d[i] - lo) / span * r.height, y1 = r.yMax - (d[i + 1] - lo) / span * r.height;
                SimTheme.Fill(new Rect(x0, Mathf.Min(y0, y1) - th * 0.5f, Mathf.Max(1f, x1 - x0 + 0.5f), Mathf.Abs(y1 - y0) + th), c);
            }
        }

        /// <summary>RR(ECG)·PP(PPG) 간격 최근 2분 + 기준 구간 중앙값 ± 산포 띠</summary>
        void DrawTachogram(Rect R, AiUi r)
        {
            SimTheme.Label(new Rect(R.x, R.y, R.width, 20), "심박 간격 [ms] · 최근 2분   띠 = 개인 기준 중앙값 ± 산포", 13, SimTheme.Sub, true);
            SimTheme.Fill(new Rect(R.xMax - 196, R.y + 8, 8, 8), EcgC, 4f); SimTheme.Label(new Rect(R.xMax - 184, R.y, 70, 20), "RR (ECG)", 11, SimTheme.Dim);
            SimTheme.Fill(new Rect(R.xMax - 104, R.y + 8, 8, 8), PpgC, 4f); SimTheme.Label(new Rect(R.xMax - 92, R.y, 90, 20), "PP (PPG)", 11, SimTheme.Dim);
            var g = new Rect(R.x + 44, R.y + 24, R.width - 44, R.height - 40);
            SimTheme.Fill(g, new Color(0.02f, 0.035f, 0.05f, 0.85f), 8f);
            bool hasE = r != null && r.ecg_tms != null && r.ecg_tms.Length > 0, hasP = r != null && r.ppg_tms != null && r.ppg_tms.Length > 0;
            if (!hasE && !hasP) { SimTheme.Label(g, "첫 판정 후 표시", 12, SimTheme.Dim, false, TextAnchor.MiddleCenter); return; }
            float bm = r.ecg_bm > 0 ? r.ecg_bm : r.ppg_bm, bs = r.ecg_bm > 0 ? r.ecg_bs : r.ppg_bs;
            // 세로 범위: 기준 띠 ±3σ 와 (이상 간격 제외한) 점들을 함께 담음
            float lo = bm > 0 ? bm - 3 * Mathf.Max(bs, 15f) : float.MaxValue, hi = bm > 0 ? bm + 3 * Mathf.Max(bs, 15f) : float.MinValue;
            TachRange(r.ecg_tms, r.ecg_tbad, ref lo, ref hi); TachRange(r.ppg_tms, r.ppg_tbad, ref lo, ref hi);
            lo = Mathf.Max(250f, lo - 20f); hi = Mathf.Min(2000f, hi + 20f);
            _tg = g; _tlo = lo; _thi = hi;
            if (r.ecg_bm > 0)
            {
                float y0 = TY(r.ecg_bm + r.ecg_bs), y1 = TY(r.ecg_bm - r.ecg_bs);
                SimTheme.Fill(new Rect(g.x, y0, g.width, y1 - y0), SimTheme.A(EcgC, 0.12f));
                SimTheme.Fill(new Rect(g.x, TY(r.ecg_bm), g.width, 1), SimTheme.A(EcgC, 0.5f));
                SimTheme.Label(new Rect(R.x, TY(r.ecg_bm) - 9, 42, 18), $"{r.ecg_bm:F0}", 11, SimTheme.A(EcgC, 0.8f), false, TextAnchor.MiddleRight);
            }
            if (r.ppg_bm > 0) SimTheme.Fill(new Rect(g.x, TY(r.ppg_bm), g.width, 1), SimTheme.A(PpgC, 0.35f));
            SimTheme.Label(new Rect(R.x, g.y - 4, 42, 16), $"{hi:F0}", 10, SimTheme.Dim, false, TextAnchor.MiddleRight);
            SimTheme.Label(new Rect(R.x, g.yMax - 12, 42, 16), $"{lo:F0}", 10, SimTheme.Dim, false, TextAnchor.MiddleRight);
            for (int k = 0; k <= 4; k++)
            {
                float tx = g.x + g.width * k / 4f;
                SimTheme.Fill(new Rect(tx, g.y, 1, g.height), new Color(1, 1, 1, 0.05f));
                SimTheme.Label(new Rect(tx - 30, g.yMax + 1, 60, 14), k == 4 ? "창 끝" : $"{-120 + 30 * k}s", 10, SimTheme.Dim, false, TextAnchor.MiddleCenter);
            }
            TachPts(r.ppg_tt, r.ppg_tms, r.ppg_tbad, SimTheme.A(PpgC, 0.75f), 3f);
            TachPts(r.ecg_tt, r.ecg_tms, r.ecg_tbad, EcgC, 4f);
        }

        // 타코그램 좌표 변환 (현재 그리는 그래프 영역·세로 범위)
        Rect _tg; float _tlo, _thi;
        float TY(float v) => _tg.yMax - 4 - Mathf.Clamp01((v - _tlo) / Mathf.Max(1f, _thi - _tlo)) * (_tg.height - 8);
        float TX(float t) => _tg.x + _tg.width * Mathf.Clamp01((t + 120f) / 120f);

        static void TachRange(float[] v, int[] bad, ref float lo, ref float hi)
        {
            if (v == null) return;
            for (int i = 0; i < v.Length; i++)
                if (bad == null || i >= bad.Length || bad[i] == 0) { lo = Mathf.Min(lo, v[i]); hi = Mathf.Max(hi, v[i]); }
        }

        void TachPts(float[] tt, float[] v, int[] bad, Color c, float sz)
        {
            if (tt == null || v == null) return;
            for (int i = 0; i < v.Length && i < tt.Length; i++)
            {
                bool b = bad != null && i < bad.Length && bad[i] != 0;
                if (b && (v[i] < _tlo || v[i] > _thi)) continue;
                SimTheme.Fill(new Rect(TX(tt[i]) - sz * 0.5f, TY(v[i]) - sz * 0.5f, sz, sz), b ? SimTheme.A(SimTheme.Gray, 0.6f) : c, sz * 0.5f);
            }
        }

        /// <summary>기준 대비 이탈 칩: |z| 2 이상 주황, 3 이상 빨강 (z = (현재 − 기준 중앙값) / 산포, 모델 입력과 같은 값)</summary>
        void DrawDeviationChips(float x, float y, float w, AiUi r)
        {
            if (r == null || !r.has_out) { SimTheme.Label(new Rect(x, y, w, 30), "기준 대비 지표: 첫 판정 후 표시", 13, SimTheme.Dim); return; }
            float cx = x;
            cx = ZChip(cx, y, $"HR {V(r.hr_ecg, "F0")} · 기준 {V(r.hr_base, "F0")}", r.zc_hr_30);
            cx = ZChip(cx, y, $"RMSSD {V(r.rmssd_now, "F0")} · 기준 {V(r.rmssd_base, "F0")} ms", r.zc_rmssd_30);
            cx = ZChip(cx, y, "SDNN", r.zc_sdnn_30);
            cx = ZChip(cx, y, "5분 HR", r.zc_hr_300);
            ZChip(cx, y, "5분 RMSSD", r.zc_rmssd_300);
        }

        static float ZChip(float x, float y, string label, float z)
        {
            bool has = z > -98f;
            float az = Mathf.Abs(z);
            Color c = !has ? SimTheme.Gray : az >= 3f ? SimTheme.Red : az >= 2f ? SimTheme.Amber : SimTheme.Sub;
            string t = has ? $"{label}  z {(z >= 0 ? "+" : "")}{z:F1}" : $"{label}  z –";
            float wdt = SimTheme.TextWidth(t, 12, true) + 22;
            var rr = new Rect(x, y, wdt, 28);
            SimTheme.Fill(rr, has && az >= 2f ? SimTheme.A(c, 0.16f) : new Color(1, 1, 1, 0.05f), 14f);
            SimTheme.Outline(rr, has && az >= 2f ? SimTheme.A(c, 0.6f) : SimTheme.Line, 1f, 14f);
            SimTheme.Label(rr, t, 12, has && az >= 2f ? c : SimTheme.Text, true, TextAnchor.MiddleCenter);
            return x + wdt + 8;
        }

        // ---------------------------------------------------------------- 오른쪽 위: AI 판정
        void DrawAi()
        {
            const float PW = 480;
            var lr = Live ? ai.Latest : null;
            bool warm = lr != null && lr.base_source != null && lr.base_source.StartsWith("file") && lr.feat_ready >= 0 && lr.feat_ready < 1f;
            float ph = lr == null ? 160 : (lr.has_out ? 452 : 404) + (warm ? 22 : 0);
            var P = new Rect(SimTheme.VW - PW - 40, 40, PW, ph);
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
            string bt = r.base_source != null && r.base_source.StartsWith("file") ? $"기준 파일 {r.base_a:F0}~{r.base_b:F0}s" : $"기준 {BaseText(r)}";
            SimTheme.Label(new Rect(x, fy, w, 18), $"{modeTxt}{pre} · {bt} · 창 {r.n_windows}개", 12, SimTheme.Sub);
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
            float left = 40 + BioW + 24, right = SimTheme.VW - 40 - 480 - 24, w = right - left;
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
