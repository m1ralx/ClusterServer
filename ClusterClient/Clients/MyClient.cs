using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using log4net;

namespace ClusterClient.Clients
{
    internal class MyClient : ClusterClientBase
    {
        private const double SuspendTime = 600;
        private readonly Random random = new Random();

        public MyClient(string[] replicaAddresses) : base(replicaAddresses)
        {
            ServerAverageTime =
                new ConcurrentDictionary<string, AverageTime>(ReplicaAddresses.ToDictionary(addr => addr,
                    addr => new AverageTime(0, 0)));
            TemporarilySuspended =
                new ConcurrentDictionary<string, DateTime>(ReplicaAddresses.ToDictionary(addr => addr,
                    addr => DateTime.MinValue));
        }

        protected override ILog Log => LogManager.GetLogger(typeof (RandomClusterClient));
        private ConcurrentDictionary<string, AverageTime> ServerAverageTime{ get; }
        private ConcurrentDictionary<string, DateTime> TemporarilySuspended{ get; }

        private bool IsNotSuspended(string address)
            => !((DateTime.Now - TemporarilySuspended[address]).TotalMilliseconds < SuspendTime);

        public override async Task<string> ProcessRequestAsync(string query, TimeSpan timeout)
        {
            var newTimeout = TimeSpan.FromMilliseconds(timeout.TotalMilliseconds / ReplicaAddresses.Length);
            List<string> availableReplics = GetAvailableReplics();
            foreach (var currentUri in availableReplics)
            {
                var sw = new Stopwatch();
                var webRequest = CreateRequest(currentUri + "?query=" + query);
                Log.InfoFormat("Processing {0}", webRequest.RequestUri);
                var resultTask = ProcessRequestInternalAsync(webRequest); // TODO Continue With
                sw.Restart();
                await Task.WhenAny(resultTask, Task.Delay(newTimeout));

                if (!resultTask.IsCompleted)
                {
                    TemporarilySuspended[currentUri] = DateTime.Now;
//                    ServerAverageTime[currentUri].Add(newTimeout.TotalMilliseconds);
                    continue;
                }

                ServerAverageTime[currentUri].Add(sw.ElapsedMilliseconds);

                return resultTask.Result;
            }
            throw new TimeoutException();
        }

        private List<string> GetAvailableReplics()
        {
            var orderedReplics = ReplicaAddresses
                            .OrderBy(addr => ServerAverageTime[addr].Value)
                            .ToList();

            var availableReplics = orderedReplics.Where(IsNotSuspended).ToList();

            if (availableReplics.Count == 0)
                availableReplics = orderedReplics;
            return availableReplics;
        }

        internal class AverageTime
        {
            private int _n;

            public AverageTime(int n, double value)
            {
                _n = n;
                Value = value;
            }

            public double Value{ get; private set; }

            public void Add(double time)
            {
                _n += 1;
                Value = (Value*_n + time)/(_n + 1);
            }
        }
    }
}