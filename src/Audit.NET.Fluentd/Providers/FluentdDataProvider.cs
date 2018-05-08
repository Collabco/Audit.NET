using Audit.Core;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Audit.Fluentd.Providers
{
    /// <summary>
    /// Send Audit Logs as Fluentd messages to a Fluentd fowarder
    /// </summary>
    /// <remarks>
    /// Settings:
    /// - RemoteAddress: remote host to send the events.
    /// - RemotePort: remote port to send the events.
    /// </remarks>
    public class FluentdDataProvider : AuditDataProvider, IDisposable
    {
        Lazy<FluentdClient> _client;
       
        public FluentdDataProvider()
        {
            _client = new Lazy<FluentdClient>(() => Initalize());
        }

        /// <summary>
        /// Gets or sets the address of the remote host to which the underlying FluentdClient should send the audit events.
        /// </summary>
        public string RemoteAddress { get; set; }

        /// <summary>
        /// Gets or sets the port number of the remote host to which the underlying FluentdClient should send the audit events.
        /// </summary>
        public int RemotePort { get; set; } = 24224;

        /// <summary>
        /// Gets or sets the tag to use for the Fluentd messages
        /// </summary>
        public string Tag { get; set; }

        /// <summary>
        /// Sends an event as an Fluentd message
        /// </summary>
        /// <param name="auditEvent">The audit event being created.</param>
        public override object InsertEvent(AuditEvent auditEvent)
        {
            var eventId = Guid.NewGuid();
            Send(eventId, auditEvent);
            return eventId;
        }

        /// <summary>
        /// Sends an event asychronously as an Fluentd message
        /// </summary>
        /// <param name="auditEvent">The audit event being created.</param>
        public override async Task<object> InsertEventAsync(AuditEvent auditEvent)
        {
            var eventId = Guid.NewGuid();
            await SendAsync(eventId, auditEvent).ConfigureAwait(false);
            return eventId;
        }

        /// <summary>
        /// Sends an event as an Fluentd message, related to a previous event
        /// </summary>
        /// <param name="auditEvent">The audit event.</param>
        /// <param name="eventId">The event id being replaced.</param>
        public override void ReplaceEvent(object eventId, AuditEvent auditEvent)
        {
            Send(eventId, auditEvent);
        }

        /// <summary>
        /// Sends an event asychronously as an Fluentd message, related to a previous event
        /// </summary>
        /// <param name="auditEvent">The audit event.</param>
        /// <param name="eventId">The event id being replaced.</param>
        public override Task ReplaceEventAsync(object eventId, AuditEvent auditEvent)
        {
            return SendAsync(eventId, auditEvent);
        }

        private FluentdClient Initalize()
        {
            return new FluentdClient
            {
                 Host = this.RemoteAddress,
                 Port = this.RemotePort,
                 Tag = this.Tag
            };
        }

        private void Send(object eventId, AuditEvent auditEvent)
        {
            auditEvent.CustomFields["_id"] = eventId;
            _client.Value.Publish(auditEvent).GetAwaiter().GetResult();
        }

        private Task SendAsync(object eventId, AuditEvent auditEvent)
        {
           auditEvent.CustomFields["_id"] = eventId;
           return _client.Value.Publish(auditEvent);
        }

        public void Dispose()
        {
            if(_client.IsValueCreated)
            {
                _client.Value.Dispose();
            }
        }
    }
}
