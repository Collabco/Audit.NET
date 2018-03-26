using Audit.Core;
using System;
using System.Net;

namespace Audit.Mqtt.Configuration
{
    public class MqttProviderConfigurator : IMqttProviderConfigurator
    {
        internal string _remoteAddress;
        internal int _remotePort = 1883;
        internal bool _enableTLS;
        internal string _topicName;
        internal System.Net.NetworkCredential _networkCredential;
        internal Func<AuditEvent, byte[]> _customSerializer;
        internal Func<byte[], AuditEvent> _customDeserializer;

        public IMqttProviderConfigurator RemoteAddress(string address)
        {
            _remoteAddress = address;
            return this;
        }

        public IMqttProviderConfigurator RemotePort(int port)
        {
            _remotePort = port;
            return this;
        }

        public IMqttProviderConfigurator EnableTLS(bool enableTLS)
        {
            _enableTLS = enableTLS;
            return this;
        }

        public IMqttProviderConfigurator TopicName(string topicName)
        {
            _topicName = topicName;
            return this;
        }

        public IMqttProviderConfigurator Credentials(System.Net.NetworkCredential credentials)
        {
            _networkCredential = credentials;
            return this;
        }

        public IMqttProviderConfigurator Credentials(string username, string password)
        {
            _networkCredential = new System.Net.NetworkCredential(username, password);
            return this;
        }


        public IMqttProviderConfigurator CustomSerializer(Func<AuditEvent, byte[]> customSerializer)
        {
            _customSerializer = customSerializer;
            return this;
        }

        public IMqttProviderConfigurator CustomDeserializer(Func<byte[], AuditEvent> customDeserializer)
        {
            _customDeserializer = customDeserializer;
            return this;
        }
    }
}
