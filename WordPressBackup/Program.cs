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
                Console.WriteLine($"TESTING MODE: only downloading {FoldersToProcess} folders.");
            }

            Console.WriteLine($"On Error Retrys: {Retrys}");

            Console.WriteLine($"File: {BackupFile}");

            if (string.IsNullOrEmpty(BackupWorkingDirectory))
            {
                BackupWorkingDirectory = Directory.GetCurrentDirectory();
            }
            else if (!Directory.Exists(BackupWorkingDirectory))
            {
                Console.WriteLine($"Invalid Backup Directory: {BackupWorkingDirectory}");
                isInputsValid = false;
            }
            Console.WriteLine($"Backup Working Directory: {BackupWorkingDirectory}");

            if (string.IsNullOrEmpty(FtpHost))
            {
                Console.WriteLine($"Missing FTP Host");
                isInputsValid = false;
            }
            Console.WriteLine($"FTP Host: {FtpHost}");

            if (string.IsNullOrEmpty(FtpUser))
            {
                Console.WriteLine($"Missing FTP User");
                isInputsValid = false;
            }
            Console.WriteLine($"FTP User: {FtpUser}");

            if (string.IsNullOrEmpty(FtpPassword))
            {
                Console.WriteLine($"Missing FTP Password");
                isInputsValid = false;
            }
            Console.WriteLine($"FTP Password: {FtpPassword}");

            if (string.IsNullOrEmpty(FtpRemote))
            {
                FtpRemote = @"/site/wwwroot";
            }
            Console.WriteLine($"FTP Remote: {FtpRemote}");

            if (string.IsNullOrEmpty(MySqlServer))
            {
                Console.WriteLine($"Missing DB Server");
                isInputsValid = false;
            }
            Console.WriteLine($"DB Server: {MySqlServer}");

            if (string.IsNullOrEmpty(MySqlDatabase))
            {
                Console.WriteLine($"Missing DB Database Name");
                isInputsValid = false;
            }
            Console.WriteLine($"DB Database Name: {MySqlDatabase}");

            if (string.IsNullOrEmpty(MySqlUser))
            {
                Console.WriteLine($"Missing DB User Name");
                isInputsValid = false;
            }
            Console.WriteLine($"DB User Name: {MySqlUser}");

            if (string.IsNullOrEmpty(MySqlPassword))
            {
                Console.WriteLine($"Missing DB Password");
                isInputsValid = false;
            }
            Console.WriteLine($"DB Password: {MySqlPassword}");


            if (!string.IsNullOrEmpty(AzStorageConnectionString)
                && !string.IsNullOrEmpty(AzStorageContainerName))
            {
                Console.WriteLine($"Backing Up to Azure");
                Console.WriteLine($"Azure Storage Connection String: {AzStorageConnectionString}");
                Console.WriteLine($"Azure Storage Container Name: {AzStorageContainerName}");
            }
            else if (!string.IsNullOrEmpty(AzStorageConnectionString)
                || !string.IsNullOrEmpty(AzStorageContainerName))
            {
                Console.WriteLine($"Missing Azure Upload Setting");
                Console.WriteLine($"Azure Storage Connection String: {AzStorageConnectionString}");
                Console.WriteLine($"Azure Storage Container Name: {AzStorageContainerName}");
                isInputsValid = false;
            }

            if (isInputsValid)
            {
                Console.WriteLine($"Creating Backup {BackupFile}!");

                RetryPolicy = Policy.Wrap(Policy
                  .Handle<Exception>()
                  .WaitAndRetry(
                      Retrys,
                      (retryCount, timespan) => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                      (exception, timeSpan, retryCount, context) => Console.WriteLine($"Retry {retryCount} : {exception.Message}")),
                  Policy.Timeout(120));

                RetryPolicyAsync = Policy.WrapAsync(Policy
                  .Handle<Exception>()
                  .WaitAndRetryAsync(
                      Retrys,
                      (retryCount, timespan) => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                      (exception, timeSpan, retryCount, context) => Console.WriteLine($"Retry {retryCount} : {exception.Message}")),
                  Policy.TimeoutAsync(120));

                try
                {
                    BackupApplication().Wait();
                    BackupDatabase();
                    ZipBackup();
                    UploadToAzure().Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Backup Error: {ex}");
                }

            }
            else
            {
                Console.WriteLine($"Invalid Inputs, cannot create backup!");
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
                Console.WriteLine($"Starting Backup Compression");

                if (File.Exists(BackupZipFile))
                    File.Delete(BackupZipFile);

                ZipFile.CreateFromDirectory(BackupFolder, BackupZipFile, CompressionLevel.Optimal, false);

                Console.WriteLine($"Backup Compression Complete!");

                Directory.Delete(BackupFolder, true);

                Console.WriteLine($"Temp Files Cleaned up!");

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

                Console.WriteLine($"Starting Database Backup");

                if (File.Exists(file))
                    File.Delete(file);

                using (MySqlConnection conn = new MySqlConnection(constring))
                using (MySqlCommand cmd = new MySqlCommand())
                using (MySqlBackup mb = new MySqlBackup(cmd))
                {
                    cmd.Connection = conn;
                    cmd.CommandTimeout = 0;
                    conn.Open();

                    // there is an export to file function on the MySqlBackup library
                    // however the export it made would not reimport
                    // I was seeing the following error when importing into MySQL using Workbench
                    //   ASCII '\0' appeared in the statement, but this is not allowed unless option --binary-mode is enabled and mysql is run in non-interactive mode. Set --binary-mode to 1 if ASCII '\0' is expected.
                    // finally decided to just remove all the \0 characters and the import worked fine.
                    // Hopefully they are not important (eek!)
                    File.WriteAllText(file, mb.ExportToString().Replace("\0", ""), Encoding.UTF8);

                    conn.Close();
                }


                Console.WriteLine($"Database Backup Complete!");
            });
        }

        private async Task UploadToAzure()
        {
            if (!string.IsNullOrEmpty(AzStorageConnectionString)
               && !string.IsNullOrEmpty(AzStorageContainerName))
            {
                await RetryPolicyAsync.ExecuteAsync(async () =>
                {
                    Console.WriteLine($"Starting Azure Upload!");
                    CloudStorageAccount storageAccount = null;

                    if (CloudStorageAccount.TryParse(AzStorageConnectionString, out storageAccount))
                    {
                        CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                        var cloudBlobContainer = cloudBlobClient.GetContainerReference(AzStorageContainerName);
                        await cloudBlobContainer.CreateIfNotExistsAsync();
                        CloudBlockBlob cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference(Path.GetFileName(BackupZipFile));
                        await cloudBlockBlob.UploadFromFileAsync(BackupZipFile);
                    }

                    Console.WriteLine($"Finished Azure Upload!");
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
                    var sw = new Stopwatch();
                    sw.Start();

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

                        Console.WriteLine($"Found {filesInRemote.Count} Files in {currentFolderRemote}");

                        //Send the entire list of files to be downloaded to the download method
                        downloads.Add(DownloadFiles(filesInRemote, currentFolderLocal));

                        //var downloadedCount = await client.DownloadFilesAsync(currentFolderLocal, filesInRemote);
                        //Console.WriteLine($"Downloaded {downloadedCount} of {filesInRemote.Count} files to {currentFolderLocal} in {sw.Elapsed:c}");

                        client.Disconnect();
                    }
                });
            }


            //Wait for all the downloads to complete before exiting
            var allDone = Task.WhenAll(downloads.ToArray());
            var waitTimer = new Stopwatch();
            waitTimer.Start();

            while (!allDone.IsCompleted)
            {
                var total = downloads.Count();
                var done = downloads.Count(x => x.IsCompleted);

                Console.WriteLine($"Waiting for all file downloads to complete {done} of {total} in {waitTimer.Elapsed:c}.");
                await Task.Delay(5000);
            }
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
                foreach (var chunk in Chunk(files, 10))
                {
                    var cancellationToken = new CancellationToken();
                    await RetryPolicyAsync.ExecuteAsync(async ct =>
                    {
                        using (FtpClient client = new FtpClient(FtpHost, FtpUser, FtpPassword))
                        {
                            client.Connect();

                            await client.DownloadFilesAsync(destination, chunk, true, FtpVerify.Throw, FtpError.None, ct)
                                 .ContinueWith(t => Console.WriteLine($"Downloaded {t.Result} files to {destination}"));

                            client.Disconnect();
                        }
                    }, cancellationToken);
                }
            }
        }

        public static IEnumerable<IEnumerable<T>> Chunk<T>(IEnumerable<T> source, int chunksize)
        {
            var temp = new List<T>();
            foreach (var item in source)
            {
                temp.Add(item);

                if (temp.Count == chunksize)
                {
                    yield return temp;
                    temp.Clear();
                }
            }

            if (temp.Any())
            {
                yield return temp;
            }
        }

    }
}
