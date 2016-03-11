using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using log4net;

namespace ClusterClient.Clients
{
    public class SmartClient : ClusterClientBase
    {
        private readonly Random random = new Random();

        public SmartClient(string[] replicaAddresses) : base(replicaAddresses)
        {
            PreviousTasks = new List<Task<string>>();
        }

        protected override ILog Log => LogManager.GetLogger(typeof (RandomClusterClient));
        private List<Task<string>> PreviousTasks{ get; }

        public override async Task<string> ProcessRequestAsync(string query, TimeSpan timeout)
        {
            var newTimeout = TimeSpan.FromMilliseconds(timeout.TotalMilliseconds/ReplicaAddresses.Length);
            foreach (var currentUri in ReplicaAddresses)
            {
                var finishedTask = PreviousTasks.FirstOrDefault(task => task != null && task.IsCompleted);
                if (finishedTask != null)
                    return finishedTask.Result;

                var webRequest = CreateRequest(currentUri + "?query=" + query);
                Log.InfoFormat("Processing {0}", webRequest.RequestUri);
                Task<string> resultTask;
                try
                {
                    resultTask = ProcessRequestInternalAsync(webRequest);
                }
                catch (Exception e)
                {
                    Log.Error($"Error while Processing {webRequest.RequestUri}");
                    continue;
                }

                await Task.WhenAny(resultTask, Task.Delay(newTimeout));
                
                if (!resultTask.IsCompleted)
                {
                    PreviousTasks.Add(resultTask);
                    continue;
                }
                return resultTask.Result;
            }
            throw new TimeoutException();
        }
    }
}