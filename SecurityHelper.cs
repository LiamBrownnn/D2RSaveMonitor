using System;
using System.IO;
using System.Linq;

namespace D2RSaveMonitor
{
    /// <summary>
    /// 보안 관련 유틸리티 함수 제공
    /// </summary>
    public static class SecurityHelper
    {
        /// <summary>
        /// 허용된 드라이브 목록 (Windows 시스템 드라이브)
        /// </summary>
        private static readonly string[] AllowedDriveLetters = { "C", "D", "E", "F", "G", "H" };

        /// <summary>
        /// 금지된 경로 패턴 (시스템 디렉토리)
        /// </summary>
        private static readonly string[] ForbiddenPaths =
        {
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData\Microsoft",
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)
        };

        /// <summary>
        /// 디렉토리 경로의 유효성 및 보안성을 검증
        /// </summary>
        /// <param name="path">검증할 경로</param>
        /// <param name="errorMessage">오류 메시지 (검증 실패 시)</param>
        /// <returns>경로가 안전하면 true, 그렇지 않으면 false</returns>
        public static bool IsValidDirectoryPath(string path, out string errorMessage)
        {
            errorMessage = null;

            // null 또는 빈 문자열 체크
            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "경로가 비어있습니다.";
                return false;
            }

            try
            {
                // 절대 경로로 변환 (상대 경로, ../ 등 정규화)
                string fullPath = Path.GetFullPath(path);

                // 경로 길이 제한 (Windows MAX_PATH)
                if (fullPath.Length > 248) // MAX_PATH - 예비 공간
                {
                    errorMessage = "경로가 너무 깁니다.";
                    return false;
                }

                // 드라이브 문자 검증
                if (!IsValidDriveLetter(fullPath))
                {
                    errorMessage = "허용되지 않은 드라이브입니다.";
                    return false;
                }

                // 금지된 시스템 디렉토리 체크
                if (IsForbiddenPath(fullPath))
                {
                    errorMessage = "시스템 디렉토리는 접근할 수 없습니다.";
                    return false;
                }

                // UNC 경로 차단 (\\server\share)
                if (fullPath.StartsWith(@"\\"))
                {
                    errorMessage = "네트워크 경로는 지원되지 않습니다.";
                    return false;
                }

                // 위험한 문자 검증
                char[] invalidChars = Path.GetInvalidPathChars();
                if (fullPath.Any(c => invalidChars.Contains(c)))
                {
                    errorMessage = "경로에 유효하지 않은 문자가 포함되어 있습니다.";
                    return false;
                }

                return true;
            }
            catch (ArgumentException)
            {
                errorMessage = "경로 형식이 올바르지 않습니다.";
                return false;
            }
            catch (NotSupportedException)
            {
                errorMessage = "지원되지 않는 경로 형식입니다.";
                return false;
            }
            catch (PathTooLongException)
            {
                errorMessage = "경로가 너무 깁니다.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"경로 검증 중 오류 발생: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 파일 경로의 유효성 및 보안성을 검증
        /// </summary>
        /// <param name="filePath">검증할 파일 경로</param>
        /// <param name="errorMessage">오류 메시지 (검증 실패 시)</param>
        /// <returns>파일 경로가 안전하면 true, 그렇지 않으면 false</returns>
        public static bool IsValidFilePath(string filePath, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "파일 경로가 비어있습니다.";
                return false;
            }

            try
            {
                // 디렉토리 경로 검증
                string directory = Path.GetDirectoryName(filePath);
                if (!IsValidDirectoryPath(directory, out errorMessage))
                {
                    return false;
                }

                // 파일명 검증
                string fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    errorMessage = "파일명이 비어있습니다.";
                    return false;
                }

                // 위험한 파일명 문자 검증
                char[] invalidFileChars = Path.GetInvalidFileNameChars();
                if (fileName.Any(c => invalidFileChars.Contains(c)))
                {
                    errorMessage = "파일명에 유효하지 않은 문자가 포함되어 있습니다.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"파일 경로 검증 중 오류 발생: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 탐색기로 안전하게 폴더 열기
        /// </summary>
        /// <param name="directoryPath">열 디렉토리 경로</param>
        /// <param name="errorMessage">오류 메시지 (실패 시)</param>
        /// <returns>성공하면 true, 실패하면 false</returns>
        public static bool OpenDirectoryInExplorer(string directoryPath, out string errorMessage)
        {
            errorMessage = null;

            // 경로 검증
            if (!IsValidDirectoryPath(directoryPath, out errorMessage))
            {
                return false;
            }

            // 디렉토리 존재 확인
            if (!Directory.Exists(directoryPath))
            {
                errorMessage = $"폴더를 찾을 수 없습니다:\n{directoryPath}";
                return false;
            }

            try
            {
                // 절대 경로로 변환
                string fullPath = Path.GetFullPath(directoryPath);

                // 안전하게 탐색기 실행
                System.Diagnostics.Process.Start("explorer.exe", fullPath);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                errorMessage = $"탐색기 실행 실패:\n{ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"폴더를 열 수 없습니다:\n{ex.Message}";
                return false;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// 드라이브 문자가 허용 목록에 있는지 확인
        /// </summary>
        private static bool IsValidDriveLetter(string path)
        {
            if (path.Length < 2 || path[1] != ':')
            {
                return false; // 드라이브 문자 없음
            }

            string driveLetter = path.Substring(0, 1).ToUpperInvariant();
            return AllowedDriveLetters.Contains(driveLetter);
        }

        /// <summary>
        /// 금지된 시스템 경로인지 확인
        /// </summary>
        private static bool IsForbiddenPath(string path)
        {
            string normalizedPath = path.ToUpperInvariant();

            foreach (string forbidden in ForbiddenPaths)
            {
                if (string.IsNullOrWhiteSpace(forbidden))
                    continue;

                string normalizedForbidden = forbidden.ToUpperInvariant();

                // 정확히 일치하거나 하위 디렉토리인 경우
                if (normalizedPath.Equals(normalizedForbidden, StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.StartsWith(normalizedForbidden + @"\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region File Extension Validation

        /// <summary>
        /// 허용된 세이브 파일 확장자 목록
        /// </summary>
        private static readonly string[] AllowedSaveExtensions = { ".d2s" };

        /// <summary>
        /// 허용된 백업 파일 확장자 목록
        /// </summary>
        private static readonly string[] AllowedBackupExtensions = { ".d2s", ".d2s.zip" };

        /// <summary>
        /// 세이브 파일 확장자가 유효한지 검증
        /// </summary>
        /// <param name="fileName">검증할 파일명</param>
        /// <returns>유효하면 true, 그렇지 않으면 false</returns>
        public static bool IsValidSaveFileExtension(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            string lowerFileName = fileName.ToLowerInvariant();

            // .d2s 확장자만 허용
            return lowerFileName.EndsWith(".d2s");
        }

        /// <summary>
        /// 백업 파일 확장자가 유효한지 검증
        /// </summary>
        /// <param name="fileName">검증할 파일명</param>
        /// <returns>유효하면 true, 그렇지 않으면 false</returns>
        public static bool IsValidBackupFileExtension(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            string lowerFileName = fileName.ToLowerInvariant();

            // .d2s 또는 .d2s.zip 확장자 허용
            return lowerFileName.EndsWith(".d2s") || lowerFileName.EndsWith(".d2s.zip");
        }

        /// <summary>
        /// 파일 목록에서 유효한 세이브 파일만 필터링
        /// </summary>
        /// <param name="files">필터링할 파일 경로 배열</param>
        /// <returns>유효한 세이브 파일 경로 배열</returns>
        public static string[] FilterValidSaveFiles(string[] files)
        {
            if (files == null || files.Length == 0)
                return new string[0];

            return files.Where(f =>
            {
                try
                {
                    string fileName = Path.GetFileName(f);
                    return IsValidSaveFileExtension(fileName);
                }
                catch
                {
                    return false; // 경로 파싱 실패 시 제외
                }
            }).ToArray();
        }

        /// <summary>
        /// 파일 목록에서 유효한 백업 파일만 필터링
        /// </summary>
        /// <param name="files">필터링할 파일 경로 배열</param>
        /// <returns>유효한 백업 파일 경로 배열</returns>
        public static string[] FilterValidBackupFiles(string[] files)
        {
            if (files == null || files.Length == 0)
                return new string[0];

            return files.Where(f =>
            {
                try
                {
                    string fileName = Path.GetFileName(f);
                    return IsValidBackupFileExtension(fileName);
                }
                catch
                {
                    return false; // 경로 파싱 실패 시 제외
                }
            }).ToArray();
        }

        #endregion
    }
}
