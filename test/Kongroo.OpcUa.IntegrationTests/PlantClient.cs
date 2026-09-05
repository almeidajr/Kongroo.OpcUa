using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Discovery;
// A namespace alias rather than a using directive: the generated model
// namespace declares its own ObjectIds, which would make every unqualified
// ObjectIds below ambiguous with Opc.Ua's.
using PlantModel = Kongroo.OpcUa.Server;

namespace Kongroo.OpcUa.IntegrationTests;

/// <summary>
/// A real OPC UA client session against the plant server, reduced to the
/// five operations the integration tests need: browse, read, write, call and
/// subscribe to events.
/// </summary>
/// <remarks>
/// <para>
/// Every node is addressed by browse path from the Objects folder rather
/// than by a literal <see cref="NodeId"/>: the Plant namespace index is
/// assigned at runtime and is not stable across runs.
/// </para>
/// <para>
/// An instance owns a session, a client host and an ephemeral certificate
/// store, so every client obtained from <see cref="ConnectAsync"/> must be
/// disposed. Instances are not thread-safe; drive one from a single test.
/// </para>
/// </remarks>
internal sealed class PlantClient : IAsyncDisposable
{
    private const string PlantNamespaceUri = "http://kongroo.dev/UA/Plant/";
    private const string PlantBrowseName = "Plant";

    private readonly IHost _host;
    private readonly ManagedSession _session;
    private readonly string _pkiRoot;
    private readonly ushort _plantNamespaceIndex;

    private PlantClient(IHost host, ManagedSession session, string pkiRoot, ushort plantNamespaceIndex)
    {
        _host = host;
        _session = session;
        _pkiRoot = pkiRoot;
        _plantNamespaceIndex = plantNamespaceIndex;
    }

    /// <summary>
    /// Discovers the endpoint at <paramref name="endpointUrl"/> and opens an
    /// anonymous, signed-and-encrypted session on it.
    /// </summary>
    /// <param name="endpointUrl">
    /// The server's <c>opc.tcp</c> URL, used for discovery as well as for the
    /// session.
    /// </param>
    /// <param name="userIdentity">
    /// Credentials to activate the session with, or <see langword="null"/> to connect
    /// anonymously.
    /// </param>
    /// <param name="cancellationToken">Abandons the connection attempt.</param>
    /// <returns>
    /// A connected client the caller owns and must dispose. When connecting
    /// fails, everything opened along the way is released before the exception
    /// propagates.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The server does not expose the Plant namespace.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was signalled while connecting.
    /// </exception>
    public static async Task<PlantClient> ConnectAsync(
        string endpointUrl,
        UserIdentity? userIdentity = null,
        CancellationToken cancellationToken = default
    )
    {
        var pkiRoot = TestPkiRoot.Create();
        var host = BuildHost(pkiRoot);

        try
        {
            await host.StartAsync(cancellationToken);

            // Not the DI-registered discovery-and-connect Func: that factory always creates the
            // session anonymously and applies a supplied identity only as a best-effort,
            // fire-and-forget update afterwards (IClientIdentityProvider exists to refresh a
            // long-lived identity on an already-connected session, not to gate the first
            // activation) — a wrong password would silently fall back to an anonymous session
            // instead of failing to connect. Resolving the endpoint and calling the session
            // factory directly lets WithUserIdentity supply the identity eagerly, so a bad
            // credential fails the very first activation.
            var discovery = host.Services.GetRequiredService<IOpcUaDiscoveryService>();
            var factory = host.Services.GetRequiredService<IManagedSessionFactory>();
            var endpoints = await discovery.GetEndpointsAsync(endpointUrl, ct: cancellationToken);
            var configuredEndpoint = new ConfiguredEndpoint(null, SelectEndpoint(endpoints), null);

            var session = userIdentity is null
                ? await factory.ConnectAsync(configuredEndpoint, cancellationToken)
                : await factory.ConnectAsync(
                    configuredEndpoint,
                    sessionBuilder => sessionBuilder.WithUserIdentity(userIdentity),
                    cancellationToken
                );

            return new PlantClient(host, session, pkiRoot, ResolvePlantNamespaceIndex(session));
        }
        catch
        {
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
            TestPkiRoot.Delete(pkiRoot);
            throw;
        }
    }

