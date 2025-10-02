# D2R Save Monitor

A save file size monitoring and automatic backup tool for Diablo 2 Resurrected

---

## Key Features

### Real-time Monitoring
- Real-time save file size tracking
- Visual danger/warning level indicators
- File capacity and percentage display

### Automatic Backup
- Auto-backup at danger level (7500 bytes)
- Periodic auto-backup (5~240 min intervals)
- Configurable periodic scope (danger-only, warning threshold, or safe zone)
- Backup compression (50~70% disk space savings)

### Backup Management
- View and restore backup history
- Filter by character
- Multi-select and bulk delete
- Display compression ratio

---

## Installation & Usage

### Requirements
- Windows 10/11
- .NET Framework 4.7.2 or higher
- Diablo 2 Resurrected

### Installation
1. Download the latest version from [Releases](https://github.com/LiamBrownnn/D2RSaveMonitor/releases)
2. Extract the ZIP file
3. Run `D2RSaveMonitor.exe`

### How to Use
1. Monitoring starts automatically on launch
2. Click "Browse" to select save folder (restarts immediately on path change)
3. Backup management
   - Auto-backup: Configure conditions in settings
   - Manual backup: Select files → "Backup Selected"
   - Restore: "Restore Backup" → Select backup → "Restore"

---

## Backup Settings

- Auto-backup at danger level (7500 bytes)
- Periodic scope selection (danger-only / warning threshold / entire range)
- Backup compression (50~70% space savings)
- Periodic backup (5~240 min)
- Max backups per file (1~100)
- Auto-backup cooldown (10~300 sec)

---

## Important Information

### D2R Save File Size Limit
- Maximum size: 8192 bytes
- Danger level: 7500 bytes (91.5%)
- Warning level: 7000 bytes (85.4%)

### Backup File Location
- Default path: `[Save folder]/Backups`
- Compressed: `Character.d2s_20251002_145530.d2s.zip`
- Uncompressed: `Character.d2s_20251002_145530.d2s`

---

## Troubleshooting

**Monitoring not working**
- Verify save folder path
- Run as administrator

**Backup restore fails**
- Try after closing the game
- Verify backup file exists

**Compression backup error**
- Verify .NET Framework 4.7.2+ is installed
- Check backup folder write permissions

---

## License

MIT License

---

## Contact

Bug reports and feature requests: [GitHub Issues](https://github.com/LiamBrownnn/D2RSaveMonitor/issues)

---

# D2R Save Monitor

Diablo 2 Resurrected 세이브 파일 크기 모니터링 및 자동 백업 도구

---

## 주요 기능

### 실시간 모니터링
- 세이브 파일 크기 실시간 추적
- 위험/경고 수준 시각적 표시
- 파일별 용량 및 퍼센티지 표시

### 자동 백업
- 위험 수준(7500 bytes) 도달 시 자동 백업
- 주기적 자동 백업 (5~240분 간격)
- 주기 백업 범위 선택 (위험만 / 경고 이상 / 전체 구간)
- 백업 파일 압축 (디스크 공간 50~70% 절약)

### 백업 관리
- 백업 히스토리 조회 및 복원
- 캐릭터별 필터링
- 다중 선택 및 일괄 삭제
- 압축률 표시

---

## 설치 및 사용

### 요구사항
- Windows 10/11
- .NET Framework 4.7.2 이상
- Diablo 2 Resurrected

### 설치
1. [Releases](https://github.com/LiamBrownnn/D2RSaveMonitor/releases)에서 최신 버전 다운로드
2. ZIP 압축 해제
3. `D2RSaveMonitor.exe` 실행

### 사용법
1. 프로그램 실행 시 자동으로 모니터링 시작
2. "찾아보기"로 세이브 폴더 선택 (경로 변경 시 즉시 재시작)
3. 백업 관리
   - 자동 백업: 백업 설정에서 조건 지정
   - 수동 백업: 파일 선택 후 "선택 백업"
   - 복원: "백업 복원" - 백업 선택 - "복원"

---

## 백업 설정

- 위험 수준 자동 백업 (7500 bytes)
- 주기 백업 범위 설정 (위험만 / 경고 이상 / 전체 구간)
- 백업 파일 압축 (50~70% 공간 절약)
- 주기적 백업 (5~240분)
- 파일당 최대 백업 개수 (1~100개)
- 자동 백업 쿨다운 (10~300초)

---

## 중요 정보

### D2R 세이브 파일 크기 제한
- 최대 크기: 8192 bytes
- 위험 수준: 7500 bytes (91.5%)
- 경고 수준: 7000 bytes (85.4%)

### 백업 파일 위치
- 기본 경로: `[세이브 폴더]/Backups`
- 압축: `캐릭터.d2s_20251002_145530.d2s.zip`
- 비압축: `캐릭터.d2s_20251002_145530.d2s`

---

## 문제 해결

**모니터링 작동 안 함**
- 세이브 폴더 경로 확인
- 관리자 권한으로 실행

**백업 복원 실패**
- 게임 종료 후 시도
- 백업 파일 존재 확인

**압축 백업 오류**
- .NET Framework 4.7.2 이상 설치 확인
- 백업 폴더 쓰기 권한 확인

---

## 라이선스

MIT License

---

## 문의

버그 리포트 및 기능 제안: [GitHub Issues](https://github.com/LiamBrownnn/D2RSaveMonitor/issues)
