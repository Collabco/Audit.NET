using Audit.Core;
using System;
using System.Net;

namespace Audit.Fluentd.Configuration
{
    public class FluentdProviderConfigurator : IFluentdProviderConfigurator
    {
        internal string _remoteAddress;
        internal int _remotePort = 24224;
        internal string _tag;
        internal bool _asynchronusWrites;

        public IFluentdProviderConfigurator RemoteAddress(string address)
        {
            _remoteAddress = address;
            return this;
        }

        public IFluentdProviderConfigurator RemotePort(int port)
        {
            _remotePort = port;
            return this;
        }

        public IFluentdProviderConfigurator Tag(string tag)
        {
            _tag = tag;
            return this;
        }

        public IFluentdProviderConfigurator AsynchronusWrites(bool asynchronus)
        {
            _asynchronusWrites = asynchronus;
            return this;
        }
    }
}
