using System;
using System.Threading.Tasks;
using log4net;

namespace ClusterClient.Clients
{
    public class RandomClusterClient : ClusterClientBase
    {
        private readonly Random random = new Random();

        public RandomClusterClient(string[] replicaAddresses)
            : base(replicaAddresses)
        {
        }

        protected override ILog Log => LogManager.GetLogger(typeof (RandomClusterClient));

        public override async Task<string> ProcessRequestAsync(string query, TimeSpan timeout)
        {
            var randomUri = ReplicaAddresses[random.Next(ReplicaAddresses.Length)];
            var randomWebRequest = CreateRequest(randomUri + "?query=" + query);

            Log.InfoFormat("Processing {0}", randomWebRequest.RequestUri);

            var resultTask = ProcessRequestInternalAsync(randomWebRequest);
            await Task.WhenAny(resultTask, Task.Delay(timeout));
            if (!resultTask.IsCompleted)
                throw new TimeoutException();

            return resultTask.Result;
        }
    }
}