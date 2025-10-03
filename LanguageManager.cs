using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace D2RSaveMonitor
{
    /// <summary>
    /// 지원하는 언어 / Supported Languages
    /// </summary>
    public enum Language
    {
        Korean,
        English
    }

    /// <summary>
    /// 다국어 관리 클래스 / Language Manager
    /// </summary>
    public static class LanguageManager
    {
        private static Language currentLanguage = Language.English;
        public static event EventHandler LanguageChanged;

        private const string RegistryKey = @"Software\D2RMonitor";

        public static Language CurrentLanguage
        {
            get => currentLanguage;
            set
            {
                if (currentLanguage != value)
                {
                    currentLanguage = value;
                    SaveLanguage();
                    LanguageChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public static void LoadLanguage()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKey))
                {
                    if (key != null)
                    {
                        int langValue = (int)key.GetValue("Language", 1);  // Default to English (1)
                        currentLanguage = (Language)langValue;
                    }
                }
            }
            catch
            {
                currentLanguage = Language.English;  // Default to English on error
            }
        }

        private static void SaveLanguage()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKey))
                {
                    if (key != null)
                    {
                        key.SetValue("Language", (int)currentLanguage);
                    }
                }
            }
            catch
            {
                // Save failed - non-critical
            }
        }

        public static string GetString(string key)
        {
            if (currentLanguage == Language.Korean)
            {
                return Strings_KR.ContainsKey(key) ? Strings_KR[key] : key;
            }
            else
            {
                return Strings_EN.ContainsKey(key) ? Strings_EN[key] : key;
            }
        }

        // 한국어 리소스
        private static readonly Dictionary<string, string> Strings_KR = new Dictionary<string, string>
        {
            // 메인 GUI
            ["MainTitle"] = "디아블로 2 레저렉션 세이브 모니터",
            ["SavePathLabel"] = "세이브 파일 경로:",
            ["BrowseButton"] = "찾아보기...",
            ["OpenSaveFolder"] = "세이브 폴더 열기",
            ["OpenBackupFolder"] = "백업 폴더 열기",
            ["ColumnFileName"] = "캐릭터 파일명",
            ["ColumnCurrentSize"] = "현재 크기",
            ["ColumnPercentage"] = "비율",
            ["ColumnMaxSize"] = "최대 크기",
            ["ColumnStatus"] = "상태",
            ["SelectedFiles"] = "선택된 파일",
            ["BackupSelected"] = "선택 백업",
            ["RestoreBackup"] = "백업 복원",
            ["BackupSettings"] = "백업 설정",
            ["StatusSafe"] = "안전",
            ["StatusWarning"] = "경고",
            ["StatusDanger"] = "위험",

            // 백업 설정
            ["BackupSettingsTitle"] = "백업 설정",
            ["AutoBackupSettings"] = "자동 백업 설정:",
            ["AutoBackupDanger"] = "위험 수준(7500 bytes) 도달 시 자동 백업",
            ["EnableCompression"] = "백업 파일 압축 (디스크 공간 50~70% 절약)",
            ["PeriodicBackupSettings"] = "주기적 백업 설정:",
            ["PeriodicBackupEnable"] = "주기적 자동 백업 활성화",
            ["BackupIntervalMin"] = "백업 주기(분):",
            ["Minutes"] = "분",
            ["BackupRetentionSettings"] = "백업 보관 설정:",
            ["MaxBackupsPerFile"] = "파일당 최대 백업 개수:",
            ["BackupCooldownSec"] = "자동 백업 쿨다운(초):",
            ["Seconds"] = "초",
            ["Count"] = "개",
            ["ResetDefaults"] = "기본값으로 복원",
            ["Cancel"] = "취소",
            ["Save"] = "저장",
            ["SaveSuccess"] = "설정이 저장되었습니다.\n\n참고: 압축 설정은 새로 생성되는 백업부터 적용됩니다.",
            ["SaveSuccessTitle"] = "저장 완료",
            ["ResetConfirm"] = "모든 설정을 기본값으로 복원하시겠습니까?",
            ["ResetConfirmTitle"] = "기본값 복원",

            // 백업 복원
            ["BackupHistoryTitle"] = "백업 목록 및 복원",
            ["CharacterFilter"] = "캐릭터 필터:",
            ["AllCharacters"] = "모든 캐릭터",
            ["Refresh"] = "새로고침",
            ["Close"] = "닫기",
            ["ColumnBackupTime"] = "백업 시간",
            ["ColumnCharacter"] = "캐릭터",
            ["ColumnFileSize"] = "파일 크기",
            ["ColumnCompression"] = "압축",
            ["ColumnTrigger"] = "백업 원인",
            ["ColumnType"] = "유형",
            ["Restore"] = "복원",
            ["Delete"] = "삭제",
            ["BackupInfoPlaceholder"] = "백업을 선택하면 상세 정보가 표시됩니다.",
            ["SelectedBackup"] = "선택된 백업:",
            ["SelectedBackupInfo"] = "선택된 백업:\n캐릭터: {0}\n백업 파일: {1}\n크기: {2} bytes ({3:F1}%)",
            ["SelectedBackupCount"] = "선택된 백업: {0}개",
            ["Compressed"] = "압축",
            ["Automatic"] = "자동",
            ["Manual"] = "수동",
            ["RestoreWithCount"] = "복원 ({0}개)",
            ["DeleteWithCount"] = "삭제 ({0}개)",

            // 메시지
            ["SelectSaveFolder"] = "D2R 세이브 파일 폴더를 선택하세요",
            ["LoadingBackups"] = "백업 목록 로딩 중...",
            ["BackupLoadFailed"] = "백업 목록 로드 실패:\n{0}",
            ["RestoreOnlyOne"] = "복원은 한 번에 하나의 백업만 가능합니다.\n첫 번째 선택된 백업만 복원됩니다.",
            ["Notice"] = "알림",
            ["RestoreConfirm"] = "다음 백업으로 복원하시겠습니까?\n\n캐릭터: {0}\n백업 시간: {1}\n파일 크기: {2} bytes\n\n현재 파일은 자동으로 백업됩니다.",
            ["RestoreConfirmTitle"] = "백업 복원 확인",
            ["RestoreSuccess"] = "복원 완료: {0}\n\n현재 파일은 다음으로 백업되었습니다:\n{1}",
            ["RestoreSuccessTitle"] = "복원 성공",
            ["RestoreFailed"] = "복원 실패: {0}",
            ["RestoreFailedTitle"] = "복원 실패",
            ["Error"] = "오류",
            ["DeleteConfirm"] = "다음 백업을 삭제하시겠습니까?\n\n캐릭터: {0}\n백업 시간: {1}\n\n이 작업은 되돌릴 수 없습니다.",
            ["DeleteMultipleConfirm"] = "{0}개의 백업을 삭제하시겠습니까?\n\n이 작업은 되돌릴 수 없습니다.",
            ["DeleteConfirmTitle"] = "백업 삭제 확인",
            ["DeleteSuccess"] = "백업이 삭제되었습니다.",
            ["DeleteSuccessTitle"] = "삭제 완료",
            ["DeleteFailed"] = "백업 삭제에 실패했습니다.",
            ["DeletePartialSuccess"] = "삭제 완료: {0}개 성공, {1}개 실패",
            ["DeleteMultipleSuccess"] = "삭제 완료: {0}개 백업 삭제",
            ["DeletePartialTitle"] = "삭제 완료 (일부 실패)",
            ["DeleteFailedTitle"] = "삭제 실패",
            ["DeleteFailedWithReason"] = "백업 삭제에 실패했습니다.\n오류: {0}",
            ["RefreshFailed"] = "새로고침 중 오류 발생:\n{0}",

            // 백업 트리거
            ["TriggerDanger"] = "위험 수준 도달",
            ["TriggerPeriodic"] = "주기적 자동",
            ["TriggerManual"] = "수동",
            ["TriggerManualSingle"] = "수동 백업 (단일)",
            ["TriggerManualBulk"] = "수동 백업 (전체)",
            ["TriggerPreRestore"] = "복원 전",
            ["CompressionRatioFormat"] = "압축 {0:F0}%",
            ["CompressionNotAvailable"] = "-",
            ["NoBackups"] = "백업이 없습니다.",
            ["Unknown"] = "알 수 없음",

            // 언어 설정
            ["Language"] = "언어 / Language",
            ["LanguageSettings"] = "언어 설정:",
            ["Korean"] = "한국어",
            ["English"] = "English",

            // Form1 UI 추가 strings
            ["BtnBrowse"] = "찾아보기...",
            ["BtnOpenSaveFolder"] = "📁 세이브",
            ["BtnOpenBackupFolder"] = "📁 백업",
            ["TipOpenSaveFolder"] = "세이브 폴더 열기",
            ["TipOpenBackupFolder"] = "백업 폴더 열기",
            ["ColFileName"] = "캐릭터 파일명",
            ["ColCurrentSize"] = "현재 크기",
            ["ColLimit"] = "제한",
            ["ColPercentage"] = "사용률",
            ["ColProgressBar"] = "진행 상태",
            ["ColStatus"] = "상태",
            ["GrpBackup"] = "백업 관리",
            ["BtnBackupSelected"] = "선택 파일 백업",
            ["BtnBackupAll"] = "전체 백업",
            ["BtnViewBackups"] = "백업 복원...",
            ["BtnBackupSettings"] = "백업 설정...",
            ["LblLastBackup"] = "마지막 백업",
            ["None"] = "없음",
            ["AutoBackup"] = "자동 백업",
            ["AutoBackupDangerOn"] = "위험 수준 도달 시 자동 백업: 사용",
            ["AutoBackupDangerOff"] = "위험 수준 자동 백업: 사용 안 함",
            ["PeriodicBackup"] = "주기적 백업",
            ["PeriodicBackupOnSummary"] = "주기 백업: {0} 간격 | 범위: {1}",
            ["PeriodicBackupOffSummary"] = "주기 백업: 사용 안 함",
            ["BackupLocationInfo"] = "백업 위치: {0}",
            ["PeriodicRangeDanger"] = "위험 구간",
            ["PeriodicRangeWarning"] = "경고 이상",
            ["PeriodicRangeAll"] = "전체 구간",
            ["Enabled"] = "활성화",
            ["Disabled"] = "비활성화",
            ["StatusMonitoring"] = "모니터링 대기 중...",
            ["BackupInitFailed"] = "백업 시스템 초기화 실패",
            ["MonitoringContinues"] = "모니터링은 계속됩니다",
            ["Warning"] = "경고",

            // 추가 오류 메시지
            ["SettingsSaveFailed"] = "설정 저장 실패: {0}",
            ["SettingsSaveError"] = "설정 저장 중 오류가 발생했습니다:\n{0}",
            ["FilterApplyError"] = "필터 적용 중 오류 발생:\n{0}",

            // 주기적 백업 범위 설정 UI
            ["PeriodicScopeGroup"] = "백업 대상 범위",
            ["PeriodicScopeDangerLabel"] = "위험 구간만 (7500 bytes 이상)",
            ["PeriodicScopeWarningLabel"] = "경고 이상 (7000 bytes 이상)",
            ["PeriodicScopeAllLabel"] = "전체 구간 (변경 발생 시마다)",
            ["PeriodicScopeHint"] = "* 주기 백업 활성화 시 선택된 조건을 만족하는 파일을 백업합니다.",

            // Form1 추가 메시지
            ["SavePathNotSet"] = "세이브 경로가 설정되지 않았습니다.",
            ["CannotOpenFolder"] = "폴더를 열 수 없습니다.",
            ["BackupSystemNotInitialized"] = "백업 시스템이 초기화되지 않았습니다.",
            ["CannotOpenBackupFolder"] = "백업 폴더를 열 수 없습니다.",
            ["BackupSuccessWithTime"] = "백업 완료: {0}\n백업 시간: {1}ms",
            ["BackupSuccessTitle"] = "백업 성공",
            ["BackupFailedWithError"] = "백업 실패: {0}\n오류: {1}",
            ["BackupFailedTitle"] = "백업 실패",
            ["ConfirmSelectedBackup"] = "{0}개의 파일을 백업하시겠습니까?",
            ["ConfirmSelectedBackupTitle"] = "선택 파일 백업 확인",
            ["SelectedBackupCompleteTitle"] = "선택 파일 백업 완료",
            ["BackupErrorOccurred"] = "백업 중 오류 발생:\n{0}",
            ["NoFilesToBackup"] = "백업할 파일이 없습니다.",
            ["ConfirmBackupAll"] = "{0}개의 캐릭터 파일을 백업하시겠습니까?",
            ["ConfirmBackupAllTitle"] = "전체 백업 확인",
            ["BackupAllCompleteTitle"] = "전체 백업 완료",
            ["BackupAllError"] = "전체 백업 중 오류 발생:\n{0}",
            ["CannotOpenBackupList"] = "백업 목록을 열 수 없습니다:\n{0}",
            ["CannotOpenSettings"] = "설정을 열 수 없습니다:\n{0}",
            ["CriticalErrorMessage"] = "{0}\n\n오류: {1}\n\n로그 위치: {2}\n\n계속 진행하시겠습니까?",
            ["CriticalErrorTitle"] = "심각한 오류",
            ["BackupCompleteStats"] = "백업 완료: {0}개 성공",
            ["BackupStatsWithFail"] = ", {0}개 실패",
            ["BtnBackupSelectedWithCount"] = "선택 파일 백업 ({0}개)"
        };

        // 영어 리소스
        private static readonly Dictionary<string, string> Strings_EN = new Dictionary<string, string>
        {
            // Main GUI
            ["MainTitle"] = "Diablo 2 Resurrected Save Monitor",
            ["SavePathLabel"] = "Save File Path:",
            ["BrowseButton"] = "Browse...",
            ["OpenSaveFolder"] = "Open Save Folder",
            ["OpenBackupFolder"] = "Open Backup Folder",
            ["ColumnFileName"] = "Character File",
            ["ColumnCurrentSize"] = "Current Size",
            ["ColumnPercentage"] = "Percentage",
            ["ColumnMaxSize"] = "Max Size",
            ["ColumnStatus"] = "Status",
            ["SelectedFiles"] = "Selected Files",
            ["BackupSelected"] = "Backup Selected",
            ["RestoreBackup"] = "Restore Backup",
            ["BackupSettings"] = "Backup Settings",
            ["StatusSafe"] = "Safe",
            ["StatusWarning"] = "Warning",
            ["StatusDanger"] = "Danger",

            // Backup Settings
            ["BackupSettingsTitle"] = "Backup Settings",
            ["AutoBackupSettings"] = "Auto Backup Settings:",
            ["AutoBackupDanger"] = "Auto-backup at danger level (7500 bytes)",
            ["EnableCompression"] = "Enable backup compression (50~70% disk space savings)",
            ["PeriodicBackupSettings"] = "Periodic Backup Settings:",
            ["PeriodicBackupEnable"] = "Enable periodic auto-backup",
            ["BackupIntervalMin"] = "Backup interval (min):",
            ["Minutes"] = "min",
            ["BackupRetentionSettings"] = "Backup Retention Settings:",
            ["MaxBackupsPerFile"] = "Max backups per file:",
            ["BackupCooldownSec"] = "Auto-backup cooldown (sec):",
            ["Seconds"] = "sec",
            ["Count"] = "count",
            ["ResetDefaults"] = "Reset to Defaults",
            ["Cancel"] = "Cancel",
            ["Save"] = "Save",
            ["SaveSuccess"] = "Settings saved successfully.\n\nNote: Compression settings apply to new backups only.",
            ["SaveSuccessTitle"] = "Save Successful",
            ["ResetConfirm"] = "Reset all settings to defaults?",
            ["ResetConfirmTitle"] = "Reset Defaults",

            // Backup History
            ["BackupHistoryTitle"] = "Backup History and Restore",
            ["CharacterFilter"] = "Character Filter:",
            ["AllCharacters"] = "All Characters",
            ["Refresh"] = "Refresh",
            ["Close"] = "Close",
            ["ColumnBackupTime"] = "Backup Time",
            ["ColumnCharacter"] = "Character",
            ["ColumnFileSize"] = "File Size",
            ["ColumnCompression"] = "Compression",
            ["ColumnTrigger"] = "Trigger",
            ["ColumnType"] = "Type",
            ["Restore"] = "Restore",
            ["Delete"] = "Delete",
            ["BackupInfoPlaceholder"] = "Select a backup to view details.",
            ["SelectedBackup"] = "Selected backup:",
            ["SelectedBackupInfo"] = "Selected backup:\nCharacter: {0}\nBackup file: {1}\nSize: {2} bytes ({3:F1}%)",
            ["SelectedBackupCount"] = "Selected backups: {0}",
            ["Compressed"] = "Compressed",
            ["Automatic"] = "Auto",
            ["Manual"] = "Manual",
            ["RestoreWithCount"] = "Restore ({0})",
            ["DeleteWithCount"] = "Delete ({0})",

            // Messages
            ["SelectSaveFolder"] = "Select D2R save file folder",
            ["LoadingBackups"] = "Loading backups...",
            ["BackupLoadFailed"] = "Failed to load backup list:\n{0}",
            ["RestoreOnlyOne"] = "Only one backup can be restored at a time.\nOnly the first selected backup will be restored.",
            ["Notice"] = "Notice",
            ["RestoreConfirm"] = "Restore from this backup?\n\nCharacter: {0}\nBackup time: {1}\nFile size: {2} bytes\n\nCurrent file will be backed up automatically.",
            ["RestoreConfirmTitle"] = "Confirm Restore",
            ["RestoreSuccess"] = "Restore complete: {0}\n\nCurrent file backed up as:\n{1}",
            ["RestoreSuccessTitle"] = "Restore Successful",
            ["RestoreFailed"] = "Restore failed: {0}",
            ["RestoreFailedTitle"] = "Restore Failed",
            ["Error"] = "Error",
            ["DeleteConfirm"] = "Delete this backup?\n\nCharacter: {0}\nBackup time: {1}\n\nThis action cannot be undone.",
            ["DeleteMultipleConfirm"] = "Delete {0} backups?\n\nThis action cannot be undone.",
            ["DeleteConfirmTitle"] = "Confirm Delete",
            ["DeleteSuccess"] = "Backup deleted successfully.",
            ["DeleteSuccessTitle"] = "Delete Successful",
            ["DeleteFailed"] = "Failed to delete backup.",
            ["DeletePartialSuccess"] = "Delete complete: {0} succeeded, {1} failed",
            ["DeleteMultipleSuccess"] = "Delete complete: {0} backups removed",
            ["DeletePartialTitle"] = "Delete Complete (Partial)",
            ["DeleteFailedTitle"] = "Delete Failed",
            ["DeleteFailedWithReason"] = "Failed to delete backup.\nError: {0}",
            ["RefreshFailed"] = "Error while refreshing:\n{0}",

            // Backup Triggers
            ["TriggerDanger"] = "Danger Level",
            ["TriggerPeriodic"] = "Periodic Auto",
            ["TriggerManual"] = "Manual",
            ["TriggerManualSingle"] = "Manual backup (single)",
            ["TriggerManualBulk"] = "Manual backup (bulk)",
            ["TriggerPreRestore"] = "Pre-Restore",
            ["CompressionRatioFormat"] = "Compressed {0:F0}%",
            ["CompressionNotAvailable"] = "-",
            ["NoBackups"] = "No backups found.",
            ["Unknown"] = "Unknown",

            // Language Settings
            ["Language"] = "Language / 언어",
            ["LanguageSettings"] = "Language Settings:",
            ["Korean"] = "한국어",
            ["English"] = "English",

            // Form1 UI additional strings
            ["BtnBrowse"] = "Browse...",
            ["BtnOpenSaveFolder"] = "📁 Save",
            ["BtnOpenBackupFolder"] = "📁 Backup",
            ["TipOpenSaveFolder"] = "Open save folder",
            ["TipOpenBackupFolder"] = "Open backup folder",
            ["ColFileName"] = "Character File",
            ["ColCurrentSize"] = "Current Size",
            ["ColLimit"] = "Limit",
            ["ColPercentage"] = "Usage",
            ["ColProgressBar"] = "Progress",
            ["ColStatus"] = "Status",
            ["GrpBackup"] = "Backup Management",
            ["BtnBackupSelected"] = "Backup Selected",
            ["BtnBackupAll"] = "Backup All",
            ["BtnViewBackups"] = "Restore Backup...",
            ["BtnBackupSettings"] = "Backup Settings...",
            ["LblLastBackup"] = "Last backup",
            ["None"] = "None",
            ["AutoBackup"] = "Auto backup",
            ["AutoBackupDangerOn"] = "Auto-backup at danger threshold: Enabled",
            ["AutoBackupDangerOff"] = "Auto-backup at danger threshold: Disabled",
            ["PeriodicBackup"] = "Periodic backup",
            ["PeriodicBackupOnSummary"] = "Periodic backup: every {0} | Scope: {1}",
            ["PeriodicBackupOffSummary"] = "Periodic backup: Disabled",
            ["BackupLocationInfo"] = "Backup location: {0}",
            ["PeriodicRangeDanger"] = "Danger only",
            ["PeriodicRangeWarning"] = "Warning or higher",
            ["PeriodicRangeAll"] = "Entire range",
            ["Enabled"] = "Enabled",
            ["Disabled"] = "Disabled",
            ["StatusMonitoring"] = "Monitoring...",
            ["BackupInitFailed"] = "Backup system initialization failed",
            ["MonitoringContinues"] = "Monitoring will continue",
            ["Warning"] = "Warning",

            // Additional error messages
            ["SettingsSaveFailed"] = "Settings save failed: {0}",
            ["SettingsSaveError"] = "An error occurred while saving settings:\n{0}",
            ["FilterApplyError"] = "Error applying filter:\n{0}",

            // Periodic backup scope settings UI
            ["PeriodicScopeGroup"] = "Backup Scope",
            ["PeriodicScopeDangerLabel"] = "Danger zone only (7500 bytes or more)",
            ["PeriodicScopeWarningLabel"] = "Warning or higher (7000 bytes or more)",
            ["PeriodicScopeAllLabel"] = "Entire range (on every change)",
            ["PeriodicScopeHint"] = "* When periodic backup is enabled, files matching the selected condition will be backed up.",

            // Form1 additional messages
            ["SavePathNotSet"] = "Save path is not set.",
            ["CannotOpenFolder"] = "Cannot open folder.",
            ["BackupSystemNotInitialized"] = "Backup system is not initialized.",
            ["CannotOpenBackupFolder"] = "Cannot open backup folder.",
            ["BackupSuccessWithTime"] = "Backup complete: {0}\nBackup time: {1}ms",
            ["BackupSuccessTitle"] = "Backup Successful",
            ["BackupFailedWithError"] = "Backup failed: {0}\nError: {1}",
            ["BackupFailedTitle"] = "Backup Failed",
            ["ConfirmSelectedBackup"] = "Backup {0} file(s)?",
            ["ConfirmSelectedBackupTitle"] = "Confirm Selected Backup",
            ["SelectedBackupCompleteTitle"] = "Selected Backup Complete",
            ["BackupErrorOccurred"] = "An error occurred during backup:\n{0}",
            ["NoFilesToBackup"] = "No files to backup.",
            ["ConfirmBackupAll"] = "Backup {0} character file(s)?",
            ["ConfirmBackupAllTitle"] = "Confirm Backup All",
            ["BackupAllCompleteTitle"] = "Backup All Complete",
            ["BackupAllError"] = "An error occurred during backup all:\n{0}",
            ["CannotOpenBackupList"] = "Cannot open backup list:\n{0}",
            ["CannotOpenSettings"] = "Cannot open settings:\n{0}",
            ["CriticalErrorMessage"] = "{0}\n\nError: {1}\n\nLog location: {2}\n\nContinue anyway?",
            ["CriticalErrorTitle"] = "Critical Error",
            ["BackupCompleteStats"] = "Backup complete: {0} succeeded",
            ["BackupStatsWithFail"] = ", {0} failed",
            ["BtnBackupSelectedWithCount"] = "Backup Selected ({0})"
        };
    }
}
