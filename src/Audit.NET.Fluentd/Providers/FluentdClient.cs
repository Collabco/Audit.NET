using Audit.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Linq;

#if(NETSTANDARD1_3)
using System.Reflection;
#endif

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

        public bool AsynchronusWrites { get; set; } = false;

        public Task Publish(AuditEvent auditEvent)
        {
            //Ensure processing task is started with first message being sent. By simply assigning the lazy value.
            var processingTask = this.processingTask.Value;
            var tcs = new TaskCompletionSource<bool>();
            queue.Enqueue(new Tuple<TaskCompletionSource<bool>, AuditEvent>(tcs, auditEvent));
            newQueueItemDelayCancellationSource?.Cancel();
            if(AsynchronusWrites) //If we are doing asynchronus writes, simply return a complete task.
            {
#if(NETSTANDARD1_3)
                return Task.CompletedTask;
#else
                return Task.FromResult(0);
#endif
            }
            return tcs.Task;
        }

        public void Dispose()
        {
            disposeClientSource.Cancel();
            newQueueItemDelayCancellationSource?.Cancel();
            Cleanup();
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
            var record = new Dictionary<string, object>();

            SetProperty(record, "id", auditEvent.CustomFields?["id"]); //This shouldn't throw a null reference exception because id is always generated in FluentDataProvider.
            SetProperty(record, "eventType", auditEvent.EventType);
            SetProperty(record, "source", auditEvent.Source ?? auditEvent.Environment?.AssemblyName);
            SetProperty(record, "level", auditEvent.AuditLevel);
            SetProperty(record, "startDate", auditEvent.StartDate);
            SetProperty(record, "endDate", auditEvent.EndDate);
            SetProperty(record, "duration", auditEvent.Duration);
            SetProperty(record, "tenantId", auditEvent.TenantId);
            SetProperty(record, "userName", auditEvent.UserName ?? auditEvent.Environment?.UserName);

            if (auditEvent.Environment != null)
            {
                SetProperty(record, "assemblyName", auditEvent.Environment.AssemblyName);
                SetProperty(record, "callingMethodName", auditEvent.Environment.CallingMethodName);
                SetProperty(record, "culture", auditEvent.Environment.Culture);
                SetProperty(record, "domainName", auditEvent.Environment.DomainName);
                SetProperty(record, "exception", auditEvent.Environment.Exception);                
                SetProperty(record, "machineName", auditEvent.Environment.MachineName);            
            }

            if (auditEvent.Target != null)
            {
                var targetDictionary = new Dictionary<string, object>();
                SetProperty(targetDictionary, "type", auditEvent.Target.Type);
                SetProperty(targetDictionary, "serializedOld", auditEvent.Target.SerializedOld);
                SetProperty(targetDictionary, "serializedNew", auditEvent.Target.SerializedNew);
                SetProperty(record, "target", targetDictionary);
            }

            if ((auditEvent.Comments?.Count ?? 0) > 0)
            {
                SetProperty(record, "comments", auditEvent.Comments.ToArray());
            }

            //If there are custom fields Serialize them in the main object
            if ((auditEvent.CustomFields?.Count ?? 0) > 0)
            {
                foreach (var property in auditEvent.CustomFields.Where(a => a.Key.ToLowerInvariant() != "id"))
                {
                    var propertyKey = property.Key.ToString();

                    if (string.IsNullOrEmpty(propertyKey) || property.Value == null)
                    {
                        continue;
                    }

                    SetProperty(record, propertyKey, property.Value);
                }
            }

            //If the event is derived then there maybe other properties
#if (NETSTANDARD1_3)
            if (auditEvent.GetType().GetTypeInfo().IsSubclassOf(typeof(AuditEvent)))
            {
                var baseTypeProperties = typeof(AuditEvent).GetRuntimeProperties().Select(p => p.Name);
                var newProperties = auditEvent.GetType().GetRuntimeProperties().Where(ap => !baseTypeProperties.Contains(ap.Name));

                foreach(var property in newProperties)
                {
                    SetProperty(record, property.Name, property.GetValue(auditEvent));
                }
            }
#else
            if (auditEvent.GetType().IsSubclassOf(typeof(AuditEvent)))
            {
                var baseTypeProperties = typeof(AuditEvent).GetProperties().Select(p => p.Name);
                var newProperties = auditEvent.GetType().GetProperties().Where(ap => !baseTypeProperties.Contains(ap.Name));

                foreach(var property in newProperties)
                {
                    SetProperty(record, property.Name, property.GetValue(auditEvent));
                }
            }
#endif

            this.emitter.Emit(auditEvent.StartDate, this.Tag, record);
        }

        private void SetProperty(Dictionary<string, object> record, string propertyName, object propertyValue)
        {
            //Convert property name to camel case
            var newPropertyName = Char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);

            if (propertyValue != null)
            {
                if (propertyValue is Guid || propertyValue is Guid?)
                {
                    record[newPropertyName] = propertyValue.ToString();
                }
                else if (propertyValue is DateTimeOffset)
                {
                    record[newPropertyName] = ((DateTimeOffset)propertyValue).ToUnixTimeMilliseconds() * 1000000;
                }
                else if(propertyValue is DateTimeOffset?)
                {
                    record[newPropertyName] = (propertyValue as DateTimeOffset?).Value.ToUnixTimeMilliseconds() * 1000000;
                }
                else if(propertyValue.GetType().IsArray)
                {
                    record[newPropertyName] = propertyValue;
                }
                else if(propertyValue is Newtonsoft.Json.Linq.JObject)
                {
                    record[newPropertyName] = (propertyValue as Newtonsoft.Json.Linq.JObject).ToObject<Dictionary<string, object>>();
                }
                else if (propertyValue is Newtonsoft.Json.Linq.JObject)
                {
                    record[propertyName] = (propertyValue as Newtonsoft.Json.Linq.JObject).ToObject<Dictionary<string, object>>();
                }
#if (NETSTANDARD1_3)
                else if (propertyValue.GetType().GetTypeInfo().ImplementedInterfaces.Any(
            i => i.IsConstructedGenericType &&
            i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
#else
                else if (propertyValue.GetType().GetInterfaces().Any(
                i => i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
#endif
                {
                    record[newPropertyName] = propertyValue;
                }
                else if (Convert.GetTypeCode(propertyValue) == TypeCode.Object)
                {
                    record[newPropertyName] = ConvertObjectToDictionary(propertyValue);
                }
                else
                {
                    record[newPropertyName] = propertyValue;
                }
            } 
        }

        private Dictionary<string, object> ConvertObjectToDictionary(object obj)
        {
            if(obj == null)
            {
                return null;
            }

            var dictionary = new Dictionary<string, object>();

#if (NETSTANDARD1_3)
            foreach (var property in obj.GetType().GetRuntimeProperties())
            {
                var propValue = property.GetValue(obj);
                SetProperty(dictionary, property.Name, propValue);
            }
#else
            foreach (var property in obj.GetType().GetProperties())
            {
                var propValue = property.GetValue(obj);
                SetProperty(dictionary, property.Name, propValue);
            }
#endif

            return dictionary;
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

#if (NETSTANDARD1_3)
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

#if (NETSTANDARD1_3)
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

#if (!NETSTANDARD1_3)
    internal static class Net45Extensions
    {
        public static long ToUnixTimeMilliseconds(this DateTimeOffset dateTimeOffset)
        {
            var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, 0, TimeSpan.Zero);
            return (long)dateTimeOffset.Subtract(epoch).TotalMilliseconds;
        }
    }
#endif
}
