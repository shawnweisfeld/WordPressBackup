using System;
using System.Collections.Generic;
using System.Text;

namespace WordPressBackup
{
    class DownloadFileGroup
    {
        public int BatchNum { get; set; }
        public string DestFolder { get; set; }
        public IEnumerable<string> SrcFiles { get; set; }
    }
}
