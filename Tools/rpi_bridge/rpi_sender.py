"""
rpi_sender.py — 라즈베리파이 센서 허브 → 시뮬레이터 실측 모드 (UDP) 송신기 / 에뮬레이터

두 가지 용도
  1) 라즈베리파이 없이 PC 에서 실측 모드 경로 점검: 기록(세션 폴더)을 실시간 속도로 UDP 전송
  2) 실제 라즈베리파이 송신 코드의 출발점: 송신 루프의 표본 자르기(ecg[ie0:ie1]) 를 센서 버퍼 읽기로 바꾸면 그대로 쓸 수 있음

패킷 (JSON 한 개 = UDP 데이터그램 하나, 기본 10 Hz) — docs/06_실측모드.md 와 Unity LivePacket 이 같은 형식
  {"type": "bio", "device": "rpi-01", "seq": 123, "t_dev": 12.30,
   "fs_ecg": 435.33, "fs_ppg": 100.66, "ecg": [...원신호...], "ppg": [...원신호...]}
  · seq   : 1씩 증가 → 수신 측이 손실을 셈
  · t_dev : 장치 시계 기준 이 묶음 첫 표본 시각 [s] (도착 시각이 아니라 측정 시각으로 시간축을 맞추기 위함)
  · 표본은 각 채널의 원래 주기 그대로, 필터·전처리 없이 보낸다 (전처리는 PC 의 추론 서버가 저장소 코드로 함)

사용 예
  python Tools/rpi_bridge/rpi_sender.py --session Assets/StreamingAssets/Replays/rec_20261118 --start 900
  python Tools/rpi_bridge/rpi_sender.py --session <폴더> --host 192.168.0.10 --loss 0.02 --jitter 15   (Wi-Fi 상황 흉내)
  python Tools/rpi_bridge/rpi_sender.py --synthetic                                                   (기록 없이 합성 파형)
표준 라이브러리만 사용 (라즈베리파이 기본 Python 에서 바로 동작).
"""
import argparse
import csv
import json
import math
import random
import socket
import time
from pathlib import Path


def load_session(d: Path):
    meta = json.loads((d / "meta.json").read_text(encoding="utf-8"))
    def col(name):
        with open(d / name, encoding="utf-8") as f:
            r = csv.reader(f)
            next(r)
            return [float(row[0]) if row and row[0] else float("nan") for row in r]
    return meta["fs"]["ecg"], meta["fs"]["ppg"], col("ecg.csv"), col("ppg.csv")


def synthetic(minutes=15, fs_e=250.0, fs_p=100.0):
    """점검용 합성 파형 (HR 72 bpm 근처). 판정 의미 없음."""
    n_e, n_p = int(minutes * 60 * fs_e), int(minutes * 60 * fs_p)
    ecg, ppg = [0.0] * n_e, [0.0] * n_p
    t, beats = 0.0, []
    while t < minutes * 60:
        beats.append(t)
        t += 60 / 72 + random.gauss(0, 0.03)
    for b in beats:
        i = int(b * fs_e)
        for k in range(-5, 6):
            if 0 <= i + k < n_e:
                ecg[i + k] += 1.0 * math.exp(-(k / 2.0) ** 2)
        j = int((b + 0.25) * fs_p)
        for k in range(0, 40):
            if 0 <= j + k < n_p:
                ppg[j + k] += math.exp(-((k - 8) / 6.0) ** 2) + 0.4 * math.exp(-((k - 22) / 7.0) ** 2)
    return fs_e, fs_p, ecg, ppg


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--session", help="세션 폴더 (meta.json + ecg.csv + ppg.csv)")
    ap.add_argument("--synthetic", action="store_true")
    ap.add_argument("--host", default="127.0.0.1", help="시뮬레이터 PC 주소")
    ap.add_argument("--port", type=int, default=5010)
    ap.add_argument("--rate", type=float, default=10.0, help="초당 패킷 수")
    ap.add_argument("--start", type=float, default=0.0, help="기록의 몇 초부터 보낼지")
    ap.add_argument("--device", default="rpi-emu")
    ap.add_argument("--loss", type=float, default=0.0, help="일부러 버릴 패킷 비율 (0~1)")
    ap.add_argument("--jitter", type=float, default=0.0, help="송신 시각 흔들림 표준편차 [ms]")
    a = ap.parse_args()
    if a.synthetic:
        fs_e, fs_p, ecg, ppg = synthetic()
    elif a.session:
        fs_e, fs_p, ecg, ppg = load_session(Path(a.session))
    else:
        ap.error("--session 또는 --synthetic 필요")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dt = 1.0 / a.rate
    t0 = time.perf_counter()
    seq, sent, lost = 0, 0, 0
    pos = a.start
    print(flush=True) or print(f"→ udp://{a.host}:{a.port}  {a.rate:.0f} 패킷/s  ECG {fs_e:.1f} Hz · PPG {fs_p:.1f} Hz  시작 {a.start:.0f}s  (Ctrl+C 종료)", flush=True)
    try:
        while True:
            ie0, ie1 = int(pos * fs_e), int((pos + dt) * fs_e)
            ip0, ip1 = int(pos * fs_p), int((pos + dt) * fs_p)
            if ie1 > len(ecg) or ip1 > len(ppg):
                print("기록 끝")
                break
            pkt = {"type": "bio", "device": a.device, "seq": seq, "t_dev": round(pos - a.start, 4),
                   "fs_ecg": fs_e, "fs_ppg": fs_p,
                   "ecg": [round(v, 3) for v in ecg[ie0:ie1]], "ppg": [round(v, 3) for v in ppg[ip0:ip1]]}
            if random.random() >= a.loss:
                sock.sendto(json.dumps(pkt, separators=(",", ":")).encode(), (a.host, a.port))
                sent += 1
            else:
                lost += 1
            seq += 1
            pos += dt
            target = t0 + seq * dt + (random.gauss(0, a.jitter / 1000.0) if a.jitter > 0 else 0.0)
            time.sleep(max(0.0, target - time.perf_counter()))
            if seq % int(a.rate * 10) == 0:
                print(f"  {pos - a.start:7.0f}s  보냄 {sent}  버림 {lost}", flush=True)
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
