using System;
using System.Linq;
using System.Threading.Tasks;
using log4net;

namespace ClusterClient.Clients
{
    internal class RoundRobinClient : ClusterClientBase
    {
        private readonly Random random = new Random();

        public RoundRobinClient(string[] replicaAddresses) : base(replicaAddresses)
        {
        }

        protected override ILog Log => LogManager.GetLogger(typeof (RandomClusterClient));

        public override async Task<string> ProcessRequestAsync(string query, TimeSpan timeout)
        {
            var newTimeout = TimeSpan.FromMilliseconds(timeout.TotalMilliseconds/ReplicaAddresses.Length);
            var orderedReplics = ReplicaAddresses.OrderBy(a => random.Next()).ToList();
            foreach (var currentUri in orderedReplics)
            {
                var webRequest = CreateRequest(currentUri + "?query=" + query);
                Log.InfoFormat("Processing {0}", webRequest.RequestUri);
                var resultTask = ProcessRequestInternalAsync(webRequest);
                await Task.WhenAny(resultTask, Task.Delay(newTimeout));
                if (!resultTask.IsCompleted)
                    continue;
                return resultTask.Result;
            }
            throw new TimeoutException();
        }
    }
}