using Audit.Core.ConfigurationApi;
using Audit.Fluentd.Configuration;
using Audit.Fluentd.Providers;
using System;
using System.Net;

namespace Audit.Core
{
    public static class FluentdProviderConfiguratorExtensions
    {
        /// <summary>
        /// Send the events as Fluentd messages to an Fluentd broker.
        /// </summary>
        /// <param name="remoteAddress">The address of the remote host to send the audit events.</param>
        /// <param name="remotePort">The port number of the remote host to send the audit events.</param>
        /// <param name="tag">The name of the Fluentd message tag to send events with</param>
        /// <returns></returns>
        public static ICreationPolicyConfigurator UseFluentd(this IConfigurator configurator, string remoteAddress, int remotePort, 
            string tag)
        {
            Configuration.DataProvider = new FluentdDataProvider()
            {
                RemoteAddress = remoteAddress,
                RemotePort = remotePort,
                Tag = tag
            };
            return new CreationPolicyConfigurator();
        }

        /// <summary>
        /// Send the events as Fluentd messages to an Fluentd broker.
        /// </summary>
        /// <param name="remoteAddress">The address of the remote host to send the audit events.</param>
        /// <param name="tag">The name of the Fluentd message tag to send events with</param>

        /// <returns></returns>
        public static ICreationPolicyConfigurator UseFluentd(this IConfigurator configurator, string remoteAddress,
            string tag)
        {
            Configuration.DataProvider = new FluentdDataProvider()
            {
                RemoteAddress = remoteAddress,
                Tag = tag
            };
            return new CreationPolicyConfigurator();
        }

        /// <summary>
        /// Send the events as Fluentd messages to an Fluentd broker.
        /// </summary>
        /// <param name="config">The Fluentd provider configuration.</param>
        public static ICreationPolicyConfigurator UseFluentd(this IConfigurator configurator, Action<IFluentdProviderConfigurator> config)
        {
            var fluentdConfig = new FluentdProviderConfigurator();
            config.Invoke(fluentdConfig);
            return UseFluentd(configurator, fluentdConfig._remoteAddress, fluentdConfig._remotePort, fluentdConfig._tag);
        }
    }
}