using FluentFTP;
using System;
using System.Collections.Concurrent;
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
        private int FolderLimit { get; set; }
        private int DownloadChunkFileCount { get; set; }
        private Logger Logger { get; set; }
        private PolicyHelper Policy { get; set; }

        public FtpHelper(string ftpHost, string ftpUser, string ftpPassword, Logger log, PolicyHelper policy, int folderLimit, int downloadChunkFileCount)
        {
            FtpHost = ftpHost;
            FtpUser = ftpUser;
            FtpPassword = ftpPassword;
            Logger = log;
            FolderLimit = folderLimit;
            DownloadChunkFileCount = downloadChunkFileCount;
            Policy = policy;
        }

        public void DownloadFolder(string destFolder, string srcFolder)
        {
            // Holds a list of folders that we need to traverse
            // using a stack to eliminate recursion
            var folders = new ConcurrentStack<string>();

            int foldersProcesed = 0;
            var remotelen = srcFolder.Length;

            //Delete the local temp directory if it already exists.
            if (Directory.Exists(destFolder))
                Directory.Delete(destFolder, true);

            // Push the root folder onto the stack
            folders.Push(srcFolder);

            string currentFolderRemote = string.Empty;

            // Start looping.
            while (folders.TryPop(out currentFolderRemote) && foldersProcesed < FolderLimit)
            {
                foldersProcesed++;
                var currentFolderLocal = destFolder + currentFolderRemote.Substring(remotelen);

                var filesInRemote = new List<string>();

                // Create the local clone of a sub folder if needed
                if (!Directory.Exists(currentFolderLocal))
                    Directory.CreateDirectory(currentFolderLocal);

                Logger.Log($"Processing {currentFolderRemote}");

                Policy.GetDefaultPolicy().Execute(() =>
                {
                    // FTP into the server and get a list of all the files and folders that exist
                    using (FtpClient client = new FtpClient(FtpHost, FtpUser, FtpPassword))
                    {
                        client.Connect();

                        foreach (var item in client.GetListing(currentFolderRemote))
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

                                var downloadedCount = client.DownloadFiles(currentFolderLocal, chunk);

                                Logger.Log($"Downloaded {downloadedCount} files in Batch {batchNum} to {currentFolderLocal}");
                            }
                        }

                        client.Disconnect();
                    }
                });
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
