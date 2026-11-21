// SimHUD.cs
// 주행 계기판 (SimTheme 디자인, 메인 메뉴와 같은 스타일).
//   왼쪽 아래: 속도·기어·RPM·차로 편차·깜빡이 + 핸들 아이콘   /   그 위: 주행 기록(REC) 상태
//   오른쪽 아래: 조작키 안내   /   위 가운데: 동기화 마커(M) 플래시 (영상과 로그를 맞추는 기준점, HUD 를 꺼도 표시)
using UnityEngine;

namespace CapstoneSim
{
    public class SimHUD : MonoBehaviour
    {
        public CarController car;
        public DrivingDataLogger logger;
        public SimBootstrap sim;
        public bool show = true;
        public bool showHelp = true;

        Texture2D _wheelIcon;

        void Update()
        {
            if (SimInput.HudTogglePressed()) show = !show;
        }

        void OnGUI()
        {
            if (!_wheelIcon) _wheelIcon = ProceduralTextures.SteeringIcon();
            var m = SimTheme.Begin();
            float W = SimTheme.VW, H = SimTheme.VH;

            if (logger && Time.realtimeSinceStartup - logger.LastMarkerRealtime < 0.3f)
                SimTheme.Fill(new Rect(W * 0.5f - 70, 6, 140, 60), Color.white, 6f);

            if (show && car)
            {
                DrawCluster(W, H);
                DrawRec(H);
                if (showHelp) DrawHelp(W, H);
            }
            SimTheme.End(m);
        }

        void DrawCluster(float W, float H)
        {
            var r = new Rect(40, H - 214, 500, 174);
            SimTheme.Panel(r, 18f, 0.8f);
            float spd = Mathf.Abs(car.SpeedKmh);
            SimTheme.Label(new Rect(r.x + 26, r.y + 12, 200, 92), $"{spd:0}", 84, SimTheme.Text, true);
            float nw = SimTheme.TextWidth($"{spd:0}", 84, true);
            SimTheme.Label(new Rect(r.x + 32 + nw, r.y + 58, 80, 30), "km/h", 18, SimTheme.Sub, true);
            // 기어
            string gear = car.Gear == -1 ? "R" : car.Gear == 0 ? "N" : "D" + car.Gear;
            var gr = new Rect(r.xMax - 92, r.y + 24, 66, 52);
            SimTheme.Fill(gr, new Color(1, 1, 1, 0.06f), 12f);
            SimTheme.Outline(gr, SimTheme.A(SimTheme.Accent, 0.5f), 1f, 12f);
            SimTheme.Label(gr, gear, 26, SimTheme.Accent, true, TextAnchor.MiddleCenter);
            // RPM
            float rpm = car.EngineRpm;
            SimTheme.Label(new Rect(r.x + 28, r.y + 104, 80, 18), "RPM", 12, SimTheme.Sub, true);
            SimTheme.Bar(new Rect(r.x + 70, r.y + 110, r.width - 190, 7), rpm / 7000f, rpm > 5800 ? SimTheme.Red : SimTheme.Accent);
            SimTheme.Label(new Rect(r.xMax - 112, r.y + 102, 90, 22), $"{rpm:0}", 14, SimTheme.Text, true, TextAnchor.MiddleRight);
            // 차로·편차·깜빡이·랩
            if (logger)
            {
                var ts = logger.LastSample;
                float off = ts.laneOffset;
                Color oc = Mathf.Abs(off) > 0.85f ? SimTheme.Red : Mathf.Abs(off) > 0.5f ? SimTheme.Amber : SimTheme.Text;
                float y = r.y + 134;
                SimTheme.Label(new Rect(r.x + 28, y, 120, 26), ts.lane == 0 ? "도로 밖" : $"{ts.lane}차로", 16, ts.lane == 0 ? SimTheme.Red : SimTheme.Text, true);
                SimTheme.Label(new Rect(r.x + 110, y, 170, 26), $"편차 {off:+0.00;-0.00} m", 16, oc, true);
                SimTheme.Label(new Rect(r.x + 270, y, 60, 26), "◀", 20, car.TurnSignal < 0 && Blink() ? SimTheme.Green : SimTheme.Dim, true, TextAnchor.MiddleCenter);
                SimTheme.Label(new Rect(r.x + 312, y, 60, 26), "▶", 20, car.TurnSignal > 0 && Blink() ? SimTheme.Green : SimTheme.Dim, true, TextAnchor.MiddleCenter);
                SimTheme.Label(new Rect(r.xMax - 112, y, 90, 26), $"랩 {logger.Lap}", 15, SimTheme.Sub, true, TextAnchor.MiddleRight);
            }
            // 핸들
            var wr = new Rect(r.xMax + 22, r.y + 22, 112, 112);
            SimTheme.Fill(new Rect(wr.x - 10, wr.y - 10, wr.width + 20, wr.height + 52), SimTheme.A(SimTheme.PanelBg, 0.6f), 16f);
            var gm = GUI.matrix;
            GUIUtility.RotateAroundPivot(car.SteeringWheelDeg, Vector2.Scale(wr.center, new Vector2(SimTheme.S, SimTheme.S)));
            GUI.DrawTexture(wr, _wheelIcon);
            GUI.matrix = gm;
            string src = car.ActiveSteerSource == SteerMode.Mouse ? "마우스" : car.ActiveSteerSource == SteerMode.Gamepad ? "패드" : "키보드";
            string mode = car.steerMode == SteerMode.Auto ? $"자동·{src}" : src;
            SimTheme.Label(new Rect(wr.x - 10, wr.yMax + 6, wr.width + 20, 22), $"{car.SteeringWheelDeg:+0;-0;0}°  {mode}", 13, SimTheme.Sub, true, TextAnchor.MiddleCenter);

            if (car.ActiveSteerSource == SteerMode.Mouse)
            {
                float half = W * car.mouseRange;
                SimTheme.Fill(new Rect(W * 0.5f - half, H - 12, half * 2f, 3), new Color(1, 1, 1, 0.25f));
                SimTheme.Fill(new Rect(W * 0.5f - 1, H - 20, 2, 14), new Color(1, 1, 1, 0.4f));
            }
        }

