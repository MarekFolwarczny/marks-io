using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Formats.Asn1;
using System.Net;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Server;

namespace ModbusServer;

/// <summary>Neměnná definice uzlu; živou hodnotu po startu drží node manager.</summary>
internal sealed record OpcUaItem(string Name, string Identifier, OpcUaValueType Type, object Value);

/// <summary>Načítá certifikátový profil a vytváří zabezpečenou konfiguraci pro obě role.</summary>
internal static class OpcUaSecurity
{
    public const string Policy = SecurityPolicies.Basic256Sha256;

    /// <summary>Načte vlastní DER certifikát s PEM klíčem a ručně schválené protistrany.</summary>
    public static async Task<(Opc.Ua.ApplicationConfiguration Configuration, X509Certificate2 Certificate)> LoadAsync(
        string profile, string applicationUri, bool server, string endpoint)
    {
        profile = Path.GetFullPath(profile);
        var publicCertificate = new X509Certificate2(File.ReadAllBytes(Path.Combine(profile, "certificate.der")));
        using var privateKey = RSA.Create();
        privateKey.ImportFromPem(File.ReadAllText(Path.Combine(profile, "private_key.pem")));
        // OPC UA stack potřebuje objekt certifikátu s exportovatelným RSA klíčem.
        using var combined = publicCertificate.CopyWithPrivateKey(privateKey);
        var certificate = new X509Certificate2(combined.Export(X509ContentType.Pkcs12));
        if (!certificate.HasPrivateKey) throw new InvalidOperationException("Profil neobsahuje soukromý klíč.");
        if (DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() || DateTime.UtcNow > certificate.NotAfter.ToUniversalTime())
            throw new InvalidOperationException("Vlastní certifikát není časově platný.");
        if (!X509Utils.CompareApplicationUriWithCertificate(certificate, applicationUri))
            throw new InvalidOperationException("Application URI neodpovídá URI v certifikátu.");

        var trusted = new CertificateIdentifierCollection();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(profile, "trusted")))
            if (Path.GetExtension(path).Equals(".der", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".pem", StringComparison.OrdinalIgnoreCase))
                trusted.Add(new CertificateIdentifier(X509CertificateLoader.LoadCertificateFromFile(path)));
        if (trusted.Count == 0) throw new InvalidOperationException("Profil neobsahuje schválené certifikáty v trusted/.");

        // Prázdný issuer store a odmítnuté veřejné certifikáty necháváme mimo profil s klíčem.
        var cache = Path.Combine(Path.GetTempPath(), "IndustrialProtocolSimulatorOpcUa",
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile))));
        var issuers = Path.Combine(cache, "issuers");
        var rejected = Path.Combine(cache, "rejected");
        Directory.CreateDirectory(issuers);
        Directory.CreateDirectory(rejected);

        var config = new Opc.Ua.ApplicationConfiguration
        {
            ApplicationName = "Industrial Protocol Simulator",
            ApplicationUri = applicationUri,
            ApplicationType = server ? ApplicationType.Server : ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier(certificate),
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(profile, "trusted"),
                    TrustedCertificates = trusted
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory, StorePath = issuers
                },
                RejectedCertificateStore = new CertificateStoreIdentifier
                {
                    StoreType = CertificateStoreType.Directory, StorePath = rejected
                },
                AutoAcceptUntrustedCertificates = false,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 5000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 5000 },
            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = new StringCollection { endpoint },
                SecurityPolicies = new ServerSecurityPolicyCollection
                {
                    new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.SignAndEncrypt, SecurityPolicyUri = Policy }
                },
                UserTokenPolicies = new UserTokenPolicyCollection { new UserTokenPolicy(UserTokenType.Anonymous) },
                MinSessionTimeout = 5000,
                MaxSessionTimeout = 5000
            }
        };
        await config.ValidateAsync(server ? ApplicationType.Server : ApplicationType.Client, CancellationToken.None);
        return (config, certificate);
    }

    /// <summary>Vyžaduje, aby pinovaný certifikát byl zároveň v seznamu důvěry.</summary>
    public static X509Certificate2 PinnedCertificate(string profile, string file)
    {
        var pinned = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(profile, file));
        var trusted = Directory.EnumerateFiles(Path.Combine(profile, "trusted"))
            .Where(path => Path.GetExtension(path).Equals(".der", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".pem", StringComparison.OrdinalIgnoreCase))
            .Select(X509CertificateLoader.LoadCertificateFromFile);
        if (!trusted.Any(cert => cert.RawData.AsSpan().SequenceEqual(pinned.RawData)))
            throw new InvalidOperationException($"{file} musí být také ve složce trusted/.");
        return pinned;
    }

    /// <summary>Ověří přesnou shodu DNS nebo IP endpointu se SAN serverového certifikátu.</summary>
    public static void VerifyEndpointHost(X509Certificate2 certificate, string endpointUrl)
    {
        var host = new Uri(endpointUrl).Host;
        var extension = certificate.Extensions["2.5.29.17"]
            ?? throw new InvalidOperationException("Certifikát serveru nemá Subject Alternative Name.");
        // GeneralName tag 2 je DNS, tag 7 obsahuje syrové bajty IPv4/IPv6 adresy.
        var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
        var matched = false;
        while (reader.HasData)
        {
            var tag = reader.PeekTag();
            if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
            {
                var dns = reader.ReadCharacterString(UniversalTagNumber.IA5String, tag);
                matched |= string.Equals(dns, host, StringComparison.OrdinalIgnoreCase);
            }
            else if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 7)))
            {
                var bytes = reader.ReadOctetString(tag);
                matched |= IPAddress.TryParse(host, out var address) && bytes.AsSpan().SequenceEqual(address.GetAddressBytes());
            }
            else reader.ReadEncodedValue();
        }
        if (!matched) throw new InvalidOperationException($"Host {host} není uveden v SAN certifikátu serveru.");
    }
}

