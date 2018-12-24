using FluentFTP;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using MySql.Data.MySqlClient;
using Polly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WordPressBackup
{
    class Program
    {
        private string _foldersToProcessString;
        private string _retrysString;
        private string _backupFile;
        private string _backupWorkingDirectory;
        private string _ftpHost;
        private string _ftpUser;
        private string _ftpPassword;
        private string _ftpRemote;
        private string _mySqlServer;
        private string _mySqlDatabase;
        private string _mySqlUser;
        private string _mySqlPassword;
        private string _azStorageConnectionString;
        private string _azStorageContainerName;
        private string _instrumentationKey;

        [Option("-folders", CommandOptionType.SingleValue,
            Description = @"OPTIONAL: This is for Debugging ONLY. The number of folders to look in on the ftp site.")]
        public string FoldersToProcessString { get => _foldersToProcessString ?? Environment.GetEnvironmentVariable("FoldersToProcess"); set => _foldersToProcessString = value; }

        public int FoldersToProcess
        {
            get
            {
                var tmp = 0;
                if (int.TryParse(FoldersToProcessString, out tmp))
                {
                    return tmp;
                }
                else
                {
                    return int.MaxValue;
                }
            }
        }

        [Option("-retrys", CommandOptionType.SingleValue,
            Description = @"In the event of a temporal issue with the backup, how often should we retry (Default = 5).")]
        public string RetrysString { get => _retrysString ?? Environment.GetEnvironmentVariable("Retrys"); set => _retrysString = value; }
        public int Retrys
        {
            get
            {
                var tmp = 0;
                if (int.TryParse(FoldersToProcessString, out tmp))
                {
                    return tmp;
                }
                else
                {
                    return 5;
                }
            }
        }

        [Option("-file", CommandOptionType.SingleValue,
            Description = @"The backup file name to use.")]
        public string BackupFile { get => _backupFile ?? Environment.GetEnvironmentVariable("BackupFile"); set => _backupFile = value; }

        [Option("-dir", CommandOptionType.SingleValue,
            Description = @"The folder put the backup in and use for temporary files during backup. (Default = the current folder)")]
        public string BackupWorkingDirectory { get => _backupWorkingDirectory ?? Environment.GetEnvironmentVariable("BackupWorkingDirectory"); set => _backupWorkingDirectory = value; }

        private string BackupFolder { get { return Path.Combine(BackupWorkingDirectory, BackupFile); } }
        private string BackupZipFile { get { return Path.Combine(BackupWorkingDirectory, $"{DateTime.UtcNow:yyyyMMdd}_{BackupFile}.zip"); } }

        [Option("-ftphost", CommandOptionType.SingleValue,
            Description = @"The host name for the FTP server (i.e. ftppub.everleap.com)")]
        public string FtpHost { get => _ftpHost ?? Environment.GetEnvironmentVariable("FtpHost"); set => _ftpHost = value; }

        [Option("-ftpuser", CommandOptionType.SingleValue,
            Description = @"The user name for the FTP server (i.e. 1234-567\0011234)")]
        public string FtpUser { get => _ftpUser ?? Environment.GetEnvironmentVariable("FtpUser"); set => _ftpUser = value; }

        [Option("-ftppwd", CommandOptionType.SingleValue,
            Description = @"The password for the FTP server")]
        public string FtpPassword { get => _ftpPassword ?? Environment.GetEnvironmentVariable("FtpPassword"); set => _ftpPassword = value; }

        [Option("-ftpremote", CommandOptionType.SingleValue,
            Description = @"The path to your application on the FTP server (Default /site/wwwroot)")]
        public string FtpRemote { get => _ftpRemote ?? Environment.GetEnvironmentVariable("FtpRemote"); set => _ftpRemote = value; }

        public string FtpLocal { get { return Path.Combine(BackupFolder, "wwwroot"); } }

        [Option("-dbserver", CommandOptionType.SingleValue,
            Description = @"The database server (i.e. my01.everleap.com)")]
        public string MySqlServer { get => _mySqlServer ?? Environment.GetEnvironmentVariable("MySqlServer"); set => _mySqlServer = value; }

        [Option("-dbname", CommandOptionType.SingleValue,
            Description = @"The database name")]
        public string MySqlDatabase { get => _mySqlDatabase ?? Environment.GetEnvironmentVariable("MySqlDatabase"); set => _mySqlDatabase = value; }

        [Option("-dbuser", CommandOptionType.SingleValue,
            Description = @"The user name for the database")]
        public string MySqlUser { get => _mySqlUser ?? Environment.GetEnvironmentVariable("MySqlUser"); set => _mySqlUser = value; }

        [Option("-dbpwd", CommandOptionType.SingleValue,
            Description = @"The password for the database")]
        public string MySqlPassword { get => _mySqlPassword ?? Environment.GetEnvironmentVariable("MySqlPassword"); set => _mySqlPassword = value; }

        [Option("-azconnection", CommandOptionType.SingleValue,
            Description = @"OPTIONAL: Use ONLY if you want to upload your backup file to Azure Storage. Storage Connection String")]
        public string AzStorageConnectionString { get => _azStorageConnectionString ?? Environment.GetEnvironmentVariable("AzStorageConnectionString"); set => _azStorageConnectionString = value; }

        [Option("-azcontainer", CommandOptionType.SingleValue,
            Description = @"OPTIONAL: Use ONLY if you want to upload your backup file to Azure Storage. Storage container name")]
        public string AzStorageContainerName { get => _azStorageContainerName ?? Environment.GetEnvironmentVariable("AzStorageContainerName"); set => _azStorageContainerName = value; }

        [Option("-instrumentationkey ", CommandOptionType.SingleValue,
            Description = @"OPTIONAL: Use ONLY if you want to send the logs to Application Insights. Application Insights Instrumentation Key")]
        public string InstrumentationKey { get => _instrumentationKey ?? Environment.GetEnvironmentVariable("InstrumentationKey"); set => _instrumentationKey = value; }

        public Logger Logger { get; set; }

        public Program()
        {

        }

        static void Main(string[] args)
        {
            CommandLineApplication.ExecuteAsync<Program>(args);
        }

        private async Task OnExecuteAsync()
        {
            Logger = new Logger(Guid.NewGuid().ToString(), BackupFile, InstrumentationKey);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            bool isInputsValid = true;

            //Validate and Echo back parameters
            if (FoldersToProcess != int.MaxValue)
            {
                Logger.Log($"TESTING MODE: only downloading {FoldersToProcess} folders.");
            }

            Logger.Log($"On Error Retrys: {Retrys}");

            Logger.Log($"File: {BackupFile}");

            if (string.IsNullOrEmpty(BackupWorkingDirectory))
            {
                BackupWorkingDirectory = Directory.GetCurrentDirectory();
            }
            else if (!Directory.Exists(BackupWorkingDirectory))
            {
                Logger.Log($"Invalid Backup Directory: {BackupWorkingDirectory}");
                isInputsValid = false;
            }
            Logger.Log($"Backup Working Directory: {BackupWorkingDirectory}");

            if (string.IsNullOrEmpty(FtpHost))
            {
                Logger.Log($"Missing FTP Host");
                isInputsValid = false;
            }
            Logger.Log($"FTP Host: {FtpHost}");

            if (string.IsNullOrEmpty(FtpUser))
            {
                Logger.Log($"Missing FTP User");
                isInputsValid = false;
            }
            Logger.Log($"FTP User: {FtpUser}");

            if (string.IsNullOrEmpty(FtpPassword))
            {
                Logger.Log($"Missing FTP Password");
                isInputsValid = false;
            }
            Logger.Log($"FTP Password: {FtpPassword}");

            if (string.IsNullOrEmpty(FtpRemote))
            {
                FtpRemote = @"/site/wwwroot";
            }
            Logger.Log($"FTP Remote: {FtpRemote}");

            if (string.IsNullOrEmpty(MySqlServer))
            {
                Logger.Log($"Missing DB Server");
                isInputsValid = false;
            }
            Logger.Log($"DB Server: {MySqlServer}");

            if (string.IsNullOrEmpty(MySqlDatabase))
            {
                Logger.Log($"Missing DB Database Name");
                isInputsValid = false;
            }
            Logger.Log($"DB Database Name: {MySqlDatabase}");

            if (string.IsNullOrEmpty(MySqlUser))
            {
                Logger.Log($"Missing DB User Name");
                isInputsValid = false;
            }
            Logger.Log($"DB User Name: {MySqlUser}");

            if (string.IsNullOrEmpty(MySqlPassword))
            {
                Logger.Log($"Missing DB Password");
                isInputsValid = false;
            }
            Logger.Log($"DB Password: {MySqlPassword}");


            if (!string.IsNullOrEmpty(AzStorageConnectionString)
                && !string.IsNullOrEmpty(AzStorageContainerName))
            {
                Logger.Log($"Backing Up to Azure");
                Logger.Log($"Azure Storage Connection String: {AzStorageConnectionString}");
                Logger.Log($"Azure Storage Container Name: {AzStorageContainerName}");
            }
            else if (!string.IsNullOrEmpty(AzStorageConnectionString)
                || !string.IsNullOrEmpty(AzStorageContainerName))
            {
                Logger.Log($"Missing Azure Upload Setting");
                Logger.Log($"Azure Storage Connection String: {AzStorageConnectionString}");
                Logger.Log($"Azure Storage Container Name: {AzStorageContainerName}");
                isInputsValid = false;
            }

            if (isInputsValid)
            {
                Logger.Log($"Creating Backup {BackupFile}!");

                try
                {
                    var policy = new PolicyHelper(Retrys, Logger);
                    var ftpHelper = new FtpHelper(FtpHost, FtpUser, FtpPassword, Logger, policy, FoldersToProcess, 10);
                    ftpHelper.DownloadFolder(FtpLocal, FtpRemote);

                    BackupDatabase();
                    ZipBackup();
                    await UploadToAzure();
                }
                catch (Exception ex)
                {
                    Logger.Log($"ERROR APP: {ex}");
                    Logger.Log(ex);
                }

            }
            else
            {
                Logger.Log($"Invalid Inputs, cannot create backup!");
            }

            stopwatch.Stop();
            Logger.Log($"{BackupFile} Done in {stopwatch.Elapsed:c}!");

            await Logger.Flush();
        }

        /// <summary>
        /// Zip up the folder contating the DB and Application backups
        /// </summary>
        private void ZipBackup()
        {
            Logger.Log($"Starting Backup Compression");

            if (File.Exists(BackupZipFile))
                File.Delete(BackupZipFile);

            ZipFile.CreateFromDirectory(BackupFolder, BackupZipFile, CompressionLevel.Optimal, false);

            Logger.Log($"Backup Compression Complete!");

            Directory.Delete(BackupFolder, true);

            Logger.Log($"Temp Files Cleaned up!");
        }

        /// <summary>
        /// Backup up the MySQL Database to a file
        /// </summary>
        private void BackupDatabase()
        {
            var constring = $"server={MySqlServer};user={MySqlUser};pwd={MySqlPassword};database={MySqlDatabase};charset=utf8;convertzerodatetime=true;";
            var file = Path.Combine(BackupFolder, "db.sql");
            var file2 = Path.Combine(BackupFolder, "db-clean.sql");

            Logger.Log($"Starting Database Backup");

            if (File.Exists(file))
                File.Delete(file);

            using (MySqlConnection conn = new MySqlConnection(constring))
            using (MySqlCommand cmd = new MySqlCommand())
            using (MySqlBackup mb = new MySqlBackup(cmd))
            {
                cmd.Connection = conn;
                cmd.CommandTimeout = 0;
                conn.Open();

                mb.ExportToFile(file);

                conn.Close();
            }

            // Using the MySqlBackup library the export it made would not reimport
            // I was seeing the following error when importing into MySQL using Workbench
            //   ASCII '\0' appeared in the statement, but this is not allowed unless option --binary-mode is enabled and mysql is run in non-interactive mode. Set --binary-mode to 1 if ASCII '\0' is expected.
            // remove all the \0 characters and the import worked fine.
            // Hopefully they are not important (eek!)
            File.WriteAllLines(file2, File.ReadAllLines(file)
                .Select(x => x.Replace("\0", "")), Encoding.UTF8);

            Logger.Log($"Database Backup Complete!");
        }

        private async Task UploadToAzure()
        {
            if (!string.IsNullOrEmpty(AzStorageConnectionString)
               && !string.IsNullOrEmpty(AzStorageContainerName))
            {
                Logger.Log($"Starting Azure Upload!");
                CloudStorageAccount storageAccount = null;

                if (CloudStorageAccount.TryParse(AzStorageConnectionString, out storageAccount))
                {
                    CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                    var cloudBlobContainer = cloudBlobClient.GetContainerReference(AzStorageContainerName);
                    await cloudBlobContainer.CreateIfNotExistsAsync();
                    CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference(Path.GetFileName(BackupZipFile));
                    await cloudBlockBlob.UploadFromFileAsync(BackupZipFile);
                }

                Logger.Log($"Finished Azure Upload!");
            }
        }

    }
}