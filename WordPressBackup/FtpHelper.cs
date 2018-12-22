using FluentFTP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WordPressBackup
{
    public class FtpHelper
    {
        private string FtpHost { get; set; }
        private string FtpUser { get; set; }
        private string FtpPassword { get; set; }
        public int FolderLimit { get; set; }
        public int DownloadChunkFileCount { get; set; }
        public Logger Logger { get; set; }

        public FtpHelper(string ftpHost, string ftpUser, string ftpPassword, Logger log, int folderLimit = int.MaxValue, int downloadChunkFileCount = 10)
        {
            FtpHost = ftpHost;
            FtpUser = ftpUser;
            FtpPassword = ftpPassword;
            Logger = log;
            FolderLimit = folderLimit;
            DownloadChunkFileCount = downloadChunkFileCount;
        }

        public async Task DownloadFolderAsync(string destFolder, string srcFolder)
        {
            // Holds a list of folders that we need to traverse
            // using a stack to eliminate recursion
            var folders = new Stack<string>();

            int foldersProcesed = 0;
            var remotelen = srcFolder.Length;

            //Delete the local temp directory if it already exists.
            if (Directory.Exists(destFolder))
                Directory.Delete(destFolder, true);

            // Push the root folder onto the stack
            folders.Push(srcFolder);

            // Start looping.
            while (folders.Count > 0 && foldersProcesed < FolderLimit)
            {
                foldersProcesed++;
                var currentFolderRemote = folders.Pop();
                var currentFolderLocal = destFolder + currentFolderRemote.Substring(remotelen);

                var filesInRemote = new List<string>();
                var sw = new Stopwatch();
                sw.Start();

                // Create the local clone of a sub folder if needed
                if (!Directory.Exists(currentFolderLocal))
                    Directory.CreateDirectory(currentFolderLocal);

                Logger.Log($"Processing {currentFolderRemote}");

                // FTP into the server and get a list of all the files and folders that exist
                using (FtpClient client = new FtpClient(FtpHost, FtpUser, FtpPassword))
                {
                    await client.ConnectAsync();

                    foreach (var item in await client.GetListingAsync(currentFolderRemote))
                    {
                        if (item.Type == FtpFileSystemObjectType.Directory)
                        {
                            //Send folders to get processed in this thread
                            if (!folders.Contains(item.FullName))
                                folders.Push(item.FullName);
                        }
                        else if (item.Type == FtpFileSystemObjectType.File)
                        {
                            //Build a list of files in this folder
                            filesInRemote.Add(item.FullName);
                        }
                    }


                    if (filesInRemote.Any())
                    {
                        int batchNum = 0;
                        foreach (var chunk in Chunk(filesInRemote, DownloadChunkFileCount))
                        {
                            batchNum++;

                            var downloadedCount = await client.DownloadFilesAsync(currentFolderLocal, chunk);

                            Logger.Log($"Downloaded {downloadedCount} files in Batch {batchNum} to {currentFolderLocal}");
                        }
                    }

                    await client.DisconnectAsync();
                }
            }
        }

        private static IEnumerable<IEnumerable<T>> Chunk<T>(IEnumerable<T> source, int chunksize)
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
