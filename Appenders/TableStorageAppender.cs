///
/// Copyright (c) 2013, Martijn Stolk
/// This work is available under the Creative Commons Attribution 3.0 Unported (CC BY 3.0) license:
/// http://creativecommons.org/licenses/by/3.0/
///
using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Diagnostics;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure.ServiceRuntime;
using log4net;
using log4net.Appender;
using log4net.Core;

namespace Ms.Azure.Logging.Appenders
{
    /// <summary>
    /// The TableStorageAppender is an appender that collects log statements in memory and will transfer them on a scheduled interval or
    /// when the appender is flushed.
    /// </summary>
    public class TableStorageAppender : AppenderSkeleton
    {
        private static readonly ILog _logger = LogManager.GetLogger(typeof(TableStorageAppender));

        /// <summary>
        /// The in-memory store of logentities to be saved to table storage
        /// </summary>
        private readonly Queue<WadTableEntity> _logEntities = new Queue<WadTableEntity>();

        /// <summary>
        /// Credentials for the Azure Storage to use
        /// </summary>
        public StorageCredentialsAccountAndKey StorageCredentials { get; private set; }

        /// <summary>
        /// Table within the Azure Storage to log to. Will be created if it does not exist.
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// The interval at which logentities are transferred to table storage
        /// </summary>
        public int TransferIntervalInMinutes { get; set; }

        /// <summary>
        /// The interval at which a log line is inserted, making sure the logging, the application and the storage are still alive.
        /// </summary>
        public int LogmarkerIntervalInMinutes { get; set; }

        private Thread _transferThread = null;
        private bool _refreshOptions = false;
        private DateTime _nextLogmarkerTime = DateTime.UtcNow;
        private TableServiceContext context = null;
        private object _flushLock = 0;
        private bool _isFlushing = false;
        private object _activateLock = 0;

        /// <summary>
        /// Constructs a new instance of the TableStorageAppender with default settings.
        /// </summary>
        /// <param name="credentials">Azure Storage credentials</param>
        public TableStorageAppender(StorageCredentialsAccountAndKey credentials)
        {
            StorageCredentials = credentials;
            TableName = "WADLogsTable";
            TransferIntervalInMinutes = 5;
            LogmarkerIntervalInMinutes = 30;
        }

        /// <summary>
        /// Constructs a new instance of the TableStorageAppender with default settings.
        /// </summary>
        /// <param name="storageAccountName">Storage account name</param>
        /// <param name="storageAccountKey">Storage account key</param>
        public TableStorageAppender(string storageAccountName, string storageAccountKey)
            : this(new StorageCredentialsAccountAndKey(storageAccountName, storageAccountKey))
        {
        }

        /// <summary>
        /// Activates the options that have been configured on this Appender. This appender supports reconfiguration while active.
        /// </summary>
        public override void ActivateOptions()
        {
            base.ActivateOptions();
            lock (_activateLock)
            {
                // Tell the thread to refresh its settings and reset the marker time
                _refreshOptions = true;
                _nextLogmarkerTime = DateTime.UtcNow;

                // Start the thread if it isn't running yet
                if (_transferThread == null || !_transferThread.IsAlive)
                {
                    _transferThread = new Thread(TransferThread)
                    {
                        Name = GetType().Name,
                        IsBackground = true
                    };
                    _transferThread.Start();
                }
            }
            _logger.Info("Activating options for " + GetType().Name);
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            // Determine timestamp (+tickcount) for the logentity. To maintain order we never want this to be the same as one of the previous entities.
            // As long as it is in use, we add one tick.
            DateTime timestamp = loggingEvent.TimeStamp.ToUniversalTime();
            lock (_logEntities)
            {
                var lastentity = _logEntities.LastOrDefault();
                if (lastentity != null && lastentity.Timestamp >= timestamp)
                    timestamp = lastentity.Timestamp.AddTicks(1);
            }

            // Create entity to be saved to table storage
            var entity = new WadTableEntity
            {
                Timestamp = timestamp,
                EventTickCount = timestamp.Ticks,
                DeploymentId = RoleEnvironment.IsAvailable ? RoleEnvironment.DeploymentId : "",
                Role = RoleEnvironment.IsAvailable ? RoleEnvironment.CurrentRoleInstance.Role.Name : "",
                RoleInstance = RoleEnvironment.IsAvailable ? RoleEnvironment.CurrentRoleInstance.Id : "",
                Level = loggingEvent.Level.Value,
                EventId = 0,
                Pid = Process.GetCurrentProcess().Id,
                Tid = Thread.CurrentThread.ManagedThreadId,
                Message = FormatEvent(loggingEvent) + (loggingEvent.ExceptionObject != null ? "\n" + loggingEvent.GetExceptionString() : "")
            };

            // Save the entity for later processing
            lock (_logEntities)
            {
                _logEntities.Enqueue(entity);
            }
        }

