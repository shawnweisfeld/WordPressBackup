using Polly;
using System;
using System.Collections.Generic;
using System.Text;

namespace WordPressBackup
{
    public class PolicyHelper
    {
        private int RetryCount { get; set; }
        public Logger Logger { get; set; }

        public PolicyHelper(int retryCount, Logger logger)
        {
            RetryCount = retryCount;
            Logger = logger;
        }

        public Policy PolicyAsync()
        {
            return Policy.WrapAsync(Policy
                  .Handle<Exception>()
                  .WaitAndRetryAsync(
                      RetryCount,
                      (retryCount, timespan) => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                      (exception, timeSpan, retryCount, context) =>
                      {
                          Logger.Log($"Retry {retryCount} : {exception.Message}");
                          Logger.Log(exception);
                      }),
                  Policy.TimeoutAsync(300));
        }

    }
}
