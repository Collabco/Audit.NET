using Audit.Core;
using System;
using System.Net;

namespace Audit.Fluentd.Configuration
{
    /// <summary>
    /// Provides a configuration for the Fluentd data provider
    /// </summary>
    public interface IFluentdProviderConfigurator
    {
        /// <summary>
        /// Specifies the address of the remote host to send the audit events.
        /// </summary>
        /// <param name="address">The IP address.</param>
        IFluentdProviderConfigurator RemoteAddress(string address);

        /// <summary>
        /// Specifies the port of the remote host to send the audit events.
        /// </summary>
        /// <param name="port">The port number.</param>
        IFluentdProviderConfigurator RemotePort(int port);
        /// <summary>
        /// Specifies the tag of the message to send to Fluentd
        /// </summary>
        /// <param name="tag">The tag of the message to send to Fluentd</param>
        /// <returns></returns>
        IFluentdProviderConfigurator Tag(string tag);

    }
}