        /// <summary>
        /// Forces a flush of the logentities collected in the appender to the table storage.
        /// </summary>
        public void Flush()
        {
            try
            {
                FlushInternal();
            }
            catch (Exception ex)
            {
                _logger.Error("Error transferring logs to storage", ex);
                context = null;
                try
                {
                    // Besides the usual retry policy on the datacontext, we notice that every now and then saving to table storage fails due to, which
                    // seems like, a faulted state of the datacontext. Hence this manual retry as the datacontext will have been cleared and will
                    // be recreated on the next attempt.
                    FlushInternal();
                }
                catch (Exception ex2)
                {
                    _logger.Error("Error transferring logs to storage (again)", ex2);
                    context = null;
                }
            }
        }

        /// <summary>
        /// Actually flushes the appender internally.
        /// </summary>
        private void FlushInternal()
        {
            lock (_flushLock)
            {
                if (_isFlushing)
                    return;
                _isFlushing = true;
            }
            try
            {
                // Logmarker needed?
                if (DateTime.UtcNow > _nextLogmarkerTime)
                {
                    _nextLogmarkerTime = DateTime.UtcNow.AddMinutes(LogmarkerIntervalInMinutes);
                    _logger.Info("Logmarker...");
                }

                // Create the context if we need to
                if (context == null || _refreshOptions)
                {
                    _refreshOptions = false;
                    _logger.Debug("Creating a new TableServiceContext...");
                    CloudStorageAccount storageAccount = new CloudStorageAccount(StorageCredentials, true);
                    CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                    tableClient.CreateTableIfNotExist(TableName);
                    context = tableClient.GetDataServiceContext();
                    context.RetryPolicy = RetryPolicies.Retry(3, TimeSpan.FromMinutes(1));
                }

                // Clear the queue, add objects to context
                while (true)
                {
                    WadTableEntity entity;
                    lock (_logEntities)
                    {
                        if (_logEntities.Count == 0)
                            break;
                        entity = _logEntities.Dequeue();
                    }
                    context.AddObject(TableName, entity);
                }

                // Save the changes (i/o operation)
                context.SaveChangesWithRetries();
            }
            finally
            {
                lock (_flushLock)
                {
                    _isFlushing = false;
                }
            }
        }

        /// <summary>
        /// Use the layoutoptions to format the event.
        /// </summary>
        private string FormatEvent(LoggingEvent evt)
        {
            System.IO.StringWriter sw = new System.IO.StringWriter();
            this.Layout.Format(sw, evt);
            return sw.ToString().Trim(); // Remove any trailing newlines
        }

        /// <summary>
        /// The transfer thread that will keep running until the application is closed. As this is a background thread, it will not block the
        /// application when it is trying to exit.
        /// </summary>
        private void TransferThread()
        {
            while (true)
            {
                // Validate minimum transfer interval
                if (TransferIntervalInMinutes < 1)
                {
                    _logger.Info("TransferIntervalInMinutes is set to less than one minute, which is invalid. Setting this to the minimum value of 1.");
                    TransferIntervalInMinutes = 1;
                }

                // Validate minimum logmarker interval
                if (LogmarkerIntervalInMinutes < 1)
                {
                    _logger.Info("LogmarkerIntervalInMinutes is set to less than one minute, which is invalid. Setting this to the minimum value of 1.");
                    LogmarkerIntervalInMinutes = 1;
                }

                // Sleep for a bit
                Thread.Sleep(TransferIntervalInMinutes * 60 * 1000);

                // Flush
                Flush();
            }
        }
    }
}
