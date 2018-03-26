using Audit.Core.ConfigurationApi;
using Audit.Mqtt.Configuration;
using Audit.Mqtt.Providers;
using System;
using System.Net;

namespace Audit.Core
{
    public static class MqttProviderConfiguratorExtensions
    {
        /// <summary>
        /// Send the events as MQTT messages to an MQTT broker.
        /// </summary>
        /// <param name="remoteAddress">The address of the remote host to send the audit events.</param>
        /// <param name="remotePort">The port number of the remote host to send the audit events.</param>
        /// <param name="enableTLS">Enable TLS when connecting to remote host</param>
        /// <param name="topicName">The name of the MQTT topic to send events to</param>
        /// <param name="credentials">Optional credentials to use to connect with</param>
        /// <param name="customSerializer">A custom serialization method, or NULL to use the json/UTF-8 default</param>
        /// <param name="customDeserializer">A custom deserialization method, or NULL to use the json/UTF-8 default</param>
        /// <returns></returns>
        public static ICreationPolicyConfigurator UseMqtt(this IConfigurator configurator, string remoteAddress, int remotePort, bool enableTLS, 
            string topicName, System.Net.NetworkCredential credentials = null, Func<AuditEvent, byte[]> customSerializer = null, Func<byte[], AuditEvent> customDeserializer = null)
        {
            Configuration.DataProvider = new MqttDataProvider()
            {
                RemoteAddress = remoteAddress,
                RemotePort = remotePort,
                TopicName = topicName,
                EnableTLS = enableTLS,
                Credentials = credentials,
                CustomSerializer = customSerializer,
                CustomDeserializer = customDeserializer
            };
            return new CreationPolicyConfigurator();
        }

        /// <summary>
        /// Send the events as MQTT messages to an MQTT broker.
        /// </summary>
        /// <param name="remoteAddress">The address of the remote host to send the audit events.</param>
        /// <param name="enableTLS">Enable TLS when connecting to remote host</param>
        /// <param name="topicName">The name of the MQTT topic to send events to</param>
        /// <param name="credentials">Optional credentials to use to connect with</param>
        /// <param name="customSerializer">A custom serialization method, or NULL to use the json/UTF-8 default</param>
        /// <param name="customDeserializer">A custom deserialization method, or NULL to use the json/UTF-8 default</param>
        /// <returns></returns>
        public static ICreationPolicyConfigurator UseMqtt(this IConfigurator configurator, string remoteAddress, bool enableTLS,
            string topicName, System.Net.NetworkCredential credentials = null, Func<AuditEvent, byte[]> customSerializer = null, Func<byte[], AuditEvent> customDeserializer = null)
        {
            Configuration.DataProvider = new MqttDataProvider()
            {
                RemoteAddress = remoteAddress,
                TopicName = topicName,
                EnableTLS = enableTLS,
                Credentials = credentials,
                CustomSerializer = customSerializer,
                CustomDeserializer = customDeserializer
            };
            return new CreationPolicyConfigurator();
        }

        /// <summary>
        /// Send the events as MQTT messages to an MQTT broker.
        /// </summary>
        /// <param name="config">The MQTT provider configuration.</param>
        public static ICreationPolicyConfigurator UseMqtt(this IConfigurator configurator, Action<IMqttProviderConfigurator> config)
        {
            var mqttConfig = new MqttProviderConfigurator();
            config.Invoke(mqttConfig);
            return UseMqtt(configurator, mqttConfig._remoteAddress, mqttConfig._remotePort, mqttConfig._enableTLS, mqttConfig._topicName,
                mqttConfig._networkCredential, mqttConfig._customSerializer, mqttConfig._customDeserializer);
        }
    }
}