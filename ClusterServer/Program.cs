using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using log4net.Config;

namespace ClusterServer
{
    public static class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof (Program));

        private static readonly byte[] Key = Encoding.UTF8.GetBytes("Контур.Шпора");
        private static int RequestId;

        public static void Main(string[] args)
        {
            XmlConfigurator.Configure();

            try
            {
                ServerArguments parsedArguments;
                if (!ServerArguments.TryGetArguments(args, out parsedArguments))
                    return;
                var listener = new HttpListener
                {
                    Prefixes =
                    {
                        $"http://{parsedArguments.MethodName}:{parsedArguments.Port}/"
                    }
                };
                listener.ProcessRequests(CreateCallback(parsedArguments));
            }
            catch (Exception e)
            {
                Log.Fatal(e);
            }
        }
        private static readonly ConcurrentDictionary<int, CancellationTokenSource> Tasks = new ConcurrentDictionary<int, CancellationTokenSource>();
        private static Func<HttpListenerContext, Task> CreateCallback(ServerArguments parsedArguments)
        {   
            return async context =>
            {
                var currentRequestId = Interlocked.Increment(ref RequestId);
                Log.Info(
                    $"Thread #{Thread.CurrentThread.ManagedThreadId} received request #{currentRequestId} at {DateTime.Now.TimeOfDay}");

                var id = int.Parse(context.Request.QueryString["id"].Trim());
                if (context.Request.QueryString["deny"] == "True")
                {
                    await DenyTask(id);
                }
                else
                {
                    var ctSource = new CancellationTokenSource();
                    var ct = ctSource.Token;
                    Tasks.AddOrUpdate(id, ctSource, (key, oldValue) => ctSource);
                    try
                    {
                        await Task.Delay(parsedArguments.MethodDuration, ct);
                    }
                    // Исключение возникает когда кто-то отменяет задачу с помощью ctSource
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                    var encryptedBytes = GetBase64HashBytes(context.Request.QueryString["query"], Encoding.UTF8);
                    // ReSharper disable once MethodSupportsCancellation
//                    if (context.Response.OutputStream != null)
                    await context.Response.OutputStream.WriteAsync(encryptedBytes, 0, encryptedBytes.Length);

                    Log.Info(
                        $"Thread #{Thread.CurrentThread.ManagedThreadId} sent response #{currentRequestId} at {DateTime.Now.TimeOfDay}");
                }
            };
        }

        private static async Task DenyTask(int id)
        {
            try
            {
                if (Tasks[id].Token.CanBeCanceled)
                    Tasks[id].Cancel();
            }
            catch (KeyNotFoundException e)
            {
                Log.Error($"Key not found : {id}");
            }
            Log.Info($"Thread #{Thread.CurrentThread.ManagedThreadId} denied task #{id}");
            await Task.CompletedTask;
        }

        private static byte[] GetBase64HashBytes(string query, Encoding encoding)
        {
            using (var hasher = new HMACMD5(Key))
            {
                var hash = Convert.ToBase64String(hasher.ComputeHash(encoding.GetBytes(query)));
                return encoding.GetBytes(hash);
            }
        }
    }
}