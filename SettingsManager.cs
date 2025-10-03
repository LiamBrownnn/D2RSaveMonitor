using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace D2RSaveMonitor
{
    /// <summary>
    /// JSON 기반 설정 관리자 / JSON-based settings manager
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "D2RSaveMonitor"
        );
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
        private const string BackupRegistryKey = @"Software\D2RMonitor\Backup";

        /// <summary>
        /// 설정 로드 (JSON 우선, 없으면 레지스트리에서 마이그레이션) / Load settings (JSON first, migrate from registry if needed)
        /// </summary>
        public static BackupSettings LoadSettings()
        {
            // JSON 파일이 있으면 JSON에서 로드
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    return LoadFromJson();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"JSON 로드 실패, 레지스트리 시도: {ex.Message}");
                    // JSON 로드 실패 시 레지스트리로 폴백
                }
            }

            // JSON 파일이 없거나 로드 실패 시 레지스트리에서 로드
            BackupSettings settings = LoadFromRegistry();

            // 레지스트리에서 로드 성공 시 JSON으로 자동 마이그레이션
            if (settings != null && HasRegistrySettings())
            {
                try
                {
                    SaveToJson(settings);
                    System.Diagnostics.Debug.WriteLine("레지스트리에서 JSON으로 마이그레이션 완료");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"JSON 마이그레이션 실패: {ex.Message}");
                }
            }

            return settings ?? new BackupSettings();
        }

        /// <summary>
        /// 설정 저장 (JSON으로만 저장) / Save settings (to JSON only)
        /// </summary>
        public static void SaveSettings(BackupSettings settings)
        {
            try
            {
                SaveToJson(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 저장 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// JSON 파일에서 로드 / Load from JSON file
        /// </summary>
        private static BackupSettings LoadFromJson()
        {
            string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
            return ParseJson(json);
        }

        /// <summary>
        /// JSON 파일에 저장 / Save to JSON file
        /// </summary>
        private static void SaveToJson(BackupSettings settings)
        {
            // 디렉토리가 없으면 생성
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            string json = SerializeToJson(settings);
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// BackupSettings를 JSON 문자열로 직렬화 / Serialize BackupSettings to JSON string
        /// </summary>
        private static string SerializeToJson(BackupSettings settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"AutoBackupOnDanger\": {settings.AutoBackupOnDanger.ToString().ToLower()},");
            sb.AppendLine($"  \"PeriodicBackupEnabled\": {settings.PeriodicBackupEnabled.ToString().ToLower()},");
            sb.AppendLine($"  \"PeriodicScope\": {(int)settings.PeriodicScope},");
            sb.AppendLine($"  \"PeriodicIntervalMinutes\": {settings.PeriodicIntervalMinutes},");
            sb.AppendLine($"  \"MaxBackupsPerFile\": {settings.MaxBackupsPerFile},");
            sb.AppendLine($"  \"BackupCooldownSeconds\": {settings.BackupCooldownSeconds},");
            sb.AppendLine($"  \"CustomBackupPath\": {EscapeJsonString(settings.CustomBackupPath ?? "")},");
            sb.AppendLine($"  \"EnableCompression\": {settings.EnableCompression.ToString().ToLower()}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// JSON 문자열을 BackupSettings로 역직렬화 / Deserialize JSON string to BackupSettings
        /// </summary>
        private static BackupSettings ParseJson(string json)
        {
            var settings = new BackupSettings();

            // 간단한 JSON 파싱 (속성이 적어서 정규식이나 복잡한 파서 불필요)
            string[] lines = json.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim().TrimEnd(',');
                if (trimmed.StartsWith("\"AutoBackupOnDanger\":"))
                {
                    settings.AutoBackupOnDanger = ExtractBoolValue(trimmed);
                }
                else if (trimmed.StartsWith("\"PeriodicBackupEnabled\":"))
                {
                    settings.PeriodicBackupEnabled = ExtractBoolValue(trimmed);
                }
                else if (trimmed.StartsWith("\"PeriodicScope\":"))
                {
                    settings.PeriodicScope = (PeriodicBackupScope)ExtractIntValue(trimmed);
                }
                else if (trimmed.StartsWith("\"PeriodicIntervalMinutes\":"))
                {
                    settings.PeriodicIntervalMinutes = ExtractIntValue(trimmed);
                }
                else if (trimmed.StartsWith("\"MaxBackupsPerFile\":"))
                {
                    settings.MaxBackupsPerFile = ExtractIntValue(trimmed);
                }
                else if (trimmed.StartsWith("\"BackupCooldownSeconds\":"))
                {
                    settings.BackupCooldownSeconds = ExtractIntValue(trimmed);
                }
                else if (trimmed.StartsWith("\"CustomBackupPath\":"))
                {
                    settings.CustomBackupPath = ExtractStringValue(trimmed);
                }
                else if (trimmed.StartsWith("\"EnableCompression\":"))
                {
                    settings.EnableCompression = ExtractBoolValue(trimmed);
                }
            }

            return settings;
        }

        /// <summary>
        /// JSON 라인에서 bool 값 추출 / Extract bool value from JSON line
        /// </summary>
        private static bool ExtractBoolValue(string line)
        {
            int colonIndex = line.IndexOf(':');
            if (colonIndex < 0) return false;

            string value = line.Substring(colonIndex + 1).Trim().TrimEnd(',').ToLower();
            return value == "true";
        }

        /// <summary>
        /// JSON 라인에서 int 값 추출 / Extract int value from JSON line
        /// </summary>
        private static int ExtractIntValue(string line)
        {
            int colonIndex = line.IndexOf(':');
            if (colonIndex < 0) return 0;

            string value = line.Substring(colonIndex + 1).Trim().TrimEnd(',');
            int result;
            return int.TryParse(value, out result) ? result : 0;
        }

        /// <summary>
        /// JSON 라인에서 string 값 추출 / Extract string value from JSON line
        /// </summary>
        private static string ExtractStringValue(string line)
        {
            int colonIndex = line.IndexOf(':');
            if (colonIndex < 0) return "";

            string value = line.Substring(colonIndex + 1).Trim().TrimEnd(',').Trim('"');
            return UnescapeJsonString(value);
        }

        /// <summary>
        /// JSON 문자열 이스케이프 / Escape JSON string
        /// </summary>
        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "\"\"";

            return "\"" + str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                + "\"";
        }

        /// <summary>
        /// JSON 문자열 언이스케이프 / Unescape JSON string
        /// </summary>
        private static string UnescapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";

            return str
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");
        }

        /// <summary>
        /// 레지스트리에서 로드 (하위 호환성) / Load from registry (backward compatibility)
        /// </summary>
        private static BackupSettings LoadFromRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(BackupRegistryKey))
                {
                    if (key == null) return new BackupSettings();

                    return new BackupSettings
                    {
                        AutoBackupOnDanger = (int)key.GetValue("AutoBackupOnDanger", 1) == 1,
                        PeriodicBackupEnabled = (int)key.GetValue("PeriodicEnabled", 0) == 1,
                        PeriodicIntervalMinutes = (int)key.GetValue("PeriodicInterval", 30),
                        MaxBackupsPerFile = (int)key.GetValue("MaxBackups", 10),
                        BackupCooldownSeconds = (int)key.GetValue("CooldownSeconds", 60),
                        CustomBackupPath = (string)key.GetValue("CustomPath", ""),
                        EnableCompression = (int)key.GetValue("EnableCompression", 1) == 1,
                        PeriodicScope = LoadPeriodicScopeFromRegistry(key)
                    };
                }
            }
            catch
            {
                return new BackupSettings();
            }
        }

        /// <summary>
        /// 레지스트리에서 PeriodicScope 로드 / Load PeriodicScope from registry
        /// </summary>
        private static PeriodicBackupScope LoadPeriodicScopeFromRegistry(RegistryKey key)
        {
            int scopeValue = (int)key.GetValue("PeriodicScope", -1);
            if (Enum.IsDefined(typeof(PeriodicBackupScope), scopeValue))
            {
                return (PeriodicBackupScope)scopeValue;
            }

            // 이전 버전 호환성: PeriodicIncludeSafeZone 키 체크
            bool includeSafeZone = (int)key.GetValue("PeriodicIncludeSafeZone", 0) == 1;
            return includeSafeZone ? PeriodicBackupScope.EntireRange : PeriodicBackupScope.WarningOrAbove;
        }

        /// <summary>
        /// 레지스트리 설정이 존재하는지 확인 / Check if registry settings exist
        /// </summary>
        private static bool HasRegistrySettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(BackupRegistryKey))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 설정 파일 경로 가져오기 / Get settings file path
        /// </summary>
        public static string GetSettingsFilePath()
        {
            return SettingsFilePath;
        }
    }
}