/// <summary>Publikuje jeden uživatelský namespace a předává události sessions do logu.</summary>
internal sealed class OpcUaServer : StandardServer
{
    private readonly string _namespaceUri;
    private readonly IReadOnlyList<OpcUaItem> _items;
    private readonly Action<string> _log;
    private readonly byte[] _writerCertificate;
    public OpcUaNodeManager? Items { get; private set; }
    public OpcUaServer(string namespaceUri, IReadOnlyList<OpcUaItem> items, byte[] writerCertificate, Action<string> log)
    {
        _namespaceUri = namespaceUri;
        _items = items;
        _writerCertificate = writerCertificate;
        _log = log;
    }

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, Opc.Ua.ApplicationConfiguration configuration)
    {
        Items = new OpcUaNodeManager(server, configuration, _namespaceUri, _items, _writerCertificate, _log);
        return new MasterNodeManager(server, configuration, null,
            new INodeManager[] { Items });
    }

    // SessionManager vzniká až při startu serveru, nikoli při tvorbě node manageru.
    protected override void OnServerStarted(IServerInternal server)
    {
        base.OnServerStarted(server);
        server.SessionManager.SessionActivated += (session, _) => _log($"Klient připojen: {session.Id}");
        server.SessionManager.SessionClosing += (session, _) => _log($"Klient odpojen: {session.Id}");
    }
}

/// <summary>Drží statický adresní prostor a autorizuje vzdálené zápisy podle certifikátu session.</summary>
internal sealed class OpcUaNodeManager : CustomNodeManager2
{
    private readonly IReadOnlyList<OpcUaItem> _items;
    private readonly Action<string> _log;
    private readonly byte[] _writerCertificate;
    private readonly Dictionary<string, BaseDataVariableState> _variables = new();
    public OpcUaNodeManager(IServerInternal server, Opc.Ua.ApplicationConfiguration config, string namespaceUri,
        IReadOnlyList<OpcUaItem> items, byte[] writerCertificate, Action<string> log) : base(server, config, namespaceUri)
    {
        _items = items;
        _writerCertificate = writerCertificate;
        _log = log;
    }

    public object ReadLocal(string identifier)
    {
        lock (Lock) return _variables[identifier].Value;
    }

    /// <summary>Místní změna musí oznámit změnovou masku také aktivním subscriptions.</summary>
    public void WriteLocal(string identifier, object value)
    {
        lock (Lock)
        {
            var variable = _variables[identifier];
            if (variable.Value.GetType() != value.GetType()) throw new ArgumentException("Typ hodnoty neodpovídá uzlu.");
            variable.Value = value;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(SystemContext, false);
        }
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> references)
    {
        if (!references.TryGetValue(ObjectIds.ObjectsFolder, out var objects))
            references[ObjectIds.ObjectsFolder] = objects = new List<IReference>();
        var folder = new FolderState(null)
        {
            SymbolicName = "Simulator",
            NodeId = new NodeId("Simulator", NamespaceIndex),
            BrowseName = new QualifiedName("Simulator", NamespaceIndex),
            DisplayName = "Simulator",
            TypeDefinitionId = ObjectTypeIds.FolderType
        };
        folder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
        objects.Add(new NodeStateReference(ReferenceTypes.Organizes, false, folder.NodeId));
        AddPredefinedNode(SystemContext, folder);
        foreach (var item in _items)
        {
            var variable = new BaseDataVariableState(folder)
            {
                SymbolicName = item.Name,
                NodeId = OpcUaValue.Node(item.Identifier, NamespaceIndex),
                BrowseName = new QualifiedName(item.Name, NamespaceIndex),
                DisplayName = item.Name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = OpcUaValue.DataType(item.Type),
                ValueRank = ValueRanks.Scalar,
                Value = item.Value,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
            variable.OnSimpleReadValue = (ISystemContext context, NodeState node, ref object value) =>
            {
                _log($"Čtení {item.Identifier}: {variable.Value}");
                value = variable.Value;
                return ServiceResult.Good;
            };
            variable.OnSimpleWriteValue = (ISystemContext context, NodeState node, ref object value) =>
            {
                // Oprávnění určuje aplikační certifikát SecureChannel, nikoli anonymní user token.
                var client = (context as ServerSystemContext)?.OperationContext?.Session?.ClientCertificate;
                if (client is null || !client.RawData.AsSpan().SequenceEqual(_writerCertificate))
                {
                    _log($"Odmítnut zápis {item.Identifier}: klient nemá oprávnění.");
                    return new ServiceResult(StatusCodes.BadUserAccessDenied);
                }
                if (value?.GetType() != item.Value.GetType())
                {
                    _log($"Odmítnut zápis {item.Identifier}: nesprávný typ.");
                    return new ServiceResult(StatusCodes.BadTypeMismatch);
                }
                if (value is string text && text.EnumerateRunes().Count() > 255)
                {
                    _log($"Odmítnut zápis {item.Identifier}: text je příliš dlouhý.");
                    return new ServiceResult(StatusCodes.BadOutOfRange);
                }
                if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d))
                {
                    _log($"Odmítnut zápis {item.Identifier}: nekonečná nebo NaN hodnota.");
                    return new ServiceResult(StatusCodes.BadOutOfRange);
                }
                _log($"Zápis {item.Identifier}: {value}");
                return ServiceResult.Good;
            };
            folder.AddChild(variable);
            _variables.Add(item.Identifier, variable);
            AddPredefinedNode(SystemContext, variable);
        }
    }
}

