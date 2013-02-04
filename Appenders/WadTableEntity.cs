using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Ms.Azure.Logging.Appenders
{
    /// <summary>
    /// The table entity compatible with the current Windows Azure Diagnostics format, so it can use the same
    /// table to log to allowing the use of any 3rd party applications that use this format.
    /// </summary>
    public class WadTableEntity : TableServiceEntity
    {
        public WadTableEntity()
        {
            var now = DateTime.UtcNow;
            PartitionKey = string.Format("{0:yyyy-MM-dd}", now);
            RowKey = string.Format("{0:dd HH:mm:ss.fff}-{1}", now, Guid.NewGuid());
        }

        public DateTime Timestamp { get; set; }
        public long EventTickCount { get; set; }
        public string DeploymentId { get; set; }
        public string Role { get; set; }
        public string RoleInstance { get; set; }
        public int Level { get; set; }
        public int EventId { get; set; }
        public int Pid { get; set; }
        public int Tid { get; set; }
        public string Message { get; set; }
    }
}
