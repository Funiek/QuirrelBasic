using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuirrelBasic.Models
{
    public class DriveFile
    {
        public string? Id { get; set; }
        public required string Name { get; set; }
        public DateTimeOffset? ModifiedTime { get; set; }

        public override string ToString() => $"File Name: {Name}, File ID: {Id}, ModifiedTime: {ModifiedTime}";
    }
}