/// <summary>Ruční klient bez discovery, pollingu a automatického reconnectu.</summary>
internal sealed class OpcUaClient : IDisposable
{
    private Opc.Ua.Client.Session? _session;
    public bool Connected => _session?.Connected == true;

    public async Task ConnectAsync(string endpointUrl, string profile, string applicationUri)
    {
        var (config, _) = await OpcUaSecurity.LoadAsync(profile, applicationUri, false, endpointUrl);
        var pinned = OpcUaSecurity.PinnedCertificate(profile, "server.der");
        // Knihovní kontrola domény v této cestě odmítá platné IP SAN; ověříme je sami před session.
        OpcUaSecurity.VerifyEndpointHost(pinned, endpointUrl);
        var description = new EndpointDescription(endpointUrl)
        {
            Server = new ApplicationDescription
            {
                ApplicationUri = X509Utils.GetApplicationUrisFromCertificate(pinned).FirstOrDefault()
                    ?? throw new InvalidOperationException("Serverový certifikát nemá Application URI v SAN."),
                ApplicationType = ApplicationType.Server
            },
            ServerCertificate = pinned.RawData,
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = OpcUaSecurity.Policy,
            UserIdentityTokens = new UserTokenPolicyCollection { new UserTokenPolicy(UserTokenType.Anonymous) },
            TransportProfileUri = Profiles.UaTcpTransport
        };
        // Popis endpointu pochází z pinovaného certifikátu; neprovádíme nezabezpečené discovery.
        var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(config));
        _session = await Opc.Ua.Client.Session.Create(config, endpoint, false, false,
            "Industrial Protocol Simulator", 5000, new UserIdentity(new AnonymousIdentityToken()), null);
        if (!Connected) throw new InvalidOperationException("OPC UA session nebyla navázána.");
    }

    /// <summary>Index namespace se může mezi restarty serveru změnit; překládá se z aktuální session.</summary>
    private NodeId Resolve(string namespaceUri, string identifier)
    {
        var session = _session ?? throw new InvalidOperationException("Klient není připojen.");
        var index = session.NamespaceUris.GetIndex(namespaceUri);
        if (index < 0) throw new ArgumentException("Server nenabízí zadaný namespace URI.");
        return OpcUaValue.Node(identifier, (ushort)index);
    }

    public async Task<object> ReadAsync(string namespaceUri, string identifier)
    {
        var session = _session ?? throw new InvalidOperationException("Klient není připojen.");
        var result = await session.ReadValueAsync(Resolve(namespaceUri, identifier));
        if (StatusCode.IsBad(result.StatusCode) || StatusCode.IsUncertain(result.StatusCode))
            throw new ServiceResultException(result.StatusCode);
        return result.Value ?? throw new InvalidOperationException("Uzel vrátil prázdnou hodnotu.");
    }

    public async Task WriteAsync(string namespaceUri, string identifier, object value)
    {
        var session = _session ?? throw new InvalidOperationException("Klient není připojen.");
        var response = await session.WriteAsync(null, new WriteValueCollection
        {
            new WriteValue { NodeId = Resolve(namespaceUri, identifier), AttributeId = Attributes.Value,
                Value = new DataValue { Value = value } }
        }, CancellationToken.None);
        if (StatusCode.IsBad(response.Results[0])) throw new ServiceResultException(response.Results[0]);
    }

    public void Dispose()
    {
        _session?.Close();
        _session?.Dispose();
        _session = null;
    }
}
