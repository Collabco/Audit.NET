using Audit.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Audit.Fluentd.Providers
{
    internal class FluentdClient : IDisposable
    {
        private TcpClient client;

        private Stream stream;

        private FluentdMessageEmitter emitter;

        System.Threading.CancellationTokenSource disposeClientSource;
        Lazy<Task> processingTask;

        public FluentdClient()
        {
            disposeClientSource = new System.Threading.CancellationTokenSource();
            processingTask = new Lazy<Task>(() => Task.Factory.StartNew(ProcessEvents, disposeClientSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        }

        public string Host { get; set; }

        public int Port { get; set; } = 24224;

        public string Tag { get; set; }

        public bool NoDelay { get; set; } = false;

        public int ReceiveBufferSize { get; set; } = 8192;

        public int SendBufferSize { get; set; } = 8192;

        public int SendTimeout { get; set; } = 5;

        public int ReceiveTimeout { get; set; } = 5;

        public bool LingerEnabled { get; set; } = true;

        public int LingerTime { get; set; } = 10;

        public Task Publish(AuditEvent auditEvent)
        {
            //Ensure processing task is started with first message being sent. By simply assigning the lazy value.
            var processingTask = this.processingTask.Value;
            var tcs = new TaskCompletionSource<bool>();
            queue.Enqueue(new Tuple<TaskCompletionSource<bool>, AuditEvent>(tcs, auditEvent));
            newQueueItemDelayCancellationSource?.Cancel();
            return tcs.Task;
        }

        public void Dispose()
        {
            disposeClientSource.Cancel();
            newQueueItemDelayCancellationSource?.Cancel();
            Cleanup();
        }

        private static object SerializePropertyValue(string propertyKey, object propertyValue)
        {
            if (propertyValue == null || Convert.GetTypeCode(propertyValue) != TypeCode.Object || propertyValue is decimal)
            {
                return propertyValue;   // immutable
            }
            else
            {
                return propertyValue.ToString();
            }
        }

        ConcurrentQueue<Tuple<TaskCompletionSource<bool>, AuditEvent>> queue = new ConcurrentQueue<Tuple<TaskCompletionSource<bool>, AuditEvent>>();
        
        System.Threading.CancellationTokenSource newQueueItemDelayCancellationSource;
        private async Task ProcessEvents()
        {
            while (!disposeClientSource.IsCancellationRequested)
            {
                try
                {
                    await EnsureConnected();
                }
                catch {}

                Tuple<TaskCompletionSource<bool>, AuditEvent> publishTask = null;

                try
                {
                    while (queue.TryDequeue(out publishTask))
                    {
                        if (!this.client.Connected)
                        {
                            throw new InvalidOperationException("No connection is currently available to the Fluentd forwarder, attempting to reconnect, please try again!");
                        }

                        Write(publishTask.Item2);
                        publishTask.Item1.SetResult(true);
                    }

                    //Introduce delay so that we don't spin at 100% cpu on an empty queue but provide cancellation token to cancel delay to allow immediate processing of new event
                    if (newQueueItemDelayCancellationSource == null || newQueueItemDelayCancellationSource.IsCancellationRequested)
                    {
                        newQueueItemDelayCancellationSource?.Dispose();
                        newQueueItemDelayCancellationSource = new System.Threading.CancellationTokenSource();
                    }

                    await Task.Delay(500, newQueueItemDelayCancellationSource.Token);
                }
                catch(TaskCanceledException) { } //Ignore these because they are intentional.
                catch(Exception ex)
                {
                    publishTask.Item1.SetException(ex); //Fail the publish task
                }
            }
        }

        private void Write(AuditEvent auditEvent)
        {
            var record = new Dictionary<string, object> {
                { "EventType", auditEvent.EventType },
                { "StartDate", auditEvent.StartDate },
                { "EndDate", auditEvent.EndDate },
                { "Duration", auditEvent.Duration }
            };

            if(auditEvent.Environment != null)
            {
                AddProperty(record, "AssemblyName", auditEvent.Environment.AssemblyName);
                AddProperty(record, "CallingMethodName", auditEvent.Environment.CallingMethodName);
                AddProperty(record, "Culture", auditEvent.Environment.Culture);
                AddProperty(record, "Exception", auditEvent.Environment.Exception);
                AddProperty(record, "MachineName", auditEvent.Environment.MachineName);
                AddProperty(record, "UserName", auditEvent.Environment.UserName);
            }

            //TODO: auditEvent.Target

            //If there are custom fields Serialize them.
            if ((auditEvent.CustomFields?.Count ?? 0) > 0)
            {
                foreach (var property in auditEvent.CustomFields)
                {
                    var propertyKey = property.Key.ToString();

                    if (string.IsNullOrEmpty(propertyKey) || property.Value == null)
                        continue;

                    record[propertyKey] = SerializePropertyValue(propertyKey, property.Value);
                }
            }

            this.emitter.Emit(auditEvent.StartDate, this.Tag, record);
        }

        private void AddProperty(Dictionary<string, object> record, string propertyName, object property)
        {
            if(property != null)
            {
                record.Add(propertyName, property);
            } 
        }

        private Task EnsureConnected()
        {
            if (this.client == null)
            {
                InitializeClient();
                return ConnectClient();
            }
            else if (!this.client.Connected)
            {
                Cleanup();
                InitializeClient();
                return ConnectClient();
            }

#if(NETSTANDARD1_3)
            return Task.CompletedTask;
#else
            return Task.FromResult(0);
#endif
        }

        private void InitializeClient()
        {
            this.client = new TcpClient();
            this.client.NoDelay = this.NoDelay;
            this.client.ReceiveBufferSize = this.ReceiveBufferSize;
            this.client.SendBufferSize = this.SendBufferSize;
            this.client.SendTimeout = this.SendTimeout;
            this.client.ReceiveTimeout = this.ReceiveTimeout;
            this.client.LingerState = new LingerOption(this.LingerEnabled, this.LingerTime);
        }

        private Task ConnectClient()
        {
            return this.client.ConnectAsync(this.Host, this.Port)
                .ContinueWith(connectResult => {

                    if(connectResult.IsFaulted)
                    {
                        throw new InvalidOperationException($"Unable to connect to host {Host}:{Port}. - {connectResult.Exception.InnerException.Message}");
                    }

                    this.stream = this.client.GetStream();
                    this.emitter = new FluentdMessageEmitter(this.stream);
                });
        }

        private void Cleanup()
        {
            try
            {
                this.stream?.Dispose();

#if(NETSTANDARD1_3)
                this.client?.Dispose();
#else
                this.client.Close();
#endif
            }
            catch { }
            finally
            {
                this.stream = null;
                this.client = null;
                this.emitter = null;
            }
        }
    }
}
