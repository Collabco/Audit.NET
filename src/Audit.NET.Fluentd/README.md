**Fluentd provider for [Audit.NET library](https://github.com/thepirat000/Audit.NET)** (An extensible framework to audit executing operations in .NET).

Sends Audit Logs over Fluentd foward protocol to a Fluentd data fowarder.

## Install

**NuGet Package** 
To install the package run the following command on the Package Manager Console:

```
PM> Install-Package Audit.NET.Fluentd
``
## Usage
Please see the [Audit.NET Readme](https://github.com/thepirat000/Audit.NET#usage)

## Configuration
Set the static `Audit.Core.Configuration.DataProvider` property to set the Fluentd data provider, or call the `UseFluentd` method on the fluent configuration. This should be done before any `AuditScope` creation, i.e. during application startup.

For example:
```c#
Audit.Core.Configuration.DataProvider = new FluentdDataProvider()
{
	RemoteAddress = "127.0.0.1",
	RemotePort = 24224, 
	Tag = "sometag"
};
```

Or by using the [fluent configuration API](https://github.com/thepirat000/Audit.NET#configuration-fluent-api):
```c#
Audit.Core.Configuration.Setup()
    .UseFluentd(config => config
        .RemoteAddress("192.168.0.1")
        .RemotePort(24224)
		.Tag("sometag")
```

### Provider Options

Mandatory:
- **RemoteAddress**: The address of the remote host to which the audit events should be sent.
- **RemotePort**: The port number of the remote host to which the audit events should be sent.
- **Tag**: Specifies the tag which should be used for the Fluentd messages.

### Notes
