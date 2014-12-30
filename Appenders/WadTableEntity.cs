///
/// Copyright (c) 2013, Martijn Stolk
/// This work is available under the Creative Commons Attribution 3.0 Unported (CC BY 3.0) license:
/// http://creativecommons.org/licenses/by/3.0/
///
using System;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

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

            // Create a PartitionKey that's compatible with WAD's PartitionKey. We're rounding on seconds so that PartitionKey stays a reasonable partition, just like WAD does.
            PartitionKey = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second).Ticks.ToString("D19");

            // Value updated in TableStorageAppender.cs with a WAD-compatible format.
            RowKey = string.Format("{0:dd HH:mm:ss.fff}-{1}", now, Guid.NewGuid()); 
        }

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
