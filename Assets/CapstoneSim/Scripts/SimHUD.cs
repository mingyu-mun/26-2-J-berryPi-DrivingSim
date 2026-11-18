// SimHUD.cs
// IMGUI(OnGUI) 기반 간단 계기판/상태 표시. 별도 UI 에셋이나 TextMeshPro 설정 없이 바로 동작한다.
// 동기화 마커(M)를 누르면 화면 우상단에 흰 사각형이 0.3초 깜빡인다
// → 운전 장면을 영상으로 함께 찍는 경우, 영상과 로그를 맞추는 기준점으로 쓸 수 있다.
using UnityEngine;

namespace CapstoneSim
{
    public class SimHUD : MonoBehaviour
    {
        public CarController car;
        public DrivingDataLogger logger;
        public bool show = true;
        public bool showHelp = true;

        GUIStyle _big, _mid, _small, _rec;
        Texture2D _white, _wheelIcon;

        void Update()
        {
            if (SimInput.HudTogglePressed()) show = !show;
        }

        void Init()
        {
            if (_big != null) return;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold };
            _mid = new GUIStyle(GUI.skin.label) { fontSize = 20 };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            _rec = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _rec.normal.textColor = new Color(1f, 0.25f, 0.25f);
            _white = Texture2D.whiteTexture;
            _wheelIcon = ProceduralTextures.SteeringIcon();
        }

        void OnGUI()
        {
            Init();

            // 동기화 마커 플래시 (HUD를 꺼도 표시) — 화면 위 가운데
            if (logger && Time.realtimeSinceStartup - logger.LastMarkerRealtime < 0.3f)
                GUI.DrawTexture(new Rect(Screen.width * 0.5f - 60, 4, 120, 56), _white);

            if (!show || !car) return;

            var box = new Rect(10, Screen.height - 150, 360, 140);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, _white);
            GUI.color = Color.white;

            // 핸들 아이콘 (계기판 오른쪽) — 실제 핸들 각도만큼 회전
            var wheelRect = new Rect(box.xMax + 10, Screen.height - 140, 110, 110);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(car.SteeringWheelDeg, wheelRect.center);
            GUI.DrawTexture(wheelRect, _wheelIcon);
            GUI.matrix = m;
            string src = car.ActiveSteerSource == SteerMode.Mouse ? "마우스" : car.ActiveSteerSource == SteerMode.Gamepad ? "패드" : "키보드";
            string mode = car.steerMode == SteerMode.Auto ? $"자동({src})" : src;
            GUI.Label(new Rect(wheelRect.x + 4, wheelRect.yMax - 2, 160, 24), $"{car.SteeringWheelDeg:+0;-0;0}°  {mode}", _small);
            if (car.ActiveSteerSource == SteerMode.Mouse)
            {
                // 마우스 조향 범위 가이드: 화면 하단 중앙의 가로 막대 + 중앙 눈금
                float half = Screen.width * car.mouseRange;
                GUI.color = new Color(1f, 1f, 1f, 0.25f);
                GUI.DrawTexture(new Rect(Screen.width * 0.5f - half, Screen.height - 12, half * 2f, 3), _white);
                GUI.DrawTexture(new Rect(Screen.width * 0.5f - 1, Screen.height - 20, 2, 14), _white);
                GUI.color = Color.white;
            }

            string gear = car.Gear == -1 ? "R" : car.Gear == 0 ? "N" : car.Gear.ToString();
            GUI.Label(new Rect(20, Screen.height - 150, 220, 60), $"{Mathf.Abs(car.SpeedKmh):F0} km/h", _big);
            GUI.Label(new Rect(250, Screen.height - 140, 120, 40), $"[{gear}]", _mid);
            GUI.Label(new Rect(20, Screen.height - 90, 350, 30), $"RPM {car.EngineRpm:F0}", _mid);

            if (logger)
            {
                var ts = logger.LastSample;
                string sig = car.TurnSignal < 0 ? "◀" : car.TurnSignal > 0 ? "▶" : "-";
                GUI.Label(new Rect(20, Screen.height - 60, 350, 30),
                    $"차로 {ts.lane}  편차 {ts.laneOffset:+0.00;-0.00} m  깜빡이 {sig}  랩 {logger.Lap}", _mid);

                if (logger.IsRecording)
                {
                    bool blink = ((int)(Time.realtimeSinceStartup * 2f)) % 2 == 0;
                    GUI.Label(new Rect(10, 10, 700, 30),
                        $"{(blink ? "●" : "○")} REC  {FormatTime(logger.ElapsedSec)}  샘플 {logger.SampleCount}  마커 {logger.MarkerCount}  [{logger.participantId}]", _rec);
                    GUI.Label(new Rect(10, 40, 1200, 24), logger.CurrentFile, _small);
                }
                else
                {
                    GUI.Label(new Rect(10, 10, 700, 30), "대기 중 — L 키를 누르면 기록 시작", _mid);
                }
            }

            if (showHelp)
            {
                string help =
                    "W/↑ 가속   S/↓/Space 브레이크(정지 후 계속 누르면 후진)   A·D/←→ 조향\n" +
                    "L 기록   M 마커   Q/E 깜빡이   R 차선 복귀   C 시점   T 조향 방식(자동/키보드/마우스/패드)   H HUD";
                var r = new Rect(Screen.width - 790, Screen.height - 54, 780, 50);
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(r, _white);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, r.height), help, _small);
            }
        }

        static string FormatTime(double sec)
        {
            int s = (int)sec;
            return $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}";
        }
    }
}
