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
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> Tasks = new ConcurrentDictionary<string, CancellationTokenSource>();

        private static Func<HttpListenerContext, Task> CreateCallback(ServerArguments parsedArguments)
        {   
            return async context =>
            {
                var currentRequestId = Interlocked.Increment(ref RequestId);
                Log.Info(
                    $"Thread #{Thread.CurrentThread.ManagedThreadId} received request #{currentRequestId} at {DateTime.Now.TimeOfDay}");

                var id = context.Request.UserHostAddress + "#" + GetTaskId(context);
                if (IsDenyRequest(context))
                {
                    DenyTask(id);
                }
                else
                {
                    var ctSource = new CancellationTokenSource();
                    var cancellationToken = ctSource.Token;
                    // ReSharper disable once AccessToModifiedClosure не понял почему R# предлагает сделать вот так: О_о
                    // var source = ctSource;
                    Tasks.AddOrUpdate(id, ctSource, (key, oldValue) => ctSource);
                    try
                    {
                        await Task.Delay(parsedArguments.MethodDuration, cancellationToken);
                    }
                    // Исключение возникает когда кто-то отменяет задачу с помощью ctSource
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                    await EvalAndSendResponse(context, currentRequestId, id);
                }
            };
        }

        private static bool IsDenyRequest(HttpListenerContext context)
        {
            return context.Request.QueryString["deny"] == "True";
        }

        private static string GetTaskId(HttpListenerContext context)
        {
            return context.Request.QueryString["id"].Trim();
        }

        private static async Task EvalAndSendResponse(HttpListenerContext context, int currentRequestId, string id)
        {
            var encryptedBytes = GetBase64HashBytes(context.Request.QueryString["query"], Encoding.UTF8);
            // ReSharper disable once MethodSupportsCancellation
            await context.Response.OutputStream.WriteAsync(encryptedBytes, 0, encryptedBytes.Length);
            RemoveFromTasks(id);
            Log.Info(
                $"Thread #{Thread.CurrentThread.ManagedThreadId} sent response #{currentRequestId} at {DateTime.Now.TimeOfDay}");
        }

        private static void RemoveFromTasks(string id)
        {
            CancellationTokenSource ctSource;
            Tasks.TryRemove(id, out ctSource);
        }

        private static void DenyTask(string id)
        {
            try
            {
                Tasks[id].Cancel();
            }
            catch (KeyNotFoundException)
            {
                Log.Error($"Key not found : {id}");
            }
            Log.Info($"Thread #{Thread.CurrentThread.ManagedThreadId} denied task {id}");
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