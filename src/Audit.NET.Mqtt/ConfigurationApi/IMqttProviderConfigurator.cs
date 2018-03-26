using Audit.Core;
using System;
using System.Net;

namespace Audit.Mqtt
.Configuration
{
    /// <summary>
    /// Provides a configuration for the MQTT data provider
    /// </summary>
    public interface IMqttProviderConfigurator
    {
        /// <summary>
        /// Specifies the address of the remote host to send the audit events.
        /// </summary>
        /// <param name="address">The IP address.</param>
        IMqttProviderConfigurator RemoteAddress(string address);

        /// <summary>
        /// Specifies the port of the remote host to send the audit events.
        /// </summary>
        /// <param name="port">The port number.</param>
        IMqttProviderConfigurator RemotePort(int port);
        /// <summary>
        /// Specifies whether the connection to the MQTT broker should use TLS or not
        /// </summary>
        /// <param name="enableTLS">Shuld enable TLS (true) or not (false)</param>
        /// <returns></returns>
        IMqttProviderConfigurator EnableTLS(bool enableTLS);
        /// <summary>
        /// Specifies the topic which should be used to send MQTT messages
        /// </summary>
        /// <param name="topicName"></param>
        /// <returns></returns>
        IMqttProviderConfigurator TopicName(string topicName);
        /// <summary>
        /// Specifies that the connection to the MQTT broker requires credentials
        /// </summary>
        /// <param name="credentials"></param>
        /// <returns></returns>
        IMqttProviderConfigurator Credentials(System.Net.NetworkCredential credentials);
        /// <summary>
        /// Specifies a custom serialization method for the MQTT messages.
        /// </summary>
        /// <param name="customSerializer">The custom serialization method</param>
        IMqttProviderConfigurator CustomSerializer(Func<AuditEvent, byte[]> customSerializer);
        /// <summary>
        /// Specifies a custom deserialization method for the MQTT messages.
        /// </summary>
        /// <param name="customDeserializer">The custom deserialization method</param>
        IMqttProviderConfigurator CustomDeserializer(Func<byte[], AuditEvent> customDeserializer);
    }
}
