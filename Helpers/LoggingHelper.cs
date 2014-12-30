///
/// Copyright (c) 2013, Martijn Stolk
/// This work is available under the Creative Commons Attribution 3.0 Unported (CC BY 3.0) license:
/// http://creativecommons.org/licenses/by/3.0/
///
using System.Collections.Specialized;
using System.IO;
using Microsoft.WindowsAzure;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using Ms.Azure.Logging.Appenders;
using Microsoft.WindowsAzure.Storage.Auth;
using System;
using Microsoft.WindowsAzure.Storage;

namespace Ms.Azure.Logging.Helpers
{

    /// <summary>
    /// Helper class to initialize logging for Azure webroles, workerroles and webapplications
    /// </summary>
    /// <remarks>
    /// This class is meant to make it as easy as possible to enable logging, using just a few statements. Some defaults are chosen
    /// that might not fit all situations, such as a TransferInterval on the TableStorageAppender of 5 minutes.
    /// </remarks>
    public static class LoggingHelper
    {
        private static ILog _logger = LogManager.GetLogger(typeof(LoggingHelper));
        private static bool _isAzureTableLoggingConfigured = false;
        private static bool _isFileLoggingConfigured = false;

        /// <summary>
        /// Clear all existing appenders and loggers
        /// </summary>
        public static void ClearAllLoggers()
        {
            Hierarchy hierarchy = LogManager.GetRepository() as Hierarchy;
            hierarchy.Configured = false;
            hierarchy.ResetConfiguration();
            _isAzureTableLoggingConfigured = false;
            _isFileLoggingConfigured = false;
        }

        /// <summary>
        /// Initializes logging from configuration. The NameValueCollection will read the following properties:
        ///  - LogType: TableStorage or File
        ///  - LogStorageName: account name for the storage account
        ///  - LogStorageKey: key for the storage account
        ///  - LogStorageTable: optional, customize the table to log to
        ///  - LogFile: Filename to log to
        ///  - LogStorageString: use a connection string to indicate storage account and credentials (makes LogStorageName and LogStorageKey obsolete and has preference)
        /// </summary>
        /// <param name="config"></param>
        public static void InitializeFromConfiguration(NameValueCollection config)
        {
            string logtype = config["LogType"];
            string loglevel = config["LogLevel"];
            if (logtype == "TableStorage")
            {
                string storageName = config["LogStorageName"];
                string storageKey = config["LogStorageKey"];
                string storageString = config["LogStorageString"];
                string storageTable = config["LogStorageCustomTable"];
                if (!string.IsNullOrWhiteSpace(storageString))
                    InitializeAzureTableLogging(CloudStorageAccount.Parse(storageString), storageTable, DetermineLevel(loglevel));
                else if (!string.IsNullOrWhiteSpace(storageName) && !string.IsNullOrWhiteSpace(storageKey))
                    InitializeAzureTableLogging(new StorageCredentials(storageName, storageKey), storageTable, DetermineLevel(loglevel));
            }
            if (logtype == "File")
            {
                string logfile = config["LogFile"];
                if (!string.IsNullOrWhiteSpace(logfile))
                    LoggingHelper.InitializeFileLogging(logfile, DetermineLevel(loglevel));
            }
        }

        /// <summary>
        /// Converts a string loglevel to the correct type in order to configure an appender
        /// </summary>
        /// <remarks>
        /// Things would be so much easier and flexible if they would've made it an Enum or some collection
        /// </remarks>
        private static Level DetermineLevel(string level)
        {
            if (level == null)
                return Level.All;
            switch (level.ToUpperInvariant())
            {
                case "ALERT":
                    return Level.Alert;
                case "CRITICAL":
                    return Level.Critical;
                case "DEBUG":
                    return Level.Debug;
                case "EMERGENCY":
                    return Level.Emergency;
                case "ERROR":
                    return Level.Error;
                case "FATAL":
                    return Level.Fatal;
                case "FINE":
                    return Level.Fine;
                case "FINER":
                    return Level.Finer;
                case "FINEST":
                    return Level.Finest;
                case "INFO":
                    return Level.Info;
                case "NOTICE":
                    return Level.Notice;
                case "OFF":
                    return Level.Off;
                case "SEVERE":
                    return Level.Severe;
                case "TRACE":
                    return Level.Trace;
                case "VERBOSE":
                    return Level.Verbose;
                case "WARN":
                    return Level.Warn;
                case "ALL":
                default:
                    return Level.All;
            }
        }

