리플레이 · 기준(baseline) 기록 폴더 (메인 메뉴 > 리플레이 모드 목록에 표시)

<이름>/meta.json   fs.ecg, fs.ppg (필수), duration_s, role("replay" | "baseline")
<이름>/ecg.csv     value 열 하나 — ECG 원신호
<이름>/ppg.csv     value 열 하나 — PPG 원신호
<이름>/labels.csv  (선택) start_s,end_s,label — 화면 참고용, 모델 입력 아님

만들기 (저장소 루트에서):
  python Tools/convert_recording.py "<측정 zip>" --out Assets/StreamingAssets/Replays/rec_20261118
  python Tools/convert_recording.py "<측정 zip>" --out Assets/StreamingAssets/Replays/rec_20261118_baseline --role baseline --end 480

이 폴더의 기록은 개인 생체 데이터이므로 git 에 올라가지 않는다 (.gitignore). 팀 공유 드라이브로 주고받을 것.
