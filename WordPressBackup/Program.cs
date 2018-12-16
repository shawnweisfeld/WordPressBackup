using FluentFTP;
using Microsoft.Extensions.CommandLineUtils;
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
        private static int RETRYS;

        private static string BKUP_FILE;
        private static string BKUP_WORKINGDIR;
        private static string BKUP_FOLDER;

        private static string FTP_HOST;
        private static string FTP_USER;
        private static string FTP_PASSWORD;
        private static string FTP_REMOTE;
        private static string FTP_LOCAL;

        private static string MYSQL_SERVER;
        private static string MYSQL_DATABASE;
        private static string MYSQL_USER;
        private static string MYSQL_PASSWORD;

        static void Main(string[] args)
        {
            CommandLineApplication commandLineApplication = new CommandLineApplication(throwOnUnexpectedArg: false);

            CommandOption coRetrys = commandLineApplication.Option(
              "-retrys <retrys>",
              @"In the event of a temporal issue with the backup, how often should we retry (Default = 5).",
              CommandOptionType.SingleValue);

            CommandOption coBkupFile = commandLineApplication.Option(
              "-file <file>",
              @"The backup file name stem to use.",
              CommandOptionType.SingleValue);

            CommandOption coBkupDir = commandLineApplication.Option(
              "-dir <file>",
              @"The folder put the backup in and use for temporary files during backup. (Default = the current folder)",
              CommandOptionType.SingleValue);

            CommandOption coFtpHost = commandLineApplication.Option(
              "-ftphost <host>",
              @"The host name for the FTP server (i.e. ftppub.everleap.com)",
              CommandOptionType.SingleValue);

            CommandOption coFtpUser = commandLineApplication.Option(
              "-ftpuser <user>",
              @"The user name for the FTP server (i.e. 1234-567\0011234)",
              CommandOptionType.SingleValue);

            CommandOption coFtpPassword = commandLineApplication.Option(
              "-ftppwd <password>",
              @"The password for the FTP server",
              CommandOptionType.SingleValue);

            CommandOption coFtpRemote = commandLineApplication.Option(
              "-ftpremote <remote>",
              @"The path to your application on the FTP server (Default /site/wwwroot)",
              CommandOptionType.SingleValue);

            CommandOption coMySqlServer = commandLineApplication.Option(
              "-dbserver <mysqlserver>",
              @"The database server (i.e. my01.everleap.com)",
              CommandOptionType.SingleValue);

            CommandOption coMySqlDatabase = commandLineApplication.Option(
              "-db <mysqldb>",
              @"The database name",
              CommandOptionType.SingleValue);

            CommandOption coMySqlUser = commandLineApplication.Option(
              "-dbuser <user>",
              @"The user name for the database",
              CommandOptionType.SingleValue);

            CommandOption coMySqlPassword = commandLineApplication.Option(
              "-dbpwd <password>",
              @"The password for the database",
              CommandOptionType.SingleValue);

            commandLineApplication.HelpOption("-? | -h | --help");

            commandLineApplication.OnExecute(() =>
            {
                if (!int.TryParse(coRetrys.Value(), out RETRYS))
                {
                    RETRYS = 5;
                }
                Write($"On Error Retrys: {RETRYS}", ConsoleColor.Blue);

                if (coBkupFile.HasValue())
                {
                    BKUP_FILE = coBkupFile.Value();
                    Write($"File: {BKUP_FILE}", ConsoleColor.Blue);
                }
                else
                {
                    Write($"Missing backup filename", ConsoleColor.Red);
                    return 1;
                }

                if (coBkupDir.HasValue())
                {
                    if (!Directory.Exists(coBkupDir.Value()))
                    {
                        Write($"Invalid Backup Directory: {coBkupDir.Value()}", ConsoleColor.Red);
                        return 1;
                    }

                    BKUP_WORKINGDIR = coBkupDir.Value();
                }
                else
                {
                    BKUP_WORKINGDIR = Directory.GetCurrentDirectory();
                }
                Write($"Backup Working Directory: {BKUP_WORKINGDIR}", ConsoleColor.Blue);

                BKUP_FOLDER = Path.Combine(BKUP_WORKINGDIR, BKUP_FILE);

                if (!coFtpHost.HasValue())
                {
                    Write($"Missing FTP Host", ConsoleColor.Red);
                    return 1;
                }
                FTP_HOST = coFtpHost.Value();
                Write($"FTP Host: {FTP_HOST}", ConsoleColor.Blue);

                if (!coFtpUser.HasValue())
                {
                    Write($"Missing FTP User", ConsoleColor.Red);
                    return 1;
                }
                FTP_USER = coFtpUser.Value();
                Write($"FTP User: {FTP_USER}", ConsoleColor.Blue);

                if (!coFtpPassword.HasValue())
                {
                    Write($"Missing FTP Password", ConsoleColor.Red);
                    return 1;
                }
                FTP_PASSWORD = coFtpPassword.Value();
                Write($"FTP Password: {FTP_PASSWORD}", ConsoleColor.Blue);

                if (coFtpRemote.HasValue())
                {
                    FTP_REMOTE = coFtpRemote.Value();
                }
                else
                {
                    FTP_REMOTE = @"/site/wwwroot";
                }
                Write($"FTP Remote: {FTP_REMOTE}", ConsoleColor.Blue);

                FTP_LOCAL = Path.Combine(BKUP_FOLDER, "wwwroot");

                if (!coMySqlServer.HasValue())
                {
                    Write($"Missing database server", ConsoleColor.Red);
                    return 1;
                }
                MYSQL_SERVER = coMySqlServer.Value();
                Write($"MySQL Server: {MYSQL_SERVER}", ConsoleColor.Blue);

                if (!coMySqlDatabase.HasValue())
                {
                    Write($"Missing database name", ConsoleColor.Red);
                    return 1;
                }
                MYSQL_DATABASE = coMySqlDatabase.Value();
                Write($"MySQL Database: {MYSQL_DATABASE}", ConsoleColor.Blue);

                if (!coMySqlUser.HasValue())
                {
                    Write($"Missing database user name", ConsoleColor.Red);
                    return 1;
                }
                MYSQL_USER = coMySqlUser.Value();
                Write($"MySQL User: {MYSQL_USER}", ConsoleColor.Blue);

                if (!coMySqlPassword.HasValue())
                {
                    Write($"Missing database password", ConsoleColor.Red);
                    return 1;
                }
                MYSQL_PASSWORD = coMySqlPassword.Value();
                Write($"MySQL Password: {MYSQL_PASSWORD}", ConsoleColor.Blue);

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                Write($"Creating Backup {BKUP_FILE}!", ConsoleColor.Green);

                BackupApplication().Wait();
                BackupDatabase();
                ZipBackup();

                stopwatch.Stop();
                Console.WriteLine($"{BKUP_FILE} Done in {stopwatch.Elapsed:c}!");

                return 0;
            });
            commandLineApplication.Execute(args);
        }


        /// <summary>
        /// Polly Policy. This will retry on an error using an exponential backoff.
        /// 
        /// Use like so:
        ///   GetRetryPolicy().Execute(() => { /* code goes here */ });
        /// 
        /// </summary>
        /// <returns></returns>
        private static Policy GetRetryPolicy()
        {
            return Policy
              .Handle<Exception>()
              .WaitAndRetry(
                  RETRYS,
                  (retryCount, timespan) => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                  (exception, timeSpan, retryCount, context) => Write($"Retry {retryCount} : {exception.Message}", ConsoleColor.Red));
        }


        /// <summary>
        /// Async Polly Policy. This will retry on an error using an exponential backoff.
        /// 
        /// Use like so:
        ///   GetRetryPolicyAsync().ExecuteAsync(() => { /* code goes here */ });
        /// 
        /// </summary>
        /// <returns></returns>
        private static Policy GetRetryPolicyAsync()
        {
            return Policy
              .Handle<Exception>()
              .WaitAndRetryAsync(
                  RETRYS,
                  (retryCount, timespan) => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                  (exception, timeSpan, retryCount, context) => Write($"Retry {retryCount} : {exception.Message}", ConsoleColor.Red));
        }

        /// <summary>
        /// Zip up the folder contating the DB and Application backups
        /// </summary>
        private static void ZipBackup()
        {
            var file = Path.Combine(BKUP_WORKINGDIR, BKUP_FILE + ".zip");

            GetRetryPolicy().Execute(() =>
            {
                Write($"Starting Backup Compression", ConsoleColor.Yellow);

                if (File.Exists(file))
                    File.Delete(file);

                ZipFile.CreateFromDirectory(BKUP_FOLDER, file, CompressionLevel.Optimal, false);

                Write($"Backup Compression Complete!", ConsoleColor.Green);

                Directory.Delete(BKUP_FOLDER, true);

                Write($"Temp Files Cleaned up!", ConsoleColor.Green);

            });
        }

        /// <summary>
        /// Backup up the MySQL Database to a file
        /// </summary>
        private static void BackupDatabase()
        {
            var constring = $"server={MYSQL_SERVER};user={MYSQL_USER};pwd={MYSQL_PASSWORD};database={MYSQL_DATABASE};charset=utf8;convertzerodatetime=true;";
            var file = Path.Combine(BKUP_FOLDER, "db.sql");

            GetRetryPolicy().Execute(() => {

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

        /// <summary>
        /// Use FTP to download the entire site.
        /// </summary>
        /// <returns></returns>
        private static async Task BackupApplication()
        {
            //Delete the local temp directory if it already exists.
            if (Directory.Exists(FTP_LOCAL))
                Directory.Delete(FTP_LOCAL, true);

            var remotelen = FTP_REMOTE.Length;

            // Holds a list of folders that we need to traverse
            // using a stack to eliminate recursion
            var folders = new Stack<string>();

            // Tasks downloading the files in the folders
            // this gives us lots of parallelism on the downloads
            var downloads = new List<Task>();

            // Push the root folder onto the stack
            folders.Push(FTP_REMOTE);

            // Start looping.
            while (folders.Count > 0)
            {
                var currentFolderRemote = folders.Pop();
                var currentFolderLocal = FTP_LOCAL + currentFolderRemote.Substring(remotelen);
                var filesInRemote = new List<string>();

                await GetRetryPolicyAsync().ExecuteAsync(async () =>
                {
                    // Create the local clone of a sub folder if needed
                    if (!Directory.Exists(currentFolderLocal))
                        Directory.CreateDirectory(currentFolderLocal);

                    // FTP into the server and get a list of all the files and folders that exist
                    using (FtpClient client = new FtpClient(FTP_HOST, FTP_USER, FTP_PASSWORD))
                    {
                        client.Connect();

                        foreach (var item in await client.GetListingAsync(currentFolderRemote))
                        {
                            Write($"Found: {item.FullName}", ConsoleColor.Yellow);

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

                        //Send the entire list of files to be downloaded to the download method
                        downloads.Add(DownloadFiles(filesInRemote, currentFolderLocal));

                        client.Disconnect();
                    }
                });
            }

            //Wait for all the downloads to complete before exiting
            Task.WaitAll(downloads.ToArray());
        }

        /// <summary>
        /// Use FTP to download the list of files to the given folder.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="destination"></param>
        /// <returns></returns>
        private static async Task DownloadFiles(IEnumerable<string> files, string destination)
        {
            if (files.Any())
            {
                await GetRetryPolicyAsync().ExecuteAsync(async () =>
                {
                    using (FtpClient client = new FtpClient(FTP_HOST, FTP_USER, FTP_PASSWORD))
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
        private static void Write(string message, ConsoleColor color)
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
