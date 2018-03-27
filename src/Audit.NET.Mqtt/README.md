**MQTT provider for [Audit.NET library](https://github.com/thepirat000/Audit.NET)** (An extensible framework to audit executing operations in .NET).

Sends Audit Logs as MQTT messages to a suitable message broker.

## Install

**NuGet Package** 
To install the package run the following command on the Package Manager Console:

```
PM> Install-Package Audit.NET.Mqtt
``
## Usage
Please see the [Audit.NET Readme](https://github.com/thepirat000/Audit.NET#usage)

## Configuration
Set the static `Audit.Core.Configuration.DataProvider` property to set the Mqtt data provider, or call the `UseMqtt` method on the fluent configuration. This should be done before any `AuditScope` creation, i.e. during application startup.

For example:
```c#
Audit.Core.Configuration.DataProvider = new MqttDataProvider()
{
	RemoteAddress = "127.0.0.1",
	RemotePort = 1883, 
	EnableTLS = true,
	TopicName = "some/topic",
	Credentials = new System.Net.NetworkCredential("username", password)
};
```

Or by using the [fluent configuration API](https://github.com/thepirat000/Audit.NET#configuration-fluent-api):
```c#
Audit.Core.Configuration.Setup()
    .UseMqtt(config => config
        .RemoteAddress("224.0.0.1")
        .RemotePort(3333)
		.EnableTLS(true)
		.TopicName("some/topic")
		.Credentials("username", "password"));
```

### Provider Options

Mandatory:
- **RemoteAddress**: The address of the remote host to which the audit events should be sent.
- **RemotePort**: The port number of the remote host to which the audit events should be sent.
- **EnableTLS**: Specifies whether the connection to the MQTT broker should use TLS or not.
- **TopicName**: Specifies the topic which should be used to send MQTT messages to.
Optional:
- **Credentials**: Specifies that the connection to the MQTT broker requires credentials.
- **CustomSerializer**: To specify a custom serialization method for the events to send as UDP packets (default is JSON encoded as UTF-8).
- **CustomDeserializer**: To specify a custom deserialization method for the events to receive UDP packets.

### Notes
