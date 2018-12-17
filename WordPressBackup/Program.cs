using FluentFTP;
using McMaster.Extensions.CommandLineUtils;
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

        public Program()
        {

        }

        /// <summary>
        /// Polly Policy. This will retry on an error using an exponential backoff.
        /// 
        /// Use like so:
        ///   RetryPolicy.Execute(() => { /* code goes here */ });
        /// </summary>
        public Policy RetryPolicy { get; private set; }

        /// <summary>
        /// Async Polly Policy. This will retry on an error using an exponential backoff.
        /// 
        /// Use like so:
        ///   RetryPolicyAsync.ExecuteAsync(() => { /* code goes here */ });        
        /// </summary>
        public Policy RetryPolicyAsync { get; private set; }

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
        private string BackupZipFile { get { return Path.Combine(BackupWorkingDirectory, BackupFile + ".zip"); } }

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

        static void Main(string[] args)
        {
            CommandLineApplication.Execute<Program>(args);
        }

        private void OnExecute()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            bool isInputsValid = true;

            //Validate and Echo back parameters
            if (FoldersToProcess != int.MaxValue)
            {
                Write($"TESTING MODE: only downloading {FoldersToProcess} folders.", ConsoleColor.Blue);
            }

            Write($"On Error Retrys: {Retrys}", ConsoleColor.Blue);

            Write($"File: {BackupFile}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(BackupWorkingDirectory))
            {
                BackupWorkingDirectory = Directory.GetCurrentDirectory();
            }
            else if (!Directory.Exists(BackupWorkingDirectory))
            {
                Write($"Invalid Backup Directory: {BackupWorkingDirectory}", ConsoleColor.Red);
                isInputsValid = false;
            }
            Write($"Backup Working Directory: {BackupWorkingDirectory}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(FtpHost))
            {
                Write($"Missing FTP Host", ConsoleColor.Red);
                isInputsValid = false;
            }
            Write($"FTP Host: {FtpHost}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(FtpUser))
            {
                Write($"Missing FTP User", ConsoleColor.Red);
                isInputsValid = false;
            }
            Write($"FTP User: {FtpUser}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(FtpPassword))
            {
                Write($"Missing FTP Password", ConsoleColor.Red);
                isInputsValid = false;
            }
            Write($"FTP Password: {FtpPassword}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(FtpRemote))
            {
                FtpRemote = @"/site/wwwroot";
            }
            Write($"FTP Remote: {FtpRemote}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(MySqlServer))
            {
                Write($"Missing DB Server", ConsoleColor.Red);
                isInputsValid = false;
            }
            Write($"DB Server: {MySqlServer}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(MySqlDatabase))
            {
                Write($"Missing DB Database Name", ConsoleColor.Red);
                isInputsValid = false;
            }
            Write($"DB Database Name: {MySqlDatabase}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(MySqlUser))
            {
                Write($"Missing DB User Name", ConsoleColor.Red);
                isInputsValid = false;
            }
            Write($"DB User Name: {MySqlUser}", ConsoleColor.Blue);

            if (string.IsNullOrEmpty(MySqlPassword))
            {
                Write($"Missing DB Password", ConsoleColor.Red);
                isInputsValid = false;
            }
            Write($"DB Password: {MySqlPassword}", ConsoleColor.Blue);


            if (!string.IsNullOrEmpty(AzStorageConnectionString)
                && !string.IsNullOrEmpty(AzStorageContainerName))
            {
                Write($"Backing Up to Azure", ConsoleColor.Green);
                Write($"Azure Storage Connection String: {AzStorageConnectionString}", ConsoleColor.Blue);
                Write($"Azure Storage Container Name: {AzStorageContainerName}", ConsoleColor.Blue);
            }
            else if (!string.IsNullOrEmpty(AzStorageConnectionString)
                || !string.IsNullOrEmpty(AzStorageContainerName))
            {
                Write($"Missing Azure Upload Setting", ConsoleColor.Red);
                Write($"Azure Storage Connection String: {AzStorageConnectionString}", ConsoleColor.Blue);
                Write($"Azure Storage Container Name: {AzStorageContainerName}", ConsoleColor.Blue);
                isInputsValid = false;
            }

            if (isInputsValid)
            {
                Write($"Creating Backup {BackupFile}!", ConsoleColor.Green);

                RetryPolicy = Policy
                  .Handle<Exception>()
                  .WaitAndRetry(
                      Retrys,
                      (retryCount, timespan) => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                      (exception, timeSpan, retryCount, context) => Write($"Retry {retryCount} : {exception.Message}", ConsoleColor.Red));

                RetryPolicyAsync = Policy
                  .Handle<Exception>()
                  .WaitAndRetryAsync(
                      Retrys,
                      (retryCount, timespan) => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                      (exception, timeSpan, retryCount, context) => Write($"Retry {retryCount} : {exception.Message}", ConsoleColor.Red));

                BackupApplication().Wait();
                BackupDatabase();
                ZipBackup();
                UploadToAzure().Wait();
            }
            else
            {
                Write($"Invalid Inputs, cannot create backup!", ConsoleColor.Red);
            }

            stopwatch.Stop();
            Console.WriteLine($"{BackupFile} Done in {stopwatch.Elapsed:c}!");
        }

        /// <summary>
        /// Zip up the folder contating the DB and Application backups
        /// </summary>
        private void ZipBackup()
        {
            RetryPolicy.Execute(() =>
            {
                Write($"Starting Backup Compression", ConsoleColor.Yellow);

                if (File.Exists(BackupZipFile))
                    File.Delete(BackupZipFile);

                ZipFile.CreateFromDirectory(BackupFolder, BackupZipFile, CompressionLevel.Optimal, false);

                Write($"Backup Compression Complete!", ConsoleColor.Green);

                Directory.Delete(BackupFolder, true);

                Write($"Temp Files Cleaned up!", ConsoleColor.Green);

            });
        }

        /// <summary>
        /// Backup up the MySQL Database to a file
        /// </summary>
        private void BackupDatabase()
        {
            var constring = $"server={MySqlServer};user={MySqlUser};pwd={MySqlPassword};database={MySqlDatabase};charset=utf8;convertzerodatetime=true;";
            var file = Path.Combine(BackupFolder, "db.sql");

            RetryPolicy.Execute(() =>
            {

                Write($"Starting Database Backup", ConsoleColor.Yellow);

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

                Write($"Database Backup Complete!", ConsoleColor.Green);
            });
        }

        private async Task UploadToAzure()
        {
            if (!string.IsNullOrEmpty(AzStorageConnectionString)
               && !string.IsNullOrEmpty(AzStorageContainerName))
            {
                await RetryPolicyAsync.ExecuteAsync(async () =>
                {
                    Write($"Starting Azure Upload!", ConsoleColor.Green);
                    CloudStorageAccount storageAccount = null;

                    if (CloudStorageAccount.TryParse(AzStorageConnectionString, out storageAccount))
                    {
                        CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                        var cloudBlobContainer = cloudBlobClient.GetContainerReference(AzStorageContainerName);
                        await cloudBlobContainer.CreateIfNotExistsAsync();
                        CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference(Path.GetFileName(BackupZipFile));
                        await cloudBlockBlob.UploadFromFileAsync(BackupZipFile);
                    }

                    Write($"Finished Azure Upload!", ConsoleColor.Green);
                });
            }
        }

        /// <summary>
        /// Use FTP to download the entire site.
        /// </summary>
        /// <returns></returns>
        private async Task BackupApplication()
        {
            int foldersProcesed = 0;

            //Delete the local temp directory if it already exists.
            if (Directory.Exists(FtpLocal))
                Directory.Delete(FtpLocal, true);

            var remotelen = FtpRemote.Length;

            // Holds a list of folders that we need to traverse
            // using a stack to eliminate recursion
            var folders = new Stack<string>();

            // Tasks downloading the files in the folders
            // this gives us lots of parallelism on the downloads
            var downloads = new List<Task>();

            // Push the root folder onto the stack
            folders.Push(FtpRemote);

            // Start looping.
            while (folders.Count > 0 && foldersProcesed < FoldersToProcess)
            {
                foldersProcesed++;
                var currentFolderRemote = folders.Pop();
                var currentFolderLocal = FtpLocal + currentFolderRemote.Substring(remotelen);
                var filesInRemote = new List<string>();

                await RetryPolicyAsync.ExecuteAsync(async () =>
                {
                    // Create the local clone of a sub folder if needed
                    if (!Directory.Exists(currentFolderLocal))
                        Directory.CreateDirectory(currentFolderLocal);

                    // FTP into the server and get a list of all the files and folders that exist
                    using (FtpClient client = new FtpClient(FtpHost, FtpUser, FtpPassword))
                    {
                        client.Connect();

                        foreach (var item in await client.GetListingAsync(currentFolderRemote))
                        {
                            if (item.Type == FtpFileSystemObjectType.Directory)
                            {
                                //Send folders to get processed in this thread
                                folders.Push(item.FullName);
                            }
                            else if (item.Type == FtpFileSystemObjectType.File)
                            {
                                //Build a list of files in this folder
                                filesInRemote.Add(item.FullName);
                            }
                        }

                        Write($"Found {filesInRemote.Count} Files in {currentFolderRemote}", ConsoleColor.Yellow);

                        //Send the entire list of files to be downloaded to the download method
                        downloads.Add(DownloadFiles(filesInRemote, currentFolderLocal));

                        client.Disconnect();
                    }
                });
            }

            Write("Waiting for all file downloads to complete.", ConsoleColor.Green);

            //Wait for all the downloads to complete before exiting
            Task.WaitAll(downloads.ToArray());
        }

        /// <summary>
        /// Use FTP to download the list of files to the given folder.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="destination"></param>
        /// <returns></returns>
        private async Task DownloadFiles(IEnumerable<string> files, string destination)
        {
            if (files.Any())
            {
                await RetryPolicyAsync.ExecuteAsync(async () =>
                {
                    using (FtpClient client = new FtpClient(FtpHost, FtpUser, FtpPassword))
                    {
                        client.Connect();

                        await client.DownloadFilesAsync(destination, files)
                             .ContinueWith(t => Write($"Downloaded {t.Result} files to {destination}", ConsoleColor.Green));

                        client.Disconnect();
                    }
                });
            }
        }

        /// <summary>
        /// Log a message in a given color to the console.
        /// 
        /// Since the app does alot of work in parallel, need to lock between changing the color and writing
        /// out the message, or there could be a race condition.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="color"></param>
        private void Write(string message, ConsoleColor color)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }
    }
}
