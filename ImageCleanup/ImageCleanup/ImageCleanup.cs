﻿namespace ImageCleanup
{
    using System;
    using System.Configuration;
    using System.IO;
    using System.ServiceProcess;
    using System.Timers;

    using ImageCleanupLib;

    using log4net;

    using Microsoft.Practices.Unity;

    public partial class ImageCleanup : ServiceBase
    {
        #region Constants

        private const string ConfigKeyImageDirectory = "ROOT_IMAGE_DIRECTORY";

        private const string ConfigKeyPeriodTimespan = "PERIOD_TIMESPAN";

        private const string ConfigKeyRetentionPeriodTimespan = "RETENTION_PERIOD_TIMESPAN";

        private const string InvalidValueForConfigurationKey = "Could not parse value: {1}  for configuration key: {0}";

        #endregion

        #region Static Fields

        private static readonly ILog Log = LogManager.GetLogger(typeof(ImageCleanup));

        #endregion

        #region Fields

        private volatile bool deleteRunning;

        #endregion

        #region Constructors and Destructors

        public ImageCleanup()
        {
            this.InitializeComponent();
            AppDomain.CurrentDomain.UnhandledException +=
                (sender, args) =>
                Log.Fatal(string.Format("Unhandled Exception from {0}", sender), args.ExceptionObject as Exception);
            this.deleteRunning = false;
        }

        #endregion

        #region Methods

        internal void InternalStart(string[] args)
        {
            this.OnStart(args);
        }

        internal void InternalStop()
        {
            this.OnStop();
        }

        protected override void OnStart(string[] args)
        {
            Log.Info("starting");

            GetRootDirectory();

            var interval = GetConfigurationParameter(
                ConfigKeyPeriodTimespan,
                TimeSpan.Parse);
            var timer = new Timer { Interval = interval.TotalMilliseconds };
            timer.Elapsed += this.OnTimer;
            timer.Start();
        }

        protected override void OnStop()
        {
            Log.Info("stopping");
        }

        private static T GetConfigurationParameter<T>(string keyName, Func<string, T> converter)
        {
            var stringValue = ConfigurationManager.AppSettings.Get(keyName);
            T value;
            try
            {
                value = converter(stringValue);
            }
            catch (Exception e)
            {
                Log.ErrorFormat(InvalidValueForConfigurationKey, keyName, stringValue);
                throw new ConfigurationErrorsException(keyName, e);
            }

            return value;
        }

        private static DirectoryInfo GetRootDirectory()
        {
            var rootDirectory = GetConfigurationParameter(
                ConfigKeyImageDirectory, 
                s =>
                    {
                        var result = new DirectoryInfo(s);

                        if (!result.Exists)
                        {
                            throw new ConfigurationErrorsException(ConfigKeyImageDirectory);
                        }

                        return result;
                    });
            return rootDirectory;
        }

        private void OnTimer(object sender, ElapsedEventArgs args)
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (s, a) => Log.Fatal(string.Format("Unhandled Exception from {0}", s), a.ExceptionObject as Exception);

            var retentionTimeSpan = GetConfigurationParameter(
                ConfigKeyRetentionPeriodTimespan, 
                TimeSpan.Parse);

            var cutoffTime = DateTime.Now.Subtract(retentionTimeSpan);
            var rootDirectory = GetRootDirectory();

            var container = ContainerManager.GetContainer();
            var deleter = container.Resolve<IImageDeleter>();

            if (this.deleteRunning)
            {
                Log.InfoFormat(
                    "Delete is running skipping starting a new delete. Consider raising the {0} and restarting the service", 
                    ConfigKeyPeriodTimespan);
                return;
            }

            Log.InfoFormat("Begining deletion of images older than {0} from root {1}", cutoffTime, rootDirectory);
            this.deleteRunning = true;
            deleter.Run(cutoffTime, rootDirectory.FullName);
            this.deleteRunning = false;
        }

        #endregion
    }
}