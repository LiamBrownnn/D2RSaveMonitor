using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace D2RSaveMonitor
{
    /// <summary>
    /// 백업 데이터 접근 추상화 / Backup data access abstraction
    /// </summary>
    public interface IBackupRepository
    {
        /// <summary>
        /// 단일 백업 생성 / Create a single backup
        /// </summary>
        Task<BackupResult> CreateAsync(string sourceFile, BackupTrigger trigger);

        /// <summary>
        /// 여러 백업 생성 / Create multiple backups
        /// </summary>
        Task<List<BackupResult>> CreateBulkAsync(IEnumerable<string> sourceFiles, BackupTrigger trigger);

        /// <summary>
        /// 특정 파일의 백업 히스토리 조회 / Get backup history for a specific file
        /// </summary>
        List<BackupMetadata> GetHistory(string characterFile);

        /// <summary>
        /// 모든 백업 조회 / Get all backups
        /// </summary>
        List<BackupMetadata> GetAll();

        /// <summary>
        /// 백업 복원 / Restore from backup
        /// </summary>
        Task<RestoreResult> RestoreAsync(BackupMetadata backup, string targetPath, bool createPreRestoreBackup = true);

        /// <summary>
        /// 백업 삭제 / Delete a backup
        /// </summary>
        Task<bool> DeleteAsync(BackupMetadata backup);

        /// <summary>
        /// 백업 가능 여부 확인 (쿨다운 체크) / Check if backup can be created (cooldown check)
        /// </summary>
        bool CanCreate(string sourceFile, BackupTrigger trigger);

        /// <summary>
        /// 백업 디렉토리 경로 / Backup directory path
        /// </summary>
        string BackupDirectory { get; }

        /// <summary>
        /// 백업 시작 이벤트 / Backup started event
        /// </summary>
        event EventHandler<BackupEventArgs> BackupStarted;

        /// <summary>
        /// 백업 완료 이벤트 / Backup completed event
        /// </summary>
        event EventHandler<BackupEventArgs> BackupCompleted;

        /// <summary>
        /// 백업 실패 이벤트 / Backup failed event
        /// </summary>
        event EventHandler<BackupErrorEventArgs> BackupFailed;

        /// <summary>
        /// 백업 진행 이벤트 / Backup progress event
        /// </summary>
        event EventHandler<BackupProgressEventArgs> BackupProgress;
    }
}