    /// <summary>
    /// Browses one level below <paramref name="parent"/>, following forward
    /// hierarchical references.
    /// </summary>
    /// <param name="parent">Node to browse from.</param>
    /// <param name="cancellationToken">Abandons the browse.</param>
    /// <returns>
    /// The browse name of each child, in the order the server returned them.
    /// Children whose browse name has no text are omitted, so the result can
    /// be shorter than the reference list.
    /// </returns>
    public async Task<IReadOnlyList<string>> BrowseChildrenAsync(
        NodeId parent,
        CancellationToken cancellationToken = default
    )
    {
        var browser = new Browser(
            _session,
            new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
            }
        );

        var references = await browser.BrowseAsync(parent, cancellationToken);

        // ArrayOf<T> enumerates through a ReadOnlySpan enumerator, which an
        // async method may not hold, so materialize before projecting.
        var descriptions = references.ToArray() ?? [];

        return [.. descriptions.Select(static reference => reference.BrowseName.Name).OfType<string>()];
    }

    /// <summary>
    /// Reads <c>Plant/<paramref name="browseName"/></c> as a
    /// <see cref="double"/>. Always hits the server, never a cache.
    /// </summary>
    /// <param name="browseName">Browse name of the variable below Plant.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>The value the server reports for that variable.</returns>
    /// <exception cref="InvalidOperationException">
    /// No node answers that browse path below the Objects folder.
    /// </exception>
    /// <exception cref="ServiceResultException">
    /// The server rejected the read, or the value is not a
    /// <see cref="double"/>.
    /// </exception>
    public async Task<double> ReadDoubleAsync(string browseName, CancellationToken cancellationToken = default)
    {
        var nodeId = await ResolvePlantChildAsync(browseName, cancellationToken);

        return await _session.ReadValueAsync<double>(nodeId, cancellationToken);
    }

    /// <summary>
    /// Reads a Plant variable and returns the service result rather than throwing on a bad status.
    /// </summary>
    /// <param name="browseName">Browse name of the variable under the Plant object.</param>
    /// <param name="cancellationToken">Cancelled by the test host; ends the read.</param>
    /// <returns>
    /// The status the server returned and, when that status is good, the value. A bad status
    /// yields <see cref="double.NaN"/>, so a caller that ignores the status cannot mistake a
    /// denial for a reading.
    /// </returns>
    public async Task<(StatusCode Status, double Value)> TryReadDoubleAsync(
        string browseName,
        CancellationToken cancellationToken = default
    )
    {
        var nodeId = await ResolvePlantChildAsync(browseName, cancellationToken);

        ArrayOf<ReadValueId> nodesToRead = [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }];

        var response = await _session.ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, cancellationToken);
        var result = response.Results[0];

        return StatusCode.IsGood(result.StatusCode) && result.WrappedValue.TryGetValue(out double value)
            ? (result.StatusCode, value)
            : (result.StatusCode, double.NaN);
    }

    /// <summary>
    /// Writes <paramref name="value"/> to
    /// <c>Plant/<paramref name="browseName"/></c>.
    /// </summary>
    /// <param name="browseName">Browse name of the variable below Plant.</param>
    /// <param name="value">Value to write, in the variable's own units.</param>
    /// <param name="cancellationToken">Abandons the write.</param>
    /// <returns>
    /// A task that completes once the server has reported a good status.
    /// Completion does not promise the node now reads back
    /// <paramref name="value"/>: the server may clamp it.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No node answers that browse path below the Objects folder.
    /// </exception>
    /// <exception cref="ServiceResultException">
    /// The server reported a bad status for the write.
    /// </exception>
    public async Task WriteDoubleAsync(string browseName, double value, CancellationToken cancellationToken = default)
    {
        var nodeId = await ResolvePlantChildAsync(browseName, cancellationToken);

        ArrayOf<WriteValue> nodesToWrite =
        [
            new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(Variant.From(value)),
            },
        ];

        var response = await _session.WriteAsync(null, nodesToWrite, cancellationToken);
        var status = response.Results[0];

        if (StatusCode.IsBad(status))
        {
            throw new ServiceResultException(status, $"Writing '{browseName}' failed.");
        }
    }

    /// <summary>
    /// Calls <c>Plant/SetSetpoint</c>, the method that reports back what the
    /// server did with the request.
    /// </summary>
    /// <param name="requested">Setpoint to ask for, in degrees Celsius.</param>
    /// <param name="cancellationToken">Abandons the call.</param>
    /// <returns>
    /// The method's single output argument: the setpoint actually applied,
    /// which differs from <paramref name="requested"/> when it was clamped.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The Plant object or the method could not be resolved, or the call
    /// returned something other than one <see cref="double"/>.
    /// </exception>
    public async Task<double> CallSetSetpointAsync(double requested, CancellationToken cancellationToken = default)
    {
        var plantNodeId = await ResolveAsync([PlantBrowseName], cancellationToken);
        var methodNodeId = await ResolvePlantChildAsync("SetSetpoint", cancellationToken);

        var outputArguments = await _session.CallAsync(
            plantNodeId,
            methodNodeId,
            cancellationToken,
            Variant.From(requested)
        );

        return outputArguments.Count == 1 && outputArguments[0].TryGetValue(out double accepted)
            ? accepted
            : throw new InvalidOperationException(
                $"SetSetpoint returned {outputArguments.Count} output argument(s), expected one Double."
            );
    }

    /// <summary>
    /// Streams one record per <c>SetpointChangedEventType</c> reported by the
    /// Plant object. The event monitored item is created when enumeration
    /// starts and removed when the enumerator is disposed, so the server's
    /// event source activates and tears down with this stream.
    /// </summary>
    /// <param name="cancellationToken">
    /// Ends the stream. Because the monitored item is created asynchronously
    /// once enumeration starts, changes made before the first result arrives
    /// may never be delivered.
    /// </param>
    /// <returns>
    /// A sequence that stays open until cancelled, yielding a
    /// <see cref="Kongroo.OpcUa.Server.SetpointChangedEventTypeRecord"/> per
    /// event. Notifications that decode to any other event type are skipped.
    /// </returns>
    /// <remarks>
    /// A private decoder registry rather than
    /// <see cref="EventRecordDecoderRegistry.Default"/>: the composed field
    /// layout drives both the filter's select clauses and the positional
    /// decode, so registering only this model's decoders keeps the two in step
    /// and leaves no process-wide state behind.
    /// </remarks>
    public async IAsyncEnumerable<PlantModel.SetpointChangedEventTypeRecord> SubscribeSetpointChangesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var plantNodeId = await ResolveAsync([PlantBrowseName], cancellationToken);
        // The session's own table, not MessageContext's: only the former is
        // populated with the server's namespaces, and both the filter's type id
        // and the decoder registration are looked up by URI.
        var namespaceUris = _session.NamespaceUris;
        var registry = PlantModel.PlantEventRecordDecoders.RegisterPlantDecoders(
            new EventRecordDecoderRegistry(),
            namespaceUris
        );
        var filter = PlantModel.SetpointChangedEventTypeRecord.EventFilters.Build(namespaceUris, registry);

        var notifications = _session.DefaultStreaming.SubscribeEventsAsync(
            plantNodeId,
            filter,
            options: null,
            cancellationToken
        );

        await foreach (var notification in notifications)
        {
            // ArrayOf<T> enumerates through a ReadOnlySpan enumerator, which an
            // async method may not hold, so materialize before decoding.
            var fields = notification.Fields.ToArray() ?? [];

            if (registry.Decode(fields) is PlantModel.SetpointChangedEventTypeRecord change)
            {
                yield return change;
            }
        }
    }

    /// <summary>
    /// Closes the session, stops the client host and removes the ephemeral
    /// certificate store.
    /// </summary>
    /// <returns>
    /// A task that completes once the session and host are down; store removal
    /// is best effort and never faults it.
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
        TestPkiRoot.Delete(_pkiRoot);
    }

    private static IHost BuildHost(string pkiRoot)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder
            .Services.AddOpcUa()
            .AddClient(options =>
            {
                options.ApplicationName = "KongrooOpcUaTestClient";
                options.ApplicationUri = "urn:localhost:KongrooOpcUaTestClient";
                options.ProductUri = "uri:kongroo.dev:KongrooOpcUaTestClient";
                options.PkiRoot = pkiRoot;
                // Test-only: the server's certificate is generated on first
                // boot into an ephemeral store, so there is nothing to trust
                // it against out of band.
                options.AutoAcceptUntrustedCertificates = true;
                options.Session = new ManagedSessionOptions { SessionName = "KongrooOpcUaTestClient" };
            })
            // Registers IOpcUaDiscoveryService; endpoint selection and the session connect
            // itself are done in ConnectAsync so a per-connect identity can be supplied eagerly.
            .AddDiscovery()
            // Registers the v2 subscription manager that ManagedSession's
            // DefaultStreaming needs; without it the property throws. The
            // publishing interval bounds how long an event waits for its
            // publish cycle, so keep it short enough not to pad a test.
            .AddSubscriptions(options =>
            {
                options.PublishingInterval = TimeSpan.FromMilliseconds(100.0);
                options.KeepAliveCount = 10;
                options.LifetimeCount = 100;
            });

        return builder.Build();
    }

    /// <summary>
    /// Picks the endpoint matching this client's security requirements from a discovery
    /// response. Mirrors the private filtering the stack's own discovery-and-connect wiring
    /// applies internally; <see cref="ConnectAsync"/> does not use that wiring directly, per the
    /// comment at its call site.
    /// </summary>
    private static EndpointDescription SelectEndpoint(ArrayOf<EndpointDescription> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            if (
                endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt
                && string.Equals(endpoint.SecurityPolicyUri, SecurityPolicies.Basic256Sha256, StringComparison.Ordinal)
            )
            {
                return endpoint;
            }
        }

        throw new InvalidOperationException("No discovered endpoint matched the configured security policy and mode.");
    }

    private static ushort ResolvePlantNamespaceIndex(ManagedSession session)
    {
        var index = session.NamespaceUris.GetIndex(PlantNamespaceUri);

        return index >= 0
            ? (ushort)index
            : throw new InvalidOperationException($"The server does not expose the namespace '{PlantNamespaceUri}'.");
    }

    private Task<NodeId> ResolvePlantChildAsync(string browseName, CancellationToken cancellationToken) =>
        ResolveAsync([PlantBrowseName, browseName], cancellationToken);

    private async Task<NodeId> ResolveAsync(IReadOnlyList<string> browseNames, CancellationToken cancellationToken)
    {
        ArrayOf<BrowsePath> browsePaths =
        [
            new BrowsePath
            {
                StartingNode = ObjectIds.ObjectsFolder,
                RelativePath = new RelativePath { Elements = [.. browseNames.Select(BuildPathElement)] },
            },
        ];

        var response = await _session.TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, cancellationToken);
        var result = response.Results[0];

        return StatusCode.IsBad(result.StatusCode) || result.Targets.Count == 0
            ? throw new InvalidOperationException(
                $"Could not resolve '{string.Join('/', browseNames)}' below the Objects folder: {result.StatusCode}."
            )
            : ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, _session.MessageContext.NamespaceUris);
    }

    private RelativePathElement BuildPathElement(string browseName)
    {
        return new RelativePathElement
        {
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IsInverse = false,
            IncludeSubtypes = true,
            TargetName = new QualifiedName(browseName, _plantNamespaceIndex),
        };
    }
}