        /// <summary>
        /// Initializes log4net with azure table logging.
        /// </summary>
        public static void InitializeAzureTableLogging(StorageCredentials credentials, string customTable = null, Level logLevel = null)
        {
            if (credentials.AccountName.StartsWith("devstoreaccount"))
                InitializeAzureTableLogging(CloudStorageAccount.DevelopmentStorageAccount, customTable, logLevel);
            else
                InitializeAzureTableLogging(new CloudStorageAccount(credentials, true), customTable, logLevel);
        }

        /// <summary>
        /// Initializes log4net with azure table logging.
        /// </summary>
        public static void InitializeAzureTableLogging(CloudStorageAccount storageAccount, string customTable = null, Level logLevel = null)
        {
            // log4net configuration must be done only once
            lock (_logger)
            {
                if (_isAzureTableLoggingConfigured)
                    return;
                _isAzureTableLoggingConfigured = true;
            }

            // Layout for our renderedmessage
            PatternLayout layout = new PatternLayout("%date %-5level [%-3thread] %logger - %message%newline");
            layout.ActivateOptions();

            // Configure appender
            TableStorageAppender tsa = new TableStorageAppender(storageAccount)
            {
                Layout = layout,
                Threshold = Level.Debug,
                Name = "TableStorageAppender",
                TransferIntervalInMinutes = 1,
                LogmarkerIntervalInMinutes = 15
            };
            if (!string.IsNullOrWhiteSpace(customTable))
                tsa.TableName = customTable;
            tsa.ActivateOptions();

            // Configure hierarchy
            Hierarchy hierarchy = LogManager.GetRepository() as Hierarchy;
            hierarchy.Root.AddAppender(tsa);
            hierarchy.Root.Level = logLevel ?? Level.All;
            hierarchy.Configured = true;
            _logger.Info("Logging to Azure Table Storage has been initialized (accountname=" + storageAccount.Credentials.AccountName + ")...");
        }

        /// <summary>
        /// Initializes log4net with file logging.
        /// </summary>
        public static void InitializeFileLogging(string logfile, Level logLevel = null)
        {
            // log4net configuration must be done only once
            lock (_logger)
            {
                if (_isFileLoggingConfigured)
                    return;
                _isFileLoggingConfigured = true;
            }

            // Layout for our renderedmessage
            PatternLayout layout = new PatternLayout("%date %-5level [%-3thread] %logger - %message%newline");
            layout.ActivateOptions();

            // Configure appender
            RollingFileAppender rfa = new RollingFileAppender
            {
                File = logfile.Replace("{tempdir}", Path.GetTempPath()),
                AppendToFile = true,
                RollingStyle = RollingFileAppender.RollingMode.Size,
                MaxSizeRollBackups = 10,
                MaximumFileSize = "5MB",
                StaticLogFileName = true,
                Layout = layout
            };
            rfa.ActivateOptions();

            // Configure hierarchy
            Hierarchy hierarchy = LogManager.GetRepository() as Hierarchy;
            hierarchy.Root.AddAppender(rfa);
            hierarchy.Root.Level = logLevel ?? Level.All;
            hierarchy.Configured = true;
            _logger.Info("Logging to local file storage has been initialized (filename=" + rfa.File + ")...");
        }

        /// <summary>
        /// Flush any appenders that we know to support flushing. Typically used to flush the TableStorageAppender just before the application shuts down.
        /// </summary>
        public static void FlushAppenders()
        {
            Hierarchy hierarchy = LogManager.GetRepository() as Hierarchy;
            if (hierarchy == null)
                return;

            foreach (var appender in hierarchy.Root.Appenders)
            {
                if (appender is TableStorageAppender)
                {
                    var tsa = (TableStorageAppender)appender;
                    tsa.Flush();
                }
                if (appender is BufferingAppenderSkeleton)
                {
                    var bas = (BufferingAppenderSkeleton)appender;
                    bas.Flush();
                }
            }
        }
    }
}