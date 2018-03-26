using Audit.Core;
using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Audit.Mqtt.Providers
{
    /// <summary>
    /// Send Audit Logs as Mqtt messages to a Mqtt broker
    /// </summary>
    /// <remarks>
    /// Settings:
    /// - RemoteAddress: remote host or multicast group to send the events.
    /// - RemotePort: remote port to send the events.
    /// - CustomSerializer: to specify a custom serialization method to send MQTT messages.
    /// - CustomDeserializer: to specify a custom deserialization method to receive MQTT messages.
    /// </remarks>
    public class MqttDataProvider : AuditDataProvider, IDisposable
    {

        Lazy<Task<IMqttClient>> _lazyConnection;
        IMqttClient _mqttClient;

        public MqttDataProvider()
        {
            CreateLazyConnection();
        }

        /// <summary>
        /// Gets or sets the address of the remote host to which the underlying MqttClient should send the audit events.
        /// </summary>
        public string RemoteAddress { get; set; }

        /// <summary>
        /// Gets or sets the port number of the remote host to which the underlying MqttClient should send the audit events.
        /// </summary>
        public int RemotePort { get; set; } = 1883;

        /// <summary>
        /// Gets or sets property indicated if true to connect using TLS
        /// </summary>
        public bool EnableTLS { get; set; }

        /// <summary>
        /// Gets or sets the topic to send the MQTT messages to.
        /// </summary>
        public string TopicName { get; set; }

        /// <summary>
        /// Gets or sets the credentias to use to connect to the MQTT broker.
        /// </summary>
        public System.Net.NetworkCredential Credentials { get; set; }

        /// <summary>
        /// Gets or sets a custom serialization method to send MQTT messages.
        /// </summary>
        public Func<AuditEvent, byte[]> CustomSerializer { get; set; }

        /// <summary>
        /// Gets or sets a custom deserialization method to receive MQTT messages.
        /// </summary>
        public Func<byte[], AuditEvent> CustomDeserializer { get; set; }

        /// <summary>
        /// Sends an event as an MQTT message
        /// </summary>
        /// <param name="auditEvent">The audit event being created.</param>
        public override object InsertEvent(AuditEvent auditEvent)
        {
            var eventId = Guid.NewGuid();
            Send(eventId, auditEvent);
            return eventId;
        }

        /// <summary>
        /// Sends an event asychronously as an MQTT message
        /// </summary>
        /// <param name="auditEvent">The audit event being created.</param>
        public override async Task<object> InsertEventAsync(AuditEvent auditEvent)
        {
            var eventId = Guid.NewGuid();
            await SendAsync(eventId, auditEvent).ConfigureAwait(false);
            return eventId;
        }

        /// <summary>
        /// Sends an event as an MQTT message, related to a previous event
        /// </summary>
        /// <param name="auditEvent">The audit event.</param>
        /// <param name="eventId">The event id being replaced.</param>
        public override void ReplaceEvent(object eventId, AuditEvent auditEvent)
        {
            Send(eventId, auditEvent);
        }

        /// <summary>
        /// Sends an event asychronously as an MQTT message, related to a previous event
        /// </summary>
        /// <param name="auditEvent">The audit event.</param>
        /// <param name="eventId">The event id being replaced.</param>
        public override async Task ReplaceEventAsync(object eventId, AuditEvent auditEvent)
        {
            await SendAsync(eventId, auditEvent);
        }

        private void CreateLazyConnection()
        {
            _lazyConnection = new Lazy<Task<IMqttClient>>(() =>
            {
                return GetClient()
                    .ContinueWith(clientResult =>
                    {
                        this._mqttClient = clientResult.Result;
                        return this._mqttClient;
                    });
            });
        }

        private void Send(object eventId, AuditEvent auditEvent)
        {
            auditEvent.CustomFields["id"] = eventId;
            var client = _lazyConnection.Value.GetAwaiter().GetResult();
            var buffer = SerializeEvent(auditEvent);

            var message = new MqttApplicationMessageBuilder()
           .WithTopic(TopicName)
           .WithPayload(buffer)
           .WithExactlyOnceQoS()
           //.WithRetainFlag()
           .Build();
       
            _mqttClient.PublishAsync(message).GetAwaiter().GetResult();
        }

        private async Task SendAsync(object eventId, AuditEvent auditEvent)
        {
            auditEvent.CustomFields["id"] = eventId;
            var client = await _lazyConnection.Value.ConfigureAwait(false);
            var buffer = SerializeEvent(auditEvent);
     
            var message = new MqttApplicationMessageBuilder()
            .WithTopic(TopicName)
            .WithPayload(buffer)
            .WithExactlyOnceQoS()
          //  .WithRetainFlag()
            .Build();

            await client.PublishAsync(message).ConfigureAwait(false);
        }

        private byte[] SerializeEvent(AuditEvent auditEvent)
        {
            if (CustomSerializer != null)
            {
                return CustomSerializer.Invoke(auditEvent);
            }
            return Encoding.UTF8.GetBytes(auditEvent.ToJson());
        }

        private AuditEvent DeserializeEvent(byte[] data)
        {
            if (CustomDeserializer != null)
            {
                return CustomDeserializer.Invoke(data);
            }
            return JsonConvert.DeserializeObject<AuditEvent>(Encoding.UTF8.GetString(data));
        }

        private Task<MQTTnet.Client.IMqttClient> GetClient()
        {
            var factory = new MqttFactory();
            var mqttClient = factory.CreateMqttClient();
            var builder = new MqttClientOptionsBuilder()
            .WithKeepAlivePeriod(TimeSpan.FromMinutes(60))
            .WithTcpServer(RemoteAddress, RemotePort)
            .WithProtocolVersion(MQTTnet.Serializer.MqttProtocolVersion.V311);

            if(EnableTLS)
            {
                builder = builder.WithTls();
            }

            if(Credentials != null)
            {
                builder = builder.WithCredentials(Credentials.UserName, Credentials.Password);
            }

            var options = builder.Build();

            return mqttClient.ConnectAsync(options)
                .ContinueWith(connectResult => 
                {
                    if(connectResult.IsFaulted)
                    {
                        throw new Exception($"Unable to connect to MQTT broker {RemoteAddress}:{RemotePort}");
                    }

                    mqttClient.Disconnected += async (s, e) =>
                    {
                        //Console.WriteLine("### DISCONNECTED FROM SERVER ###");
                        await Task.Delay(TimeSpan.FromSeconds(1));
                       
                        try
                        {
                            await mqttClient.ConnectAsync(options);
                        }
                        catch
                        {
                            //Console.WriteLine("### RECONNECTING FAILED ###");
                        }
                    };

                    return mqttClient;
                });
        }

        public void Dispose()
        {
            _mqttClient?.Dispose();
            _mqttClient = null;
        }
    }
}
