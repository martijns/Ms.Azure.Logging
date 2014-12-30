using log4net;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using Microsoft.WindowsAzure.Storage;
using Ms.Azure.Logging.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ms.Azure.Logging.TestApp
{
    class Program
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Program));

        static void Main(string[] args)
        {
            LoggingHelper.InitializeAzureTableLogging(CloudStorageAccount.DevelopmentStorageAccount);
            (LogManager.GetRepository() as Hierarchy).Root.AddAppender(new log4net.Appender.ConsoleAppender { Layout = new PatternLayout("%date %-5level [%-3thread] %logger - %message%newline") });
            Logger.Info("Logging initialized");
            for (int i = 1; i <= 10; i++)
            {
                Logger.Info("Message " + i + "/" + 10);
                Thread.Sleep(1000);
            }
            Logger.Info("Done");
            LoggingHelper.FlushAppenders();
        }
    }
}