        void DrawRec(float H)
        {
            if (!logger) return;
            var r = new Rect(40, H - 262, 500, 38);
            if (logger.IsRecording)
            {
                SimTheme.Fill(r, new Color(0.35f, 0.04f, 0.07f, 0.85f), 19f);
                SimTheme.Outline(r, SimTheme.A(SimTheme.Red, 0.6f), 1f, 19f);
                SimTheme.Fill(new Rect(r.x + 16, r.y + 13, 12, 12), Blink() ? SimTheme.Red : SimTheme.A(SimTheme.Red, 0.3f), 6f);
                SimTheme.Label(new Rect(r.x + 38, r.y, r.width - 50, r.height),
                    $"REC  {FormatTime(logger.ElapsedSec)}   ·   샘플 {logger.SampleCount}   ·   마커 {logger.MarkerCount}   ·   {logger.participantId}", 15, SimTheme.Text, true);
            }
            else
            {
                SimTheme.Fill(r, SimTheme.A(SimTheme.PanelBg, 0.6f), 19f);
                SimTheme.Label(new Rect(r.x + 18, r.y, r.width - 30, r.height), "주행 기록 대기 — L 키로 기록 시작", 14, SimTheme.Sub, true);
            }
        }

        void DrawHelp(float W, float H)
        {
            string[] keys = { "W/↑", "S/↓", "A·D", "L", "M", "Q/E", "R", "C", "T", "H", "Esc" };
            string[] acts = { "가속", "브레이크", "조향", "기록", "마커", "깜빡이", "복귀", "시점", "조향방식", "HUD", "메뉴" };
            float y = H - 54, h = 30, total = 0f;
            for (int i = 0; i < keys.Length; i++) total += SimTheme.TextWidth(acts[i], 13) + 8 + SimTheme.TextWidth(keys[i], 12, true) + 14 + 6 + 14;
            float x = W - 40 - total + 14;
            SimTheme.Fill(new Rect(x - 14, y - 6, total + 14, h + 12), SimTheme.A(SimTheme.PanelBg, 0.6f), 12f);
            for (int i = 0; i < keys.Length; i++)
            {
                float aw = SimTheme.TextWidth(acts[i], 13), kw = SimTheme.TextWidth(keys[i], 12, true) + 14;
                var kr = new Rect(x, y + 4, kw, h - 8);
                SimTheme.Fill(kr, new Color(1, 1, 1, 0.1f), 5f);
                SimTheme.Outline(kr, new Color(1, 1, 1, 0.12f), 1f, 5f);
                SimTheme.Label(kr, keys[i], 12, SimTheme.Text, true, TextAnchor.MiddleCenter);
                x += kw + 6;
                SimTheme.Label(new Rect(x, y, aw + 4, h), acts[i], 13, SimTheme.Sub);
                x += aw + 8 + 14;
            }
        }

        static bool Blink() => ((int)(Time.realtimeSinceStartup * 2f)) % 2 == 0;

        static string FormatTime(double sec)
        {
            int s = (int)sec;
            return $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}";
        }
    }
}
