using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using log4net;

namespace ClusterClient.Clients
{
    internal class RequestAllClient : ClusterClientBase
    {
        public RequestAllClient(string[] replicaAddresses) : base(replicaAddresses)
        {
        }
        public class AddressAndTask
        {
            public readonly string Address;
            public readonly Task<string> Task;
            public AddressAndTask(string address, Task<string> task)
            {
                Address = address;
                Task = task;
            }
        }
        protected override ILog Log => LogManager.GetLogger(typeof (RandomClusterClient));
        private volatile int _requestId;
        public override async Task<string> ProcessRequestAsync(string query, TimeSpan timeout)
        {
            var currentRequestId = Interlocked.Increment(ref _requestId);
            List<AddressAndTask> requestTasksWithAddress = GetTasksWithAddress(query, currentRequestId);

            var taskDelay = new[] { Task.Delay(timeout) };

            while (true)
            {
                var requestTaskWithDelay = requestTasksWithAddress
                    .Select(t => t.Task)
                    .Concat(taskDelay);
                var firstFinishedTask = await Task.WhenAny(requestTaskWithDelay);

                ThrowIfTimeout(taskDelay[0]);

                var firstFinishedRequest = requestTasksWithAddress
                    .FirstOrDefault(t => t.Task.Status == TaskStatus.RanToCompletion);

                if (firstFinishedRequest != null)
                {
                    DenyAllTasks(query, currentRequestId, requestTasksWithAddress, firstFinishedTask);

                    return firstFinishedRequest.Task.Result;
                }
                requestTasksWithAddress.RemoveAll(t => t.Task == firstFinishedTask);
            }
        }

        private List<AddressAndTask> GetTasksWithAddress(string query, int currentRequestId)
        {
            return ReplicaAddresses
                .Select(addr =>
                {
                    var request = CreateRequest($"{addr}?query={query}&id={currentRequestId}&deny={false}");
                    Log.InfoFormat("Processing {0}", request.RequestUri);
                    return new AddressAndTask(addr, ProcessRequestInternalAsync(request));
                })
                .Where(t => t != null)
                .ToList();
        }

        private static void DenyAllTasks(string query, int currentRequestId, List<AddressAndTask> requestTasks, Task firstFinishedTask)
        {
            requestTasks
                .Where(t => t.Task != firstFinishedTask)
                .ToList()
                .ForEach(t => DenyTask(currentRequestId, query, t.Address));
        }

        private static void ThrowIfTimeout(Task taskDelay)
        {
            if (taskDelay.IsCompleted)
                throw new TimeoutException();
        }

        private static async void DenyTask(int id, string query, string address)
        {
            var request = CreateRequest($"{address}?query={query}&id={id}&deny={true}");
            using (await request.GetResponseAsync())
            {
            }
        }
    }
}