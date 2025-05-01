using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuirrelBasic.Models
{
    public class DrivesConfig
    {
        public string GoogleClientSecretPath { get; set; }
        public string GoogleFolderId { get; set; }

        public DrivesConfig(string googleClientSecretPath, string googleFolderId)
        {
            GoogleClientSecretPath = googleClientSecretPath;
            GoogleFolderId = googleFolderId;
        }
    }
}
